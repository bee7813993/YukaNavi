using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using YukaNavi.Api;

namespace YukaNavi.Core
{
    /// <summary>
    /// Google の認証が切れている (再ログインが必要)。一時的な通信エラーとは区別され、
    /// これを受けた側はログアウト扱いにする。
    /// </summary>
    public class GoogleAuthException : System.Exception
    {
        public GoogleAuthException(string message) : base(message) { }
    }

    /// <summary>
    /// アプリ自身の Google ログイン。認証は中継サーバー (relay) の app_auth フローで
    /// ブラウザ側で行い、トークンは端末 (PlayerPrefs) にだけ保存する。
    /// Client ID / Secret は relay が管理し、アプリは relay の URL しか知らない
    /// (仕様: KaraokeRequestorWeb の mypage_google_relay_server.php)。
    /// </summary>
    public static class GoogleAccount
    {
        const string TokenKey = "yukanavi.google_account";

        class TokenData
        {
            [JsonProperty("google_sub")] public string GoogleSub;
            [JsonProperty("google_email")] public string GoogleEmail;
            [JsonProperty("access_token")] public string AccessToken;
            [JsonProperty("refresh_token")] public string RefreshToken;
            [JsonProperty("expires_at")] public long ExpiresAt;
        }

        class PollResult
        {
            [JsonProperty("status")] public string Status;
            [JsonProperty("token")] public TokenData Token;
        }

        class RefreshResult
        {
            [JsonProperty("access_token")] public string AccessToken;
            [JsonProperty("expires_in")] public long ExpiresIn;
        }

        static TokenData _cached;
        static bool _cacheLoaded;
        static bool _loginInProgress;
        static bool _cancelRequested;

        /// <summary>ログイン済みか (トークンを端末に保持しているか)。</summary>
        public static bool IsLoggedIn
        {
            get { return Load() != null; }
        }

        /// <summary>ログイン中の Google アカウント (メール)。未ログインなら ""。</summary>
        public static string Email
        {
            get
            {
                var token = Load();
                return token != null ? token.GoogleEmail ?? "" : "";
            }
        }

        /// <summary>LoginAsync のブラウザ認証待ちの最中か (画面の表示切替用)。</summary>
        public static bool IsLoginInProgress
        {
            get { return _loginInProgress; }
        }

        /// <summary>直近のログイン失敗の理由 (成功・中止時は null)。</summary>
        public static string LastLoginError { get; private set; }

        /// <summary>進行中のブラウザ認証待ちを中止する。</summary>
        public static void CancelLogin()
        {
            _cancelRequested = true;
        }

        /// <summary>ログアウトする (端末からトークンを消す。Drive 上のデータは残る)。</summary>
        public static void Logout()
        {
            _cached = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
        }

        const string RevokeUrl = "https://oauth2.googleapis.com/revoke";

        /// <summary>
        /// Google のトークンを失効させてからログアウトする (連携データ削除用)。
        /// 失効はベストエフォート: 通信エラーや失効済み (HTTP 400) でも必ずローカルの
        /// トークンは消す。
        /// </summary>
        public static async Task RevokeAndLogoutAsync()
        {
            var token = Load();
            string target = token == null ? null
                : (!string.IsNullOrEmpty(token.RefreshToken) ? token.RefreshToken : token.AccessToken);
            if (!string.IsNullOrEmpty(target))
            {
                try
                {
                    await HttpAsync("POST", RevokeUrl + "?token=" + UnityWebRequest.EscapeURL(target));
                }
                catch (System.Exception)
                {
                    // 失効に失敗してもローカルのログアウトは続行する
                }
            }
            Logout();
        }

        /// <summary>
        /// ブラウザ (iOS はアプリ内シート) で Google ログインする。relay の認証ページを開き、
        /// 完了を app_poll のポーリングで待つ (2秒間隔・最大5分)。成功でトークンを端末に
        /// 保存して true。中止・タイムアウト・失敗で false (理由は LastLoginError)。
        /// </summary>
        public static async Task<bool> LoginAsync()
        {
            if (_loginInProgress)
            {
                return false;
            }
            _loginInProgress = true;
            _cancelRequested = false;
            LastLoginError = null;
            try
            {
                string session = NewSession();
                string relay = AppConfig.GoogleRelayUrl;
                if (!InAppBrowser.Open(relay + "?action=app_auth&session=" + session))
                {
                    LastLoginError = "認証ページを開けませんでした。中継サーバー URL を確認してください";
                    return false;
                }

                long deadline = Now() + 300;
                long sheetClosedAt = 0; // iOS: ユーザーがシートを閉じた時刻 (0 = 閉じていない)
                while (Now() < deadline)
                {
                    await Task.Delay(2000);
                    if (_cancelRequested)
                    {
                        return false;
                    }
                    // iOS: ユーザーがアプリ内シートを自分で閉じた → 猶予付きで中止扱いにする。
                    // 認証完了と同時に閉じた場合の取りこぼしを防ぐため、猶予内はポーリングを
                    // 続け、その間に "ok" が来ればそのまま成功にする
                    if (InAppBrowser.ConsumeUserClosed())
                    {
                        sheetClosedAt = Now();
                    }
                    if (sheetClosedAt != 0 && Now() >= sheetClosedAt + 5)
                    {
                        return false; // 中止扱い (LastLoginError = null → 呼び出し側は中止表示)
                    }
                    string json;
                    try
                    {
                        json = await HttpAsync("GET", relay + "?action=app_poll&session=" + session);
                    }
                    catch (System.Exception)
                    {
                        continue; // 一時的な通信エラーは無視して待ち続ける
                    }
                    PollResult result;
                    try
                    {
                        result = JsonConvert.DeserializeObject<PollResult>(json);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    if (result == null || result.Status == "pending")
                    {
                        continue;
                    }
                    if (result.Status == "ok" && result.Token != null
                        && !string.IsNullOrEmpty(result.Token.AccessToken))
                    {
                        SaveToken(result.Token);
                        return true;
                    }
                    LastLoginError = result.Status == "expired"
                        ? "認証がタイムアウトしました。もう一度お試しください"
                        : "認証に失敗しました (" + result.Status + ")";
                    return false;
                }
                LastLoginError = "認証がタイムアウトしました。もう一度お試しください";
                return false;
            }
            finally
            {
                InAppBrowser.Dismiss(); // 成功・失敗・中止・タイムアウトのどの経路でもシートを閉じる
                _loginInProgress = false;
            }
        }

        /// <summary>
        /// 有効なアクセストークンを返す。期限の60秒前を過ぎていたら relay 経由で
        /// リフレッシュする。認証切れは GoogleAuthException、通信エラーは ApiException。
        /// </summary>
        public static async Task<string> GetAccessTokenAsync()
        {
            var token = Load();
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new GoogleAuthException("Google にログインしていません");
            }
            if (Now() < token.ExpiresAt - 60)
            {
                return token.AccessToken;
            }
            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                throw new GoogleAuthException("Google の認証が切れています");
            }
            string resp;
            try
            {
                resp = await HttpAsync("POST", AppConfig.GoogleRelayUrl + "?action=app_refresh",
                    "{\"refresh_token\":" + JsonConvert.ToString(token.RefreshToken) + "}");
            }
            catch (ApiException e)
            {
                if (e.HttpStatus >= 400)
                {
                    // relay がリフレッシュ失敗を返した = refresh_token が無効
                    throw new GoogleAuthException("Google の認証が切れています");
                }
                throw; // 通信エラー (一時的) はそのまま
            }
            RefreshResult data = null;
            try
            {
                data = JsonConvert.DeserializeObject<RefreshResult>(resp);
            }
            catch (JsonException)
            {
            }
            if (data == null || string.IsNullOrEmpty(data.AccessToken))
            {
                throw new GoogleAuthException("Google の認証が切れています");
            }
            token.AccessToken = data.AccessToken;
            token.ExpiresAt = Now() + data.ExpiresIn;
            SaveToken(token);
            return token.AccessToken;
        }

        static TokenData Load()
        {
            if (!_cacheLoaded)
            {
                _cacheLoaded = true;
                string json = PlayerPrefs.GetString(TokenKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        _cached = JsonConvert.DeserializeObject<TokenData>(json);
                    }
                    catch (JsonException)
                    {
                        _cached = null;
                    }
                }
            }
            return _cached;
        }

        static void SaveToken(TokenData token)
        {
            _cached = token;
            _cacheLoaded = true;
            PlayerPrefs.SetString(TokenKey, JsonConvert.SerializeObject(token));
            PlayerPrefs.Save();
        }

        /// <summary>認証セッション ID (128bit 乱数の16進64桁。relay 側で形式検証される)。</summary>
        static string NewSession()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        static long Now()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// <summary>relay への HTTP。通信エラー = ApiException(status 0)、HTTP 4xx/5xx = ApiException(status)。</summary>
        static async Task<string> HttpAsync(string method, string url, string jsonBody = null)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                if (jsonBody != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(
                        System.Text.Encoding.UTF8.GetBytes(jsonBody));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 15;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result == UnityWebRequest.Result.ConnectionError
                    || req.result == UnityWebRequest.Result.DataProcessingError)
                {
                    throw new ApiException("中継サーバーに接続できません: " + req.error);
                }
                if (req.responseCode >= 400)
                {
                    throw new ApiException("中継サーバーエラー (HTTP " + req.responseCode + ")",
                        (int)req.responseCode);
                }
                return req.downloadHandler.text;
            }
        }
    }
}
