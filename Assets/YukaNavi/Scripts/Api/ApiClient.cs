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

        /// <summary>
        /// 予約投稿 (exec.php の XHR モード)。成功で新しい予約 ID を返す。
        /// 注意: exec.php は不正入力を HTTP 200 + 空応答で拒否する (api/README.md 参照)。
        /// </summary>
        public async Task<int> PostRequestAsync(string filename, string fullpath, string singerName,
                                                string comment, string kind = "動画", bool secret = false)
        {
            var form = new UnityEngine.WWWForm();
            form.AddField("filename", filename);
            form.AddField("fullpath", fullpath);
            form.AddField("singer", "");
            form.AddField("freesinger", singerName);
            form.AddField("comment", comment ?? "");
            form.AddField("kind", kind);
            if (secret)
            {
                form.AddField("secret", "1");
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
