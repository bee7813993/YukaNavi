using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
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
                new Color(1f, 1f, 1f, 0.25f), Color.white, 44);
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
        }

        /// <summary>いまの検索条件を保存/解除する。保存はチップとして検索トップに並ぶ。</summary>
        void ToggleSaveSearch()
        {
            if (_queryStack.Count == 0)
            {
                return;
            }
            var query = _queryStack[_queryStack.Count - 1];
            bool added = LocalMypage.ToggleSavedSearch(ToSavedSearch(query));
            Se.Play(added ? Se.Confirm : Se.Tap);
            UpdateSaveButton(query);
        }

        void UpdateSaveButton(SearchQuery query)
        {
            bool saved = LocalMypage.IsSavedSearch(ToSavedSearch(query));
            _saveButton.image.color = saved ? Color.white : new Color(1f, 1f, 1f, 0.25f);
            _saveButtonLabel.text = saved ? "★" : "☆";
            _saveButtonLabel.color = saved ? UiFactory.Primary : Color.white;
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
            else if (_rows.Count == 0 && _queryStack.Count > 0)
            {
                // RebuildAll (テーマ変更) 等で行が消えていたら現在の条件で引き直す
                _ = RunCurrentAsync();
            }
        }

        /// <summary>ナビの「戻る」: 検索履歴が残っていれば1つ前の条件に戻る。</summary>
        public override bool OnBackRequested()
        {
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
            int serial = ++_searchSerial;
            _titleText.text = query.Label;
            _searchInput.text = query.Keyword ?? ""; // 完全一致検索中は空 (プレースホルダー表示)
            UpdateSaveButton(query);
            SetStatus("検索中...", false);
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
                le.preferredHeight = 112f;
                var button = rowGo.AddComponent<Button>();
                rowGo.AddComponent<PressEffect>();
                button.onClick.AddListener(() => ReserveScreen.Open(Manager, entry));

                var nameText = UiFactory.CreateText(rowGo.transform, "Name", item.Name, 28,
                    UiFactory.TextDark, TextAnchor.MiddleLeft);
                UiFactory.StretchFull(nameText.rectTransform);
                nameText.rectTransform.offsetMin = new Vector2(24f, 6f);
                nameText.rectTransform.offsetMax = new Vector2(-24f, -6f);
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

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
            if (result.Items == null || result.Items.Count == 0)
            {
                SetStatus("見つかりませんでした", false);
                return;
            }
            int shown = Mathf.Min(result.Items.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                AddSongCard(query, result.Items[i]);
            }
            string unit = _groupSongs ? "曲" : "ファイル";
            SetStatus(result.Total > shown
                ? $"{result.Total} {unit}中 {shown} 件を表示中 (ワードを足して絞り込めます)"
                : _groupSongs
                    ? $"{result.Total} 曲・{result.FilesTotal} ファイル"
                    : $"{result.Total} ファイル", false);
        }

        // カード内テキストの折り返し幅 (画面幅 1080 - リスト余白 - カード内余白の概算)
        const float CardTextWidth = 980f;

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
            float height = lines * (fontSize + 12f) + 4f;
            var text = UiFactory.CreateText(card, "Text", label, fontSize, color, TextAnchor.UpperLeft);
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
            float height = lines * (fontSize + 12f) + 4f;
            var text = UiFactory.CreateText(card, "Link", label, fontSize,
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
        const float FileNameLineHeight = 34f;

        static float FileBlockHeight(ListerFileDto file)
        {
            float height = 12f;
            if (!string.IsNullOrEmpty(file.Comment))
            {
                height += 40f;
            }
            string name = ReserveScreen.BaseName(file.FoundPath);
            height += WrapLines(name, FileNameFontSize, BlockTextWidth) * FileNameLineHeight + 4f;
            if (!string.IsNullOrEmpty(file.Worker))
            {
                height += 42f;
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
                var commentText = UiFactory.CreateText(blockGo.transform, "Comment",
                    "【" + file.Comment + "】", 26, UiFactory.TextDark, TextAnchor.MiddleLeft);
                SetBlockRow(commentText.rectTransform, -cy, 36f);
                commentText.verticalOverflow = VerticalWrapMode.Truncate;
                cy += 40f;
            }

            // ファイル名 (折り返して全文表示)
            string fileName = ReserveScreen.BaseName(file.FoundPath);
            float nameHeight = WrapLines(fileName, FileNameFontSize, BlockTextWidth)
                * FileNameLineHeight + 4f;
            var fileText = UiFactory.CreateText(blockGo.transform, "Name",
                fileName, FileNameFontSize, UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetBlockRow(fileText.rectTransform, -cy, nameHeight);
            fileText.verticalOverflow = VerticalWrapMode.Overflow;
            cy += nameHeight + 4f;

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
                linkRect.sizeDelta = new Vector2(Mathf.Min(EstimateWidth(label, 26) + 10f, 900f), 38f);
                link.verticalOverflow = VerticalWrapMode.Truncate;
                link.raycastTarget = true;
                var linkButton = link.gameObject.AddComponent<Button>();
                linkButton.transition = Selectable.Transition.None;
                linkButton.onClick.AddListener(() => PushExact("worker", worker));
            }
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
