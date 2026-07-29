#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using YukaNavi.Api;
using YukaNavi.Core;
using YukaNavi.UI;

namespace YukaNavi
{
    /// <summary>
    /// ユーザーマニュアル (docs/user-manual.md) 用スクリーンショットの自動撮影
    /// (エディタ専用、ビルドには含まれない)。
    /// メニュー「YukaNavi > スクショ > マニュアル用一括撮影」から再生中に起動し、
    /// マニュアルの各章に対応する画面を順に開いて Build/Screenshots/manual/ に保存する。
    ///
    /// 撮影解像度は Game ビューの解像度設定で決まる (縦画面は 1080x1920 推奨)。
    /// 予約一覧・検索結果は接続先サーバーの実データをそのまま写すため、
    /// あらかじめ接続設定を済ませてから実行する。
    /// </summary>
    public class ManualScreenshotDirector : MonoBehaviour
    {
        /// <summary>検索結果の撮影に使うキーワード (サーバーのデータに合わせて変更可)。</summary>
        public const string SearchKeyword = "風船 ";

        /// <summary>ダッシュボードだけを撮る (横向きの Game ビューで実行する)。</summary>
        public static bool DashboardOnly;

        public static void Run(bool dashboardOnly = false)
        {
            if (FindFirstObjectByType<ManualScreenshotDirector>() != null)
            {
                return; // 実行中
            }
            DashboardOnly = dashboardOnly;
            new GameObject("ManualScreenshotDirector").AddComponent<ManualScreenshotDirector>();
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

            string dir = Path.Combine("Build", "Screenshots", "manual");
            Directory.CreateDirectory(dir);
            Debug.Log("[YukaNavi] マニュアル用スクショ撮影開始 (" + Screen.width + "x" + Screen.height
                + "): " + Path.GetFullPath(dir));

            if (DashboardOnly)
            {
                yield return CaptureDashboard(screens, dir);
                Debug.Log("[YukaNavi] 撮影完了: " + Path.GetFullPath(dir));
                Destroy(gameObject);
                yield break;
            }

            // --- 1章: ホームとメニュー ---
            screens.ShowAsRoot<HomeScreen>();
            yield return Shoot(dir, "01_home", 2.5f);

            OpenNavMenu();
            yield return Shoot(dir, "02_menu", 1.2f);
            if (GlobalNav.Instance != null)
            {
                GlobalNav.Instance.CloseMenu();
            }
            yield return new WaitForSeconds(0.5f);

            // --- 2章: 接続設定 ---
            screens.Show<ConnectScreen>();
            yield return Shoot(dir, "03_connect", 1.5f);

            // --- 4章: 曲をさがす ---
            screens.Show<SearchScreen>();
            yield return Shoot(dir, "04_search", 1.5f);

            SearchResultScreen.Open(screens, new SearchResultScreen.SearchQuery
            {
                Kind = SearchResultScreen.QueryKind.ListerAnyword,
                Keyword = SearchKeyword,
                Label = "検索: " + SearchKeyword,
            });
            yield return Shoot(dir, "05_search_result", 3.5f);

            PeriodScreen.Open(screens);
            yield return Shoot(dir, "06_period", 3f);

            NameIndexScreen.Open(screens, "program");
            yield return Shoot(dir, "07_name_index", 3f);

            // --- 5章: 予約の確認 ---
            ReserveScreen.Open(screens, new ReserveScreen.Entry
            {
                Line1 = "サンプル曲",
                Line2 = "サンプル歌手　／　サンプル作品",
                Filename = "sample.mp4",
                FullPath = "C:\\Sample\\sample.mp4",
            });
            yield return Shoot(dir, "08_reserve", 3f);

            // --- 6章: 予約一覧と予約の詳細 ---
            screens.Show<QueueScreen>();
            yield return Shoot(dir, "09_queue", 3f);

            yield return CaptureRequestDetail(screens, dir);

            // --- 7章: リモコン ---
            screens.Show<PlayerScreen>();
            yield return Shoot(dir, "11_player", 3f);

            // --- 8章: マイページ ---
            MypageScreen.Open(screens, 0);
            yield return Shoot(dir, "12_mypage", 3f);

            // --- 9章: きせかえ ---
            screens.Show<SkinScreen>();
            yield return Shoot(dir, "13_skin", 2.5f);

            screens.ShowAsRoot<HomeScreen>();
            Debug.Log("[YukaNavi] マニュアル用スクショ撮影完了: " + Path.GetFullPath(dir)
                + "\n(QR読み取り・動画プレビューは「この画面を1枚撮影」で手動撮影してください)");
            Destroy(gameObject);
        }

        /// <summary>予約一覧の先頭の曲で「予約の詳細」を開いて撮る (予約が無ければ飛ばす)。</summary>
        IEnumerator CaptureRequestDetail(ScreenManager screens, string dir)
        {
            RequestListDto list = null;
            var task = AppConfig.CreateClient().GetRequestsAsync();
            float until = Time.realtimeSinceStartup + 8f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                list = task.Result;
            }
            if (list == null || list.Items == null || list.Items.Count == 0)
            {
                Debug.Log("[YukaNavi] 予約が無いため 10_request_detail は撮影しません");
                yield break;
            }
            RequestDetailScreen.Open(screens, list.Items[0]);
            yield return Shoot(dir, "10_request_detail", 2.5f);
        }

        /// <summary>ダッシュボード (横向き) を撮る。</summary>
        IEnumerator CaptureDashboard(ScreenManager screens, string dir)
        {
            if (Screen.width <= Screen.height)
            {
                Debug.LogWarning("[YukaNavi] ダッシュボードは横向きです。"
                    + "Game ビューの解像度を横長 (例: 1920x1080) にしてから実行してください");
            }
            screens.Show<DashboardScreen>();
            yield return Shoot(dir, "14_dashboard", 4f);
            screens.ShowAsRoot<HomeScreen>();
        }

        /// <summary>下部ナビのメニューを開く (エディタ撮影用に内部メソッドを呼ぶ)。</summary>
        static void OpenNavMenu()
        {
            var nav = GlobalNav.Instance;
            if (nav == null)
            {
                return;
            }
            var method = typeof(GlobalNav).GetMethod("OpenMenu",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(nav, null);
            }
        }

        IEnumerator Shoot(string dir, string name, float wait)
        {
            yield return new WaitForSeconds(wait); // 画面遷移とデータ読み込みを待つ
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            Debug.Log("[YukaNavi] 撮影: " + name + ".png");
            yield return new WaitForSeconds(0.6f); // 書き込み完了を待つ
        }
    }
}
#endif
