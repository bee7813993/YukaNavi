using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 画面下部に常時表示するナビゲーションバー (戻る / メニュー / ホーム) と、
    /// メニューボタンで画面いっぱいに展開するメニュー (リンクラ型)。
    /// メニューは上部に主要機能の大バナー、下部に機能グリッドを置き、背景はホームが透ける。
    /// </summary>
    public class GlobalNav : MonoBehaviour
    {
        /// <summary>バーの高さ。各画面はこの分だけ下部を空けてレイアウトする。</summary>
        public const float BarHeight = 140f;

        ScreenManager _screens;
        GameObject _menuPanel;
        RectTransform _menuContent;
        Image _menuButtonIcon;
        Sprite _menuSprite;
        Sprite _closeSprite;

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
            layout.padding = new RectOffset(8, 8, 8, 8);

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
            // ホームが透けるオーバーレイ (リンクラ風に透過多め)
            var overlay = _menuPanel.AddComponent<Image>();
            overlay.color = new Color(0.95f, 0.93f, 0.99f, 0.55f);

            // 中身 (ナビバーの上・画面端に余白)
            _menuContent = UiFactory.CreatePanel(panelRect, "Content");
            _menuContent.anchorMin = new Vector2(0f, 0f);
            _menuContent.anchorMax = new Vector2(1f, 1f);
            _menuContent.offsetMin = new Vector2(40f, BarHeight + 30f);
            _menuContent.offsetMax = new Vector2(-40f, -70f);

            // 大バナー: 主要2機能 (専用のバナー画像)
            AddBigBanner(_menuContent, 0, "曲をさがす",
                "Art/UI/Banners/yukanavi_banner_search_song_1000x220",
                () => _screens.Show<SearchScreen>());
            AddBigBanner(_menuContent, 1, "予約一覧",
                "Art/UI/Banners/yukanavi_banner_queue_1000x220",
                () => _screens.Show<QueueScreen>());

            // 機能グリッド (2列)。リンクラ同様に下部 (ナビバーの上) に寄せ、中間はホームが透ける
            var grid = UiFactory.CreatePanel(_menuContent, "Grid");
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 0f);
            grid.pivot = new Vector2(0.5f, 0f);
            grid.anchoredPosition = new Vector2(0f, 20f);
            grid.sizeDelta = new Vector2(0f, 380f);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            gridLayout.cellSize = new Vector2(486f, 175f);
            gridLayout.spacing = new Vector2(24f, 24f);
            gridLayout.childAlignment = TextAnchor.LowerCenter;

            AddGridItem(grid, "マイページ", "履歴・お気に入り",
                "Art/UI/Icons/yukanavi_icon_mypage_256", () => _screens.Show<MypageScreen>());
            AddGridItem(grid, "リモコン", "プレイヤー操作",
                "Art/UI/Icons/yukanavi_icon_remote_256", () => _screens.Show<PlayerScreen>());
            AddGridItem(grid, "きせかえ", "背景とキャラの変更",
                "Art/UI/Icons/yukanavi_icon_skin_256", () => _screens.Show<SkinScreen>());
            AddGridItem(grid, "接続設定", "サーバーとの接続",
                "Art/UI/Icons/yukanavi_icon_settings_256", () => _screens.Show<ConnectScreen>());

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

            var labelText = UiFactory.CreateText(button.transform, "Label", label, 34,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(labelText.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(124f, 56f);
            labelText.rectTransform.offsetMax = new Vector2(-12f, -32f);
            var captionText = UiFactory.CreateText(button.transform, "Caption", caption, 22,
                UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(captionText.rectTransform);
            captionText.rectTransform.offsetMin = new Vector2(124f, 28f);
            captionText.rectTransform.offsetMax = new Vector2(-12f, -104f);
            button.onClick.AddListener(() =>
            {
                CloseMenu();
                Se.Play(Se.Transition);
                onClick();
            });
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
            if (_menuPanel.activeSelf)
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
            _menuPanel.SetActive(true);
            if (_closeSprite != null)
            {
                _menuButtonIcon.sprite = _closeSprite;
            }
            StartCoroutine(PopRoutine());
        }

        void CloseMenu()
        {
            _menuPanel.SetActive(false);
            if (_menuSprite != null)
            {
                _menuButtonIcon.sprite = _menuSprite;
            }
        }

        /// <summary>メニューをふわっと出す (0.96 → 1.0)。</summary>
        IEnumerator PopRoutine()
        {
            const float duration = 0.16f;
            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                float k = e / duration;
                float scale = Mathf.Lerp(0.96f, 1f, 1f - (1f - k) * (1f - k));
                _menuContent.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            _menuContent.localScale = Vector3.one;
        }

        void OnBack()
        {
            // メニュー展開中は閉じるだけ
            if (_menuPanel.activeSelf)
            {
                CloseMenu();
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
