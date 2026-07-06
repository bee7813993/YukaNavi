using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/player.php の data。</summary>
    public class PlayerActionDto
    {
        /// <summary>実際に操作したプレイヤー ("mpc" / "foobar")</summary>
        [JsonProperty("player")] public string Player;
        [JsonProperty("action")] public string Action;
        [JsonProperty("message")] public string Message;
        /// <summary>volume_get / volume_up / volume_down 時の現在音量 (それ以外は null)</summary>
        [JsonProperty("volume")] public int? Volume;
    }
}
