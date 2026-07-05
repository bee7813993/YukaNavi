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

        /// <summary>タイムアウト秒</summary>
        public int TimeoutSeconds = 10;

        public ApiClient(string baseUrl)
        {
            BaseUrl = baseUrl;
        }

        /// <summary>機能フラグ取得。アプリ起動時に1回呼んで UI 出し分けに使う。</summary>
        public Task<CapabilitiesDto> GetCapabilitiesAsync()
            => GetApiAsync<CapabilitiesDto>("api/capabilities.php");

        /// <summary>ローカルファイル検索(生データ)。</summary>
        public Task<SearchResultDto> SearchAsync(string keyword)
            => GetApiAsync<SearchResultDto>(
                "api/search.php?keyword=" + UnityWebRequest.EscapeURL(keyword));

        /// <summary>/api/ エンベロープ応答を解釈して data を返す。</summary>
        async Task<T> GetApiAsync<T>(string path)
        {
            string json = await GetTextAsync(path);
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
        async Task<string> GetTextAsync(string path)
        {
            string url = BaseUrl.TrimEnd('/') + "/" + path;
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
                    throw new ApiException($"{req.error} ({url})");
                }
                return req.downloadHandler.text;
            }
        }
    }

    /// <summary>API 呼び出しの失敗 (通信エラー・サーバーエラー応答の両方)。</summary>
    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
    }
}
