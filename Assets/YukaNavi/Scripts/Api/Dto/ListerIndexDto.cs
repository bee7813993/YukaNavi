using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/lister_index.php mode=years の data。</summary>
    public class ListerYearsDto
    {
        [JsonProperty("years")] public List<ListerYearDto> Years;
        /// <summary>リリース日が未設定でどの期にも入らない曲数</summary>
        [JsonProperty("no_date")] public int NoDate;
    }

    public class ListerYearDto
    {
        [JsonProperty("year")] public int Year;
        [JsonProperty("songs")] public int Songs;
    }

    /// <summary>mode=quarters の data。</summary>
    public class ListerQuartersDto
    {
        [JsonProperty("year")] public int Year;
        [JsonProperty("quarters")] public List<ListerQuarterDto> Quarters;
    }

    public class ListerQuarterDto
    {
        /// <summary>1=1〜3月:冬 / 2=4〜6月:春 / 3=7〜9月:夏 / 4=10〜12月:秋</summary>
        [JsonProperty("q")] public int Q;
        [JsonProperty("label")] public string Label;
        [JsonProperty("songs")] public int Songs;
        [JsonProperty("programs")] public int Programs;
    }

    /// <summary>mode=programs の data (期指定またはシリーズ指定)。</summary>
    public class ListerProgramsDto
    {
        [JsonProperty("year")] public int Year;
        [JsonProperty("quarter")] public int Quarter;
        [JsonProperty("label")] public string Label;
        /// <summary>シリーズ指定 (mode=programs&group=) のときのみ</summary>
        [JsonProperty("group")] public string Group;
        [JsonProperty("programs")] public List<ListerProgramDto> Programs;
    }

    public class ListerProgramDto
    {
        [JsonProperty("program")] public string Program;
        /// <summary>シリーズ名 (無ければ null)</summary>
        [JsonProperty("group")] public string Group;
        [JsonProperty("songs")] public int Songs;
    }

    /// <summary>mode=songs の data。同じ曲の複数ファイル (別動画) は files にまとまる。</summary>
    public class ListerIndexSongsDto
    {
        /// <summary>曲数</summary>
        [JsonProperty("total")] public int Total;
        /// <summary>ファイル数</summary>
        [JsonProperty("files_total")] public int FilesTotal;
        [JsonProperty("items")] public List<ListerSongGroupDto> Items;
    }

    public class ListerSongGroupDto
    {
        [JsonProperty("song_name")] public string SongName;
        [JsonProperty("song_artist")] public string Artist;
        [JsonProperty("program_name")] public string ProgramName;
        [JsonProperty("tie_up_group_name")] public string TieUpGroup;
        /// <summary>OP / ED / 3rdシングル など作品名の補足</summary>
        [JsonProperty("song_op_ed")] public string OpEd;
        [JsonProperty("files")] public List<ListerFileDto> Files;
    }

    public class ListerFileDto
    {
        [JsonProperty("found_path")] public string FoundPath;
        /// <summary>ファイルのコメント (リリックビデオ 等。内部メモは除去済み)</summary>
        [JsonProperty("found_comment")] public string Comment;
        /// <summary>動画制作者</summary>
        [JsonProperty("found_worker")] public string Worker;
        [JsonProperty("found_file_size")] public long FileSize;
    }
}
