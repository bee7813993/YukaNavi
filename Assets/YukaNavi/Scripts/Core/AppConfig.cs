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
        const string KeySkinId = "yukanavi.skin_id";
        const string KeyMypageUserId = "yukanavi.mypage_userid";

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

        /// <summary>選択中のスキン ID (skins/ 配下のフォルダ名。空 = デフォルト)。</summary>
        public static string SkinId
        {
            get { return PlayerPrefs.GetString(KeySkinId, ""); }
            set { PlayerPrefs.SetString(KeySkinId, value); PlayerPrefs.Save(); }
        }

        /// <summary>接続設定を一度でも保存したか (false なら初回起動 → 接続設定画面から始める)。</summary>
        public static bool IsConfigured
        {
            get { return PlayerPrefs.GetInt(KeyConfigured, 0) == 1; }
            set { PlayerPrefs.SetInt(KeyConfigured, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// マイページのユーザー ID (UUID)。初回アクセス時に生成して永続化する。
        /// Web 版の Cookie YkariUserID に相当し、同じ値を送ることでデータを共有できる。
        /// </summary>
        public static string MypageUserId
        {
            get
            {
                string id = PlayerPrefs.GetString(KeyMypageUserId, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = System.Guid.NewGuid().ToString();
                    PlayerPrefs.SetString(KeyMypageUserId, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        /// <summary>
        /// 現在のサーバーにデバイスリンクしたマイページ userid ("" = 未リンク)。
        /// サーバー (部屋) ごとに保存し、リンク済みサーバーでは Web 版と同じユーザーとして振る舞う。
        /// </summary>
        public static string LinkedMypageUserId
        {
            get { return PlayerPrefs.GetString(LinkedMypageKey(), ""); }
            set
            {
                PlayerPrefs.SetString(LinkedMypageKey(), value ?? "");
                PlayerPrefs.Save();
            }
        }

        static string LinkedMypageKey()
        {
            return "yukanavi.mypage_link." + ServerUrl.Trim().TrimEnd('/');
        }

        /// <summary>
        /// サーバーへ送る実効ユーザー ID。リンク済みならリンク先 (Web 版と同じユーザー)、
        /// 未リンクなら端末固有 ID。exec.php の予約履歴もこの ID に記録される。
        /// </summary>
        public static string EffectiveMypageUserId
        {
            get
            {
                string linked = LinkedMypageUserId;
                return string.IsNullOrEmpty(linked) ? MypageUserId : linked;
            }
        }

        /// <summary>現在の設定で ApiClient を作る。</summary>
        public static ApiClient CreateClient()
        {
            var client = new ApiClient(ServerUrl, EasyPass);
            client.UserId = EffectiveMypageUserId;
            client.Username = Username;
            return client;
        }
    }
}
