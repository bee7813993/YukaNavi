using System.Collections;
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
        RectTransform _screenLayer;
        float _appliedSafeTop = -1f;
        float _appliedSafeBottom = -1f;

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
            // 幅基準で固定する。0.5 (中間) だと 9:16 より細長い端末で Canvas の実効幅が
            // 1080 を下回り、固定幅のカードやグリッドが左右にはみ出す
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // セーフエリア (ノッチ・パンチホールカメラ・下部ホームバー) を Canvas 単位に換算
            ReadSafeInsets(out float safeTop, out float safeBottom);
            UiFactory.SafeTop = safeTop;
            UiFactory.SafeBottom = safeBottom;
            _appliedSafeTop = safeTop;
            _appliedSafeBottom = safeBottom;

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
            // 上のセーフエリア分は下げる (ノッチ裏は各バーの背景が受け持つ。
            // 下はナビバーの高さ [GlobalNav.BarHeight] に含める)
            var screenLayer = UiFactory.CreatePanel(canvasGo.transform, "Screens");
            UiFactory.StretchFull(screenLayer);
            screenLayer.offsetMax = new Vector2(0f, -UiFactory.SafeTop);
            _screenLayer = screenLayer;
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
            _screens.Register<NameIndexScreen>();
            _screens.Register<UrlRequestScreen>();
            _screens.Register<PeriodScreen>();
            _screens.Register<RequestDetailScreen>();

            // 下部の常時表示ナビゲーションバー (戻る / メニュー / ホーム)
            GlobalNav.Create(canvasGo.transform, _screens);

            // トースト (操作結果の通知) の親。生成時に最後の子になるので常に最前面
            UiFactory.ToastRoot = canvasGo.transform;

            if (AppConfig.IsConfigured)
            {
                _screens.ShowAsRoot<HomeScreen>();
            }
            else
            {
                _screens.ShowAsRoot<ConnectScreen>();
            }

            // 起動スプラッシュ (最前面。スキンに splash.png があればそれ、なければ標準)
            ShowSplash(canvasGo.transform);

            // 共有メニュー経由で起動された場合は URL リクエスト画面を開く
            HandleSharedUrl();
        }

        /// <summary>
        /// アプリ内スプラッシュ。しばらく表示してフェードアウトする。
        /// スキンフォルダに splash.png を置くときせかえで差し替えられる。
        /// </summary>
        void ShowSplash(Transform canvas)
        {
            Sprite sprite = null;
            var skinTex = SkinManager.LoadTexture(SkinManager.Current(), "splash.png");
            if (skinTex != null)
            {
                sprite = Sprite.Create(skinTex,
                    new Rect(0f, 0f, skinTex.width, skinTex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                sprite = UiFactory.LoadSprite("Art/ScreenArt/yukanavi_splash_portrait");
            }
            if (sprite == null)
            {
                return;
            }

            var go = new GameObject("Splash");
            go.transform.SetParent(canvas, false);
            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // 画像が画面比と合わないときの下地
            UiFactory.StretchFull(bg.rectTransform);

            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(go.transform, false);
            var img = imgGo.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            UiFactory.StretchFull(img.rectTransform);
            var fitter = imgGo.AddComponent<AspectRatioFitter>();
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            StartCoroutine(SplashRoutine(go, sprite, skinTex));
        }

        IEnumerator SplashRoutine(GameObject splash, Sprite sprite, Texture2D skinTex)
        {
            yield return new WaitForSeconds(1.4f);
            var images = splash.GetComponentsInChildren<Image>();
            float t = 0f;
            const float fade = 0.4f;
            while (t < fade)
            {
                t += Time.deltaTime;
                foreach (var image in images)
                {
                    var c = image.color;
                    c.a = 1f - Mathf.Clamp01(t / fade);
                    image.color = c;
                }
                yield return null;
            }
            Destroy(splash);
            Destroy(sprite);
            if (skinTex != null)
            {
                Destroy(skinTex);
            }
        }

        /// <summary>Screen.safeArea を Canvas 単位 (幅 1080 基準) の上下インセットに換算する。</summary>
        static void ReadSafeInsets(out float top, out float bottom)
        {
            var safe = Screen.safeArea;
            float toCanvas = 1080f / Screen.width; // 幅基準スケール (matchWidthOrHeight = 0)
            top = Mathf.Max(0f, (Screen.height - safe.yMax) * toCanvas);
            bottom = Mathf.Max(0f, safe.yMin * toCanvas);
        }

        void Update()
        {
            // エディタの Simulator 切り替え等でセーフエリアが変わったら UI を作り直して追従する
            // (UI は生成時にインセットを焼き込むため)
            ReadSafeInsets(out float top, out float bottom);
            if (Mathf.Abs(top - _appliedSafeTop) < 0.5f && Mathf.Abs(bottom - _appliedSafeBottom) < 0.5f)
            {
                return;
            }
            _appliedSafeTop = top;
            _appliedSafeBottom = bottom;
            UiFactory.SafeTop = top;
            UiFactory.SafeBottom = bottom;
            _screenLayer.offsetMax = new Vector2(0f, -top);
            _screens.RebuildAll();
            if (GlobalNav.Instance != null)
            {
                GlobalNav.Instance.Rebuild();
            }
        }

        /// <summary>起動中に共有された場合も、アプリ復帰のタイミングで受け取る。</summary>
        void OnApplicationFocus(bool focused)
        {
            if (focused && _screens != null)
            {
                HandleSharedUrl();
            }
        }

        void HandleSharedUrl()
        {
            string url = ShareIntent.ConsumePendingUrl();
            if (string.IsNullOrEmpty(url) || !AppConfig.IsConfigured)
            {
                return;
            }
            UrlRequestScreen.OpenWithUrl(_screens, url);
        }
    }
}
