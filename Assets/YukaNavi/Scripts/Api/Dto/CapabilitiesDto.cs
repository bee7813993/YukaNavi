using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/capabilities.php の data。UI の出し分けに使う機能フラグ。</summary>
    public class CapabilitiesDto
    {
        [JsonProperty("features")] public FeaturesDto Features;
        [JsonProperty("player")] public PlayerInfoDto Player;
        [JsonProperty("request")] public RequestInfoDto Request;
        /// <summary>サーバー情報 (旧サーバーでは null)</summary>
        [JsonProperty("server")] public CapServerDto Server;
    }

    /// <summary>capabilities の server セクション (server_info.php の ServerInfoDto とは別物)。</summary>
    public class CapServerDto
    {
        /// <summary>Web 版で「〇〇部屋」と表示される部屋名。未設定時は空文字</summary>
        [JsonProperty("room_name")] public string RoomName;
        /// <summary>移動できる部屋の一覧 (Web 版の部屋ドロップダウンと同じ条件)。旧サーバーでは null</summary>
        [JsonProperty("rooms")] public System.Collections.Generic.List<RoomDto> Rooms;
    }

    /// <summary>移動できる部屋 (別部屋URL設定の1エントリ)。</summary>
    public class RoomDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("url")] public string Url;
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
        /// <summary>別プレイヤー再生 (useotherplayer。旧サーバーでは常に false)</summary>
        [JsonProperty("otherplayer")] public bool Otherplayer;
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
        /// <summary>別プレイヤー再生チェックの表示名 (旧サーバーでは null)</summary>
        [JsonProperty("otherplayer_disc")] public string OtherplayerDisc;
    }
}
