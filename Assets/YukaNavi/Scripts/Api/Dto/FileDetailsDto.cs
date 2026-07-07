using System.Collections.Generic;
using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>GET /api/file_details.php の data。音声トラック一覧 + 動画詳細。</summary>
    public class FileDetailsDto
    {
        /// <summary>音声トラックのラベル一覧 (判別できない場合は空)</summary>
        [JsonProperty("tracks")] public List<string> Tracks;
        /// <summary>動画詳細 (解析できない場合は null)</summary>
        [JsonProperty("details")] public VideoDetailsDto Details;
    }

    public class VideoDetailsDto
    {
        /// <summary>"4:15" 形式の曲の長さ</summary>
        [JsonProperty("duration")] public string Duration;
        /// <summary>曲の長さ (秒)。exec.php の duration に渡す</summary>
        [JsonProperty("duration_seconds")] public int DurationSeconds;
        [JsonProperty("resolution")] public string Resolution;
        [JsonProperty("frame_rate")] public float FrameRate;
        [JsonProperty("video_codec")] public string VideoCodec;
        [JsonProperty("audio_codec")] public string AudioCodec;
        [JsonProperty("audio_channels")] public string AudioChannels;
        [JsonProperty("audio_sample_rate")] public string AudioSampleRate;
        [JsonProperty("bitrate")] public string Bitrate;
    }
}
