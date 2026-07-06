using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 画面下部に常時表示するナビゲーションバー (戻る / メニュー / ホーム)。
    /// リンクラ等のソシャゲ UI を参考にした構成。「メニュー」から各機能画面へ遷移する。
    /// 画面レイヤーより後に生成されるため常に最前面に描画される。
    /// </summary>
    public class GlobalNav : MonoBehaviour
    {
        /// <summary>バーの高さ。各画面はこの分だけ下部を空けてレイアウトする。</summary>
        public const float BarHeight = 140f;

        ScreenManager _screens;
        GameObject _menuPanel;
        GameObject _closeCatcher;

        public static GlobalNav Create(Transform canvasParent, ScreenManager screens)
        {
            var go = new GameObject("GlobalNav");
            go.transform.SetParent(canvasParent, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            var nav = go.AddComponent<GlobalNav>();
            nav._screens = screens;
            nav.Build();
            return nav;
        }

        void Build()
        {
            // メニュー外タップで閉じるためのキャッチャー (メニュー表示中のみ有効)
            _closeCatcher = new GameObject("MenuCloseCatcher");
            _closeCatcher.transform.SetParent(transform, false);
            var catcherRect = _closeCatcher.AddComponent<RectTransform>();
            UiFactory.StretchFull(catcherRect);
            var catcherImg = _closeCatcher.AddComponent<Image>();
            catcherImg.color = new Color(0f, 0f, 0f, 0f);
            var catcherButton = _closeCatcher.AddComponent<Button>();
            catcherButton.transition = Selectable.Transition.None;
            catcherButton.onClick.AddListener(CloseMenu);
            _closeCatcher.SetActive(false);

            BuildMenuPanel();

            // バー本体
            var bar = UiFactory.CreatePanel(transform, "Bar", new Color(1f, 1f, 1f, 0.96f));
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, BarHeight);
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.spacing = 4f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            AddBarButton(bar, "戻る", OnBack);
            AddBarButton(bar, "メニュー", ToggleMenu);
            AddBarButton(bar, "ホーム", OnHome);
        }

        void BuildMenuPanel()
        {
            _menuPanel = new GameObject("MenuPanel");
            _menuPanel.transform.SetParent(transform, false);
            var panelRect = _menuPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, BarHeight + 14f);
            panelRect.sizeDelta = new Vector2(640f, 0f);
            var panelImg = _menuPanel.AddComponent<Image>();
            panelImg.color = new Color(1f, 1f, 1f, 0.97f);
            var layout = _menuPanel.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 10f;
            layout.padding = new RectOffset(14, 14, 14, 14);
            var fitter = _menuPanel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddMenuItem("曲をさがす", true, () => _screens.Show<SearchScreen>());
            AddMenuItem("予約一覧", true, () => _screens.Show<QueueScreen>());
            AddMenuItem("リモコン", true, () => _screens.Show<PlayerScreen>());
            AddMenuItem("接続設定", true, () => _screens.Show<ConnectScreen>());

            _menuPanel.SetActive(false);
        }

        void AddBarButton(RectTransform bar, string label, System.Action onClick)
        {
            var button = UiFactory.CreateButton(bar, label, label,
                new Color(1f, 1f, 1f, 0f), UiFactory.Primary, 36);
            button.onClick.AddListener(() => onClick());
        }

        void AddMenuItem(string label, bool enabled, System.Action onClick)
        {
            var button = UiFactory.CreateButton(_menuPanel.transform, label, label,
                UiFactory.Primary, Color.white, 34);
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 100f;
            button.interactable = enabled;
            if (onClick != null)
            {
                button.onClick.AddListener(() =>
                {
                    CloseMenu();
                    Se.Play(Se.Transition);
                    onClick();
                });
            }
        }

        void ToggleMenu()
        {
            Se.Play(Se.Tap);
            bool open = !_menuPanel.activeSelf;
            _menuPanel.SetActive(open);
            _closeCatcher.SetActive(open);
        }

        void CloseMenu()
        {
            _menuPanel.SetActive(false);
            _closeCatcher.SetActive(false);
        }

        void OnBack()
        {
            CloseMenu();
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
