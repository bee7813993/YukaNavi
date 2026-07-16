using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>
    /// /api/song_metadata.php の data。曲メタデータ (曲名・歌手・作品・使われ方・
    /// 補足説明) と読み仮名。GET の応答と、修正 (POST action=correct) の送信の両方に使う。
    /// </summary>
    public class SongMetadataDto
    {
        [JsonProperty("song_name")] public string SongName;
        [JsonProperty("song_ruby")] public string SongRuby;
        [JsonProperty("lister_artist")] public string Artist;
        [JsonProperty("lister_artist_ruby")] public string ArtistRuby;
        [JsonProperty("lister_work")] public string Work;
        [JsonProperty("lister_work_ruby")] public string WorkRuby;
        /// <summary>使われ方 (OP / ED / 挿入歌 / ライブ披露曲 など)</summary>
        [JsonProperty("lister_op_ed")] public string OpEd;
        [JsonProperty("lister_comment")] public string Comment;

        public SongMetadataDto Clone()
        {
            return (SongMetadataDto)MemberwiseClone();
        }

        public bool SameAs(SongMetadataDto other)
        {
            return other != null
                && (SongName ?? "") == (other.SongName ?? "")
                && (SongRuby ?? "") == (other.SongRuby ?? "")
                && (Artist ?? "") == (other.Artist ?? "")
                && (ArtistRuby ?? "") == (other.ArtistRuby ?? "")
                && (Work ?? "") == (other.Work ?? "")
                && (WorkRuby ?? "") == (other.WorkRuby ?? "")
                && (OpEd ?? "") == (other.OpEd ?? "")
                && (Comment ?? "") == (other.Comment ?? "");
        }
    }
}
