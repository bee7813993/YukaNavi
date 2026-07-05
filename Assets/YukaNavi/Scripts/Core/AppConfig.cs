using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>アプリ設定。PlayerPrefs で永続化する。</summary>
    public static class AppConfig
    {
        const string KeyServerUrl = "yukanavi.server_url";

        public const string DefaultServerUrl = "http://localhost/";

        /// <summary>接続先のゆかりサーバー URL。</summary>
        public static string ServerUrl
        {
            get { return PlayerPrefs.GetString(KeyServerUrl, DefaultServerUrl); }
            set
            {
                PlayerPrefs.SetString(KeyServerUrl, value);
                PlayerPrefs.Save();
            }
        }
    }
}
