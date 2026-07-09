using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/mypage.php action=pair_apply の data。</summary>
    public class MypagePairDto
    {
        [JsonProperty("userid")] public string UserId;
    }

    /// <summary>action=summary の data。</summary>
    public class MypageSummaryDto
    {
        [JsonProperty("displayname")] public string DisplayName;
        [JsonProperty("history")] public int History;
        [JsonProperty("later")] public int Later;
        [JsonProperty("favorite")] public int Favorite;
        [JsonProperty("keyword")] public int Keyword;
        [JsonProperty("google_linked")] public bool GoogleLinked;
    }

    /// <summary>action=history / later / favorite の data。</summary>
    public class MypageItemsDto
    {
        [JsonProperty("items")] public List<MypageItemDto> Items;
    }

    public class MypageItemDto
    {
        [JsonProperty("fullpath")] public string FullPath;
        [JsonProperty("songfile")] public string Songfile;
        [JsonProperty("kind")] public string Kind;
        /// <summary>予約回数 (履歴のみ)</summary>
        [JsonProperty("times")] public int Times;
        /// <summary>最終予約日時 unixtime (履歴のみ)</summary>
        [JsonProperty("last_requested_at")] public long LastRequestedAt;
        /// <summary>追加日時 unixtime (あとで歌う / お気に入り)</summary>
        [JsonProperty("added_at")] public long AddedAt;
    }

    /// <summary>action=keyword の data。</summary>
    public class MypageKeywordsDto
    {
        [JsonProperty("items")] public List<MypageKeywordDto> Items;
    }

    public class MypageKeywordDto
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("keyword")] public string Keyword;
        [JsonProperty("search_type")] public string SearchType;
        [JsonProperty("search_params")] public string SearchParams;
        [JsonProperty("added_at")] public long AddedAt;
    }

}
