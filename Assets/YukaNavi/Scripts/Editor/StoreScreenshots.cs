using UnityEditor;
using UnityEngine;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// ストア掲載用スクリーンショットの撮影メニュー。
    /// 撮影解像度は Game ビューの解像度設定で決まる (App Store 用は 1284x2778、
    /// Google Play 用は 1080x1920 を Game ビューの「+」で追加して切り替える)。
    /// 出力先: Build/Screenshots/<解像度>/
    /// </summary>
    public static class StoreScreenshots
    {
        [MenuItem("YukaNavi/スクショ/ストア用一括撮影 (再生中に実行)")]
        public static void RunSequence()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) を開始してから実行してください");
                return;
            }
            ScreenshotDirector.Run();
        }

        [MenuItem("YukaNavi/スクショ/この画面を1枚撮影 (再生中に実行)")]
        public static void CaptureOne()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) を開始してから実行してください");
                return;
            }
            string dir = System.IO.Path.Combine("Build", "Screenshots",
                Screen.width + "x" + Screen.height);
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir,
                "shot_" + System.DateTime.Now.ToString("HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[YukaNavi] 撮影: " + System.IO.Path.GetFullPath(path));
        }
    }
}
