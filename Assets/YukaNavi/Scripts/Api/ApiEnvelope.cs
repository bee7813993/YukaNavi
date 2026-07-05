using Newtonsoft.Json;

namespace YukaNavi.Api
{
    /// <summary>
    /// /api/ 配下の応答エンベロープ {"ok":bool, "data":..., "error":"..."}。
    /// 仕様: KaraokeRequestorWeb/api/README.md「共通仕様」
    /// </summary>
    public class ApiEnvelope<T>
    {
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("data")] public T Data;
        [JsonProperty("error")] public string Error;
    }
}
