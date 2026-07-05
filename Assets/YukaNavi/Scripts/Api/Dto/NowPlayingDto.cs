using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/nowplaying.php の data。再生状態。</summary>
    public class NowPlayingDto
    {
        /// <summary>false ならプレイヤー停止中 (以下のフィールドは無効)</summary>
        [JsonProperty("playing")] public bool Playing;
        [JsonProperty("status")] public string Status;
        [JsonProperty("playtime")] public float Playtime;
        [JsonProperty("totaltime")] public float Totaltime;
        [JsonProperty("playtime_txt")] public string PlaytimeText;
        [JsonProperty("totaltime_txt")] public string TotaltimeText;
        [JsonProperty("playingtitle")] public string PlayingTitle;
        [JsonProperty("playingfile")] public string PlayingFile;
        [JsonProperty("playingsinger")] public string PlayingSinger;
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
