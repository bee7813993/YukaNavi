using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 検索画面。「ファイル名 (Everything)」と「アニソンDB (ListerDB)」の2モードを持つ。
    /// キーワード検索 → 結果リスト → 予約 (ReserveDialog)。
    /// </summary>
    public class SearchScreen : ScreenBase
    {
        /// <summary>結果リストの表示上限 (UGUI の負荷対策。超過分は絞り込みを促す)</summary>
        const int MaxRows = 100;

        bool _listerMode;
        Button _fileTab;
        Button _listerTab;
        InputField _searchInput;
        Button _searchButton;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        ReserveDialog _reserveDialog;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            // 上部バー
            var topBar = UiFactory.CreatePanel(transform, "TopBar", UiFactory.Primary);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 110f);
            var title = UiFactory.CreateText(topBar, "Title", "曲をさがす", 42, Color.white);
            UiFactory.StretchFull(title.rectTransform);

            // 検索モードタブ (ファイル名 / アニソンDB)
            var tabBar = UiFactory.CreatePanel(transform, "Tabs");
            tabBar.anchorMin = new Vector2(0f, 1f);
            tabBar.anchorMax = new Vector2(1f, 1f);
            tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -118f);
            tabBar.offsetMin = new Vector2(20f, tabBar.offsetMin.y);
            tabBar.offsetMax = new Vector2(-20f, tabBar.offsetMax.y);
            tabBar.sizeDelta = new Vector2(tabBar.sizeDelta.x, 72f);
            var tabLayout = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.childForceExpandWidth = true;
            tabLayout.childForceExpandHeight = true;
            tabLayout.spacing = 8f;
            _fileTab = UiFactory.CreateButton(tabBar, "FileTab", "ファイル名でさがす", UiFactory.Primary, Color.white, 30);
            _fileTab.onClick.AddListener(() => SetMode(false));
            _listerTab = UiFactory.CreateButton(tabBar, "ListerTab", "アニソンDBでさがす", UiFactory.Primary, Color.white, 30);
            _listerTab.onClick.AddListener(() => SetMode(true));

            // 検索行 (入力 + ボタン)
            _searchInput = UiFactory.CreateInputField(transform, "SearchInput", "曲名・アーティスト名など");
            var inputRect = _searchInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.anchoredPosition = new Vector2(0f, -206f);
            inputRect.offsetMin = new Vector2(20f, inputRect.offsetMin.y);
            inputRect.offsetMax = new Vector2(-260f, inputRect.offsetMax.y);
            inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, 90f);

            _searchButton = UiFactory.CreateButton(transform, "SearchButton", "検索",
                UiFactory.Primary, Color.white, 38);
            var searchBtnRect = _searchButton.GetComponent<RectTransform>();
            searchBtnRect.anchorMin = searchBtnRect.anchorMax = new Vector2(1f, 1f);
            searchBtnRect.pivot = new Vector2(1f, 1f);
            searchBtnRect.anchoredPosition = new Vector2(-20f, -206f);
            searchBtnRect.sizeDelta = new Vector2(220f, 90f);
            _searchButton.onClick.AddListener(() => _ = SearchAsync());

            // ステータス行
            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -308f);
            statusRect.sizeDelta = new Vector2(-40f, 40f);

            var scrollRectT = UiFactory.CreateScrollList(transform, "ResultList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -356f);

            _reserveDialog = ReserveDialog.Create(transform);
            SetMode(false);
        }

        /// <summary>検索モードを切り替える (タブの見た目・プレースホルダ・結果クリア)。</summary>
        void SetMode(bool listerMode)
        {
            _listerMode = listerMode;
            var offColor = new Color(0.78f, 0.76f, 0.84f);
            _fileTab.image.color = listerMode ? offColor : UiFactory.Primary;
            _listerTab.image.color = listerMode ? UiFactory.Primary : offColor;
            var placeholder = (Text)_searchInput.placeholder;
            placeholder.text = listerMode ? "曲名・歌手・作品名など" : "曲名・アーティスト名など";
            ClearRows();
            SetStatus("", false);
        }

        public override void OnShow()
        {
            _reserveDialog.HideAll();
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

        async Task SearchAsync()
        {
            string keyword = (_searchInput.text ?? "").Trim();
            if (keyword == "")
            {
                SetStatus("検索ワードを入力してください", true);
                Se.Play(Se.Error);
                return;
            }
            _searchButton.interactable = false;
            SetStatus("検索中...", false);
            ClearRows();
            try
            {
                var entries = new List<ReserveDialog.Entry>();
                int total;
                if (_listerMode)
                {
                    var result = await AppConfig.CreateClient().SearchListerAsync(keyword, MaxRows);
                    total = result.Total;
                    if (total > 0 && result.Items != null)
                    {
                        foreach (var item in result.Items)
                        {
                            if (string.IsNullOrEmpty(item.FoundPath))
                            {
                                continue;
                            }
                            string line2 = item.Artist ?? "";
                            if (!string.IsNullOrEmpty(item.ProgramName))
                            {
                                line2 += (line2 != "" ? "　／　" : "") + item.ProgramName;
                                if (!string.IsNullOrEmpty(item.OpEd))
                                {
                                    line2 += " (" + item.OpEd + ")";
                                }
                            }
                            entries.Add(new ReserveDialog.Entry
                            {
                                Line1 = string.IsNullOrEmpty(item.SongName)
                                    ? ReserveDialog.BaseName(item.FoundPath)
                                    : item.SongName,
                                Line2 = line2,
                                Filename = ReserveDialog.BaseName(item.FoundPath),
                                FullPath = item.FoundPath,
                            });
                        }
                    }
                }
                else
                {
                    var result = await AppConfig.CreateClient().SearchAsync(keyword);
                    total = result.Total;
                    if (result.Items != null)
                    {
                        foreach (var item in result.Items)
                        {
                            entries.Add(new ReserveDialog.Entry
                            {
                                Line1 = item.Name,
                                Line2 = "",
                                Filename = item.Name,
                                FullPath = item.FullPath,
                            });
                        }
                    }
                }

                if (entries.Count == 0)
                {
                    SetStatus("見つかりませんでした", false);
                    return;
                }
                int shown = Mathf.Min(entries.Count, MaxRows);
                for (int i = 0; i < shown; i++)
                {
                    AddResultRow(entries[i]);
                }
                SetStatus(total > shown
                    ? $"{total} 件中 {shown} 件を表示中 (ワードを足して絞り込めます)"
                    : $"{entries.Count} 件見つかりました", false);
            }
            catch (System.Exception e)
            {
                SetStatus("検索に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            finally
            {
                _searchButton.interactable = true;
            }
        }

        void AddResultRow(ReserveDialog.Entry entry)
        {
            bool twoLines = !string.IsNullOrEmpty(entry.Line2);
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = twoLines ? 132f : 112f;
            var button = rowGo.AddComponent<Button>();
            button.onClick.AddListener(() => _reserveDialog.Open(entry));

            var nameText = UiFactory.CreateText(rowGo.transform, "Name", entry.Line1, 30,
                UiFactory.TextDark, twoLines ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            UiFactory.StretchFull(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(24f, twoLines ? 52f : 6f);
            nameText.rectTransform.offsetMax = new Vector2(-24f, -6f);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            if (twoLines)
            {
                var sub = UiFactory.CreateText(rowGo.transform, "Sub", entry.Line2, 24,
                    new Color(0.45f, 0.42f, 0.55f), TextAnchor.LowerLeft);
                UiFactory.StretchFull(sub.rectTransform);
                sub.rectTransform.offsetMin = new Vector2(24f, 10f);
                sub.rectTransform.offsetMax = new Vector2(-24f, -84f);
                sub.verticalOverflow = VerticalWrapMode.Truncate;
            }

            _rows.Add(rowGo);
        }
    }
}
