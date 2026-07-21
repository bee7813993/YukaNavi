using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace YukaNavi.Core
{
    /// <summary>
    /// アプリ内ブラウザ。iOS では SFSafariViewController のシートで開く
    /// (App Store ガイドライン 4: 認証を外部ブラウザに出さない対応。
    ///  Assets/Plugins/iOS/YukaNaviSafariView.mm とセット)。
    /// それ以外のプラットフォームでは従来どおり既定ブラウザで開く。
    /// </summary>
    public static class InAppBrowser
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int YukaNaviSafariView_Open(string url);
        [DllImport("__Internal")] static extern void YukaNaviSafariView_Dismiss();
        [DllImport("__Internal")] static extern int YukaNaviSafariView_ConsumeUserClosed();
#endif

        /// <summary>
        /// URL を開く (iOS: アプリ内シート / その他: 既定ブラウザ)。
        /// iOS で http/https 以外や解釈できない URL だった場合は開かず false。
        /// </summary>
        public static bool Open(string url)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return YukaNaviSafariView_Open(url) != 0;
#else
            Application.OpenURL(url);
            return true;
#endif
        }

        /// <summary>アプリ内シートを閉じる (開いていない・iOS 以外では何もしない)。</summary>
        public static void Dismiss()
        {
#if UNITY_IOS && !UNITY_EDITOR
            YukaNaviSafariView_Dismiss();
#endif
        }

        /// <summary>ユーザーが自分でシートを閉じたかを1回だけ返す (iOS 以外は常に false)。</summary>
        public static bool ConsumeUserClosed()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return YukaNaviSafariView_ConsumeUserClosed() != 0;
#else
            return false;
#endif
        }
    }
}
