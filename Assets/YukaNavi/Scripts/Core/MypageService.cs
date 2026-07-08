using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using YukaNavi.Api;

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
            PlayerPrefs.DeleteKey(AutoLinkOptOutKey()); // 手動リンク = 自動リンク拒否の解除
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
            // 明示的な解除なので、Google 持ち歩きによる自動リンクもこのサーバーでは止める
            // (再連携はペアコード入力か「Google 同期で連携する」の明示操作で)
            PlayerPrefs.SetInt(AutoLinkOptOutKey(), 1);
            PlayerPrefs.Save();
        }

        static string AutoLinkOptOutKey()
        {
            return "yukanavi.mypage_autolink_optout." + NormalizedServer();
        }

        // ---- Google 同期の持ち歩き (部屋をまたぐ自動引き継ぎ) ----

        const string GoogleCarryKey = "yukanavi.google_carry";
        static string _autoLinkTriedServer; // 失敗した部屋で毎回試さないためのメモ (起動中のみ)

        /// <summary>直近の自動リンク失敗時のサーバーメッセージ (「別の Google 連携設定」等)。</summary>
        public static string LastAutoLinkError { get; private set; }

        /// <summary>Google 同期を持ち歩いているか (トークンを端末に保存済みか)。</summary>
        public static bool HasGoogleCarry
        {
            get { return !string.IsNullOrEmpty(PlayerPrefs.GetString(GoogleCarryKey, "")); }
        }

        /// <summary>持ち歩き中の Google アカウント (メール)。未保持なら ""。</summary>
        public static string GoogleCarryEmail
        {
            get
            {
                var token = GetCarry();
                return token != null ? token.GoogleEmail ?? "" : "";
            }
        }

        static MypageGoogleTokenDto GetCarry()
        {
            string json = PlayerPrefs.GetString(GoogleCarryKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<MypageGoogleTokenDto>(json);
            }
            catch
            {
                return null;
            }
        }

        static void SetCarry(MypageGoogleTokenDto token)
        {
            PlayerPrefs.SetString(GoogleCarryKey, JsonConvert.SerializeObject(token));
            PlayerPrefs.Save();
        }

        /// <summary>持ち歩きをやめる (端末からトークンを消す)。</summary>
        public static void ClearGoogleCarry()
        {
            PlayerPrefs.DeleteKey(GoogleCarryKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// リンク済みの部屋から Google トークンを取得して端末に保存する (持ち歩き開始)。
        /// Google 未連携 (404) などで取得できなければ false。
        /// </summary>
        public static async Task<bool> FetchGoogleCarryAsync()
        {
            if (!IsLinked)
            {
                return false;
            }
            try
            {
                var token = await AppConfig.CreateClient().MypageGoogleTokenGetAsync(Uid);
                SetCarry(token);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 明示的に「Google 同期で連携する」を選んだとき: このサーバーの自動リンク拒否を
        /// 解除して連携を試みる。
        /// </summary>
        public static Task<bool> GoogleLinkNowAsync()
        {
            PlayerPrefs.DeleteKey(AutoLinkOptOutKey());
            PlayerPrefs.Save();
            _autoLinkTriedServer = null; // 同一起動内の再試行を許す
            return TryGoogleAutoLinkAsync();
        }

        /// <summary>
        /// 未リンクの部屋で、持ち歩きトークンによる自動リンクを試みる。
        /// 成功すると Drive から復元されたユーザーとリンクし、端末データも統合される。
        /// Google 同期未設定の部屋 (503) や通信エラーは静かにスキップして false。
        /// トークン無効 (401) は持ち歩きを破棄する (再連携が必要)。
        /// </summary>
        public static async Task<bool> TryGoogleAutoLinkAsync()
        {
            if (IsLinked)
            {
                return true;
            }
            if (PlayerPrefs.GetInt(AutoLinkOptOutKey(), 0) == 1)
            {
                return false; // このサーバーでは明示的に連携解除されている
            }
            var token = GetCarry();
            if (token == null || _autoLinkTriedServer == NormalizedServer())
            {
                return false;
            }
            LastAutoLinkError = null;
            try
            {
                var client = AppConfig.CreateClient();
                var result = await client.MypageGoogleRegisterAsync(token);
                if (result.Refreshed)
                {
                    token.AccessToken = result.AccessToken;
                    token.TokenExpiresAt = result.TokenExpiresAt;
                    SetCarry(token);
                }
                AppConfig.LinkedMypageUserId = result.UserId;
                // 端末データの統合とミラー (ペアコードでのリンクと同じ後処理)
                await client.MypageImportAsync(result.UserId, BuildExportJson());
                MarkSynced();
                await GetFavoriteAsync();
                await GetLaterAsync();
                await GetSavedSearchesAsync();
                return true;
            }
            catch (ApiException e)
            {
                _autoLinkTriedServer = NormalizedServer();
                LastAutoLinkError = e.Message;
                if (e.HttpStatus == 401)
                {
                    // トークン無効。持ち歩きを破棄する (画面側は HasGoogleCarry の変化で気づける)
                    ClearGoogleCarry();
                }
                return false;
            }
            catch (System.Exception)
            {
                _autoLinkTriedServer = NormalizedServer();
                return false;
            }
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
