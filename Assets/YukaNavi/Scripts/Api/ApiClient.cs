using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace YukaNavi.Api
{
    /// <summary>
    /// ゆかりサーバーの /api/ を呼ぶ薄い HTTP クライアント。
    /// エンベロープ {"ok", "data"|"error"} を解釈して data を返す。
    /// 仕様: KaraokeRequestorWeb/api/README.md
    /// </summary>
    public class ApiClient
    {
        /// <summary>例: "http://localhost/"</summary>
        public string BaseUrl { get; }

        /// <summary>easyauth の認証キーワード (不要なら null/空)。クエリ easypass= として送る。</summary>
        public string EasyPass;

        /// <summary>マイページのユーザー ID (Cookie YkariUserID として送る)。</summary>
        public string UserId;

        /// <summary>歌う人の名前 (Cookie YkariUsername として送る)。</summary>
        public string Username;

        /// <summary>タイムアウト秒</summary>
        public int TimeoutSeconds = 10;

        public ApiClient(string baseUrl, string easyPass = null)
        {
            BaseUrl = baseUrl;
            EasyPass = easyPass;
        }

        /// <summary>サーバー情報 (認証不要)。接続設定画面の疎通確認に使う。旧サーバーは 404。</summary>
        public Task<ServerInfoDto> GetServerInfoAsync()
            => GetApiAsync<ServerInfoDto>("api/server_info.php", false);

        /// <summary>機能フラグ取得。アプリ起動時に1回呼んで UI 出し分けに使う。</summary>
        public Task<CapabilitiesDto> GetCapabilitiesAsync()
            => GetApiAsync<CapabilitiesDto>("api/capabilities.php");

        /// <summary>ローカルファイル検索(生データ)。</summary>
        public Task<SearchResultDto> SearchAsync(string keyword)
            => GetApiAsync<SearchResultDto>(
                "api/search.php?keyword=" + UnityWebRequest.EscapeURL(keyword));

        /// <summary>
        /// ListerDB (アニソンDB) のなんでも検索。曲名・歌手・作品名・シリーズ等を横断する
        /// あいまい検索で、結果は曲単位。サーバーに ListerDB が未設定の場合は ApiException。
        /// </summary>
        public async Task<ListerSearchResultDto> SearchListerAsync(string keyword, int limit = 100)
        {
            string path = "search_listerdb_songlist_json.php?anyword=" + UnityWebRequest.EscapeURL(keyword)
                + "&length=" + limit + "&orderby=song_name&scending=ASC";
            string json = await GetTextAsync(path, true);
            if (string.IsNullOrWhiteSpace(json))
            {
                // ListerDB 未設定・DB 破損時は空応答が返る
                throw new ApiException("サーバーにアニソンDB (ListerDB) が設定されていないようです");
            }
            try
            {
                var result = JsonConvert.DeserializeObject<ListerSearchResultDto>(json);
                if (result == null)
                {
                    throw new ApiException("アニソンDB応答の解釈に失敗");
                }
                return result;
            }
            catch (JsonException)
            {
                throw new ApiException("アニソンDB応答の解釈に失敗");
            }
        }

        /// <summary>期別インデックス: 年ごとの曲数。</summary>
        public Task<ListerYearsDto> GetListerYearsAsync()
            => GetApiAsync<ListerYearsDto>("api/lister_index.php?mode=years");

        /// <summary>期別インデックス: 指定年の期ごとの曲数・作品数。</summary>
        public Task<ListerQuartersDto> GetListerQuartersAsync(int year)
            => GetApiAsync<ListerQuartersDto>("api/lister_index.php?mode=quarters&year=" + year);

        /// <summary>期別インデックス: 期内の作品一覧。quarter: 1=冬 2=春 3=夏 4=秋。</summary>
        public Task<ListerProgramsDto> GetListerProgramsAsync(int year, int quarter)
            => GetApiAsync<ListerProgramsDto>(
                "api/lister_index.php?mode=programs&year=" + year + "&quarter=" + quarter);

        /// <summary>シリーズ内の作品一覧 (シリーズでの再検索用)。</summary>
        public Task<ListerProgramsDto> GetListerGroupProgramsAsync(string group)
            => GetApiAsync<ListerProgramsDto>(
                "api/lister_index.php?mode=programs&group=" + UnityWebRequest.EscapeURL(group));

        /// <summary>頭文字インデックス: 頭文字ごとの名前数。target: program | artist | group。</summary>
        public Task<ListerInitialsDto> GetListerInitialsAsync(string target)
            => GetApiAsync<ListerInitialsDto>(
                "api/lister_index.php?mode=initials&target=" + target);

        /// <summary>
        /// 頭文字インデックス: 名前一覧。initial (頭文字) か keyword (部分一致) のどちらかで絞る。
        /// </summary>
        public Task<ListerNamesDto> GetListerNamesAsync(string target, string initial = null, string keyword = null)
        {
            string path = "api/lister_index.php?mode=names&target=" + target;
            if (!string.IsNullOrEmpty(initial))
            {
                path += "&initial=" + UnityWebRequest.EscapeURL(initial);
            }
            else if (!string.IsNullOrEmpty(keyword))
            {
                path += "&keyword=" + UnityWebRequest.EscapeURL(keyword);
            }
            return GetApiAsync<ListerNamesDto>(path);
        }

        /// <summary>
        /// ListerDB の曲検索 (曲単位グルーピング、同じ曲の複数動画は Files に並ぶ)。
        /// program / artist / group / worker は完全一致 (AND)、anyword はあいまい検索。
        /// order: date_desc (既定。ファイル更新日の新しい順) / date_asc / name (曲名順)。
        /// </summary>
        public Task<ListerIndexSongsDto> GetListerIndexSongsAsync(
            string program = null, string artist = null, string group = null, string worker = null,
            string anyword = null, string order = null, bool flat = false)
        {
            string path = "api/lister_index.php?mode=songs";
            if (!string.IsNullOrEmpty(order))
            {
                path += "&order=" + order;
            }
            if (flat)
            {
                path += "&flat=1"; // グルーピングせずファイル単位で返す
            }
            if (!string.IsNullOrEmpty(program))
            {
                path += "&program=" + UnityWebRequest.EscapeURL(program);
            }
            if (!string.IsNullOrEmpty(artist))
            {
                path += "&artist=" + UnityWebRequest.EscapeURL(artist);
            }
            if (!string.IsNullOrEmpty(group))
            {
                path += "&group=" + UnityWebRequest.EscapeURL(group);
            }
            if (!string.IsNullOrEmpty(worker))
            {
                path += "&worker=" + UnityWebRequest.EscapeURL(worker);
            }
            if (!string.IsNullOrEmpty(anyword))
            {
                path += "&anyword=" + UnityWebRequest.EscapeURL(anyword);
            }
            return GetApiAsync<ListerIndexSongsDto>(path);
        }

        /// <summary>
        /// ユーザー識別 Cookie (YkariUserID / YkariUsername) を付与する。
        /// これにより予約が Web 版と同じ仕組みでユーザーの履歴に紐づく。
        /// </summary>
        void ApplyCookies(UnityWebRequest req)
        {
            if (string.IsNullOrEmpty(UserId))
            {
                return;
            }
            // Unity 内蔵のクッキーエンジンが Set-Cookie (exec.php の YkariUsername 等) を
            // 記憶して自動付与し、明示指定の Cookie ヘッダーとカンマ結合されて
            // 「ゆーふ, YkariUserID=...」のような壊れた名前になるため、毎回クリアする
            UnityWebRequest.ClearCookieCache(new System.Uri(BaseUrl));
            string cookie = "YkariUserID=" + UserId;
            if (!string.IsNullOrEmpty(Username))
            {
                cookie += "; YkariUsername=" + UnityWebRequest.EscapeURL(Username);
            }
            req.SetRequestHeader("Cookie", cookie);
        }

        /// <summary>予約一覧。limit=0 で全件。</summary>
        public Task<RequestListDto> GetRequestsAsync(int limit = 0, int offset = 0)
        {
            string path = "api/requests.php";
            if (limit > 0)
            {
                path += "?limit=" + limit + "&offset=" + offset;
            }
            return GetApiAsync<RequestListDto>(path);
        }

        /// <summary>再生状態。停止中は Playing=false のみ。</summary>
        public Task<NowPlayingDto> GetNowPlayingAsync()
            => GetApiAsync<NowPlayingDto>("api/nowplaying.php");

        /// <summary>予約オプション (exec.php に渡す任意項目)。</summary>
        public class RequestOptions
        {
            /// <summary>キー変更 (-6〜+6、0 = 変更なし)</summary>
            public int Keychange;
            /// <summary>シークレット予約</summary>
            public bool Secret;
            /// <summary>BGV モード (exec.php 側で kind がカラオケ配信になる)</summary>
            public bool Loop;
            /// <summary>別プレイヤー再生 (exec.php 側で kind が 動画_別プ になる)</summary>
            public bool OtherPlayer;
            /// <summary>この曲の後に小休止</summary>
            public bool Pause;
            /// <summary>音ズレ補正 ms (-9900〜9900)</summary>
            public int AudioDelay;
            /// <summary>音量増減 % (-100〜100、0 = 変更なし)</summary>
            public int Volume;
            /// <summary>音声トラック (0 = 1トラック目)</summary>
            public int Track;
            /// <summary>曲の長さ (秒、0 = 不明)。残り時間の計算に使われる</summary>
            public int Duration;
            /// <summary>既存予約の差し替え先 id (負なら新規)</summary>
            public int SelectId = -1;
        }

        /// <summary>
        /// 予約投稿 (exec.php の XHR モード)。成功で新しい予約 ID を返す。
        /// 注意: exec.php は不正入力を HTTP 200 + 空応答で拒否する (api/README.md 参照)。
        /// </summary>
        public async Task<int> PostRequestAsync(string filename, string fullpath, string singerName,
                                                string comment, string kind = "動画",
                                                RequestOptions options = null)
        {
            var form = new UnityEngine.WWWForm();
            form.AddField("filename", filename);
            form.AddField("fullpath", fullpath);
            form.AddField("singer", "");
            form.AddField("freesinger", singerName);
            form.AddField("comment", comment ?? "");
            form.AddField("kind", kind);
            if (options != null)
            {
                if (options.Keychange != 0)
                {
                    form.AddField("keychange", options.Keychange.ToString());
                }
                if (options.Secret)
                {
                    form.AddField("secret", "1");
                }
                if (options.Loop)
                {
                    form.AddField("loop", "1");
                }
                if (options.OtherPlayer)
                {
                    form.AddField("otherplayer", "1");
                }
                if (options.Pause)
                {
                    form.AddField("pause", "1");
                }
                if (options.AudioDelay != 0)
                {
                    form.AddField("audiodelay", options.AudioDelay.ToString());
                }
                if (options.Volume != 0)
                {
                    form.AddField("volume", options.Volume.ToString());
                }
                if (options.Track > 0)
                {
                    form.AddField("track", options.Track.ToString());
                }
                if (options.Duration > 0)
                {
                    form.AddField("duration", options.Duration.ToString());
                }
                if (options.SelectId >= 0)
                {
                    form.AddField("selectid", options.SelectId.ToString());
                }
            }

            string url = BaseUrl.TrimEnd('/') + "/exec.php";
            if (!string.IsNullOrEmpty(EasyPass))
            {
                url += "?easypass=" + UnityWebRequest.EscapeURL(EasyPass);
            }
            using (var req = UnityWebRequest.Post(url, form))
            {
                req.SetRequestHeader("X-Requested-With", "XMLHttpRequest");
                req.timeout = TimeoutSeconds;
                ApplyCookies(req);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new ApiException($"{req.error} ({url})", (int)req.responseCode);
                }
                // 応答は {"newid":N}。先頭に改行が付くため trim する
                string text = (req.downloadHandler.text ?? "").Trim();
                if (text == "")
                {
                    throw new ApiException("予約が受け付けられませんでした (入力内容を確認してください)");
                }
                try
                {
                    var result = JsonConvert.DeserializeObject<NewRequestResult>(text);
                    return result.NewId;
                }
                catch (JsonException)
                {
                    string head = text.Length > 80 ? text.Substring(0, 80) : text;
                    throw new ApiException("予約応答の解釈に失敗: " + head);
                }
            }
        }

        class NewRequestResult
        {
            [JsonProperty("newid")] public int NewId;
        }

        /// <summary>
        /// プレイヤー統一制御 (/api/player.php)。プレイヤー種別はサーバー側で自動判定される。
        /// 非対応の操作はサーバーが 501 を返す (ApiException.HttpStatus == 501)。
        /// </summary>
        public Task<PlayerActionDto> PlayerActionAsync(string action, string extraQuery = null)
        {
            string path = "api/player.php?action=" + action;
            if (!string.IsNullOrEmpty(extraQuery))
            {
                path += "&" + extraQuery;
            }
            return GetApiAsync<PlayerActionDto>(path);
        }

        /// <summary>
        /// ファイル詳細 (音声トラック一覧 + 動画詳細情報)。予約確認画面用。
        /// 解析結果はサーバー側でキャッシュされる。
        /// </summary>
        public Task<FileDetailsDto> GetFileDetailsAsync(string fullpath)
            => GetApiAsync<FileDetailsDto>(
                "api/file_details.php?fullpath=" + UnityWebRequest.EscapeURL(fullpath));

        /// <summary>
        /// 音声トラックの一覧を取得する (動画ファイルのみ有効)。
        /// 戻り値はトラックのラベル一覧。空 = 判別できない (mp4 以外・解析失敗)。
        /// </summary>
        public async Task<System.Collections.Generic.List<string>> GetTrackListAsync(string fullpath)
        {
            var labels = new System.Collections.Generic.List<string>();
            try
            {
                string json = await GetTextAsync(
                    "gettracklist_json.php?fullpath=" + UnityWebRequest.EscapeURL(fullpath), true);
                // 旧サーバーでは JSON の前に PHP Warning が混入することがあるため '[' 以降を読む
                int start = json.IndexOf('[');
                if (start < 0)
                {
                    return labels;
                }
                var arr = Newtonsoft.Json.Linq.JArray.Parse(json.Substring(start));
                foreach (var item in arr)
                {
                    // 各要素は [トラック種別, ラベル] の配列
                    string label = "";
                    if (item is Newtonsoft.Json.Linq.JArray inner && inner.Count > 1)
                    {
                        label = inner[1]?.ToString() ?? "";
                    }
                    labels.Add(label);
                }
            }
            catch
            {
                labels.Clear();
            }
            return labels;
        }

        /// <summary>
        /// 予約に入っている歌う人の一覧。Cookie の YkariUsername (自分の名前) が先頭に入る。
        /// </summary>
        public async Task<System.Collections.Generic.List<string>> GetSingerListAsync()
        {
            var singers = new System.Collections.Generic.List<string>();
            try
            {
                string json = await GetTextAsync("getsingerlist_json.php", true);
                int start = json.IndexOf('[');
                if (start < 0)
                {
                    return singers;
                }
                var arr = Newtonsoft.Json.Linq.JArray.Parse(json.Substring(start));
                foreach (var item in arr)
                {
                    string name = item?["singer"]?.ToString() ?? "";
                    if (name != "" && !singers.Contains(name))
                    {
                        singers.Add(name);
                    }
                }
            }
            catch
            {
            }
            return singers;
        }

        /// <summary>
        /// 予約の個別移動。action は up / down / warikomi (次に再生)。
        /// 戻り値は情報メッセージ (「すでに一番上です。」等。空 = 正常に移動)。
        /// </summary>
        public async Task<string> MoveRequestAsync(int id, string action)
        {
            var data = await GetApiAsync<MoveResult>($"api/request_move.php?id={id}&action={action}");
            return data.Message ?? "";
        }

        /// <summary>予約の削除。</summary>
        public async Task DeleteRequestAsync(int id)
        {
            await GetApiAsync<DeleteResult>("api/request_delete.php?id=" + id);
        }

        // ---- マイページ連携 (/api/mypage.php) ----

        static string Esc(string value)
        {
            return UnityWebRequest.EscapeURL(value ?? "");
        }

        /// <summary>デバイスリンクのペアコードを適用し、Web 版のマイページ userid を得る。</summary>
        public async Task<string> MypagePairApplyAsync(string code)
        {
            var data = await GetApiAsync<MypagePairDto>(
                "api/mypage.php?action=pair_apply&code=" + Esc(code));
            return data.UserId;
        }

        /// <summary>マイページの件数と Google 連携有無。</summary>
        public Task<MypageSummaryDto> MypageSummaryAsync(string userid)
            => GetApiAsync<MypageSummaryDto>(
                "api/mypage.php?action=summary&userid=" + Esc(userid));

        /// <summary>一覧取得。list は "history" / "later" / "favorite"。</summary>
        public Task<MypageItemsDto> MypageListAsync(string userid, string list)
            => GetApiAsync<MypageItemsDto>(
                $"api/mypage.php?action={list}&userid=" + Esc(userid));

        /// <summary>曲の追加。list は "history" / "later" / "favorite"。</summary>
        public Task MypageAddAsync(string userid, string list, string fullpath,
                                   string songfile, string kind)
            => GetApiAsync<object>($"api/mypage.php?action={list}_add&userid=" + Esc(userid)
                + "&fullpath=" + Esc(fullpath) + "&songfile=" + Esc(songfile)
                + "&kind=" + Esc(kind));

        /// <summary>曲の削除。list は "history" / "later" / "favorite"。</summary>
        public Task MypageRemoveAsync(string userid, string list, string fullpath)
            => GetApiAsync<object>($"api/mypage.php?action={list}_remove&userid=" + Esc(userid)
                + "&fullpath=" + Esc(fullpath));

        /// <summary>お気に入り検索の一覧。</summary>
        public Task<MypageKeywordsDto> MypageKeywordsAsync(string userid)
            => GetApiAsync<MypageKeywordsDto>(
                "api/mypage.php?action=keyword&userid=" + Esc(userid));

        /// <summary>お気に入り検索の追加。</summary>
        public Task MypageKeywordAddAsync(string userid, string keyword,
                                          string searchType, string searchParams)
            => GetApiAsync<object>("api/mypage.php?action=keyword_add&userid=" + Esc(userid)
                + "&keyword=" + Esc(keyword) + "&search_type=" + Esc(searchType)
                + "&search_params=" + Esc(searchParams));

        /// <summary>お気に入り検索の削除 (条件一致)。</summary>
        public Task MypageKeywordRemoveAsync(string userid, string keyword,
                                             string searchType, string searchParams)
            => GetApiAsync<object>("api/mypage.php?action=keyword_remove&userid=" + Esc(userid)
                + "&keyword=" + Esc(keyword) + "&search_type=" + Esc(searchType)
                + "&search_params=" + Esc(searchParams));

        /// <summary>
        /// 端末内データの一括統合 (Web 版エクスポート形式 version 1 の JSON を送る)。
        /// サーバー側で重複はスキップされるため何度実行しても安全。
        /// </summary>
        public Task MypageImportAsync(string userid, string json)
        {
            var form = new UnityEngine.WWWForm();
            form.AddField("action", "import");
            form.AddField("userid", userid);
            form.AddField("data", json);
            return PostApiAsync<object>("api/mypage.php", form);
        }

        /// <summary>予約1件の再生状況変更 (「未再生」「再生済」等。/api/playstatus.php)。</summary>
        public async Task SetPlayStatusAsync(int id, string nowplaying)
        {
            await GetApiAsync<PlayStatusResult>("api/playstatus.php?id=" + id
                + "&nowplaying=" + UnityWebRequest.EscapeURL(nowplaying));
        }

        class PlayStatusResult
        {
            [JsonProperty("nowplaying")] public string Nowplaying;
        }

        /// <summary>
        /// 予約へのコメント追記 (commentedit.php)。Web 版と同じく既存コメントの末尾に
        /// 「>> コメント by 名前」の形式で追記される (再生中の曲ならニコ風コメントにも流れる)。
        /// </summary>
        public async Task AddRequestCommentAsync(int id, string comment, string name)
        {
            string path = "commentedit.php?id=" + id
                + "&addcomment=" + UnityWebRequest.EscapeURL(comment)
                + "&name=" + UnityWebRequest.EscapeURL(name ?? "");
            string html = await GetTextAsync(path, true);
            if (html != null && (html.Contains("wrong id") || html.Contains("nodata")))
            {
                throw new ApiException("コメントを追加できませんでした (予約が見つかりません)");
            }
        }

        /// <summary>
        /// 予約の並べ替え (requestlist_reorder.php)。表示順 (上から) の id 配列を送る。
        /// サーバー側で未再生のみが並べ替えられ、再生中・再生済みの順序は保持される。
        /// </summary>
        public async Task ReorderRequestsAsync(System.Collections.Generic.List<int> ids)
        {
            string url = BaseUrl.TrimEnd('/') + "/requestlist_reorder.php";
            if (!string.IsNullOrEmpty(EasyPass))
            {
                url += "?easypass=" + UnityWebRequest.EscapeURL(EasyPass);
            }
            string json = JsonConvert.SerializeObject(new ReorderBody { Ids = ids });
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = TimeoutSeconds;
                ApplyCookies(req);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new ApiException($"{req.error} ({url})", (int)req.responseCode);
                }
                var result = JsonConvert.DeserializeObject<ReorderResult>(req.downloadHandler.text ?? "");
                if (result == null || result.Status != "ok")
                {
                    throw new ApiException("並べ替えに失敗: " + (result?.Message ?? "不明なエラー"));
                }
            }
        }

        class ReorderBody
        {
            [JsonProperty("ids")] public System.Collections.Generic.List<int> Ids;
        }

        class ReorderResult
        {
            [JsonProperty("status")] public string Status;
            [JsonProperty("message")] public string Message;
        }

        class MoveResult
        {
            [JsonProperty("message")] public string Message;
        }

        class DeleteResult
        {
            [JsonProperty("deleted")] public bool Deleted;
        }

        /// <summary>/api/ エンベロープ応答を解釈して data を返す。</summary>
        async Task<T> GetApiAsync<T>(string path, bool withAuth = true)
        {
            string json = await GetTextAsync(path, withAuth);
            return ParseEnvelope<T>(json);
        }

        static T ParseEnvelope<T>(string json)
        {
            ApiEnvelope<T> env;
            try
            {
                env = JsonConvert.DeserializeObject<ApiEnvelope<T>>(json);
            }
            catch (JsonException e)
            {
                throw new ApiException("JSON 解釈に失敗: " + e.Message);
            }
            if (env == null || !env.Ok)
            {
                throw new ApiException(string.IsNullOrEmpty(env?.Error) ? "サーバーがエラー応答 (詳細なし)" : env.Error);
            }
            return env.Data;
        }

        /// <summary>POST してエンベロープを解釈する (大きなデータの送信用)。</summary>
        async Task<T> PostApiAsync<T>(string path, UnityEngine.WWWForm form)
        {
            string url = BaseUrl.TrimEnd('/') + "/" + path;
            if (!string.IsNullOrEmpty(EasyPass))
            {
                url += (url.IndexOf('?') >= 0 ? "&" : "?")
                    + "easypass=" + UnityWebRequest.EscapeURL(EasyPass);
            }
            using (var req = UnityWebRequest.Post(url, form))
            {
                req.timeout = TimeoutSeconds;
                ApplyCookies(req);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new ApiException($"{req.error} ({url})", (int)req.responseCode);
                }
                return ParseEnvelope<T>(req.downloadHandler.text);
            }
        }

        /// <summary>GET してテキストを返す。エディタ/実行時どちらでも動くよう Task.Yield でポーリングする。</summary>
        async Task<string> GetTextAsync(string path, bool withAuth)
        {
            string url = BaseUrl.TrimEnd('/') + "/" + path;
            if (withAuth && !string.IsNullOrEmpty(EasyPass))
            {
                url += (url.IndexOf('?') >= 0 ? "&" : "?") + "easypass=" + UnityWebRequest.EscapeURL(EasyPass);
            }
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                ApplyCookies(req);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new ApiException($"{req.error} ({url})", (int)req.responseCode);
                }
                return req.downloadHandler.text;
            }
        }
    }

    /// <summary>API 呼び出しの失敗 (通信エラー・サーバーエラー応答の両方)。</summary>
    public class ApiException : Exception
    {
        /// <summary>HTTP ステータスコード (通信自体の失敗やアプリ層エラーは 0)。</summary>
        public int HttpStatus;

        public ApiException(string message, int httpStatus = 0) : base(message)
        {
            HttpStatus = httpStatus;
        }
    }
}
