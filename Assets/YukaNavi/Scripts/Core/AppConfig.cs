using UnityEngine;
using YukaNavi.Api;

namespace YukaNavi.Core
{
    /// <summary>アプリ設定。PlayerPrefs で永続化する。</summary>
    public static class AppConfig
    {
        const string KeyServerUrl = "yukanavi.server_url";
        const string KeyEasyPass = "yukanavi.easypass";
        const string KeyConfigured = "yukanavi.configured";
        const string KeyUsername = "yukanavi.username";

        /// <summary>
        /// 既定の接続先。Android 実機は localhost に繋がらないため試験サーバーを使う。
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
            set { PlayerPrefs.SetString(KeyServerUrl, value); PlayerPrefs.Save(); }
        }

        /// <summary>easyauth の認証キーワード (不要なら空)。</summary>
        public static string EasyPass
        {
            get { return PlayerPrefs.GetString(KeyEasyPass, ""); }
            set { PlayerPrefs.SetString(KeyEasyPass, value); PlayerPrefs.Save(); }
        }

        /// <summary>予約時に使う「歌う人」の名前。</summary>
        public static string Username
        {
            get { return PlayerPrefs.GetString(KeyUsername, ""); }
            set { PlayerPrefs.SetString(KeyUsername, value); PlayerPrefs.Save(); }
        }

        /// <summary>接続設定を一度でも保存したか (false なら初回起動 → 接続設定画面から始める)。</summary>
        public static bool IsConfigured
        {
            get { return PlayerPrefs.GetInt(KeyConfigured, 0) == 1; }
            set { PlayerPrefs.SetInt(KeyConfigured, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>現在の設定で ApiClient を作る。</summary>
        public static ApiClient CreateClient()
        {
            return new ApiClient(ServerUrl, EasyPass);
        }
    }
}
