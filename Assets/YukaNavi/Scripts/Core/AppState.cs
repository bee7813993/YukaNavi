using System.Threading.Tasks;
using YukaNavi.Api;

namespace YukaNavi.Core
{
    /// <summary>
    /// サーバー由来の状態のキャッシュ。capabilities は起動後の初回取得を使い回し、
    /// 接続設定の保存時に Invalidate() で取り直す。
    /// </summary>
    public static class AppState
    {
        public static CapabilitiesDto Capabilities { get; private set; }

        static string _fetchedForUrl;

        /// <summary>capabilities を取得する (キャッシュ済みならそれを返す)。</summary>
        public static async Task<CapabilitiesDto> EnsureCapabilitiesAsync()
        {
            string url = AppConfig.ServerUrl;
            if (Capabilities != null && _fetchedForUrl == url)
            {
                return Capabilities;
            }
            Capabilities = await AppConfig.CreateClient().GetCapabilitiesAsync();
            _fetchedForUrl = url;
            return Capabilities;
        }

        /// <summary>接続設定が変わったときに呼ぶ。</summary>
        public static void Invalidate()
        {
            Capabilities = null;
            _fetchedForUrl = null;
        }
    }
}
