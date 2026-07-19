#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using YukaNavi.UI;

namespace YukaNavi
{
    /// <summary>
    /// ストア掲載用スクリーンショットの自動撮影 (エディタ専用、ビルドには含まれない)。
    /// メニュー「YukaNavi > スクショ > ストア用一括撮影」から再生中に起動し、
    /// 主要画面を順に遷移しながら Game ビューの解像度で Build/Screenshots/ に保存する。
    /// 撮影解像度は Game ビューの解像度設定 (例: 1284x2778) で決まる。
    /// 予約一覧・検索結果は接続先サーバーのデモデータをそのまま写す。
    /// </summary>
    public class ScreenshotDirector : MonoBehaviour
    {
        public static void Run()
        {
            if (FindFirstObjectByType<ScreenshotDirector>() != null)
            {
                return; // 実行中
            }
            new GameObject("ScreenshotDirector").AddComponent<ScreenshotDirector>();
        }

        IEnumerator Start()
        {
            var appRoot = FindFirstObjectByType<AppRoot>();
            if (appRoot == null || appRoot.Screens == null)
            {
                Debug.LogError("[YukaNavi] AppRoot が見つかりません (再生を開始してから実行してください)");
                Destroy(gameObject);
                yield break;
            }
            var screens = appRoot.Screens;

            // タイトル画面が残っていれば閉じる (全スクショに写り込むため)
            var title = GameObject.Find("Title");
            if (title != null)
            {
                Destroy(title);
            }

            string dir = Path.Combine("Build", "Screenshots",
                Screen.width + "x" + Screen.height);
            Directory.CreateDirectory(dir);
            Debug.Log("[YukaNavi] スクショ撮影開始 (" + Screen.width + "x" + Screen.height
                + "): " + Path.GetFullPath(dir));

            screens.ShowAsRoot<HomeScreen>();
            yield return Shoot(dir, "01_home", 2.5f);

            screens.Show<SearchScreen>();
            yield return Shoot(dir, "02_search", 1.5f);

            SearchResultScreen.Open(screens, new SearchResultScreen.SearchQuery
            {
                Kind = SearchResultScreen.QueryKind.ListerAnyword,
                Keyword = "童謡",
                Label = "童謡",
            });
            yield return Shoot(dir, "03_search_result", 3f);

            ReserveScreen.Open(screens, new ReserveScreen.Entry
            {
                Line1 = "ことりの音楽会",
                Line2 = "デモ合唱団 はる組　／　はるいろ童謡集",
                Filename = "03_ことりの音楽会.mp4",
                FullPath = "C:\\YukaNaviDemo\\はるいろ童謡集\\03_ことりの音楽会.mp4",
            });
            yield return Shoot(dir, "04_reserve", 3f);

            screens.Show<QueueScreen>();
            yield return Shoot(dir, "05_queue", 3f);

            screens.Show<SkinScreen>();
            yield return Shoot(dir, "06_skin", 2f);

            screens.ShowAsRoot<HomeScreen>();
            Debug.Log("[YukaNavi] スクショ撮影完了: " + Path.GetFullPath(dir));
            Destroy(gameObject);
        }

        IEnumerator Shoot(string dir, string name, float wait)
        {
            yield return new WaitForSeconds(wait); // 画面遷移とデータ読み込みを待つ
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return new WaitForSeconds(0.6f); // 書き込み完了を待つ
        }
    }
}
#endif
