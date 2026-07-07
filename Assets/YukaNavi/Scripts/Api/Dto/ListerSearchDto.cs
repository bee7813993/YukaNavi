using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>
    /// GET /search_listerdb_songlist_json.php の応答 (DataTables 形式・エンベロープなし)。
    /// 曲単位 (GROUP BY song_name, program_name) の検索結果。
    /// </summary>
    public class ListerSearchResultDto
    {
        [JsonProperty("recordsTotal")] public int Total;
        [JsonProperty("data")] public List<ListerSongDto> Items;
    }

    public class ListerSongDto
    {
        [JsonProperty("song_name")] public string SongName;
        [JsonProperty("song_artist")] public string Artist;
        [JsonProperty("program_name")] public string ProgramName;
        /// <summary>OP / ED などの区分</summary>
        [JsonProperty("song_op_ed")] public string OpEd;
        [JsonProperty("tie_up_group_name")] public string TieUpGroup;
        /// <summary>動画制作者</summary>
        [JsonProperty("found_worker")] public string Worker;
        /// <summary>代表ファイルのフルパス (そのまま予約に使える)</summary>
        [JsonProperty("found_path")] public string FoundPath;
    }
}
