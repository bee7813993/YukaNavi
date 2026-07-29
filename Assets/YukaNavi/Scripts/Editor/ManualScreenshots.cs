using UnityEditor;
using UnityEngine;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// ユーザーマニュアル (docs/user-manual.md) 用スクリーンショットの撮影メニュー。
    /// 出力先: Build/Screenshots/manual/
    /// 縦画面は Game ビューの解像度を 1080x1920 に、
    /// ダッシュボードは横長 (1920x1080) にしてから実行する。
    /// </summary>
    public static class ManualScreenshots
    {
        [MenuItem("YukaNavi/スクショ/マニュアル用一括撮影 (再生中に実行)")]
        public static void RunSequence()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) を開始してから実行してください");
                return;
            }
            ManualScreenshotDirector.Run();
        }

        [MenuItem("YukaNavi/スクショ/マニュアル用: ダッシュボードのみ (横向きで実行)")]
        public static void RunDashboard()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) を開始してから実行してください");
                return;
            }
            ManualScreenshotDirector.Run(dashboardOnly: true);
        }
    }
}
