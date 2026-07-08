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
        GameObject _urlCard;
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
            UiFactory.OnSubmit(_searchInput, RunSearch); // Enter でも検索

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
                new[] { "リスターDBでさがす", "ファイル名で探す(Everything)" }, 25);
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
            // カードの高さは文字の大きさ設定に追従 (ラベル1行 + キャプション2行)
            float wayCellHeight = 20f + (UiFactory.ScaledFontSize(32) + 10f) + UiFactory.LineHeight(21) * 2f;
            grid.sizeDelta = new Vector2(grid.sizeDelta.x, wayCellHeight * 4f + 16f * 3f);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            gridLayout.cellSize = new Vector2(508f, wayCellHeight);
            gridLayout.spacing = new Vector2(16f, 16f);
            gridLayout.childAlignment = TextAnchor.UpperCenter;

            // 履歴・お気に入り曲はマイページ (ナビから常時行ける) にあるためここには置かない
            AddWayCard(grid, "作品名で探す", "頭文字インデックス\nから作品をさがす",
                () => NameIndexScreen.Open(Manager, "program"));
            AddWayCard(grid, "歌手名で探す", "頭文字インデックス\nから歌手をさがす",
                () => NameIndexScreen.Open(Manager, "artist"));
            AddWayCard(grid, "シリーズ名で探す", "シリーズから作品を\nたどってさがす",
                () => NameIndexScreen.Open(Manager, "group"));
            AddWayCard(grid, "期別リスト", "アニメの期ごとに\n作品からさがす",
                () => PeriodScreen.Open(Manager));
            AddWayCard(grid, "年代別リスト", "1年ごとにまとめて\n作品からさがす",
                () => PeriodScreen.OpenYearly(Manager));
            AddWayCard(grid, "お気に入り検索", "☆保存した歌手・\n作品・ワードから",
                () => MypageScreen.Open(Manager, 3));
            _urlCard = AddWayCard(grid, "URLでリクエスト", "YouTube等のURLを\n直接再生する",
                () => UrlRequestScreen.Open(Manager)).gameObject;

            _listerMode = PlayerPrefs.GetInt(ModePrefKey, 1) == 1;
            ApplyMode();
        }

        public override void OnShow()
        {
            // 予約の変更 (曲えらびなおし) 中はタイトルで状態を示す
            _topTitle.text = ReserveScreen.EditSession != null ? "差しかえる曲をえらぶ" : "曲をさがす";
            RebuildSavedChips(); // 保存/解除の変更を反映する
            _ = ApplySearchAvailabilityAsync();
        }

        /// <summary>
        /// サーバーで使えない検索タブを隠す (リスターDB 未設定 / Everything 未起動)。
        /// 取得に失敗した時は両方表示のまま (検索実行時にサーバーがエラーを返す)。
        /// </summary>
        async System.Threading.Tasks.Task ApplySearchAvailabilityAsync()
        {
            bool lister = true;
            bool everything = true;
            bool internet = true;
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                if (caps.Features != null)
                {
                    lister = caps.Features.ListerSearch;
                    everything = caps.Features.EverythingSearch;
                    internet = caps.Features.Internet;
                }
            }
            catch (System.Exception)
            {
                return;
            }
            _listerTab.gameObject.SetActive(lister);
            _fileTab.gameObject.SetActive(everything);
            _urlCard.SetActive(internet);
            // 使えない方が選択中だったら残っている方へ寄せる
            if (!lister && _listerMode && everything)
            {
                _listerMode = false;
                ApplyMode();
            }
            else if (!everything && !_listerMode && lister)
            {
                _listerMode = true;
                ApplyMode();
            }
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
            UiFactory.AddHorizontalMoreIndicator(scroll); // 右に続きがあるとき「›」を出す

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
                var le = chip.gameObject.AddComponent<LayoutElement>();
                // Text.preferredWidth はレイアウト前に信用できず、コンテンツ総幅が狂って
                // 後ろのチップまでスクロールできなくなるため概算幅を使う
                le.preferredWidth = Mathf.Max(UiFactory.EstimateTextWidth(item.Label, 24) + 48f, 110f);
                chip.onClick.AddListener(() =>
                {
                    Se.Play(Se.Transition);
                    SearchResultScreen.Open(Manager, SearchResultScreen.FromSavedSearch(item));
                });
                _chips.Add(chip.gameObject);
            }
        }

        /// <summary>「他の方法でさがす」のカード (メニューの機能カードと同じ雰囲気)。</summary>
        Button AddWayCard(RectTransform grid, string label, string caption, System.Action onTap)
        {
            float labelHeight = UiFactory.ScaledFontSize(32) + 10f;
            var button = UiFactory.CreateButton(grid, label, "",
                new Color(1f, 1f, 1f, 0.92f), Color.white);
            var labelText = UiFactory.CreateText(button.transform, "Label", label, 32,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var labelRect = labelText.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -10f);
            labelRect.offsetMin = new Vector2(28f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-12f, labelRect.offsetMax.y);
            labelRect.sizeDelta = new Vector2(labelRect.sizeDelta.x, labelHeight);
            UiFactory.FitLabel(labelText, 20);
            var captionText = UiFactory.CreateText(button.transform, "Caption", caption, 21,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            var captionRect = captionText.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -(10f + labelHeight));
            captionRect.offsetMin = new Vector2(28f, captionRect.offsetMin.y);
            captionRect.offsetMax = new Vector2(-12f, captionRect.offsetMax.y);
            captionRect.sizeDelta = new Vector2(captionRect.sizeDelta.x, UiFactory.LineHeight(21) * 2f);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                onTap();
            });
            return button;
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
