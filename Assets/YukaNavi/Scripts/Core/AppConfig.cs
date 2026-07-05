using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>アプリ設定。PlayerPrefs で永続化する。</summary>
    public static class AppConfig
    {
        const string KeyServerUrl = "yukanavi.server_url";

        /// <summary>
        /// 既定の接続先。Android 実機は localhost に繋がらないため試験サーバーを使う。
        /// (接続設定画面は M1 で実装予定。それまでの暫定)
        /// </summary>
        public static string DefaultServerUrl
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return "http://ykr.moe:11004/";
#else
                return "http://localhost/";
#endif
            }
        }

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
