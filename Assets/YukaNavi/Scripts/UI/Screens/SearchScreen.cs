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
    /// 検索画面。キーワード検索 → 結果リスト → 予約確認モーダル → 予約完了演出。
    /// デンモクの操作動線 (検索して選んで予約) をキーワード検索メインで実装する。
    /// </summary>
    public class SearchScreen : ScreenBase
    {
        /// <summary>結果リストの表示上限 (UGUI の負荷対策。超過分は絞り込みを促す)</summary>
        const int MaxRows = 100;

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
        SearchItemDto _selected;

        // 予約完了演出
        GameObject _completeOverlay;
        RectTransform _completePose;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            // 上部バー (戻る + タイトル)
            var topBar = UiFactory.CreatePanel(transform, "TopBar", UiFactory.Primary);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 110f);

            // 戻る操作はグローバルナビバーが担当する
            var title = UiFactory.CreateText(topBar, "Title", "曲をさがす", 42, Color.white);
            UiFactory.StretchFull(title.rectTransform);

            // 検索行 (入力 + ボタン)
            _searchInput = UiFactory.CreateInputField(transform, "SearchInput", "曲名・アーティスト名など");
            var inputRect = _searchInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.anchoredPosition = new Vector2(0f, -130f);
            inputRect.offsetMin = new Vector2(20f, inputRect.offsetMin.y);
            inputRect.offsetMax = new Vector2(-260f, inputRect.offsetMax.y);
            inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, 90f);

            _searchButton = UiFactory.CreateButton(transform, "SearchButton", "検索",
                UiFactory.Primary, Color.white, 38);
            var searchBtnRect = _searchButton.GetComponent<RectTransform>();
            searchBtnRect.anchorMin = searchBtnRect.anchorMax = new Vector2(1f, 1f);
            searchBtnRect.pivot = new Vector2(1f, 1f);
            searchBtnRect.anchoredPosition = new Vector2(-20f, -130f);
            searchBtnRect.sizeDelta = new Vector2(220f, 90f);
            _searchButton.onClick.AddListener(() => _ = SearchAsync());

            // ステータス行
            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -235f);
            statusRect.sizeDelta = new Vector2(-40f, 40f);

            BuildResultList();
            BuildConfirmModal();
            BuildCompleteOverlay();
        }

        void BuildResultList()
        {
            var scrollRectT = UiFactory.CreateScrollList(transform, "ResultList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -290f);
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
                var result = await AppConfig.CreateClient().SearchAsync(keyword);
                if (result.Count == 0)
                {
                    SetStatus("見つかりませんでした", false);
                    return;
                }
                int shown = Mathf.Min(result.Items.Count, MaxRows);
                for (int i = 0; i < shown; i++)
                {
                    AddResultRow(result.Items[i]);
                }
                SetStatus(result.Count > shown
                    ? $"{result.Count} 件中 {shown} 件を表示中 (ワードを足して絞り込めます)"
                    : $"{result.Count} 件見つかりました", false);
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

        void AddResultRow(SearchItemDto item)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 112f;
            var button = rowGo.AddComponent<Button>();
            button.onClick.AddListener(() => OpenConfirm(item));

            var nameText = UiFactory.CreateText(rowGo.transform, "Name", item.Name, 29,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(24f, 6f);
            nameText.rectTransform.offsetMax = new Vector2(-24f, -6f);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            _rows.Add(rowGo);
        }

        void OpenConfirm(SearchItemDto item)
        {
            Se.Play(Se.Tap);
            _selected = item;
            _modalSongText.text = item.Name;
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
                    _selected.Name, _selected.FullPath, name, (_commentInput.text ?? "").Trim());
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
