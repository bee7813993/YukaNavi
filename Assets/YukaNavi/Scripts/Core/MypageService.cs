using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using YukaNavi.Api;
using YukaNavi.UI;

namespace YukaNavi.Core
{
    /// <summary>
    /// マイページデータの窓口。現在のサーバーにデバイスリンク済みならサーバー
    /// (/api/mypage.php = Web 版と同じデータ) を正とし、未リンクなら端末内 (LocalMypage) を使う。
    ///
    /// リンク中の方針:
    /// - 読み: サーバーから取得。お気に入り系はローカルへもミラーする
    ///   (別サーバーに移ったときにローカルデータとして使える + IsIn* の即時判定に使う)
    /// - 書き: ローカルに書いてからサーバーへも送る (二重書き)
    /// - 履歴: サーバーへの記録は exec.php (予約時) が自動で行う。ローカルの履歴は
    ///   端末の蓄積として別に保ち、サーバー内容で置き換えない
    /// </summary>
    public static class MypageService
    {
        /// <summary>現在のサーバーにデバイスリンク済みか。</summary>
        public static bool IsLinked
        {
            get { return !string.IsNullOrEmpty(AppConfig.LinkedMypageUserId); }
        }

        static string Uid
        {
            get { return AppConfig.LinkedMypageUserId; }
        }

        // ---- 別サーバーから戻ってきたときの自動再統合 ----

        const string SyncedServerKey = "yukanavi.mypage_synced_server";
        static Task _resync;

        static string NormalizedServer()
        {
            return AppConfig.ServerUrl.Trim().TrimEnd('/');
        }

        /// <summary>
        /// リンク済みサーバーへ接続し直した後の初回アクセスで、端末内データをサーバーへ
        /// 再統合する (別サーバー滞在中にローカルへ貯めた履歴・お気に入りを反映する)。
        /// import は冪等なので重複しない。同期済みのサーバーでは何もしない。
        /// </summary>
        static Task EnsureResyncAsync()
        {
            if (!IsLinked || PlayerPrefs.GetString(SyncedServerKey, "") == NormalizedServer())
            {
                return Task.CompletedTask;
            }
            if (_resync == null || _resync.IsCompleted)
            {
                _resync = ResyncAsync();
            }
            return _resync;
        }

        static async Task ResyncAsync()
        {
            await AppConfig.CreateClient().MypageImportAsync(Uid, BuildExportJson());
            MarkSynced();
        }

        static void MarkSynced()
        {
            PlayerPrefs.SetString(SyncedServerKey, NormalizedServer());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 端末内データを Web 版エクスポート形式 (version 1) の JSON にする。
        /// 履歴は回数ぶんの行に展開し、日時を 1 秒ずつずらす (回数を保ちつつ冪等にするため。
        /// サーバー側の重複判定は fullpath + 日時)。
        /// </summary>
        static string BuildExportJson()
        {
            var history = new List<Dictionary<string, object>>();
            foreach (var item in LocalMypage.GetHistory())
            {
                int times = Mathf.Max(1, item.Times);
                for (int i = 0; i < times; i++)
                {
                    history.Add(new Dictionary<string, object>
                    {
                        { "fullpath", item.FullPath },
                        { "songfile", item.Songfile },
                        { "kind", item.Kind ?? "" },
                        { "requested_at", item.LastAt - i },
                    });
                }
            }
            var later = new List<Dictionary<string, object>>();
            foreach (var item in LocalMypage.GetLater())
            {
                later.Add(SongRow(item));
            }
            var favorite = new List<Dictionary<string, object>>();
            foreach (var item in LocalMypage.GetFavorite())
            {
                favorite.Add(SongRow(item));
            }
            var keywords = new List<Dictionary<string, object>>();
            foreach (var search in LocalMypage.GetSavedSearches())
            {
                string keyword, type, prms;
                ToServerKeyword(search, out keyword, out type, out prms);
                if (string.IsNullOrEmpty(keyword))
                {
                    continue;
                }
                keywords.Add(new Dictionary<string, object>
                {
                    { "keyword", keyword },
                    { "search_type", type },
                    { "search_params", prms },
                    { "added_at", search.AddedAt },
                });
            }
            return JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "version", 1 },
                { "exported_at", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "history", history },
                { "later", later },
                { "favorite_songs", favorite },
                { "favorite_keywords", keywords },
            });
        }

        static Dictionary<string, object> SongRow(LocalMypage.Item item)
        {
            return new Dictionary<string, object>
            {
                { "fullpath", item.FullPath },
                { "songfile", item.Songfile },
                { "kind", item.Kind ?? "" },
                { "added_at", item.AddedAt },
            };
        }

        // ---- 読み ----

        public static async Task<List<LocalMypage.Item>> GetHistoryAsync()
        {
            if (!IsLinked)
            {
                return LocalMypage.GetHistory();
            }
            await EnsureResyncAsync();
            var data = await AppConfig.CreateClient().MypageListAsync(Uid, "history");
            var list = new List<LocalMypage.Item>();
            if (data.Items != null)
            {
                foreach (var item in data.Items)
                {
                    list.Add(new LocalMypage.Item
                    {
                        FullPath = item.FullPath,
                        Songfile = item.Songfile,
                        Kind = item.Kind,
                        Times = item.Times,
                        LastAt = item.LastRequestedAt,
                    });
                }
            }
            return list;
        }

        public static Task<List<LocalMypage.Item>> GetLaterAsync()
        {
            return GetSongListAsync("later", LocalMypage.GetLater, LocalMypage.ReplaceLater);
        }

        public static Task<List<LocalMypage.Item>> GetFavoriteAsync()
        {
            return GetSongListAsync("favorite", LocalMypage.GetFavorite, LocalMypage.ReplaceFavorite);
        }

        static async Task<List<LocalMypage.Item>> GetSongListAsync(
            string listName,
            System.Func<List<LocalMypage.Item>> localGet,
            System.Action<List<LocalMypage.Item>> localReplace)
        {
            if (!IsLinked)
            {
                return localGet();
            }
            await EnsureResyncAsync();
            var data = await AppConfig.CreateClient().MypageListAsync(Uid, listName);
            var list = new List<LocalMypage.Item>();
            if (data.Items != null)
            {
                foreach (var item in data.Items)
                {
                    list.Add(new LocalMypage.Item
                    {
                        FullPath = item.FullPath,
                        Songfile = item.Songfile,
                        Kind = item.Kind,
                        AddedAt = item.AddedAt,
                    });
                }
            }
            localReplace(new List<LocalMypage.Item>(list)); // ミラー (Web 側での追加・削除を反映)
            return list;
        }

        public static async Task<List<LocalMypage.SavedSearch>> GetSavedSearchesAsync()
        {
            if (!IsLinked)
            {
                return LocalMypage.GetSavedSearches();
            }
            await EnsureResyncAsync();
            var data = await AppConfig.CreateClient().MypageKeywordsAsync(Uid);
            var list = new List<LocalMypage.SavedSearch>();
            if (data.Items != null)
            {
                foreach (var item in data.Items)
                {
                    list.Add(FromServerKeyword(item));
                }
            }
            LocalMypage.ReplaceSavedSearches(new List<LocalMypage.SavedSearch>(list));
            return list;
        }

        // ---- 書き (ローカルへ書いてからサーバーへも送る) ----

        /// <summary>追加/解除をトグルする。追加されたら true。サーバー送信失敗は例外。</summary>
        public static async Task<bool> ToggleFavoriteAsync(string fullpath, string songfile, string kind)
        {
            bool added = LocalMypage.ToggleFavorite(fullpath, songfile, kind);
            if (IsLinked)
            {
                var client = AppConfig.CreateClient();
                if (added)
                {
                    await client.MypageAddAsync(Uid, "favorite", fullpath, songfile, kind);
                }
                else
                {
                    await client.MypageRemoveAsync(Uid, "favorite", fullpath);
                }
            }
            return added;
        }

        /// <summary>追加/解除をトグルする。追加されたら true。</summary>
        public static async Task<bool> ToggleLaterAsync(string fullpath, string songfile, string kind)
        {
            bool added = LocalMypage.ToggleLater(fullpath, songfile, kind);
            if (IsLinked)
            {
                var client = AppConfig.CreateClient();
                if (added)
                {
                    await client.MypageAddAsync(Uid, "later", fullpath, songfile, kind);
                }
                else
                {
                    await client.MypageRemoveAsync(Uid, "later", fullpath);
                }
            }
            return added;
        }

        /// <summary>保存/解除をトグルする。保存されたら true。</summary>
        public static async Task<bool> ToggleSavedSearchAsync(LocalMypage.SavedSearch search)
        {
            bool added = LocalMypage.ToggleSavedSearch(search);
            if (IsLinked)
            {
                var client = AppConfig.CreateClient();
                string keyword, type, prms;
                ToServerKeyword(search, out keyword, out type, out prms);
                if (added)
                {
                    await client.MypageKeywordAddAsync(Uid, keyword, type, prms);
                }
                else
                {
                    await client.MypageKeywordRemoveAsync(Uid, keyword, type, prms);
                }
            }
            return added;
        }

        public static async Task RemoveLaterAsync(string fullpath)
        {
            LocalMypage.RemoveLater(fullpath);
            if (IsLinked)
            {
                await AppConfig.CreateClient().MypageRemoveAsync(Uid, "later", fullpath);
            }
        }

        public static async Task RemoveFavoriteAsync(string fullpath)
        {
            LocalMypage.RemoveFavorite(fullpath);
            if (IsLinked)
            {
                await AppConfig.CreateClient().MypageRemoveAsync(Uid, "favorite", fullpath);
            }
        }

        public static async Task RemoveHistoryAsync(string fullpath)
        {
            LocalMypage.RemoveHistory(fullpath);
            if (IsLinked)
            {
                await AppConfig.CreateClient().MypageRemoveAsync(Uid, "history", fullpath);
            }
        }

        // ---- デバイスリンク ----

        /// <summary>
        /// ペアコードでこのサーバーにデバイスリンクする。
        /// 端末内データ (履歴・お気に入り曲・あとで歌う・お気に入り検索) を一括でサーバーへ
        /// 統合し、統合後のサーバー内容をローカルへミラーする。
        /// </summary>
        public static async Task LinkAsync(string code)
        {
            var client = AppConfig.CreateClient();
            string userid = await client.MypagePairApplyAsync(code);

            // 端末内データをサーバーへ一括統合 (import は冪等)
            await client.MypageImportAsync(userid, BuildExportJson());

            AppConfig.LinkedMypageUserId = userid;
            MarkSynced();

            // 統合後のサーバー内容をローカルへ取り込む (Web 側で貯めたデータがアプリでも使える)
            await GetFavoriteAsync();
            await GetLaterAsync();
            await GetSavedSearchesAsync();
        }

        /// <summary>このサーバーとのリンクを解除する (端末内データはそのまま残る)。</summary>
        public static void Unlink()
        {
            AppConfig.LinkedMypageUserId = "";
        }

        // ---- Google Drive 直接同期 ----
        // アプリが Google にログイン (GoogleAccount) し、Drive の mypage_data.json を
        // Web 版と同一形式で読み書きする。ローカルが一次データで、Drive はユーザー自身の
        // 保存場所 + 部屋をまたぐ持ち運び。同期は自動 (起動時 pull + ローカル変更時 push)。

        const string DriveSyncedAtKey = "yukanavi.drive_synced_at";
        static bool _pulledThisSession; // push は Drive の内容を取り込んだ後のみ (消失防止)
        static bool _applyingPull;      // pull の適用中はローカル変更フックを無視する
        static int _pushSerial;

        /// <summary>最後に Drive と同期した日時 (unixtime、0 = 未同期)。</summary>
        public static long LastDriveSyncAt
        {
            get
            {
                long at;
                return long.TryParse(PlayerPrefs.GetString(DriveSyncedAtKey, "0"), out at) ? at : 0;
            }
        }

        /// <summary>
        /// 起動時に呼ぶ。ローカル変更の自動 push フックを張り、ログイン済みなら
        /// Drive から取り込む (fire & forget)。
        /// </summary>
        public static void StartGoogleSync()
        {
            LocalMypage.Changed += OnLocalChanged;
            if (GoogleAccount.IsLoggedIn)
            {
                _ = PullFromDriveAsync(true);
            }
        }

        /// <summary>
        /// Drive の内容をローカルへ統合する (履歴 = 回数・日時の大きい方 / 曲・検索 = 重複回避で追加)。
        /// silent なら失敗を静かにスキップする (認証切れだけはログアウト + トースト)。
        /// </summary>
        public static async Task<bool> PullFromDriveAsync(bool silent)
        {
            try
            {
                string json = await GoogleDrive.DownloadAsync();
                if (json != null)
                {
                    _applyingPull = true;
                    try
                    {
                        ApplyDriveJson(json);
                    }
                    finally
                    {
                        _applyingPull = false;
                    }
                }
                _pulledThisSession = true; // Drive にファイルがまだ無い場合も push してよい
                MarkDriveSynced();
                return true;
            }
            catch (GoogleAuthException)
            {
                HandleAuthLost();
                if (!silent)
                {
                    throw;
                }
                return false;
            }
            catch (System.Exception)
            {
                if (!silent)
                {
                    throw;
                }
                return false; // 通信エラー等は次の変更か次回起動で追い付く
            }
        }

        /// <summary>ローカルデータで Drive を上書きする (手動「Driveへ保存」)。失敗は例外。</summary>
        public static async Task PushToDriveAsync()
        {
            try
            {
                await GoogleDrive.UploadAsync(BuildExportJson());
                _pulledThisSession = true; // 明示 push 後は自動 push も許可
                MarkDriveSynced();
            }
            catch (GoogleAuthException)
            {
                HandleAuthLost();
                throw;
            }
        }

        /// <summary>
        /// Google 連携データの削除 (App Store ガイドライン対応)。Drive 上の mypage_data.json を
        /// 削除し、トークンを失効させてログアウトする。Drive の削除に失敗したら中断して例外
        /// (ログイン状態は維持され、再試行できる)。失効の失敗は無視してログアウトまで進む。
        /// 端末内のデータは残る。
        /// </summary>
        public static async Task DeleteDriveDataAsync()
        {
            // 予約済みの自動 push を無効化し、次のログインで pull するまで push を止める
            // (削除後に debounce 中の push が走ると Drive にファイルを作り直してしまう)
            _pushSerial++;
            _pulledThisSession = false;
            try
            {
                await GoogleDrive.DeleteAsync();
            }
            catch (GoogleAuthException)
            {
                HandleAuthLost();
                throw;
            }
            await GoogleAccount.RevokeAndLogoutAsync();
            PlayerPrefs.DeleteKey(DriveSyncedAtKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// ローカル変更の自動 push。10秒の debounce で連続変更をまとめる。
        /// このセッションで Drive をまだ読んでいない間は push しない
        /// (取り込み前のローカルデータで Drive 側を上書きして消さないため)。
        /// </summary>
        static async void OnLocalChanged()
        {
            if (_applyingPull || !_pulledThisSession || !GoogleAccount.IsLoggedIn)
            {
                return;
            }
            int serial = ++_pushSerial;
            await Task.Delay(10000);
            // _pulledThisSession は待機中に連携データ削除で false に戻ることがあるため再確認
            if (serial != _pushSerial || !_pulledThisSession || !GoogleAccount.IsLoggedIn)
            {
                return;
            }
            try
            {
                await GoogleDrive.UploadAsync(BuildExportJson());
                MarkDriveSynced();
            }
            catch (GoogleAuthException)
            {
                HandleAuthLost();
            }
            catch (System.Exception)
            {
                // 次の変更か次回起動時に追い付く
            }
        }

        static void HandleAuthLost()
        {
            GoogleAccount.Logout();
            UiFactory.ShowToast("Google の認証が切れました。もう一度ログインしてください", true);
        }

        static void MarkDriveSynced()
        {
            PlayerPrefs.SetString(DriveSyncedAtKey,
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            PlayerPrefs.Save();
        }

        // Drive 上の mypage_data.json (Web 版エクスポート形式 version 1) の読み取り用
        class DriveData
        {
            [JsonProperty("history")] public List<DriveHistoryRow> History;
            [JsonProperty("later")] public List<DriveSongRow> Later;
            [JsonProperty("favorite_songs")] public List<DriveSongRow> FavoriteSongs;
            [JsonProperty("favorite_keywords")] public List<MypageKeywordDto> FavoriteKeywords;
        }

        class DriveHistoryRow
        {
            [JsonProperty("fullpath")] public string FullPath;
            [JsonProperty("songfile")] public string Songfile;
            [JsonProperty("kind")] public string Kind;
            [JsonProperty("requested_at")] public long RequestedAt;
        }

        class DriveSongRow
        {
            [JsonProperty("fullpath")] public string FullPath;
            [JsonProperty("songfile")] public string Songfile;
            [JsonProperty("kind")] public string Kind;
            [JsonProperty("added_at")] public long AddedAt;
        }

        static void ApplyDriveJson(string json)
        {
            var data = JsonConvert.DeserializeObject<DriveData>(json);
            if (data == null)
            {
                return;
            }
            if (data.History != null)
            {
                // 行形式 (1予約 = 1行) を曲ごとに集約してから統合する
                var byPath = new Dictionary<string, LocalMypage.Item>();
                foreach (var row in data.History)
                {
                    if (string.IsNullOrEmpty(row.FullPath))
                    {
                        continue;
                    }
                    LocalMypage.Item item;
                    if (!byPath.TryGetValue(row.FullPath, out item))
                    {
                        item = new LocalMypage.Item
                        {
                            FullPath = row.FullPath,
                            Songfile = row.Songfile,
                            Kind = row.Kind ?? "",
                        };
                        byPath[row.FullPath] = item;
                    }
                    item.Times++;
                    item.LastAt = System.Math.Max(item.LastAt, row.RequestedAt);
                }
                LocalMypage.MergeHistory(new List<LocalMypage.Item>(byPath.Values));
            }
            if (data.Later != null)
            {
                LocalMypage.MergeLater(ToItems(data.Later));
            }
            if (data.FavoriteSongs != null)
            {
                LocalMypage.MergeFavorite(ToItems(data.FavoriteSongs));
            }
            if (data.FavoriteKeywords != null)
            {
                var searches = new List<LocalMypage.SavedSearch>();
                foreach (var row in data.FavoriteKeywords)
                {
                    if (!string.IsNullOrEmpty(row.Keyword))
                    {
                        searches.Add(FromServerKeyword(row));
                    }
                }
                LocalMypage.MergeSavedSearches(searches);
            }
        }

        static List<LocalMypage.Item> ToItems(List<DriveSongRow> rows)
        {
            var items = new List<LocalMypage.Item>();
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.FullPath))
                {
                    continue;
                }
                items.Add(new LocalMypage.Item
                {
                    FullPath = row.FullPath,
                    Songfile = row.Songfile,
                    Kind = row.Kind ?? "",
                    AddedAt = row.AddedAt,
                });
            }
            return items;
        }

        // ---- お気に入り検索の相互変換 (アプリ SavedSearch ⇔ Web favorite_keyword) ----

        // Web の search_params は URL クエリ形式。完全一致検索は param に ListerDB のカラム名を入れる
        static readonly Dictionary<string, string> FieldToParam = new Dictionary<string, string>
        {
            { "artist", "artist" },
            { "program", "program_name" },
            { "group", "tie_up_group_name" },
            { "worker", "found_worker" },
        };

        /// <summary>アプリの保存検索を Web 版 favorite_keyword の形式に変換する。</summary>
        public static void ToServerKeyword(LocalMypage.SavedSearch search,
                                           out string keyword, out string type, out string prms)
        {
            // サーバー由来の条件は元の形式をそのまま返す (丸め直すと別条件として重複する)
            if (!string.IsNullOrEmpty(search.ServerType))
            {
                keyword = search.Kind == "exact" ? search.Value : search.Keyword;
                type = search.ServerType;
                prms = search.ServerParams ?? "";
                return;
            }
            switch (search.Kind)
            {
                case "everything":
                    keyword = search.Keyword;
                    type = "search";
                    prms = "";
                    break;
                case "exact":
                    keyword = search.Value;
                    type = "listerdb_songlist";
                    string param;
                    if (!FieldToParam.TryGetValue(search.Field ?? "", out param))
                    {
                        param = "anyword";
                    }
                    prms = "param=" + param + "&match=exact";
                    break;
                default: // lister (あいまい)
                    keyword = search.Keyword;
                    type = "listerdb_songlist";
                    prms = "param=anyword";
                    break;
            }
        }

        /// <summary>Web 版 favorite_keyword をアプリの保存検索に変換する。</summary>
        public static LocalMypage.SavedSearch FromServerKeyword(MypageKeywordDto item)
        {
            var search = new LocalMypage.SavedSearch
            {
                AddedAt = item.AddedAt,
                ServerType = item.SearchType,
                ServerParams = item.SearchParams,
            };
            string param = GetQueryValue(item.SearchParams, "param");
            if (item.SearchType == "search")
            {
                search.Kind = "everything";
                search.Keyword = item.Keyword;
                search.Label = item.Keyword;
                return search;
            }
            foreach (var pair in FieldToParam)
            {
                if (pair.Value == param)
                {
                    search.Kind = "exact";
                    search.Field = pair.Key;
                    search.Value = item.Keyword;
                    search.Label = FieldLabel(pair.Key) + ": " + item.Keyword;
                    return search;
                }
            }
            // それ以外 (anyword / song_name / Web 独自の種別) はあいまい検索として扱う
            search.Kind = "lister";
            search.Keyword = item.Keyword;
            search.Label = item.Keyword;
            return search;
        }

        static string FieldLabel(string field)
        {
            switch (field)
            {
                case "artist": return "歌手";
                case "program": return "作品";
                case "group": return "シリーズ";
                case "worker": return "制作";
                default: return field;
            }
        }

        static string GetQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query))
            {
                return "";
            }
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == key)
                {
                    return UnityEngine.Networking.UnityWebRequest.UnEscapeURL(pair.Substring(eq + 1));
                }
            }
            return "";
        }
    }
}
