using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using YukaNavi.Core;
using YukaNavi.UI;

namespace YukaNavi
{
    /// <summary>
    /// アプリの起動点。再生開始時に Canvas / EventSystem / BGM / 画面群をコードで構築する。
    /// 初回起動 (未設定) は接続設定画面、設定済みならホーム画面から始まる。
    /// </summary>
    public class AppRoot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<AppRoot>() != null)
            {
                return;
            }
            var go = new GameObject("YukaNaviApp");
            go.AddComponent<AppRoot>();
            DontDestroyOnLoad(go);
        }

        ScreenManager _screens;
        AudioSource _bgmSource;

        void Start()
        {
            // フォーカスを失っても再生状態のポーリングを止めない
            // (エディタや PC でウィンドウが非アクティブだと既定ではポーズし、
            //  曲の切り替わり表示がタッチするまで止まって見える。モバイルでは無視される設定)
            Application.runInBackground = true;

            // EventSystem (プロジェクトは新 Input System のみのため InputSystemUIInputModule を使う)
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // Canvas (基準解像度 1080x1920 の縦持ち想定)
            var canvasGo = new GameObject("AppCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // SE / BGM
            var seSource = gameObject.AddComponent<AudioSource>();
            Se.Init(seSource);
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.volume = 0.35f;
            // 既定ミュート (カラオケの邪魔をしない)。スキンに BGM があればそちらを流す
            Bgm.Init(_bgmSource, Resources.Load<AudioClip>("Audio/BGM/yukanavi_home_loop"));

            // スキンのテーマ色を画面構築前に適用する (UI は生成時に色を焼き込むため)
            YukariTheme.ApplyFromSkin(SkinManager.Current());

            // 最奥の下地 (ホームが背後にいない画面 [初回の接続設定など] で半透明背景が暗くならないように)
            var canvasBg = UiFactory.CreatePanel(canvasGo.transform, "CanvasBackground", UiFactory.PanelBg);
            UiFactory.StretchFull(canvasBg);

            // 画面登録 (専用レイヤーに置き、後から作るナビバーが常に前面になるようにする)
            var screenLayer = UiFactory.CreatePanel(canvasGo.transform, "Screens");
            UiFactory.StretchFull(screenLayer);
            _screens = new ScreenManager(screenLayer);
            _screens.Register<HomeScreen>();
            _screens.Register<ConnectScreen>();
            _screens.Register<SearchScreen>();
            _screens.Register<QueueScreen>();
            _screens.Register<PlayerScreen>();
            _screens.Register<QrScanScreen>();
            _screens.Register<SkinScreen>();
            _screens.Register<MypageScreen>();
            _screens.Register<ReserveScreen>();
            _screens.Register<SearchResultScreen>();
            _screens.Register<PeriodScreen>();
            _screens.Register<RequestDetailScreen>();

            // 下部の常時表示ナビゲーションバー (戻る / メニュー / ホーム)
            GlobalNav.Create(canvasGo.transform, _screens);

            if (AppConfig.IsConfigured)
            {
                _screens.ShowAsRoot<HomeScreen>();
            }
            else
            {
                _screens.ShowAsRoot<ConnectScreen>();
            }
        }
    }
}
