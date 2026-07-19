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

        /// <summary>画面マネージャ (エディタのスクショ自動撮影などが使う)。</summary>
        public ScreenManager Screens
        {
            get { return _screens; }
        }

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
            _screens.Register<MypageLinkScreen>();
            _screens.Register<ReserveScreen>();
            _screens.Register<MetadataEditScreen>();
            _screens.Register<SearchResultScreen>();
            _screens.Register<NameIndexScreen>();
            _screens.Register<UrlRequestScreen>();
            _screens.Register<PeriodScreen>();
            _screens.Register<RequestDetailScreen>();

            // 下部の常時表示ナビゲーションバー (戻る / メニュー / ホーム)
            GlobalNav.Create(canvasGo.transform, _screens);

            // トースト (操作結果の通知) の親。生成時に最後の子になるので常に最前面
            UiFactory.ToastRoot = canvasGo.transform;

            // マイページの Google Drive 自動同期 (ログイン済みなら起動時に取り込む)
            PlayerPrefs.DeleteKey("yukanavi.google_carry"); // 旧・持ち歩きトークンの掃除
            MypageService.StartGoogleSync();

            if (AppConfig.IsConfigured)
            {
                _screens.ShowAsRoot<HomeScreen>();
            }
            else
            {
                _screens.ShowAsRoot<ConnectScreen>();
            }

            // 起動タイトル (最前面。背景はスキンの splash.png、なければ標準)
            ShowTitle(canvasGo.transform);

            // 共有メニュー経由で起動された場合は URL リクエスト画面を開く
            HandleSharedUrl();
        }

        GameObject _titleGo;
        Button _titleGoogleButton;
        Text _titleGoogleLabel;
        GameObject _titleStatusPanel;
        Text _titleStatusText;

        /// <summary>
        /// 起動タイトル画面。splash 画像を背景に「Touch To Start」(点滅) を表示し、
        /// 画面のどこをタップしても進む (Google ボタンなど個別 UI はそちらが優先)。
        /// 下部に Google 同期の状態を表示し、未ログインなら任意のログイン導線を出す
        /// (タップだけで進める。選択の強制はしない)。
        /// スキンフォルダに splash.png を置くときせかえで背景を差し替えられる。
        /// </summary>
        void ShowTitle(Transform canvas)
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

            var go = new GameObject("Title");
            go.transform.SetParent(canvas, false);
            _titleGo = go;
            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // 画像が画面比と合わないときの下地 + 下の画面への誤タップ防止
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

            // ---- 下部 UI (セーフエリアの上に積む) ----
            float statusH = Mathf.Max(84f, UiFactory.LineHeight(26) + 24f);
            float statusBottom = UiFactory.SafeBottom + 60f;

            // 状態行 1: 未ログイン/認証待ちのボタン (認証待ち中はタップで中止)
            _titleGoogleButton = UiFactory.CreateButton(go.transform, "GoogleLogin", "",
                UiFactory.PrimaryPale, UiFactory.Primary, 26);
            _titleGoogleLabel = _titleGoogleButton.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_titleGoogleLabel);
            SetTitleRow(_titleGoogleButton.GetComponent<RectTransform>(), statusBottom, statusH, 720f);
            _titleGoogleButton.onClick.AddListener(() =>
            {
                if (GoogleAccount.IsLoginInProgress)
                {
                    Se.Play(Se.Tap);
                    GoogleAccount.CancelLogin();
                    return;
                }
                _ = TitleLoginAsync();
            });

            // 状態行 2: ログイン済みの表示 (半透明の白帯 + テキスト。タップは背面の開始判定へ通す)
            var statusPanel = UiFactory.CreatePanel(go.transform, "GoogleStatus",
                new Color(1f, 1f, 1f, 0.8f));
            _titleStatusPanel = statusPanel.gameObject;
            SetTitleRow(statusPanel, statusBottom, statusH, 720f);
            statusPanel.GetComponent<Image>().raycastTarget = false;
            _titleStatusText = UiFactory.CreateText(statusPanel, "Text", "", 24,
                UiFactory.TextDark);
            UiFactory.StretchFull(_titleStatusText.rectTransform);
            UiFactory.FitLabelOneLine(_titleStatusText);
            _titleStatusText.raycastTarget = false;

            // 画面のどこをタップしても進む (最奥の下地がタップを受ける)
            bool closing = false;
            var startButton = bg.gameObject.AddComponent<Button>();
            startButton.transition = Selectable.Transition.None;
            startButton.onClick.AddListener(() =>
            {
                if (closing)
                {
                    return;
                }
                closing = true;
                Se.Play(Se.Transition);
                StartCoroutine(TitleFadeRoutine(go, sprite, skinTex));
            });

            // Touch To Start (点滅)
            var touchText = UiFactory.CreateText(go.transform, "TouchToStart",
                "Touch To Start", 46, Color.white);
            touchText.fontStyle = FontStyle.Bold;
            touchText.raycastTarget = false;
            var outline = touchText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
            var touchRect = touchText.rectTransform;
            touchRect.anchorMin = touchRect.anchorMax = new Vector2(0.5f, 0.5f);
            touchRect.pivot = new Vector2(0.5f, 0.5f);
            touchRect.anchoredPosition = new Vector2(0f, -40f);
            touchRect.sizeDelta = new Vector2(900f, UiFactory.LineHeight(46));
            StartCoroutine(TitleBlinkRoutine(touchText));

            UpdateTitleStatus();
        }

        /// <summary>タイトル下部の行配置 (下端基準・中央寄せ)。</summary>
        static void SetTitleRow(RectTransform rect, float bottom, float height, float width)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        void UpdateTitleStatus()
        {
            if (_titleGo == null)
            {
                return; // タイトルは閉じられた
            }
            bool busy = GoogleAccount.IsLoginInProgress;
            bool loggedIn = GoogleAccount.IsLoggedIn;
            _titleGoogleButton.gameObject.SetActive(busy || !loggedIn);
            _titleStatusPanel.SetActive(!busy && loggedIn);
            if (busy)
            {
                _titleGoogleLabel.text = "ブラウザで認証中... (タップで中止)";
            }
            else if (!loggedIn)
            {
                _titleGoogleLabel.text = "Google でログイン";
            }
            else
            {
                _titleStatusText.text = "Google 同期: " + GoogleAccount.Email;
            }
        }

        /// <summary>
        /// タイトルからの Google ログイン。「はじめる」で先に進んでもポーリングは続き、
        /// 完了時はトーストで知らせる。成功後は Drive と双方向に同期する (取込 → 保存)。
        /// </summary>
        async System.Threading.Tasks.Task TitleLoginAsync()
        {
            Se.Play(Se.Tap);
            var login = GoogleAccount.LoginAsync();
            UpdateTitleStatus(); // 認証待ち表示へ
            bool ok = await login;
            UpdateTitleStatus();
            if (!ok)
            {
                if (!string.IsNullOrEmpty(GoogleAccount.LastLoginError))
                {
                    Se.Play(Se.Error);
                    UiFactory.ShowToast(GoogleAccount.LastLoginError, true);
                }
                else
                {
                    UiFactory.ShowToast("ログインを中止しました");
                }
                return;
            }
            Se.Play(Se.Confirm);
            UiFactory.ShowToast("Google にログインしました: " + GoogleAccount.Email);
            try
            {
                await MypageService.PullFromDriveAsync(false);
                await MypageService.PushToDriveAsync();
                UiFactory.ShowToast("Google Drive と同期しました");
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("同期に失敗: " + e.Message, true);
            }
            UpdateTitleStatus();
        }

        /// <summary>Touch To Start のゆっくりした点滅 (タイトルが消えるまで)。</summary>
        IEnumerator TitleBlinkRoutine(Text text)
        {
            float t = 0f;
            while (text != null)
            {
                t += Time.deltaTime;
                var c = text.color;
                c.a = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2f / 1.4f);
                text.color = c;
                yield return null;
            }
        }

        IEnumerator TitleFadeRoutine(GameObject title, Sprite sprite, Texture2D skinTex)
        {
            // CanvasGroup で子 (画像・ボタン・テキスト) をまとめてフェードアウト
            var group = title.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            float t = 0f;
            const float fade = 0.4f;
            while (t < fade)
            {
                t += Time.deltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }
            _titleGo = null;
            Destroy(title);
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
