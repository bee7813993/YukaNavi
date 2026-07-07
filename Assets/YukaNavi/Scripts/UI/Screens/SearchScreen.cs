using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 検索トップ画面 (キョクナビ風)。最上部にキーワード検索、その下に検索対象の切替
    /// (アニソンDB / ファイル名)、さらに「他の方法でさがす」動線 (期別リスト・履歴・
    /// お気に入り曲) を並べる。検索結果は SearchResultScreen に遷移して表示する。
    /// </summary>
    public class SearchScreen : ScreenBase
    {
        /// <summary>検索対象 (アニソンDB / ファイル名) の保存キー。結果画面の再検索も参照する。</summary>
        public const string ModePrefKey = "search.lister_mode";

        bool _listerMode = true;
        Button _listerTab;
        Button _fileTab;
        Text _topTitle;
        InputField _searchInput;
        RectTransform _chipContent;
        Text _chipHint;
        readonly System.Collections.Generic.List<GameObject> _chips =
            new System.Collections.Generic.List<GameObject>();

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, "曲をさがす");
            _topTitle = topBar.GetComponentInChildren<Text>();

            // キーワード検索 (入力 + ボタン)
            _searchInput = UiFactory.CreateInputField(transform, "SearchInput", "曲名・歌手・作品名など");
            var inputRect = _searchInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.anchoredPosition = new Vector2(0f, -134f);
            inputRect.offsetMin = new Vector2(20f, inputRect.offsetMin.y);
            inputRect.offsetMax = new Vector2(-260f, inputRect.offsetMax.y);
            inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, 92f);

            var searchButton = UiFactory.CreateButton(transform, "SearchButton", "検索",
                UiFactory.Primary, Color.white, 38);
            var searchBtnRect = searchButton.GetComponent<RectTransform>();
            searchBtnRect.anchorMin = searchBtnRect.anchorMax = new Vector2(1f, 1f);
            searchBtnRect.pivot = new Vector2(1f, 1f);
            searchBtnRect.anchoredPosition = new Vector2(-20f, -134f);
            searchBtnRect.sizeDelta = new Vector2(220f, 92f);
            searchButton.onClick.AddListener(RunSearch);

            // 保存したワードのチップ (横スクロール)。結果画面の ☆ で保存できる
            BuildSavedChips();

            // 検索対象の切替 (アニソンDB / ファイル名)
            var tabBar = UiFactory.CreatePanel(transform, "Tabs");
            tabBar.anchorMin = new Vector2(0f, 1f);
            tabBar.anchorMax = new Vector2(1f, 1f);
            tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -318f);
            tabBar.offsetMin = new Vector2(20f, tabBar.offsetMin.y);
            tabBar.offsetMax = new Vector2(-20f, tabBar.offsetMax.y);
            tabBar.sizeDelta = new Vector2(tabBar.sizeDelta.x, 80f);
            var tabs = UiFactory.CreateSegmentTabs(tabBar,
                new[] { "アニソンDBでさがす", "ファイル名でさがす" }, 28);
            _listerTab = tabs[0];
            _fileTab = tabs[1];
            _listerTab.onClick.AddListener(() => SetMode(true));
            _fileTab.onClick.AddListener(() => SetMode(false));

            // 他の方法でさがす
            var wayLabel = UiFactory.CreateText(transform, "WaysLabel", "他の方法でさがす", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var wayLabelRect = wayLabel.rectTransform;
            wayLabelRect.anchorMin = new Vector2(0f, 1f);
            wayLabelRect.anchorMax = new Vector2(1f, 1f);
            wayLabelRect.pivot = new Vector2(0.5f, 1f);
            wayLabelRect.anchoredPosition = new Vector2(0f, -428f);
            wayLabelRect.offsetMin = new Vector2(28f, wayLabelRect.offsetMin.y);
            wayLabelRect.offsetMax = new Vector2(-28f, wayLabelRect.offsetMax.y);
            wayLabelRect.sizeDelta = new Vector2(wayLabelRect.sizeDelta.x, 36f);

            var grid = UiFactory.CreatePanel(transform, "Ways");
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.anchoredPosition = new Vector2(0f, -474f);
            grid.offsetMin = new Vector2(20f, grid.offsetMin.y);
            grid.offsetMax = new Vector2(-20f, grid.offsetMax.y);
            grid.sizeDelta = new Vector2(grid.sizeDelta.x, 320f);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            gridLayout.cellSize = new Vector2(508f, 150f);
            gridLayout.spacing = new Vector2(16f, 16f);
            gridLayout.childAlignment = TextAnchor.UpperCenter;

            AddWayCard(grid, "期別リスト", "アニメの期ごとに\n作品からさがす",
                () => PeriodScreen.Open(Manager));
            AddWayCard(grid, "履歴", "予約したことが\nある曲から",
                () => MypageScreen.Open(Manager, 0));
            AddWayCard(grid, "お気に入り曲", "マイページの\nお気に入りから",
                () => MypageScreen.Open(Manager, 2));

            _listerMode = PlayerPrefs.GetInt(ModePrefKey, 1) == 1;
            ApplyMode();
        }

        public override void OnShow()
        {
            // 予約の変更 (曲えらびなおし) 中はタイトルで状態を示す
            _topTitle.text = ReserveScreen.EditSession != null ? "差しかえる曲をえらぶ" : "曲をさがす";
            RebuildSavedChips(); // 保存/解除の変更を反映する
        }

        /// <summary>保存ワードのチップ行 (横スクロール) を作る。</summary>
        void BuildSavedChips()
        {
            var panel = UiFactory.CreatePanel(transform, "SavedChips");
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -240f);
            panel.offsetMin = new Vector2(20f, panel.offsetMin.y);
            panel.offsetMax = new Vector2(-20f, panel.offsetMax.y);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, 66f);

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(panel, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            UiFactory.StretchFull(viewportRect);
            viewportGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _chipContent = contentGo.AddComponent<RectTransform>();
            _chipContent.anchorMin = new Vector2(0f, 0f);
            _chipContent.anchorMax = new Vector2(0f, 1f);
            _chipContent.pivot = new Vector2(0f, 0.5f);
            _chipContent.sizeDelta = Vector2.zero;
            _chipContent.anchoredPosition = Vector2.zero;
            var layout = contentGo.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 10f;
            layout.padding = new RectOffset(4, 4, 6, 6);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = _chipContent;
            scroll.viewport = viewportRect;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _chipHint = UiFactory.CreateText(panel, "Hint",
                "検索結果の ☆ でワードを保存すると、ここに並びます", 22, UiFactory.TextMuted,
                TextAnchor.MiddleLeft);
            UiFactory.StretchFull(_chipHint.rectTransform);
            _chipHint.rectTransform.offsetMin = new Vector2(8f, 0f);
        }

        void RebuildSavedChips()
        {
            foreach (var chip in _chips)
            {
                Destroy(chip);
            }
            _chips.Clear();

            var saved = LocalMypage.GetSavedSearches();
            _chipHint.gameObject.SetActive(saved.Count == 0);
            foreach (var search in saved)
            {
                var item = search;
                var chip = UiFactory.CreateButton(_chipContent, "Chip", item.Label,
                    UiFactory.PrimaryPale, UiFactory.PrimaryDark, 24);
                var text = chip.GetComponentInChildren<Text>();
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Max(text.preferredWidth + 44f, 110f);
                chip.onClick.AddListener(() =>
                {
                    Se.Play(Se.Transition);
                    SearchResultScreen.Open(Manager, SearchResultScreen.FromSavedSearch(item));
                });
                _chips.Add(chip.gameObject);
            }
        }

        /// <summary>「他の方法でさがす」のカード (メニューの機能カードと同じ雰囲気)。</summary>
        void AddWayCard(RectTransform grid, string label, string caption, System.Action onTap)
        {
            var button = UiFactory.CreateButton(grid, label, "",
                new Color(1f, 1f, 1f, 0.92f), Color.white);
            var labelText = UiFactory.CreateText(button.transform, "Label", label, 32,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(labelText.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(28f, 78f);
            labelText.rectTransform.offsetMax = new Vector2(-12f, -14f);
            var captionText = UiFactory.CreateText(button.transform, "Caption", caption, 21,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            UiFactory.StretchFull(captionText.rectTransform);
            captionText.rectTransform.offsetMin = new Vector2(28f, 12f);
            captionText.rectTransform.offsetMax = new Vector2(-12f, -76f);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                onTap();
            });
        }

        void SetMode(bool listerMode)
        {
            Se.Play(Se.Tap);
            if (_listerMode == listerMode)
            {
                return;
            }
            _listerMode = listerMode;
            PlayerPrefs.SetInt(ModePrefKey, listerMode ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMode();
        }

        void ApplyMode()
        {
            UiFactory.SetSegmentSelected(new[] { _listerTab, _fileTab }, _listerMode ? 0 : 1);
            var placeholder = (Text)_searchInput.placeholder;
            placeholder.text = _listerMode ? "曲名・歌手・作品名など" : "ファイル名の一部";
        }

        void RunSearch()
        {
            string keyword = (_searchInput.text ?? "").Trim();
            if (keyword == "")
            {
                Se.Play(Se.Error);
                return;
            }
            Se.Play(Se.Transition);
            SearchResultScreen.Open(Manager, new SearchResultScreen.SearchQuery
            {
                Kind = _listerMode
                    ? SearchResultScreen.QueryKind.ListerAnyword
                    : SearchResultScreen.QueryKind.Everything,
                Keyword = keyword,
                Label = "検索: " + keyword,
            });
        }
    }
}
