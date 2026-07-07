using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/requests.php の data。予約一覧。</summary>
    public class RequestListDto
    {
        /// <summary>reqorder 降順 (先頭 = 最後に再生される曲)</summary>
        [JsonProperty("items")] public List<RequestItemDto> Items;
        [JsonProperty("total")] public int Total;
        [JsonProperty("has_more")] public bool HasMore;
        /// <summary>未再生の残り件数</summary>
        [JsonProperty("remaining_count")] public int RemainingCount;
        /// <summary>未再生の残り合計秒数 (duration 不明の曲は含まない)</summary>
        [JsonProperty("remaining_seconds")] public int RemainingSeconds;
    }

    public class RequestItemDto
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("reqorder")] public int Reqorder;
        [JsonProperty("songfile")] public string Songfile;
        /// <summary>表示名。シークレット予約は伏せ字テキストになっている</summary>
        [JsonProperty("display_name")] public string DisplayName;
        [JsonProperty("song_name")] public string SongName;
        [JsonProperty("lister_artist")] public string ListerArtist;
        [JsonProperty("lister_work")] public string ListerWork;
        [JsonProperty("lister_op_ed")] public string ListerOpEd;
        [JsonProperty("lister_comment")] public string ListerComment;
        [JsonProperty("singer")] public string Singer;
        [JsonProperty("comment")] public string Comment;
        [JsonProperty("kind")] public string Kind;
        /// <summary>未再生 / 再生中 / 再生済 など (日本語文字列)</summary>
        [JsonProperty("nowplaying")] public string Nowplaying;
        /// <summary>予約の変更用: ファイルのフルパス (旧サーバーでは null)</summary>
        [JsonProperty("fullpath")] public string FullPath;
        [JsonProperty("secret")] public int Secret;
        [JsonProperty("loop")] public int Loop;
        [JsonProperty("pause")] public int Pause;
        [JsonProperty("track")] public int Track;
        [JsonProperty("keychange")] public int Keychange;
        [JsonProperty("audiodelay")] public int Audiodelay;
        [JsonProperty("duration")] public int Duration;
        [JsonProperty("volume")] public int Volume;
        /// <summary>再生順の位置 (1 = 次に近い)</summary>
        [JsonProperty("position")] public int Position;
    }
}
