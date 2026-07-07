using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// Android の共有メニュー (ACTION_SEND, text/plain) で受け取ったテキストから URL を取り出す。
    /// Assets/Plugins/Android/AndroidManifest.xml の intent-filter とセットで動く。
    /// エディタ・Android 以外では常に null を返す。
    /// </summary>
    public static class ShareIntent
    {
        /// <summary>共有された URL を1回だけ取り出す (同じ intent は2度処理しない)。</summary>
        public static string ConsumePendingUrl()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    string action = intent.Call<string>("getAction");
                    if (action != "android.intent.action.SEND")
                    {
                        return null;
                    }
                    string text = intent.Call<string>("getStringExtra", "android.intent.extra.TEXT");
                    // 同じ intent を2回処理しないように action を消しておく
                    intent.Call<AndroidJavaObject>("setAction", "");
                    if (string.IsNullOrEmpty(text))
                    {
                        return null;
                    }
                    // 共有テキストにはページタイトル等が混ざることがあるため URL 部分だけ抜き出す
                    var match = System.Text.RegularExpressions.Regex.Match(text, @"https?://\S+");
                    return match.Success ? match.Value : null;
                }
            }
            catch (System.Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }
    }
}
