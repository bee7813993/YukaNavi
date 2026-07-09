using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;
using YukaNavi.Api;

namespace YukaNavi.Core
{
    /// <summary>
    /// Google Drive の appDataFolder にある mypage_data.json の読み書き。
    /// Web 版 (KaraokeRequestorWeb の mypage_google_drive.php) と同一ファイル・
    /// 同一形式 (version 1) で、Web 版の Google 連携と同じデータを共有する。
    /// トークンは GoogleAccount が管理する。
    /// </summary>
    public static class GoogleDrive
    {
        const string FileName = "mypage_data.json";
        const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
        const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";

        /// <summary>Drive のマイページデータ (JSON) を読む。ファイルがまだ無ければ null。</summary>
        public static async Task<string> DownloadAsync()
        {
            string token = await GoogleAccount.GetAccessTokenAsync();
            string id = await FindFileIdAsync(token);
            if (id == null)
            {
                return null;
            }
            return await RequestAsync("GET",
                FilesUrl + "/" + UnityWebRequest.EscapeURL(id) + "?alt=media", null, null, token);
        }

        /// <summary>Drive のマイページデータを書く (ファイルが無ければ作る)。</summary>
        public static async Task UploadAsync(string json)
        {
            string token = await GoogleAccount.GetAccessTokenAsync();
            string id = await FindFileIdAsync(token);
            if (id != null)
            {
                await RequestAsync("PATCH",
                    UploadUrl + "/" + UnityWebRequest.EscapeURL(id) + "?uploadType=media",
                    json, "application/json", token);
            }
            else
            {
                // 新規作成は multipart (メタデータ + 本体)
                string boundary = "mpboundary_" + System.Guid.NewGuid().ToString("N");
                string meta = "{\"name\":\"" + FileName + "\",\"parents\":[\"appDataFolder\"]}";
                string body = "--" + boundary
                    + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n" + meta + "\r\n"
                    + "--" + boundary + "\r\nContent-Type: application/json\r\n\r\n" + json + "\r\n"
                    + "--" + boundary + "--";
                await RequestAsync("POST", UploadUrl + "?uploadType=multipart",
                    body, "multipart/related; boundary=" + boundary, token);
            }
        }

        static async Task<string> FindFileIdAsync(string token)
        {
            string url = FilesUrl + "?spaces=appDataFolder"
                + "&q=" + UnityWebRequest.EscapeURL("name='" + FileName + "'")
                + "&fields=" + UnityWebRequest.EscapeURL("files(id)");
            string resp = await RequestAsync("GET", url, null, null, token);
            FileList data = null;
            try
            {
                data = JsonConvert.DeserializeObject<FileList>(resp);
            }
            catch (JsonException)
            {
            }
            return (data?.Files != null && data.Files.Count > 0) ? data.Files[0].Id : null;
        }

        class FileList
        {
            [JsonProperty("files")] public List<FileEntry> Files;
        }

        class FileEntry
        {
            [JsonProperty("id")] public string Id;
        }

        static async Task<string> RequestAsync(string method, string url, string body,
                                               string contentType, string accessToken)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(
                        System.Text.Encoding.UTF8.GetBytes(body));
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + accessToken);
                if (contentType != null)
                {
                    req.SetRequestHeader("Content-Type", contentType);
                }
                req.timeout = 20;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result == UnityWebRequest.Result.ConnectionError
                    || req.result == UnityWebRequest.Result.DataProcessingError)
                {
                    throw new ApiException("Google Drive に接続できません: " + req.error);
                }
                if (req.responseCode == 401)
                {
                    // アクセストークンが取り消された等 (期限切れは GetAccessTokenAsync が更新済み)
                    throw new GoogleAuthException("Google の認証が切れています");
                }
                if (req.responseCode >= 400)
                {
                    throw new ApiException("Google Drive エラー (HTTP " + req.responseCode + ")",
                        (int)req.responseCode);
                }
                return req.downloadHandler.text;
            }
        }
    }
}
