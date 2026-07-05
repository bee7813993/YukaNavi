using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/server_info.php の data。唯一 easyauth の認証前に取得できる。</summary>
    public class ServerInfoDto
    {
        /// <summary>常に "yukari"。接続先がゆかりかどうかの判定に使う。</summary>
        [JsonProperty("app")] public string App;
        [JsonProperty("version")] public string Version;
        [JsonProperty("easyauth_required")] public bool EasyauthRequired;
    }
}
