using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/search.php の data。ローカルファイル検索の生データ。</summary>
    public class SearchResultDto
    {
        [JsonProperty("keyword")] public string Keyword;
        [JsonProperty("total")] public int Total;
        [JsonProperty("count")] public int Count;
        [JsonProperty("items")] public List<SearchItemDto> Items;
    }

    public class SearchItemDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        /// <summary>予約投稿 (exec.php) にそのまま渡すフルパス</summary>
        [JsonProperty("fullpath")] public string FullPath;
        [JsonProperty("size")] public long Size;
        /// <summary>おすすめ度 (prioritydb 重み、既定 50)</summary>
        [JsonProperty("priority")] public int? Priority;
        /// <summary>動画制作者 (ListerDB 照会。連携なし・不明なら空)</summary>
        [JsonProperty("worker")] public string Worker;
    }
}
