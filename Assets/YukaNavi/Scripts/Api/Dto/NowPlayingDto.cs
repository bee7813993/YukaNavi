using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/nowplaying.php の data。再生状態。</summary>
    public class NowPlayingDto
    {
        /// <summary>false ならプレイヤー停止中 (以下のフィールドは無効)</summary>
        [JsonProperty("playing")] public bool Playing;
        /// <summary>MPC の状態番号 (文字列): "2"=再生中 / "1"=一時停止</summary>
        [JsonProperty("status")] public string Status;
        /// <summary>再生位置 (ミリ秒)</summary>
        [JsonProperty("playtime")] public float Playtime;
        /// <summary>曲の長さ (ミリ秒)</summary>
        [JsonProperty("totaltime")] public float Totaltime;
        [JsonProperty("playtime_txt")] public string PlaytimeText;
        [JsonProperty("totaltime_txt")] public string TotaltimeText;
        [JsonProperty("playingtitle")] public string PlayingTitle;
        [JsonProperty("playingfile")] public string PlayingFile;
        [JsonProperty("playingsinger")] public string PlayingSinger;
        /// <summary>プレイヤー種別 "mpc" | "foobar" | "none" (旧サーバーでは null)</summary>
        [JsonProperty("player")] public string Player;
        /// <summary>再生中の曲の現在キー (半音。旧サーバーでは常に 0)</summary>
        [JsonProperty("keychange")] public int Keychange;
        [JsonProperty("nextsong")] public NextSongDto NextSong;
    }

    /// <summary>次に再生される曲の情報 (ない場合は null)。</summary>
    public class NextSongDto
    {
        /// <summary>表示タイトル (シークレット予約は伏せ字)</summary>
        [JsonProperty("title")] public string Title;
        [JsonProperty("songfile")] public string Songfile;
        [JsonProperty("show_file")] public bool ShowFile;
        [JsonProperty("singer")] public string Singer;
        [JsonProperty("kind")] public string Kind;
    }
}
