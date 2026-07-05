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
