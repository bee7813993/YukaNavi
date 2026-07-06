using System.Collections;
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
    /// キーワード検索 → 結果リスト → 予約確認モーダル → 予約完了演出。
    /// </summary>
    public class SearchScreen : ScreenBase
    {
        /// <summary>結果リストの表示上限 (UGUI の負荷対策。超過分は絞り込みを促す)</summary>
        const int MaxRows = 100;

        /// <summary>検索結果の共通形 (どちらのモードでも同じ予約フローに流す)。</summary>
        class SearchEntry
        {
            public string Line1;     // 曲名 (またはファイル名)
            public string Line2;     // 歌手 / 作品 (ファイル検索では空)
            public string Filename;  // 予約に使う表示ファイル名
            public string FullPath;  // 予約に使うフルパス
        }

        bool _listerMode;
        Button _fileTab;
        Button _listerTab;
        InputField _searchInput;
        Button _searchButton;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();

        // 予約確認モーダル
        GameObject _modal;
        Text _modalSongText;
        Text _modalErrorText;
        InputField _nameInput;
        InputField _commentInput;
        Button _submitButton;
        SearchEntry _selected;

        // 予約完了演出
        GameObject _completeOverlay;
        RectTransform _completePose;

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

            BuildResultList();
            BuildConfirmModal();
            BuildCompleteOverlay();
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

        void BuildResultList()
        {
            var scrollRectT = UiFactory.CreateScrollList(transform, "ResultList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -356f);
        }

        void BuildConfirmModal()
        {
            _modal = new GameObject("ConfirmModal");
            _modal.transform.SetParent(transform, false);
            var overlayRect = _modal.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _modal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);

            var card = UiFactory.CreatePanel(_modal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.55f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(940f, 820f);

            var title = UiFactory.CreateText(card, "Title", "この曲を予約しますか？", 40, UiFactory.PrimaryDark);
            SetCardRow(title.rectTransform, -30f, 60f);

            _modalSongText = UiFactory.CreateText(card, "Song", "", 32, UiFactory.TextDark);
            SetCardRow(_modalSongText.rectTransform, -105f, 130f);

            var nameLabel = UiFactory.CreateText(card, "NameLabel", "歌う人の名前", 28,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(nameLabel.rectTransform, -255f, 36f);
            _nameInput = UiFactory.CreateInputField(card, "NameInput", "名前 (必須)");
            SetCardRow(_nameInput.GetComponent<RectTransform>(), -295f, 80f);

            var commentLabel = UiFactory.CreateText(card, "CommentLabel", "コメント (任意)", 28,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(commentLabel.rectTransform, -395f, 36f);
            _commentInput = UiFactory.CreateInputField(card, "CommentInput", "");
            SetCardRow(_commentInput.GetComponent<RectTransform>(), -435f, 80f);

            _modalErrorText = UiFactory.CreateText(card, "Error", "", 26, UiFactory.Danger);
            SetCardRow(_modalErrorText.rectTransform, -530f, 40f);

            _submitButton = UiFactory.CreateButton(card, "Submit", "予約する", UiFactory.Primary, Color.white, 38);
            var submitRect = _submitButton.GetComponent<RectTransform>();
            submitRect.anchorMin = submitRect.anchorMax = new Vector2(0.5f, 0f);
            submitRect.pivot = new Vector2(0.5f, 0f);
            submitRect.anchoredPosition = new Vector2(-190f, 40f);
            submitRect.sizeDelta = new Vector2(340f, 96f);
            _submitButton.onClick.AddListener(() => _ = SubmitAsync());

            var cancelButton = UiFactory.CreateButton(card, "Cancel", "やめる",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 38);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(190f, 40f);
            cancelRect.sizeDelta = new Vector2(340f, 96f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _modal.SetActive(false);
            });

            _modal.SetActive(false);
        }

        static void SetCardRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(50f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-50f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        void BuildCompleteOverlay()
        {
            _completeOverlay = new GameObject("CompleteOverlay");
            _completeOverlay.transform.SetParent(transform, false);
            var overlayRect = _completeOverlay.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _completeOverlay.AddComponent<Image>();
            overlay.color = new Color(0.97f, 0.95f, 1f, 0.88f);
            var closeButton = _completeOverlay.AddComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _completeOverlay.SetActive(false);
            });

            var message = UiFactory.CreateText(_completeOverlay.transform, "Message", "予約したよ♪", 64, UiFactory.Primary);
            var msgRect = message.rectTransform;
            msgRect.anchorMin = msgRect.anchorMax = new Vector2(0.5f, 1f);
            msgRect.pivot = new Vector2(0.5f, 1f);
            msgRect.anchoredPosition = new Vector2(0f, -220f);
            msgRect.sizeDelta = new Vector2(800f, 90f);

            var pose = UiFactory.CreateImage(_completeOverlay.transform, "Pose", "Art/Mascot/yukari_pose_request_complete");
            pose.preserveAspect = true;
            _completePose = pose.rectTransform;
            _completePose.anchorMin = _completePose.anchorMax = new Vector2(0.5f, 0.45f);
            _completePose.pivot = new Vector2(0.5f, 0.5f);
            _completePose.sizeDelta = new Vector2(700f, 1050f);

            var hint = UiFactory.CreateText(_completeOverlay.transform, "Hint", "タップで閉じる", 30, UiFactory.PrimaryDark);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 60f);
            hintRect.sizeDelta = new Vector2(600f, 44f);

            _completeOverlay.SetActive(false);
        }

        public override void OnShow()
        {
            _modal.SetActive(false);
            _completeOverlay.SetActive(false);
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

        /// <summary>パス区切り (\ と / の両方) を考慮した basename。</summary>
        static string BaseName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            int cut = Mathf.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return cut >= 0 ? path.Substring(cut + 1) : path;
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
                var entries = new List<SearchEntry>();
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
                            entries.Add(new SearchEntry
                            {
                                Line1 = string.IsNullOrEmpty(item.SongName) ? BaseName(item.FoundPath) : item.SongName,
                                Line2 = line2,
                                Filename = BaseName(item.FoundPath),
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
                            entries.Add(new SearchEntry
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

        void AddResultRow(SearchEntry entry)
        {
            bool twoLines = !string.IsNullOrEmpty(entry.Line2);
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = twoLines ? 132f : 112f;
            var button = rowGo.AddComponent<Button>();
            button.onClick.AddListener(() => OpenConfirm(entry));

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

        void OpenConfirm(SearchEntry entry)
        {
            Se.Play(Se.Tap);
            _selected = entry;
            _modalSongText.text = string.IsNullOrEmpty(entry.Line2)
                ? entry.Line1
                : entry.Line1 + "\n" + entry.Line2;
            _modalErrorText.text = "";
            _nameInput.text = AppConfig.Username;
            _commentInput.text = "";
            _submitButton.interactable = true;
            _modal.SetActive(true);
        }

        async Task SubmitAsync()
        {
            if (_selected == null)
            {
                return;
            }
            string name = (_nameInput.text ?? "").Trim();
            if (name == "")
            {
                _modalErrorText.text = "名前を入力してください";
                Se.Play(Se.Error);
                return;
            }
            AppConfig.Username = name;
            _submitButton.interactable = false;
            _modalErrorText.text = "";
            try
            {
                await AppConfig.CreateClient().PostRequestAsync(
                    _selected.Filename, _selected.FullPath, name, (_commentInput.text ?? "").Trim());
                _modal.SetActive(false);
                ShowComplete();
            }
            catch (System.Exception e)
            {
                _modalErrorText.text = "予約に失敗: " + e.Message;
                Se.Play(Se.Error);
                _submitButton.interactable = true;
            }
        }

        void ShowComplete()
        {
            Se.Play(Se.ReservationComplete);
            _completeOverlay.SetActive(true);
            StartCoroutine(CompletePopRoutine());
        }

        /// <summary>完了ポーズをぽよんと登場させる (0.7 → 1.05 → 1.0)。</summary>
        IEnumerator CompletePopRoutine()
        {
            const float duration = 0.35f;
            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                float t = e / duration;
                float scale = t < 0.7f
                    ? Mathf.Lerp(0.7f, 1.05f, t / 0.7f)
                    : Mathf.Lerp(1.05f, 1.0f, (t - 0.7f) / 0.3f);
                _completePose.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            _completePose.localScale = Vector3.one;
        }
    }
}
