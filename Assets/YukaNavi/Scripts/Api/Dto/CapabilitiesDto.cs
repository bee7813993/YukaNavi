using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/capabilities.php の data。UI の出し分けに使う機能フラグ。</summary>
    public class CapabilitiesDto
    {
        [JsonProperty("features")] public FeaturesDto Features;
        [JsonProperty("player")] public PlayerInfoDto Player;
        [JsonProperty("request")] public RequestInfoDto Request;
    }

    public class FeaturesDto
    {
        [JsonProperty("mypage")] public bool Mypage;
        [JsonProperty("bingo")] public bool Bingo;
        [JsonProperty("keychange")] public bool Keychange;
        [JsonProperty("secret")] public bool Secret;
        [JsonProperty("bgv")] public bool Bgv;
        [JsonProperty("userpause")] public bool Userpause;
        [JsonProperty("haishin")] public bool Haishin;
        [JsonProperty("nonamerequest")] public bool Nonamerequest;
        [JsonProperty("google_sync")] public bool GoogleSync;
        [JsonProperty("easyauth")] public bool Easyauth;
        [JsonProperty("new_request_list")] public bool NewRequestList;
        [JsonProperty("new_search_ui")] public bool NewSearchUi;
    }

    public class PlayerInfoDto
    {
        /// <summary>1=MPC-BE / 2=foobar2000 / 3=自動 / 4=その他</summary>
        [JsonProperty("mode")] public int Mode;
        [JsonProperty("autoplay")] public bool Autoplay;
    }

    public class RequestInfoDto
    {
        /// <summary>匿名リクエスト時のデフォルト名</summary>
        [JsonProperty("noname_username")] public string NonameUsername;
    }
}
