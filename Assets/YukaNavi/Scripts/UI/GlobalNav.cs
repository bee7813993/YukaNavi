using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 画面下部に常時表示するナビゲーションバー (戻る / メニュー / ホーム) と、
    /// メニューボタンで画面いっぱいに展開するメニュー (リンクラ型)。
    /// メニューは上部に主要機能の大バナー、下部に機能グリッドを置く。
    /// 背景の透過はホーム表示中のみ (他画面ではフォームの文字が透けて読みづらく、
    /// App Store 審査でも「画面が重なって見える」と指摘されたため、ほぼ不透明にする)。
    /// </summary>
    public class GlobalNav : MonoBehaviour
    {
        /// <summary>
        /// バーの高さ (下のセーフエリア込み)。各画面はこの分だけ下部を空けてレイアウトする。
        /// </summary>
        public static float BarHeight => 140f + UiFactory.SafeBottom;

        ScreenManager _screens;
        GameObject _menuPanel;
        RectTransform _menuContent;
        CanvasGroup _menuGroup;
        Image _menuOverlay;
        Image _menuButtonIcon;
        Sprite _menuSprite;
        Sprite _closeSprite;
        bool _menuOpen;
        Coroutine _menuAnim;

        public static GlobalNav Instance { get; private set; }

        public static GlobalNav Create(Transform canvasParent, ScreenManager screens)
        {
            var go = new GameObject("GlobalNav");
            go.transform.SetParent(canvasParent, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            var nav = go.AddComponent<GlobalNav>();
            nav._screens = screens;
            nav.Build();
            Instance = nav;
            return nav;
        }

        /// <summary>UI を作り直す (テーマ色の変更時)。</summary>
        public void Rebuild()
        {
            if (_menuAnim != null)
            {
                StopCoroutine(_menuAnim); // 破棄済みの UI を触らないよう開閉アニメを止める
                _menuAnim = null;
            }
            _menuOpen = false;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            Build();
        }

        void Build()
        {
            BuildMenuPanel();

            // バー本体
            var bar = UiFactory.CreatePanel(transform, "Bar", new Color(1f, 1f, 1f, 0.96f));
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, BarHeight);
            // 上向きのソフトシャドウで画面と区切る
            var barShadow = bar.gameObject.AddComponent<Shadow>();
            barShadow.effectColor = new Color(0.22f, 0.12f, 0.38f, 0.2f);
            barShadow.effectDistance = new Vector2(0f, 5f);
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.spacing = 4f;
            // ボタンはホームバー (下のセーフエリア) を避けた上側に置く
            layout.padding = new RectOffset(8, 8, 8, 8 + Mathf.RoundToInt(UiFactory.SafeBottom));

            AddBarButton(bar, "Back", "Art/UI/Icons/yukanavi_icon_back_256", OnBack, out _);
            AddBarButton(bar, "Menu", "Art/UI/Icons/yukanavi_icon_menu_256", ToggleMenu,
                out _menuButtonIcon);
            AddBarButton(bar, "Home", "Art/UI/Icons/yukanavi_icon_home_256", OnHome, out _);
            _menuSprite = _menuButtonIcon.sprite;
            _closeSprite = UiFactory.LoadSprite("Art/UI/Icons/yukanavi_icon_close_256");
        }

        /// <summary>全画面のメニューパネル (リンクラ型)。</summary>
        void BuildMenuPanel()
        {
            _menuPanel = new GameObject("MenuPanel");
            _menuPanel.transform.SetParent(transform, false);
            var panelRect = _menuPanel.AddComponent<RectTransform>();
            UiFactory.StretchFull(panelRect);
            _menuGroup = _menuPanel.AddComponent<CanvasGroup>(); // 開閉フェード用
            // オーバーレイ。透過度は開くたびに OpenMenu が現在の画面に合わせて決める
            // (ホームの上では壁紙が透ける演出、他画面の上ではほぼ不透明)
            _menuOverlay = _menuPanel.AddComponent<Image>();
            _menuOverlay.color = new Color(0.95f, 0.93f, 0.99f, 0.97f);

            // 中身 (ナビバーの上・画面端に余白)
            _menuContent = UiFactory.CreatePanel(panelRect, "Content");
            _menuContent.anchorMin = new Vector2(0f, 0f);
            _menuContent.anchorMax = new Vector2(1f, 1f);
            _menuContent.offsetMin = new Vector2(40f, BarHeight + 30f);
            _menuContent.offsetMax = new Vector2(-40f, -70f - UiFactory.SafeTop);

            // 大バナー: 主要2機能 (専用のバナー画像)。
            // 横向き (ダッシュボード表示中) は縦の余裕がないため、バナーは出さず
            // グリッド項目で代替する
            bool landscape = Screen.width > Screen.height;
            if (!landscape)
            {
                AddBigBanner(_menuContent, 0, "曲をさがす",
                    "Art/UI/Banners/yukanavi_banner_search_song_1000x220",
                    () => _screens.Show<SearchScreen>());
                AddBigBanner(_menuContent, 1, "予約一覧",
                    "Art/UI/Banners/yukanavi_banner_queue_1000x220",
                    () => _screens.Show<QueueScreen>());
            }

            // 機能グリッド (2列)。リンクラ同様に下部 (ナビバーの上) に寄せ、中間はホームが透ける
            var grid = UiFactory.CreatePanel(_menuContent, "Grid");
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 0f);
            grid.pivot = new Vector2(0.5f, 0f);
            grid.anchoredPosition = new Vector2(0f, 20f);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = landscape ? 3 : 2;
            // セルの高さはラベル+キャプション (文字の大きさ設定に追従) が収まる分を確保する
            float cellH = Mathf.Max(175f,
                16f + UiFactory.LineHeight(34) + 4f + UiFactory.LineHeight(22) + 16f);
            gridLayout.cellSize = new Vector2(486f, cellH);
            gridLayout.spacing = new Vector2(24f, 24f);
            gridLayout.childAlignment = TextAnchor.LowerCenter;
            int itemCount = landscape ? 8 : 6; // 横向きはバナー代替の2項目が増える
            int rowCount = Mathf.CeilToInt(itemCount / (float)gridLayout.constraintCount);
            grid.sizeDelta = new Vector2(0f, cellH * rowCount + 24f * (rowCount - 1));

            if (landscape)
            {
                AddGridItem(grid, "曲をさがす", "キーワードでさがす",
                    "Art/UI/Icons/yukanavi_icon_search_song_256", () => _screens.Show<SearchScreen>());
                AddGridItem(grid, "予約一覧", "順番の確認と操作",
                    "Art/UI/Icons/yukanavi_icon_queue_256", () => _screens.Show<QueueScreen>());
            }

            AddGridItem(grid, "マイページ", "履歴・お気に入り",
                "Art/UI/Icons/yukanavi_icon_mypage_256", () => _screens.Show<MypageScreen>());
            AddGridItem(grid, "リモコン", "プレイヤー操作",
                "Art/UI/Icons/yukanavi_icon_remote_256", () => _screens.Show<PlayerScreen>());
            AddGridItem(grid, "きせかえ", "背景とキャラの変更",
                "Art/UI/Icons/yukanavi_icon_skin_256", () => _screens.Show<SkinScreen>());
            AddGridItem(grid, "設定", "接続と文字の大きさ",
                "Art/UI/Icons/yukanavi_icon_settings_256", () => _screens.Show<ConnectScreen>());
            AddGridItem(grid, "Web版を開く", "ブラウザで表示",
                "Art/UI/Icons/yukanavi_icon_room_door_256", OpenWebVersion);
            AddGridItem(grid, "ダッシュボード", "タブレット据え置き表示",
                "Art/UI/Icons/yukanavi_icon_queue_256", () => _screens.Show<DashboardScreen>());

            _menuPanel.SetActive(false);
        }

        /// <summary>メニュー上部の大きなバナーボタン (画像バナー。1000x220 素材)。</summary>
        void AddBigBanner(RectTransform parent, int index, string name, string bannerPath,
                          System.Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = UiFactory.LoadSprite(bannerPath);
            img.preserveAspect = true;
            var rect = img.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -60f - index * 245f);
            rect.sizeDelta = new Vector2(0f, 220f);
            UiFactory.AddShadow(go, 5f);

            var button = go.AddComponent<Button>();
            go.AddComponent<PressEffect>();
            button.onClick.AddListener(() =>
            {
                CloseMenu();
                Se.Play(Se.Transition);
                onClick();
            });
        }

        /// <summary>メニュー下部の機能カード (アイコン+ラベル+説明)。</summary>
        void AddGridItem(RectTransform grid, string label, string caption, string iconPath,
                         System.Action onClick)
        {
            var button = UiFactory.CreateButton(grid, label, "",
                new Color(1f, 1f, 1f, 0.88f), Color.white); // 半透明白 (背景がうっすら透ける)

            var icon = UiFactory.CreateImage(button.transform, "Icon", iconPath);
            icon.color = UiFactory.Primary; // 白単色素材を紫に着色
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(28f, 0f);
            iconRect.sizeDelta = new Vector2(72f, 72f);

            // ラベル (上) + キャプション (下)。高さは文字の大きさ設定に追従し、
            // 収まらない文言は枠内で自動縮小する (固定配置だと大きい設定で重なる)
            float labelH = UiFactory.LineHeight(34);
            var labelText = UiFactory.CreateText(button.transform, "Label", label, 34,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var labelRect = labelText.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -16f);
            labelRect.offsetMin = new Vector2(124f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-12f, labelRect.offsetMax.y);
            labelRect.sizeDelta = new Vector2(labelRect.sizeDelta.x, labelH);
            UiFactory.FitLabel(labelText);

            var captionText = UiFactory.CreateText(button.transform, "Caption", caption, 22,
                UiFactory.TextMuted, TextAnchor.MiddleLeft);
            var captionRect = captionText.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -16f - labelH - 4f);
            captionRect.offsetMin = new Vector2(124f, captionRect.offsetMin.y);
            captionRect.offsetMax = new Vector2(-12f, captionRect.offsetMax.y);
            captionRect.sizeDelta = new Vector2(captionRect.sizeDelta.x, UiFactory.LineHeight(22));
            UiFactory.FitLabel(captionText);
            button.onClick.AddListener(() =>
            {
                CloseMenu();
                Se.Play(Se.Transition);
                onClick();
            });
        }

        /// <summary>Web 版をブラウザで開く (かんたん認証があればパス付き URL で認証を通す)。</summary>
        static void OpenWebVersion()
        {
            string url = AppConfig.ServerUrl;
            if (!string.IsNullOrEmpty(AppConfig.EasyPass))
            {
                url = url.TrimEnd('/') + "/?easypass="
                    + System.Uri.EscapeDataString(AppConfig.EasyPass);
            }
            Application.OpenURL(url);
        }

        /// <summary>ナビバーのボタン (アイコンのみ)。</summary>
        void AddBarButton(RectTransform bar, string name, string iconPath, System.Action onClick,
                          out Image iconImage)
        {
            var button = UiFactory.CreateButton(bar, name, "",
                new Color(1f, 1f, 1f, 0f), UiFactory.Primary, 36);

            iconImage = UiFactory.CreateImage(button.transform, "Icon", iconPath);
            iconImage.color = UiFactory.Primary; // 白単色素材を紫に着色
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconRect = iconImage.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(64f, 64f);

            button.onClick.AddListener(() => onClick());
        }

        void ToggleMenu()
        {
            Se.Play(Se.Tap);
            if (_menuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        void OpenMenu()
        {
            _menuOpen = true;
            // ホームの上では壁紙・キャラが透ける演出 (リンクラ風)。それ以外の画面の上では
            // 下のフォームの文字が透けて読みづらいため、ほぼ不透明にして重なりを断つ
            bool overHome = _screens.Current is HomeScreen;
            var c = _menuOverlay.color;
            c.a = overHome ? 0.55f : 0.97f;
            _menuOverlay.color = c;
            _menuPanel.SetActive(true);
            if (_closeSprite != null)
            {
                _menuButtonIcon.sprite = _closeSprite;
            }
            if (_menuAnim != null)
            {
                StopCoroutine(_menuAnim);
            }
            _menuAnim = StartCoroutine(MenuRoutine(true));
        }

        /// <summary>
        /// メニューを閉じる (開いていなければ何もしない)。ナビ以外の経路で画面を
        /// 切り替えるとき (共有インテント等) にも呼ぶ — メニューの透過度は開いた時点の
        /// 画面に合わせて決まるため、下の画面だけ差し替わると表示が食い違う。
        /// </summary>
        public void CloseMenu()
        {
            if (!_menuOpen && !_menuPanel.activeSelf)
            {
                return;
            }
            _menuOpen = false;
            if (_menuSprite != null)
            {
                _menuButtonIcon.sprite = _menuSprite;
            }
            if (_menuAnim != null)
            {
                StopCoroutine(_menuAnim);
            }
            _menuAnim = StartCoroutine(MenuRoutine(false));
        }

        /// <summary>
        /// メニューの開閉アニメ。フェード+コンテンツが下からふわっと上がる (閉じは逆再生)。
        /// 開閉を連打しても、そのときの表示状態から続きが再生される。
        /// </summary>
        IEnumerator MenuRoutine(bool open)
        {
            const float duration = 0.18f;
            float from = _menuGroup.alpha;
            float to = open ? 1f : 0f;
            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(e / duration), 2f); // easeOut
                float a = Mathf.Lerp(from, to, k);
                _menuGroup.alpha = a;
                _menuContent.anchoredPosition = new Vector2(0f, -50f * (1f - a));
                float scale = Mathf.Lerp(0.97f, 1f, a);
                _menuContent.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            _menuGroup.alpha = to;
            _menuContent.anchoredPosition = new Vector2(0f, -50f * (1f - to));
            _menuContent.localScale = open ? Vector3.one : new Vector3(0.97f, 0.97f, 1f);
            if (!open)
            {
                _menuPanel.SetActive(false);
            }
            _menuAnim = null;
        }

        void Update()
        {
            // Android の戻るボタン / 戻るジェスチャ (Input System では Escape として届く)。
            // ナビの「戻る」ボタンと同じ動作にする。エディタでも Esc キーで確認できる
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                OnBack();
            }
        }

        void OnBack()
        {
            // メニュー展開中は閉じるだけ
            if (_menuOpen)
            {
                CloseMenu();
                Se.Play(Se.Tap);
                return;
            }
            // 画面内の階層・検索履歴が先 (期別リストの階層戻りや再検索の遡りに使う)
            var current = _screens.Current;
            if (current != null && current.OnBackRequested())
            {
                Se.Play(Se.Tap);
                return;
            }
            if (_screens.Back())
            {
                Se.Play(Se.Transition);
            }
        }

        void OnHome()
        {
            CloseMenu();
            Se.Play(Se.Transition);
            _screens.ShowAsRoot<HomeScreen>();
        }
    }
}
