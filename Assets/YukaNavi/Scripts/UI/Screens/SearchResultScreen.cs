using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 検索結果画面。検索トップ (SearchScreen) や期別リスト (PeriodScreen) から遷移する。
    /// ListerDB の結果は曲単位のカードで表示し、同じ曲の複数動画 (ライブ映像/リリック
    /// ビデオ等) はカード内に並ぶ。カード内の 歌手 / 作品 / シリーズ / 制作 のテキストは
    /// リンクになっていて、タップでそのキーの再検索に切り替わる (Web 版と同じ操作感)。
    /// シリーズの再検索は曲一覧ではなく「シリーズの作品一覧」を表示する。
    /// 検索条件は画面内スタックに積まれ、ナビの「戻る」で1つ前へ遡れる。
    /// </summary>
    public class SearchResultScreen : ScreenBase
    {
        /// <summary>結果リストの表示上限 (UGUI の負荷対策)</summary>
        const int MaxRows = 100;

        public enum QueryKind
        {
            Everything,    // ファイル名 (Everything) あいまい検索
            ListerAnyword, // ListerDB あいまい検索
            ListerExact,   // ListerDB 完全一致 (再検索・期別リストの作品)
        }

        public class SearchQuery
        {
            public QueryKind Kind;
            public string Keyword;    // Everything / ListerAnyword
            public string ExactField; // ListerExact: program / artist / group / worker
            public string ExactValue;
            public string Label;      // タイトル表示用
        }

        static SearchQuery _pending;

        /// <summary>曲一覧の並び順 (端末に記憶する)。</summary>
        enum SortOrder
        {
            DateDesc, // ファイル更新日の新しい順 (既定)
            DateAsc,
            NameAsc,
        }

        const string SortPrefKey = "search.sort";
        const string GroupPrefKey = "search.group_songs";

        readonly List<SearchQuery> _queryStack = new List<SearchQuery>();
        readonly List<GameObject> _rows = new List<GameObject>();
        /// <summary>取得時の年齢制限曲設定。変更されていたら OnShow で引き直す。</summary>
        bool _loadedIncludeAgeLimit;
        GameObject _ageLimitModal;
        /// <summary>動画プレビュー可否 (capabilities の preview。取得失敗・旧サーバーは false)</summary>
        bool _previewEnabled;
        GameObject _previewModal;
        VideoPlayer _previewPlayer;
        RenderTexture _previewTexture;
        AudioSource _previewAudio;
        Slider _previewSlider;
        Text _previewTimeText;
        bool _previewSuppressSlider; // Update からの value 設定を onValueChanged が無視するため
        float _previewLastSeekAt = -1f; // 直近のユーザー操作時刻 (この直後は自動追従を止める)
        Text _titleText;
        Text _statusText;
        InputField _searchInput;
        RectTransform _listContent;
        GameObject _sortBar;
        Button[] _sortTabs;
        Button _saveButton;
        Text _saveButtonLabel;
        Button _groupToggle;
        Text _groupToggleLabel;
        SortOrder _sort = SortOrder.DateDesc;
        bool _groupSongs = true;
        int _searchSerial;

        /// <summary>検索条件を指定して結果画面を開く (検索履歴は新規に始まる)。</summary>
        public static void Open(ScreenManager manager, SearchQuery query)
        {
            _pending = query;
            manager.Show<SearchResultScreen>();
        }

        /// <summary>完全一致検索で開くショートカット。</summary>
        public static void OpenExact(ScreenManager manager, string field, string value)
        {
            Open(manager, MakeExactQuery(field, value));
        }

        static SearchQuery MakeExactQuery(string field, string value)
        {
            return new SearchQuery
            {
                Kind = QueryKind.ListerExact,
                ExactField = field,
                ExactValue = value,
                Label = FieldLabel(field) + ": " + value,
            };
        }

        static string FieldLabel(string field)
        {
            switch (field)
            {
                case "program":
                    return "作品";
                case "artist":
                    return "歌手";
                case "group":
                    return "シリーズ";
                case "worker":
                    return "制作";
                default:
                    return field;
            }
        }

        static float EstimateWidth(string text, int fontSize)
        {
            return UiFactory.EstimateTextWidth(text, fontSize);
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, "検索結果");
            _titleText = topBar.GetComponentInChildren<Text>();
            // 右端の ☆ ボタンと被らないようタイトルを内側に寄せる
            _titleText.rectTransform.offsetMin = new Vector2(104f, 0f);
            _titleText.rectTransform.offsetMax = new Vector2(-104f, 0f);

            // この検索を保存 (☆ トグル)。保存すると検索トップのチップに並ぶ
            _saveButton = UiFactory.CreateButton(topBar, "SaveSearch", "☆",
                UiFactory.PrimaryPale, UiFactory.Primary, 44);
            _saveButton.image.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            _saveButtonLabel = _saveButton.GetComponentInChildren<Text>();
            var saveRect = _saveButton.GetComponent<RectTransform>();
            saveRect.anchorMin = saveRect.anchorMax = new Vector2(1f, 0.5f);
            saveRect.pivot = new Vector2(1f, 0.5f);
            saveRect.anchoredPosition = new Vector2(-20f, 0f);
            saveRect.sizeDelta = new Vector2(76f, 76f);
            _saveButton.onClick.AddListener(ToggleSaveSearch);

            // 検索フォーム (結果画面でもワードを変えて検索し直せる)
            _searchInput = UiFactory.CreateInputField(transform, "SearchInput", "ワードを変えて検索");
            var inputRect = _searchInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.anchoredPosition = new Vector2(0f, -122f);
            inputRect.offsetMin = new Vector2(20f, inputRect.offsetMin.y);
            inputRect.offsetMax = new Vector2(-260f, inputRect.offsetMax.y);
            inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, 84f);

            var searchButton = UiFactory.CreateButton(transform, "SearchButton", "検索",
                UiFactory.Primary, Color.white, 34);
            var searchBtnRect = searchButton.GetComponent<RectTransform>();
            searchBtnRect.anchorMin = searchBtnRect.anchorMax = new Vector2(1f, 1f);
            searchBtnRect.pivot = new Vector2(1f, 1f);
            searchBtnRect.anchoredPosition = new Vector2(-20f, -122f);
            searchBtnRect.sizeDelta = new Vector2(220f, 84f);
            searchButton.onClick.AddListener(RunKeywordSearch);
            UiFactory.OnSubmit(_searchInput, RunKeywordSearch); // Enter でも検索

            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -214f);
            statusRect.sizeDelta = new Vector2(-40f, 40f);

            // 並び順と表示切替 (曲一覧のときだけ表示)
            var sortBar = UiFactory.CreatePanel(transform, "SortBar");
            sortBar.anchorMin = new Vector2(0f, 1f);
            sortBar.anchorMax = new Vector2(1f, 1f);
            sortBar.pivot = new Vector2(0.5f, 1f);
            sortBar.anchoredPosition = new Vector2(0f, -258f);
            sortBar.offsetMin = new Vector2(20f, sortBar.offsetMin.y);
            sortBar.offsetMax = new Vector2(-20f, sortBar.offsetMax.y);
            sortBar.sizeDelta = new Vector2(sortBar.sizeDelta.x, 68f);

            var tabArea = UiFactory.CreatePanel(sortBar, "Tabs");
            tabArea.anchorMin = new Vector2(0f, 0f);
            tabArea.anchorMax = new Vector2(1f, 1f);
            tabArea.offsetMax = new Vector2(-310f, 0f);
            _sortTabs = UiFactory.CreateSegmentTabs(tabArea,
                new[] { "新しい順", "古い順", "曲名順" }, 24);
            for (int i = 0; i < _sortTabs.Length; i++)
            {
                int index = i;
                _sortTabs[i].onClick.AddListener(() => SetSort((SortOrder)index));
            }

            // 曲でまとめる / ファイルごとに表示 の切替
            _groupToggle = UiFactory.CreateButton(sortBar, "GroupToggle", "曲でまとめる",
                UiFactory.Primary, Color.white, 24);
            _groupToggleLabel = _groupToggle.GetComponentInChildren<Text>();
            var groupRect = _groupToggle.GetComponent<RectTransform>();
            groupRect.anchorMin = new Vector2(1f, 0f);
            groupRect.anchorMax = new Vector2(1f, 1f);
            groupRect.pivot = new Vector2(1f, 0.5f);
            groupRect.anchoredPosition = Vector2.zero;
            groupRect.offsetMin = new Vector2(-300f, 0f);
            groupRect.offsetMax = new Vector2(0f, 0f);
            _groupToggle.onClick.AddListener(ToggleGrouping);
            _sortBar = sortBar.gameObject;

            _sort = (SortOrder)Mathf.Clamp(PlayerPrefs.GetInt(SortPrefKey, 0), 0, 2);
            UiFactory.SetSegmentSelected(_sortTabs, (int)_sort);
            _groupSongs = PlayerPrefs.GetInt(GroupPrefKey, 1) == 1;
            UpdateGroupToggle();

            var scrollRectT = UiFactory.CreateScrollList(transform, "ResultList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -338f);

            BuildAgeLimitModal(); // 最後に作って最前面にする
        }

        /// <summary>いまの検索条件を保存/解除する。保存はチップとして検索トップに並ぶ。</summary>
        async void ToggleSaveSearch()
        {
            if (_queryStack.Count == 0)
            {
                return;
            }
            var query = _queryStack[_queryStack.Count - 1];
            try
            {
                bool added = await MypageService.ToggleSavedSearchAsync(ToSavedSearch(query));
                Se.Play(added ? Se.Confirm : Se.Tap);
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("保存の同期に失敗: " + e.Message, true);
            }
            UpdateSaveButton(query);
        }

        void UpdateSaveButton(SearchQuery query)
        {
            bool saved = LocalMypage.IsSavedSearch(ToSavedSearch(query));
            // 白帯の上に置くため、未保存は淡色 + ☆ / 保存済みはテーマ色 + ★
            _saveButton.image.color = saved ? UiFactory.Primary : UiFactory.PrimaryPale;
            _saveButtonLabel.text = saved ? "★" : "☆";
            _saveButtonLabel.color = saved ? Color.white : UiFactory.Primary;
        }

        static LocalMypage.SavedSearch ToSavedSearch(SearchQuery query)
        {
            switch (query.Kind)
            {
                case QueryKind.Everything:
                    return new LocalMypage.SavedSearch
                    {
                        Kind = "everything",
                        Keyword = query.Keyword,
                        Label = query.Keyword,
                    };
                case QueryKind.ListerAnyword:
                    return new LocalMypage.SavedSearch
                    {
                        Kind = "lister",
                        Keyword = query.Keyword,
                        Label = query.Keyword,
                    };
                default:
                    return new LocalMypage.SavedSearch
                    {
                        Kind = "exact",
                        Field = query.ExactField,
                        Value = query.ExactValue,
                        Label = query.Label,
                    };
            }
        }

        /// <summary>保存した検索を SearchQuery に戻す (検索トップのチップが使う)。</summary>
        public static SearchQuery FromSavedSearch(LocalMypage.SavedSearch saved)
        {
            switch (saved.Kind)
            {
                case "everything":
                    return new SearchQuery
                    {
                        Kind = QueryKind.Everything,
                        Keyword = saved.Keyword,
                        Label = "検索: " + saved.Keyword,
                    };
                case "exact":
                    return MakeExactQuery(saved.Field, saved.Value);
                default:
                    return new SearchQuery
                    {
                        Kind = QueryKind.ListerAnyword,
                        Keyword = saved.Keyword,
                        Label = "検索: " + saved.Keyword,
                    };
            }
        }

        /// <summary>結果画面の検索フォームから検索し直す (履歴に積むので「戻る」で前の結果へ)。</summary>
        void RunKeywordSearch()
        {
            string keyword = (_searchInput.text ?? "").Trim();
            if (keyword == "")
            {
                Se.Play(Se.Error);
                return;
            }
            Se.Play(Se.Tap);
            bool lister = PlayerPrefs.GetInt(SearchScreen.ModePrefKey, 1) == 1;
            _queryStack.Add(new SearchQuery
            {
                Kind = lister ? QueryKind.ListerAnyword : QueryKind.Everything,
                Keyword = keyword,
                Label = "検索: " + keyword,
            });
            _ = RunCurrentAsync();
        }

        void SetSort(SortOrder sort)
        {
            Se.Play(Se.Tap);
            if (_sort == sort)
            {
                return;
            }
            _sort = sort;
            PlayerPrefs.SetInt(SortPrefKey, (int)sort);
            PlayerPrefs.Save();
            UiFactory.SetSegmentSelected(_sortTabs, (int)_sort);
            _ = RunCurrentAsync();
        }

        string SortParam()
        {
            switch (_sort)
            {
                case SortOrder.DateAsc:
                    return "date_asc";
                case SortOrder.NameAsc:
                    return "name";
                default:
                    return "date_desc";
            }
        }

        void ToggleGrouping()
        {
            Se.Play(Se.Tap);
            _groupSongs = !_groupSongs;
            PlayerPrefs.SetInt(GroupPrefKey, _groupSongs ? 1 : 0);
            PlayerPrefs.Save();
            UpdateGroupToggle();
            _ = RunCurrentAsync();
        }

        void UpdateGroupToggle()
        {
            _groupToggle.image.color = _groupSongs
                ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            _groupToggleLabel.text = _groupSongs ? "✓ 曲でまとめる" : "曲でまとめる";
        }

        public override void OnShow()
        {
            if (_pending != null)
            {
                _queryStack.Clear();
                _queryStack.Add(_pending);
                _pending = null;
                _ = RunCurrentAsync();
            }
            else if ((_rows.Count == 0 || _loadedIncludeAgeLimit != AppConfig.IncludeAgeLimit)
                && _queryStack.Count > 0)
            {
                // RebuildAll (テーマ変更) で行が消えていたときと、
                // 年齢制限曲の設定が変わっていたときは現在の条件で引き直す
                _ = RunCurrentAsync();
            }
        }

        /// <summary>
        /// ナビの「戻る」: モーダルが開いていればそれを閉じ、
        /// 検索履歴が残っていれば1つ前の条件に戻る。
        /// </summary>
        public override bool OnBackRequested()
        {
            if (_previewModal != null)
            {
                ClosePreviewModal();
                return true;
            }
            if (_ageLimitModal != null && _ageLimitModal.activeSelf)
            {
                CloseAgeLimitModal();
                return true;
            }
            if (_queryStack.Count > 1)
            {
                _queryStack.RemoveAt(_queryStack.Count - 1);
                _ = RunCurrentAsync();
                return true;
            }
            return false;
        }

        /// <summary>リンクタップ: 完全一致条件を履歴に積んで実行する。</summary>
        void PushExact(string field, string value)
        {
            Se.Play(Se.Tap);
            _queryStack.Add(MakeExactQuery(field, value));
            _ = RunCurrentAsync();
        }

        void SetStatus(string message, bool isError)
        {
            HideLoading(); // 結果・エラーの表示 = ローディング終了
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
        }

        void ClearRows()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
        }

        async Task RunCurrentAsync()
        {
            if (_queryStack.Count == 0)
            {
                return;
            }
            var query = _queryStack[_queryStack.Count - 1];
            _loadedIncludeAgeLimit = AppConfig.IncludeAgeLimit;
            int serial = ++_searchSerial;
            // プレビュー可否 (サーバーがアクセス元と online_preview 設定から判定して返す)
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                _previewEnabled = caps.Features != null && caps.Features.Preview;
            }
            catch (System.Exception)
            {
                _previewEnabled = false; // 未接続・旧サーバーはプレビューなし
            }
            if (serial != _searchSerial)
            {
                return; // 取得待ちの間に別の検索が始まっていたら何もしない
            }
            _titleText.text = (ReserveScreen.EditSession != null ? "差しかえ｜" : "") + query.Label;
            _searchInput.text = query.Keyword ?? ""; // 完全一致検索中は空 (プレースホルダー表示)
            UpdateSaveButton(query);
            SetStatus("", false);
            ShowLoading("検索中...");
            ClearRows();

            // 並び順は曲一覧にだけ効く
            bool isSongList = query.Kind == QueryKind.ListerAnyword
                || (query.Kind == QueryKind.ListerExact && query.ExactField != "group");
            _sortBar.SetActive(isSongList);

            try
            {
                if (query.Kind == QueryKind.Everything)
                {
                    var result = await AppConfig.CreateClient().SearchAsync(query.Keyword);
                    if (serial != _searchSerial)
                    {
                        return;
                    }
                    ShowEverythingResult(result);
                }
                else if (query.Kind == QueryKind.ListerExact && query.ExactField == "group")
                {
                    // シリーズは曲一覧ではなく「シリーズの作品一覧」を出す
                    var result = await AppConfig.CreateClient().GetListerGroupProgramsAsync(query.ExactValue);
                    if (serial != _searchSerial)
                    {
                        return;
                    }
                    ShowProgramsResult(result);
                }
                else
                {
                    var client = AppConfig.CreateClient();
                    bool flat = !_groupSongs;
                    var result = query.Kind == QueryKind.ListerAnyword
                        ? await client.GetListerIndexSongsAsync(
                            anyword: query.Keyword, order: SortParam(), flat: flat)
                        : await client.GetListerIndexSongsAsync(
                            query.ExactField == "program" ? query.ExactValue : null,
                            query.ExactField == "artist" ? query.ExactValue : null,
                            null,
                            query.ExactField == "worker" ? query.ExactValue : null,
                            null,
                            SortParam(),
                            flat);
                    if (serial != _searchSerial)
                    {
                        return;
                    }
                    ShowSongsResult(query, result);
                }
            }
            catch (System.Exception e)
            {
                if (serial == _searchSerial)
                {
                    SetStatus("検索に失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
            }
        }

        // ---- Everything (ファイル名) 結果 ----

        void ShowEverythingResult(SearchResultDto result)
        {
            if (result.Items == null || result.Items.Count == 0)
            {
                SetStatus("見つかりませんでした", false);
                return;
            }
            int shown = Mathf.Min(result.Items.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                var item = result.Items[i];
                var entry = new ReserveScreen.Entry
                {
                    Line1 = item.Name,
                    Line2 = "",
                    Filename = item.Name,
                    FullPath = item.FullPath,
                };
                var rowGo = new GameObject("Row");
                rowGo.transform.SetParent(_listContent, false);
                var img = rowGo.AddComponent<Image>();
                img.color = UiFactory.CardBg;
                UiFactory.Roundify(img);
                UiFactory.AddShadow(rowGo, 3f);
                var le = rowGo.AddComponent<LayoutElement>();
                var button = rowGo.AddComponent<Button>();
                rowGo.AddComponent<PressEffect>();
                button.onClick.AddListener(() => ReserveScreen.Open(Manager, entry));

                // ファイル名は文章ではないので半角スペースで折り返さず、行数に合わせて全文表示する
                int nameLines = UiFactory.EstimateWrapLines(item.Name, 28,
                    UiFactory.CanvasWidth - 90f);
                float nameHeight = nameLines * UiFactory.LineHeight(28) + 4f;
                bool hasWorker = !string.IsNullOrEmpty(item.Worker);
                bool showPreview = _previewEnabled && CanPreview(item.FullPath);
                float linkRowHeight = (hasWorker || showPreview)
                    ? UiFactory.LineHeight(26) + 6f : 0f;
                le.preferredHeight = Mathf.Max(nameHeight + linkRowHeight + 28f, 112f);

                var nameText = UiFactory.CreateText(rowGo.transform, "Name",
                    UiFactory.NoWordWrap(item.Name), 28,
                    UiFactory.TextDark, TextAnchor.MiddleLeft);
                UiFactory.StretchFull(nameText.rectTransform);
                nameText.rectTransform.offsetMin = new Vector2(24f, 6f + linkRowHeight);
                nameText.rectTransform.offsetMax = new Vector2(-24f, -6f);
                nameText.verticalOverflow = VerticalWrapMode.Overflow;

                float previewWidth = showPreview
                    ? EstimateWidth("▶ プレビュー", 26) + 40f : 0f;
                // 動画制作者 (ListerDB 照会結果)。タップで制作者の再検索へ
                if (hasWorker)
                {
                    string worker = item.Worker;
                    string label = "制作: " + worker;
                    var link = UiFactory.CreateText(rowGo.transform, "Worker",
                        label, 26, UiFactory.Primary, TextAnchor.MiddleLeft);
                    var linkRect = link.rectTransform;
                    linkRect.anchorMin = linkRect.anchorMax = new Vector2(0f, 0f);
                    linkRect.pivot = new Vector2(0f, 0f);
                    linkRect.anchoredPosition = new Vector2(24f, 10f);
                    linkRect.sizeDelta = new Vector2(
                        Mathf.Min(EstimateWidth(label, 26) + 10f, 900f - previewWidth),
                        UiFactory.LineHeight(26));
                    link.verticalOverflow = VerticalWrapMode.Truncate;
                    link.raycastTarget = true;
                    var linkButton = link.gameObject.AddComponent<Button>();
                    linkButton.transition = Selectable.Transition.None;
                    linkButton.onClick.AddListener(() => PushExact("worker", worker));
                }

                if (showPreview)
                {
                    // プレビューリンク (リンク行の右寄せ。行本体のタップより優先)。
                    // 文字行より上下に広げてタップしやすくする (見た目は中央の1行のまま)
                    var plink = UiFactory.CreateText(rowGo.transform, "Preview",
                        "▶ プレビュー", 26, UiFactory.Primary, TextAnchor.MiddleRight);
                    var pRect = plink.rectTransform;
                    pRect.anchorMin = pRect.anchorMax = new Vector2(1f, 0f);
                    pRect.pivot = new Vector2(1f, 0f);
                    pRect.anchoredPosition = new Vector2(-24f, 0f);
                    pRect.sizeDelta = new Vector2(
                        EstimateWidth("▶ プレビュー", 26) + 24f, UiFactory.LineHeight(26) + 20f);
                    plink.raycastTarget = true;
                    string path = item.FullPath;
                    string title = item.Name;
                    var pButton = plink.gameObject.AddComponent<Button>();
                    pButton.transition = Selectable.Transition.None;
                    pButton.onClick.AddListener(() => OpenPreviewModal(path, title));
                }

                _rows.Add(rowGo);
            }
            SetStatus(result.Total > shown
                ? $"{result.Total} 件中 {shown} 件を表示中 (ワードを足して絞り込めます)"
                : $"{result.Items.Count} 件見つかりました", false);
        }

        // ---- シリーズ → 作品一覧 ----

        void ShowProgramsResult(ListerProgramsDto result)
        {
            if (result.Programs == null || result.Programs.Count == 0)
            {
                SetStatus("作品が見つかりませんでした", false);
                return;
            }
            foreach (var program in result.Programs)
            {
                string name = program.Program;
                var rowGo = UiFactory.CreateIndexRow(_listContent, name, $"{program.Songs} 曲",
                    () => PushExact("program", name));
                _rows.Add(rowGo);
            }
            SetStatus($"{result.Programs.Count} 作品 (タップで曲一覧)", false);
        }

        // ---- 曲カード (ListerDB 結果) ----

        void ShowSongsResult(SearchQuery query, ListerIndexSongsDto result)
        {
            // 年齢制限フィルタで隠れた曲があるときだけオプトイン導線を出す (Web 版のチェック相当)。
            // 一度有効化した利用者には、逆に戻すための「表示中」行を出す
            bool offerAgeLimit = !AppConfig.IncludeAgeLimit && result.AgelimitHidden > 0;

            if (result.Items == null || result.Items.Count == 0)
            {
                bool anywordFallback = query.Kind == QueryKind.ListerAnyword
                    && !string.IsNullOrEmpty(query.Keyword);
                SetStatus(anywordFallback ? "リスターDBでは見つかりませんでした" : "見つかりませんでした", false);
                if (offerAgeLimit)
                {
                    // 年齢制限フィルタで隠れている曲の案内 (ファイル名検索誘導より上に出す)
                    AddAgeLimitOptInRow(result.AgelimitHidden);
                }
                if (anywordFallback)
                {
                    // アニソンDB 未収録の曲 (新曲など) はリスターDB検索では見つからない。
                    // ファイルはあるかもしれないのでファイル名検索への誘導を出す
                    _ = AddEverythingFallbackRowAsync(query.Keyword);
                }
                return;
            }
            int shown = Mathf.Min(result.Items.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                AddSongCard(query, result.Items[i]);
            }
            if (offerAgeLimit)
            {
                AddAgeLimitOptInRow(result.AgelimitHidden);
            }
            else if (AppConfig.IncludeAgeLimit)
            {
                AddAgeLimitShowingRow();
            }
            string unit = _groupSongs ? "曲" : "ファイル";
            SetStatus(result.Total > shown
                ? $"{result.Total} {unit}中 {shown} 件を表示中 (ワードを足して絞り込めます)"
                : _groupSongs
                    ? $"{result.Total} 曲・{result.FilesTotal} ファイル"
                    : $"{result.Total} ファイル", false);
        }

        /// <summary>
        /// リスターDB検索が0件のとき、同じキーワードでのファイル名 (Everything) 検索への
        /// 誘導を出す。アニソンDBにまだ登録されていない曲はリスターDB検索に出ないため、
        /// ファイル自体はあるのに「見つからない」ように見えるのを防ぐ。
        /// </summary>
        async Task AddEverythingFallbackRowAsync(string keyword)
        {
            int serial = _searchSerial;
            try
            {
                // Everything 検索がサーバーで使えない構成では出さない (判定不能時は出す)
                var caps = await AppState.EnsureCapabilitiesAsync();
                if (caps.Features != null && !caps.Features.EverythingSearch)
                {
                    return;
                }
            }
            catch (System.Exception)
            {
            }
            if (serial != _searchSerial)
            {
                return; // 別の検索が始まっていたら何もしない
            }

            float noteHeight = UiFactory.LineHeight(24) * 2f;
            float buttonHeight = Mathf.Max(96f, UiFactory.LineHeight(30) + 28f);

            var rowGo = new GameObject("EverythingFallback");
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 12f + noteHeight + 16f + buttonHeight + 8f;

            var note = UiFactory.CreateText(rowGo.transform, "Note",
                "アニソンDBにまだ登録されていない曲は\nリスターDB検索では見つかりません", 24,
                UiFactory.TextMuted);
            var noteRect = note.rectTransform;
            noteRect.anchorMin = new Vector2(0f, 1f);
            noteRect.anchorMax = new Vector2(1f, 1f);
            noteRect.pivot = new Vector2(0.5f, 1f);
            noteRect.anchoredPosition = new Vector2(0f, -12f);
            noteRect.sizeDelta = new Vector2(-40f, noteHeight);

            string label = "ファイル名 (Everything) でさがす";
            var button = UiFactory.CreateSoftButton(rowGo.transform, "Search", label, 30);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 8f);
            buttonRect.sizeDelta = new Vector2(
                Mathf.Min(UiFactory.EstimateTextWidth(label, 30) + 80f, 980f), buttonHeight);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _queryStack.Add(new SearchQuery
                {
                    Kind = QueryKind.Everything,
                    Keyword = keyword,
                    Label = "検索: " + keyword,
                });
                _ = RunCurrentAsync();
            });

            _rows.Add(rowGo);
        }

        // ---- 年齢制限曲のオプトイン (Web 版のチェックボックスと同じ仕組み) ----

        /// <summary>
        /// 年齢制限フィルタで隠れた曲があるときの案内行。タップで有効化 (初回のみ
        /// 18 歳以上の確認モーダルを挟む) して同じ条件で再検索する。
        /// </summary>
        void AddAgeLimitOptInRow(int hiddenCount)
        {
            float noteHeight = UiFactory.LineHeight(24);
            float buttonHeight = Mathf.Max(96f, UiFactory.LineHeight(30) + 28f);

            var rowGo = new GameObject("AgeLimitOptIn");
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 12f + noteHeight + 16f + buttonHeight + 8f;

            var note = UiFactory.CreateText(rowGo.transform, "Note",
                $"年齢制限のある作品の曲が {hiddenCount} 曲あります", 24,
                UiFactory.TextMuted);
            var noteRect = note.rectTransform;
            noteRect.anchorMin = new Vector2(0f, 1f);
            noteRect.anchorMax = new Vector2(1f, 1f);
            noteRect.pivot = new Vector2(0.5f, 1f);
            noteRect.anchoredPosition = new Vector2(0f, -12f);
            noteRect.sizeDelta = new Vector2(-40f, noteHeight);

            string label = "年齢制限曲を表示する";
            var button = UiFactory.CreateSoftButton(rowGo.transform, "Show", label, 30);
            UiFactory.FitLabel(button.GetComponentInChildren<Text>()); // 大きい文字設定でも1行に収める
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 8f);
            buttonRect.sizeDelta = new Vector2(
                Mathf.Min(UiFactory.EstimateTextWidth(label, 30) + 80f, 980f), buttonHeight);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                if (!AppConfig.AgeLimitAccepted)
                {
                    _ageLimitModal.SetActive(true); // 初回のみ 18 歳以上の確認を出す
                    return;
                }
                EnableAgeLimit();
            });

            _rows.Add(rowGo);
        }

        /// <summary>
        /// 年齢制限曲を表示中の利用者向けの行。タップで無効化して同じ条件で再検索する
        /// (設定を戻す導線として、曲のある結果一覧の末尾に出す)。
        /// </summary>
        void AddAgeLimitShowingRow()
        {
            float buttonHeight = Mathf.Max(72f, UiFactory.LineHeight(24) + 20f);

            var rowGo = new GameObject("AgeLimitShowing");
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 8f + buttonHeight + 8f;

            string label = "✓ 年齢制限曲を表示中（タップで非表示に）";
            var button = UiFactory.CreateSoftButton(rowGo.transform, "Hide", label, 24);
            UiFactory.FitLabel(button.GetComponentInChildren<Text>()); // 大きい文字設定でも1行に収める
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 8f);
            buttonRect.sizeDelta = new Vector2(
                Mathf.Min(UiFactory.EstimateTextWidth(label, 24) + 80f, 980f), buttonHeight);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                AppConfig.IncludeAgeLimit = false;
                _ = RunCurrentAsync();
            });

            _rows.Add(rowGo);
        }

        void EnableAgeLimit()
        {
            AppConfig.IncludeAgeLimit = true;
            _ = RunCurrentAsync();
        }

        void BuildAgeLimitModal()
        {
            _ageLimitModal = new GameObject("AgeLimitModal");
            _ageLimitModal.transform.SetParent(transform, false);
            UiFactory.StretchFull(_ageLimitModal.AddComponent<RectTransform>());
            var overlay = _ageLimitModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);
            var overlayButton = _ageLimitModal.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(CloseAgeLimitModal);

            const string bodyText =
                "年齢制限のある作品のタイアップ曲を\n検索結果に表示します。\n18歳以上の方のみ有効にしてください。";
            float y = 28f;
            float titleH = UiFactory.LineHeight(34);
            // 大きい文字設定では折り返しが増えるため行数を実測して確保する
            float bodyH = UiFactory.EstimateWrapLines(bodyText, 26, 780f) * UiFactory.LineHeight(26);
            float cardH = y + titleH + 16f + bodyH + 28f + 92f + 28f;

            var card = UiFactory.CreatePanel(_ageLimitModal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880f, cardH);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            var cardButton = card.gameObject.AddComponent<Button>(); // カード内タップの抜け防止
            cardButton.transition = Selectable.Transition.None;

            var title = UiFactory.CreateText(card, "Title", "年齢制限曲の表示", 34, UiFactory.PrimaryDark);
            SetModalRow(title.rectTransform, -y, titleH);
            y += titleH + 16f;

            var body = UiFactory.CreateText(card, "Body", bodyText, 26, UiFactory.TextDark);
            SetModalRow(body.rectTransform, -y, bodyH);
            y += bodyH + 28f;

            var okButton = UiFactory.CreateButton(card, "Ok", "表示する", UiFactory.Primary, Color.white, 30);
            var okRect = okButton.GetComponent<RectTransform>();
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 1f);
            okRect.pivot = new Vector2(0.5f, 1f);
            okRect.anchoredPosition = new Vector2(-190f, -y);
            okRect.sizeDelta = new Vector2(340f, 92f);
            okButton.onClick.AddListener(AcceptAgeLimit);

            var cancelButton = UiFactory.CreateSoftButton(card, "Cancel", "やめる", 30);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 1f);
            cancelRect.pivot = new Vector2(0.5f, 1f);
            cancelRect.anchoredPosition = new Vector2(190f, -y);
            cancelRect.sizeDelta = new Vector2(340f, 92f);
            cancelButton.onClick.AddListener(CloseAgeLimitModal);

            _ageLimitModal.SetActive(false);
        }

        void AcceptAgeLimit()
        {
            Se.Play(Se.Confirm);
            AppConfig.AgeLimitAccepted = true;
            _ageLimitModal.SetActive(false);
            EnableAgeLimit();
        }

        void CloseAgeLimitModal()
        {
            Se.Play(Se.Tap);
            _ageLimitModal.SetActive(false);
        }

        static void SetModalRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(50f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-50f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        // ---- 動画プレビュー (Web 版 preview_video_stream.php をそのまま再生) ----

        /// <summary>
        /// 全画面のプレビューモーダルを開いて再生する。Web 版と同じくミュートで
        /// 再生を開始し、モーダル内のボタンで音を出せる。「閉じる」ボタン・戻るで終了。
        /// </summary>
        void OpenPreviewModal(string fullPath, string title)
        {
            Se.Play(Se.Tap);
            ClosePreviewModal();
            // プレビュー中は BGM を止め、画面も消灯させない
            Bgm.SetSuppressed(true);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _previewModal = new GameObject("PreviewModal");
            _previewModal.transform.SetParent(transform, false);
            UiFactory.StretchFull(_previewModal.AddComponent<RectTransform>());
            // 暗幕 (raycastTarget=true で背後の検索リストへのタップも遮る)。
            // 「外タップで閉じる」はシークバー操作などが誤って閉じる原因になるため付けない。
            // 閉じるのは「閉じる」ボタンと戻る (OnBackRequested) だけにする。
            var overlay = _previewModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.85f);

            // Screens レイヤーは上側を SafeTop ぶんインセット済みのため、ここでは足さない
            float topY = 30f;
            var titleText = UiFactory.CreateText(_previewModal.transform, "Title",
                title, 28, Color.white);
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -topY);
            titleRect.offsetMin = new Vector2(40f, titleRect.offsetMin.y);
            titleRect.offsetMax = new Vector2(-40f, titleRect.offsetMax.y);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, UiFactory.LineHeight(28));
            UiFactory.FitLabel(titleText);

            var statusText = UiFactory.CreateText(_previewModal.transform, "Status",
                "読み込み中...", 28, Color.white);
            UiFactory.StretchFull(statusText.rectTransform);

            var videoGo = new GameObject("Video");
            videoGo.transform.SetParent(_previewModal.transform, false);
            var videoImage = videoGo.AddComponent<RawImage>();
            videoImage.raycastTarget = false;
            videoImage.enabled = false; // 実サイズ確定まで非表示
            var videoRect = videoImage.rectTransform;
            videoRect.anchorMin = videoRect.anchorMax = new Vector2(0.5f, 0.5f);
            videoRect.pivot = new Vector2(0.5f, 0.5f);
            videoRect.anchoredPosition = Vector2.zero;

            // 下部ボタン: ミュート切替 / 閉じる (常時前面のナビバーの上に出す)
            float buttonY = GlobalNav.BarHeight + 24f;
            var muteButton = UiFactory.CreateSoftButton(_previewModal.transform, "Mute",
                "音を出す", 26);
            var muteLabel = muteButton.GetComponentInChildren<Text>();
            UiFactory.FitLabel(muteLabel);
            var muteRect = muteButton.GetComponent<RectTransform>();
            muteRect.anchorMin = muteRect.anchorMax = new Vector2(0.5f, 0f);
            muteRect.pivot = new Vector2(1f, 0f);
            muteRect.anchoredPosition = new Vector2(-12f, buttonY);
            muteRect.sizeDelta = new Vector2(300f, 84f);
            muteButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                if (_previewAudio == null)
                {
                    return;
                }
                _previewAudio.mute = !_previewAudio.mute;
                muteLabel.text = _previewAudio.mute ? "音を出す" : "ミュートにする";
            });

            var closeButton = UiFactory.CreateSoftButton(_previewModal.transform, "Close",
                "閉じる", 26);
            UiFactory.FitLabel(closeButton.GetComponentInChildren<Text>());
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0f, 0f);
            closeRect.anchoredPosition = new Vector2(12f, buttonY);
            closeRect.sizeDelta = new Vector2(300f, 84f);
            closeButton.onClick.AddListener(ClosePreviewModal);

            // シークバー (経過 / 全体 の時間表示 + スライダー。ボタンの上に置く)
            float sliderY = buttonY + 84f + 20f;
            _previewTimeText = UiFactory.CreateText(_previewModal.transform, "Time",
                "0:00 / 0:00", 22, Color.white);
            var timeRect = _previewTimeText.rectTransform;
            timeRect.anchorMin = new Vector2(0f, 0f);
            timeRect.anchorMax = new Vector2(1f, 0f);
            timeRect.pivot = new Vector2(0.5f, 0f);
            timeRect.anchoredPosition = new Vector2(0f, sliderY + 44f);
            timeRect.offsetMin = new Vector2(40f, timeRect.offsetMin.y);
            timeRect.offsetMax = new Vector2(-40f, timeRect.offsetMax.y);
            timeRect.sizeDelta = new Vector2(timeRect.sizeDelta.x, UiFactory.LineHeight(22));

            _previewSlider = UiFactory.CreateSlider(_previewModal.transform, "Seek", 0f, 1f,
                wholeNumbers: false);
            // CreateSlider は 44px のつまみだけが当たり判定 (トラック/塗りは
            // raycastTarget=false)。そのままだとバーの余白タップが背後の overlay
            // (外タップ=閉じる) に抜け、シーク操作でプレビューが閉じてしまう。
            // スライダー領域全体を覆う透明の当たり判定を足して塞ぎ、バー全域で掴めるようにする。
            // (この Image は子のトラック/つまみより後ろに描画されるので見た目は変わらない)
            var sliderHit = _previewSlider.gameObject.AddComponent<Image>();
            sliderHit.color = new Color(0f, 0f, 0f, 0f);
            var sliderRect = _previewSlider.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.anchoredPosition = new Vector2(0f, sliderY);
            sliderRect.offsetMin = new Vector2(40f, sliderRect.offsetMin.y);
            sliderRect.offsetMax = new Vector2(-40f, sliderRect.offsetMax.y);
            sliderRect.sizeDelta = new Vector2(sliderRect.sizeDelta.x, 40f);
            _previewSlider.value = 0f;
            _previewSlider.onValueChanged.AddListener(OnPreviewSeek);

            // 再生 URL (Web 版と同じエンドポイント。Range 対応済みでシークも効く)
            string url = AppConfig.ServerUrl.TrimEnd('/')
                + "/preview_video_stream.php?path=" + UnityWebRequest.EscapeURL(fullPath);
            if (!string.IsNullOrEmpty(AppConfig.EasyPass))
            {
                // 現状サーバーは見ないが、将来の認証追加に備えてクエリで付けておく (無害)
                url += "&easypass=" + UnityWebRequest.EscapeURL(AppConfig.EasyPass);
            }

            // 音声は AudioSource 経由で出す (Direct は 5.1ch 音声を iOS で無音にする・
            // Android で音声トラック初期化に失敗して映像ごと止まることがあるため)
            _previewAudio = _previewModal.AddComponent<AudioSource>();
            _previewAudio.playOnAwake = false;
            _previewAudio.mute = true; // Web 版と同じくミュートで開始

            _previewPlayer = _previewModal.AddComponent<VideoPlayer>();
            _previewPlayer.renderMode = VideoRenderMode.RenderTexture;
            _previewPlayer.playOnAwake = false;
            _previewPlayer.isLooping = false;
            _previewPlayer.source = VideoSource.Url;
            _previewPlayer.url = url;
            _previewPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _previewPlayer.EnableAudioTrack(0, true);
            _previewPlayer.SetTargetAudioSource(0, _previewAudio);
            _previewPlayer.errorReceived += (vp, message) =>
            {
                if (statusText != null)
                {
                    statusText.gameObject.SetActive(true); // 再生開始後のエラーも見えるように
                    statusText.text = "再生できませんでした";
                }
                Debug.Log("[YukaNavi] プレビュー再生エラー: " + message);
            };
            _previewPlayer.prepareCompleted += vp =>
            {
                if (_previewModal == null || statusText == null)
                {
                    return; // 準備中に閉じられた
                }
                int w = (int)vp.width;
                int h = (int)vp.height;
                if (w <= 0 || h <= 0)
                {
                    w = 16;
                    h = 9;
                }
                _previewTexture = new RenderTexture(w, h, 0);
                vp.targetTexture = _previewTexture;
                videoImage.texture = _previewTexture;
                // 上下の UI (タイトル / シークバー + ボタン) を避けた領域に contain で収める
                var modalRect = ((RectTransform)_previewModal.transform).rect;
                float topInset = topY + UiFactory.LineHeight(28) + 30f;
                float bottomInset = sliderY + 44f + UiFactory.LineHeight(22) + 20f;
                float availW = modalRect.width - 60f;
                float availH = modalRect.height - topInset - bottomInset;
                float scale = Mathf.Min(availW / w, availH / h);
                videoRect.sizeDelta = new Vector2(w * scale, h * scale);
                videoRect.anchoredPosition = new Vector2(0f, (bottomInset - topInset) * 0.5f);
                videoImage.enabled = true;
                statusText.gameObject.SetActive(false);
                vp.Play();
            };
            _previewPlayer.Prepare();
        }

        /// <summary>シークバー操作: 動画を該当位置へ移動する。</summary>
        void OnPreviewSeek(float value)
        {
            if (_previewSuppressSlider || _previewPlayer == null || !_previewPlayer.isPrepared)
            {
                return; // Update からの自動追従 or 準備前は無視
            }
            double length = _previewPlayer.length;
            if (length > 0)
            {
                _previewPlayer.time = value * length;
                _previewLastSeekAt = Time.unscaledTime;
                if (_previewTimeText != null)
                {
                    _previewTimeText.text = FormatTime(value * length) + " / " + FormatTime(length);
                }
            }
        }

        void Update()
        {
            if (_previewPlayer == null || _previewSlider == null || !_previewPlayer.isPrepared)
            {
                return;
            }
            double length = _previewPlayer.length;
            if (length <= 0)
            {
                return;
            }
            // 直近にユーザーが操作した直後は、シーク完了までスライダーを動かさない
            if (Time.unscaledTime - _previewLastSeekAt < 0.4f)
            {
                return;
            }
            _previewSuppressSlider = true;
            _previewSlider.value = (float)(_previewPlayer.time / length);
            _previewSuppressSlider = false;
            if (_previewTimeText != null)
            {
                _previewTimeText.text = FormatTime(_previewPlayer.time) + " / " + FormatTime(length);
            }
        }

        static string FormatTime(double seconds)
        {
            int total = Mathf.Max(0, (int)seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        void ClosePreviewModal()
        {
            if (_previewPlayer != null)
            {
                _previewPlayer.Stop();
                Destroy(_previewPlayer);
                _previewPlayer = null;
            }
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                Destroy(_previewTexture);
                _previewTexture = null;
            }
            if (_previewModal != null)
            {
                Destroy(_previewModal);
                _previewModal = null;
            }
            _previewAudio = null;
            _previewSlider = null;
            _previewTimeText = null;
            // モーダルが RebuildAll 等で先に破棄されていても確実に元へ戻す (冪等)
            Bgm.SetSuppressed(false);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        public override void OnHide()
        {
            ClosePreviewModal(); // 画面遷移で再生 (音声含む) を確実に止める
        }

        public override void OnRebuild()
        {
            // モーダルと VideoPlayer は RebuildAll が子ごと破棄する。
            // RenderTexture はアセットのため自前で破棄する
            _previewModal = null;
            _previewPlayer = null;
            _previewAudio = null;
            _previewSlider = null;
            _previewTimeText = null;
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                Destroy(_previewTexture);
                _previewTexture = null;
            }
            Bgm.SetSuppressed(false);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        // カード内テキストの折り返し幅 (実効キャンバス幅 - リスト余白 - カード内余白の概算)
        static float CardTextWidth => UiFactory.CanvasWidth - 100f;

        static int WrapLines(string text, int fontSize, float width)
        {
            return UiFactory.EstimateWrapLines(text, fontSize, width);
        }

        /// <summary>
        /// 曲カード: 曲名 / 歌手 (リンク) / 作品 [区分] (リンク) / シリーズ (リンク) /
        /// ファイルごとのブロック (【コメント】+ ファイル名 + 制作リンク。タップで予約)。
        /// どの行も折り返して全文表示する。1曲に絞れている検索が多いため画面幅を大きく使う。
        /// </summary>
        void AddSongCard(SearchQuery query, ListerSongGroupDto song)
        {
            var files = song.Files ?? new List<ListerFileDto>();

            bool showGroup = !string.IsNullOrEmpty(song.TieUpGroup)
                && !(query.Kind == QueryKind.ListerExact && query.ExactField == "group"
                     && query.ExactValue == song.TieUpGroup);

            var cardGo = new GameObject("SongCard");
            cardGo.transform.SetParent(_listContent, false);
            var img = cardGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(cardGo, 3f);
            var le = cardGo.AddComponent<LayoutElement>();
            var card = (RectTransform)cardGo.transform;

            // 上から順に積み上げて配置し、最後に使った高さをカードの高さにする
            float y = 16f;

            // 曲名
            y += AddWrappedText(card, song.SongName, y, 38, UiFactory.TextDark);
            y += 6f;

            // 歌手 (リンク)
            if (!string.IsNullOrEmpty(song.Artist))
            {
                string artist = song.Artist;
                y += AddLinkRow(card, artist, y, 30, () => PushExact("artist", artist));
            }

            // 作品 [区分] (リンク)
            if (!string.IsNullOrEmpty(song.ProgramName))
            {
                string program = song.ProgramName;
                string label = program + (string.IsNullOrEmpty(song.OpEd) ? "" : " [" + song.OpEd + "]");
                y += AddLinkRow(card, label, y, 30, () => PushExact("program", program));
            }

            // シリーズ (リンク。いまシリーズ検索中でなければ)
            if (showGroup)
            {
                string group = song.TieUpGroup;
                y += AddLinkRow(card, "シリーズ: " + group, y, 26, () => PushExact("group", group));
            }

            // ファイルブロック
            y += 10f;
            foreach (var file in files)
            {
                float blockH = FileBlockHeight(file);
                AddFileBlock(card, song, file, -y, blockH);
                y += blockH + 12f;
            }

            le.preferredHeight = y + 6f;
            _rows.Add(cardGo);
        }

        /// <summary>折り返して全文表示するテキスト行。使った高さを返す。</summary>
        float AddWrappedText(RectTransform card, string label, float y, int fontSize, Color color)
        {
            int lines = WrapLines(label, fontSize, CardTextWidth);
            float height = lines * UiFactory.LineHeight(fontSize) + 4f;
            var text = UiFactory.CreateText(card, "Text", UiFactory.NoWordWrap(label),
                fontSize, color, TextAnchor.UpperLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return height;
        }

        /// <summary>リンク風テキストの行 (折り返して全文表示、タップ可)。使った高さを返す。</summary>
        float AddLinkRow(RectTransform card, string label, float y, int fontSize, System.Action onTap)
        {
            int lines = WrapLines(label, fontSize, CardTextWidth);
            float height = lines * UiFactory.LineHeight(fontSize) + 4f;
            var text = UiFactory.CreateText(card, "Link", UiFactory.NoWordWrap(label), fontSize,
                UiFactory.Primary, TextAnchor.UpperLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            if (lines == 1)
            {
                // 1行ならタップ領域を文字幅に合わせる (誤タップ防止)
                rect.anchorMax = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(24f, -y);
                rect.sizeDelta = new Vector2(
                    Mathf.Min(EstimateWidth(label, fontSize) + 10f, CardTextWidth), height);
            }
            else
            {
                rect.anchorMax = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(0f, -y);
                rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -y);
            }
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = true;
            var button = text.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onTap());
            return height;
        }

        /// <summary>ファイルブロック内テキストの折り返し幅 (概算)。</summary>
        const float BlockTextWidth = 950f;
        const int FileNameFontSize = 24;

        /// <summary>プレビューできるファイルか (.mp4 のみ。flv は Unity で再生できない)。</summary>
        static bool CanPreview(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(".mp4", System.StringComparison.OrdinalIgnoreCase);
        }

        // 各行の高さは文字の大きさ設定 (FontScale) に追従させる
        // (固定値だと 130% 以上で縦 Truncate になり行が消える・はみ出す)
        float FileBlockHeight(ListerFileDto file)
        {
            float height = 12f;
            if (!string.IsNullOrEmpty(file.Comment))
            {
                height += UiFactory.LineHeight(26) + 4f;
            }
            string name = ReserveScreen.BaseName(file.FoundPath);
            height += WrapLines(name, FileNameFontSize, BlockTextWidth)
                * UiFactory.LineHeight(FileNameFontSize) + 4f;
            if (!string.IsNullOrEmpty(file.Worker)
                || (_previewEnabled && CanPreview(file.FoundPath)))
            {
                height += UiFactory.LineHeight(26) + 4f;
            }
            return height + 12f;
        }

        /// <summary>
        /// ファイル1つ分のブロック (タップで予約確認へ)。
        /// 【コメント】 / ファイル名 (折り返して全文) / 制作リンク をそれぞれ独立した行で出す。
        /// </summary>
        void AddFileBlock(RectTransform card, ListerSongGroupDto song, ListerFileDto file,
                          float y, float height)
        {
            var blockGo = new GameObject("File");
            blockGo.transform.SetParent(card, false);
            var img = blockGo.AddComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2f;
            img.color = new Color(0.96f, 0.945f, 0.99f);
            var rect = blockGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(20f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-20f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            var button = blockGo.AddComponent<Button>();
            blockGo.AddComponent<PressEffect>();

            var entry = new ReserveScreen.Entry
            {
                Line1 = string.IsNullOrEmpty(song.SongName)
                    ? ReserveScreen.BaseName(file.FoundPath)
                    : song.SongName,
                Line2 = BuildEntryLine2(song, file),
                Filename = ReserveScreen.BaseName(file.FoundPath),
                FullPath = file.FoundPath,
            };
            button.onClick.AddListener(() => ReserveScreen.Open(Manager, entry));

            float cy = 12f;
            if (!string.IsNullOrEmpty(file.Comment))
            {
                float commentH = UiFactory.LineHeight(26);
                var commentText = UiFactory.CreateText(blockGo.transform, "Comment",
                    "【" + file.Comment + "】", 26, UiFactory.TextDark, TextAnchor.MiddleLeft);
                SetBlockRow(commentText.rectTransform, -cy, commentH);
                commentText.verticalOverflow = VerticalWrapMode.Truncate;
                cy += commentH + 4f;
            }

            // ファイル名 (折り返して全文表示。単語折り返しはしない)
            string fileName = ReserveScreen.BaseName(file.FoundPath);
            float nameHeight = WrapLines(fileName, FileNameFontSize, BlockTextWidth)
                * UiFactory.LineHeight(FileNameFontSize) + 4f;
            var fileText = UiFactory.CreateText(blockGo.transform, "Name",
                UiFactory.NoWordWrap(fileName), FileNameFontSize, UiFactory.TextMuted,
                TextAnchor.UpperLeft);
            SetBlockRow(fileText.rectTransform, -cy, nameHeight);
            fileText.verticalOverflow = VerticalWrapMode.Overflow;
            cy += nameHeight + 4f;

            bool showPreview = _previewEnabled && CanPreview(file.FoundPath);
            float previewWidth = showPreview
                ? EstimateWidth("▶ プレビュー", 26) + 40f : 0f;
            if (!string.IsNullOrEmpty(file.Worker))
            {
                // 制作者リンク (独立行。ブロック本体のタップより優先される)
                string worker = file.Worker;
                string label = "制作: " + worker;
                var link = UiFactory.CreateText(blockGo.transform, "Worker",
                    label, 26, UiFactory.Primary, TextAnchor.MiddleLeft);
                var linkRect = link.rectTransform;
                linkRect.anchorMin = linkRect.anchorMax = new Vector2(0f, 1f);
                linkRect.pivot = new Vector2(0f, 1f);
                linkRect.anchoredPosition = new Vector2(16f, -cy);
                linkRect.sizeDelta = new Vector2(
                    Mathf.Min(EstimateWidth(label, 26) + 10f, 900f - previewWidth),
                    UiFactory.LineHeight(26));
                link.verticalOverflow = VerticalWrapMode.Truncate;
                link.raycastTarget = true;
                var linkButton = link.gameObject.AddComponent<Button>();
                linkButton.transition = Selectable.Transition.None;
                linkButton.onClick.AddListener(() => PushExact("worker", worker));
            }

            if (showPreview)
            {
                // プレビューリンク (制作リンクと同じ行の右寄せ。ブロック本体のタップより優先)
                AddPreviewLink(blockGo.transform, -cy, file.FoundPath, entry.Line1);
            }
        }

        /// <summary>「▶ プレビュー」リンク (行の右端に置く小リンク)。</summary>
        void AddPreviewLink(Transform parent, float y, string fullPath, string title)
        {
            const string label = "▶ プレビュー";
            var link = UiFactory.CreateText(parent, "Preview",
                label, 26, UiFactory.Primary, TextAnchor.MiddleRight);
            var rect = link.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // 文字行より上下に 12px ずつ広げてタップしやすくする (見た目は中央の1行のまま)
            rect.anchoredPosition = new Vector2(-16f, y + 12f);
            rect.sizeDelta = new Vector2(EstimateWidth(label, 26) + 24f,
                UiFactory.LineHeight(26) + 24f);
            link.raycastTarget = true;
            var button = link.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OpenPreviewModal(fullPath, title));
        }

        static void SetBlockRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(16f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-16f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        static string BuildEntryLine2(ListerSongGroupDto song, ListerFileDto file)
        {
            string line = song.Artist ?? "";
            if (!string.IsNullOrEmpty(song.ProgramName))
            {
                line += (line != "" ? "　／　" : "") + song.ProgramName;
                if (!string.IsNullOrEmpty(song.OpEd))
                {
                    line += " [" + song.OpEd + "]";
                }
            }
            if (!string.IsNullOrEmpty(file.Comment))
            {
                line += (line != "" ? "　" : "") + "【" + file.Comment + "】";
            }
            return line;
        }

    }
}
