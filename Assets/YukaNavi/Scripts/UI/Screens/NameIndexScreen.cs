using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 頭文字インデックス検索画面 (作品名 / 歌手名 / シリーズ名で探す)。
    /// 上部のテキスト検索と、五十音の頭文字グリッド → 名前一覧 → 曲リスト (検索結果画面) の
    /// 2通りで辿れる。Web 版の program_index / column_index 相当。
    /// ナビの「戻る」で名前一覧 → 頭文字グリッドへ遡る。
    /// </summary>
    public class NameIndexScreen : ScreenBase
    {
        enum Level
        {
            Initials,
            Names,
        }

        static string _pendingTarget;

        string _target = "program";
        Level _level = Level.Initials;
        Text _titleText;
        InputField _searchInput;
        Text _placeholderText;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        int _loadSerial;

        /// <summary>target: program (作品名) / artist (歌手名) / group (シリーズ名)</summary>
        public static void Open(ScreenManager manager, string target)
        {
            _pendingTarget = target;
            manager.Show<NameIndexScreen>();
        }

        static string TargetTitle(string target)
        {
            switch (target)
            {
                case "artist":
                    return "歌手名で探す";
                case "group":
                    return "シリーズ名で探す";
                default:
                    return "作品名で探す";
            }
        }

        static string TargetNoun(string target)
        {
            switch (target)
            {
                case "artist":
                    return "歌手名";
                case "group":
                    return "シリーズ名";
                default:
                    return "作品名";
            }
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, TargetTitle(_target));
            _titleText = topBar.GetComponentInChildren<Text>();

            // 名前のキーワード検索 (頭文字を使わずに直接絞り込む)
            _searchInput = UiFactory.CreateInputField(transform, "SearchInput", "");
            _placeholderText = _searchInput.placeholder as Text;
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

            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.TextMuted);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -240f);
            statusRect.sizeDelta = new Vector2(-40f, 40f);

            var scrollRectT = UiFactory.CreateScrollList(transform, "IndexList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -286f);
        }

        public override void OnShow()
        {
            if (_pendingTarget != null)
            {
                _target = _pendingTarget;
                _pendingTarget = null;
                _titleText.text = TargetTitle(_target);
                _placeholderText.text = TargetNoun(_target) + "の一部を入力";
                _searchInput.text = "";
                _ = ShowInitialsAsync();
            }
            else if (_rows.Count == 0)
            {
                _ = ShowInitialsAsync();
            }
        }

        /// <summary>ナビの「戻る」: 名前一覧からは頭文字グリッドへ戻る。</summary>
        public override bool OnBackRequested()
        {
            if (_level == Level.Names)
            {
                _ = ShowInitialsAsync();
                return true;
            }
            return false;
        }

        void RunSearch()
        {
            string keyword = (_searchInput.text ?? "").Trim();
            if (keyword == "")
            {
                return;
            }
            Se.Play(Se.Tap);
            _ = ShowNamesAsync(null, keyword);
        }

        void SetStatus(string message, bool isError)
        {
            HideLoading(); // 結果・エラーの表示 = ローディング終了
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextMuted;
        }

        void ClearRows()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
        }

        // ---- 頭文字グリッド ----

        async Task ShowInitialsAsync()
        {
            _level = Level.Initials;
            int serial = ++_loadSerial;
            SetStatus("", false);
            ShowLoading();
            ClearRows();
            ListerInitialsDto data;
            try
            {
                data = await AppConfig.CreateClient().GetListerInitialsAsync(_target);
            }
            catch (System.Exception e)
            {
                if (serial == _loadSerial)
                {
                    SetStatus("読み込みに失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                return;
            }
            if (serial != _loadSerial || _level != Level.Initials)
            {
                return;
            }
            var counts = new Dictionary<string, int>();
            if (data.Initials != null)
            {
                foreach (var initial in data.Initials)
                {
                    counts[initial.Head] = initial.Names;
                }
            }
            SetStatus(TargetNoun(_target) + "の頭文字をえらんでください", false);
            BuildInitialGrid(counts);
        }

        void BuildInitialGrid(Dictionary<string, int> counts)
        {
            string[][] kanaRows =
            {
                new[] { "あ", "い", "う", "え", "お" },
                new[] { "か", "き", "く", "け", "こ" },
                new[] { "さ", "し", "す", "せ", "そ" },
                new[] { "た", "ち", "つ", "て", "と" },
                new[] { "な", "に", "ぬ", "ね", "の" },
                new[] { "は", "ひ", "ふ", "へ", "ほ" },
                new[] { "ま", "み", "む", "め", "も" },
                new[] { "や", null, "ゆ", null, "よ" },
                new[] { "ら", "り", "る", "れ", "ろ" },
                new[] { "わ", "を", "ん", null, null },
            };

            var gridGo = new GameObject("InitialGrid");
            gridGo.transform.SetParent(_listContent, false);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.cellSize = new Vector2(194f, 96f);
            grid.spacing = new Vector2(10f, 12f);
            grid.childAlignment = TextAnchor.UpperCenter;
            var le = gridGo.AddComponent<LayoutElement>();
            le.preferredHeight = kanaRows.Length * 96f + (kanaRows.Length - 1) * 12f + 4f;
            _rows.Add(gridGo);

            foreach (var row in kanaRows)
            {
                foreach (var ch in row)
                {
                    if (ch == null)
                    {
                        var emptyGo = new GameObject("Empty");
                        emptyGo.transform.SetParent(gridGo.transform, false);
                        emptyGo.AddComponent<RectTransform>();
                        continue;
                    }
                    if (counts.TryGetValue(ch, out int names) && names > 0)
                    {
                        string initial = ch;
                        var button = UiFactory.CreateSoftButton(gridGo.transform, ch, ch, 34);
                        button.onClick.AddListener(() =>
                        {
                            Se.Play(Se.Tap);
                            _ = ShowNamesAsync(initial, null);
                        });
                    }
                    else
                    {
                        // データのない頭文字は押せない見た目で残す (五十音表の形を保つ)
                        var cellGo = new GameObject(ch);
                        cellGo.transform.SetParent(gridGo.transform, false);
                        var cellImg = cellGo.AddComponent<Image>();
                        cellImg.color = new Color(1f, 1f, 1f, 0.25f);
                        UiFactory.Roundify(cellImg);
                        cellImg.raycastTarget = false;
                        var cellText = UiFactory.CreateText(cellGo.transform, "Label", ch, 34,
                            new Color(0.62f, 0.60f, 0.70f));
                        UiFactory.StretchFull(cellText.rectTransform);
                    }
                }
            }

            // かな以外 (英数字・記号・ルビ未設定) は「その他」にまとまる
            if (counts.TryGetValue("その他", out int others) && others > 0)
            {
                var otherGo = new GameObject("Others");
                otherGo.transform.SetParent(_listContent, false);
                otherGo.AddComponent<RectTransform>();
                var otherLe = otherGo.AddComponent<LayoutElement>();
                otherLe.preferredHeight = 96f;
                var button = UiFactory.CreateSoftButton(otherGo.transform, "OthersButton",
                    "その他 (英数字など) " + others + " 件", 28);
                UiFactory.StretchFull((RectTransform)button.transform);
                button.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    _ = ShowNamesAsync("その他", null);
                });
                _rows.Add(otherGo);
            }
        }

        // ---- 名前一覧 ----

        async Task ShowNamesAsync(string initial, string keyword)
        {
            _level = Level.Names;
            int serial = ++_loadSerial;
            SetStatus("", false);
            ShowLoading();
            ClearRows();
            ListerNamesDto data;
            try
            {
                data = await AppConfig.CreateClient().GetListerNamesAsync(_target, initial, keyword);
            }
            catch (System.Exception e)
            {
                if (serial == _loadSerial)
                {
                    SetStatus("読み込みに失敗: " + e.Message, true);
                    Se.Play(Se.Error);
                }
                return;
            }
            if (serial != _loadSerial || _level != Level.Names)
            {
                return;
            }
            string cond = initial != null ? "「" + initial + "」" : "「" + keyword + "」";
            if (data.Names == null || data.Names.Count == 0)
            {
                SetStatus(cond + " の" + TargetNoun(_target) + "は見つかりませんでした", false);
                return;
            }
            SetStatus(cond + " " + data.Names.Count + " 件"
                + (data.Names.Count >= 500 ? " (最初の500件)" : ""), false);
            foreach (var entry in data.Names)
            {
                string name = entry.Name;
                string sub;
                if (_target == "program")
                {
                    sub = entry.Songs + " 曲";
                    if (!string.IsNullOrEmpty(entry.Group))
                    {
                        sub += "　／　" + entry.Group + "シリーズ";
                    }
                }
                else if (_target == "group")
                {
                    sub = entry.Programs + " 作品・" + entry.Songs + " 曲";
                }
                else
                {
                    sub = entry.Songs + " 曲";
                }
                _rows.Add(UiFactory.CreateIndexRow(_listContent, name, sub, () =>
                {
                    Se.Play(Se.Transition);
                    SearchResultScreen.OpenExact(Manager, _target, name);
                }));
            }
        }
    }
}
