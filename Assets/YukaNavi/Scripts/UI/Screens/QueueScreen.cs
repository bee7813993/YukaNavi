using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 予約一覧画面。3秒間隔のポーリングで一覧を更新し、再生中の曲をハイライトする。
    /// 行タップで操作メニュー (次に再生 / 上へ / 下へ / 削除) を開く。
    /// 一般のカラオケ端末の慣習に合わせ、操作は全ユーザーが行える (削除のみ2度押し確認)。
    /// </summary>
    public class QueueScreen : ScreenBase
    {
        const float PollIntervalSeconds = 3f;

        Text _headerText;
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        string _lastSignature = "";
        bool _refreshing;
        Coroutine _polling;

        // 操作モーダル
        GameObject _modal;
        Text _modalSongText;
        Text _modalInfoText;
        Button _deleteButton;
        Text _deleteLabel;
        bool _deleteArmed;
        RequestItemDto _selected;

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
            var title = UiFactory.CreateText(topBar, "Title", "予約一覧", 42, Color.white);
            UiFactory.StretchFull(title.rectTransform);

            // ヘッダー (残り件数・時間)
            _headerText = UiFactory.CreateText(transform, "Header", "", 30, UiFactory.PrimaryDark);
            var headerRect = _headerText.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -122f);
            headerRect.sizeDelta = new Vector2(-40f, 46f);

            // ステータス行 (エラー等)
            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -172f);
            statusRect.sizeDelta = new Vector2(-40f, 36f);

            // 一覧
            var scrollRectT = UiFactory.CreateScrollList(transform, "QueueList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -215f);

            BuildOpsModal();
        }

        void BuildOpsModal()
        {
            _modal = new GameObject("OpsModal");
            _modal.transform.SetParent(transform, false);
            var overlayRect = _modal.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _modal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);
            var overlayButton = _modal.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(CloseModal);

            var card = UiFactory.CreatePanel(_modal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880f, 900f);
            // カード内タップがオーバーレイの「閉じる」に抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            _modalSongText = UiFactory.CreateText(card, "Song", "", 34, UiFactory.TextDark);
            SetCardRow(_modalSongText.rectTransform, -40f, 130f);

            _modalInfoText = UiFactory.CreateText(card, "Info", "", 28, UiFactory.PrimaryDark);
            SetCardRow(_modalInfoText.rectTransform, -180f, 40f);

            var warikomi = AddOpsButton(card, "次に再生 (割り込み)", UiFactory.Primary, -240f);
            warikomi.onClick.AddListener(() => _ = MoveAsync("warikomi"));
            var up = AddOpsButton(card, "上へ", UiFactory.Primary, -350f);
            up.onClick.AddListener(() => _ = MoveAsync("up"));
            var down = AddOpsButton(card, "下へ", UiFactory.Primary, -460f);
            down.onClick.AddListener(() => _ = MoveAsync("down"));

            _deleteButton = AddOpsButton(card, "削除する", UiFactory.Danger, -570f);
            _deleteLabel = _deleteButton.GetComponentInChildren<Text>();
            _deleteButton.onClick.AddListener(() => _ = DeleteAsync());

            var close = AddOpsButton(card, "閉じる", new Color(0.75f, 0.73f, 0.80f), -680f);
            close.onClick.AddListener(CloseModal);

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

        Button AddOpsButton(RectTransform card, string label, Color color, float y)
        {
            var button = UiFactory.CreateButton(card, label, label, color, Color.white, 34);
            var rect = button.GetComponent<RectTransform>();
            SetCardRow(rect, y, 96f);
            return button;
        }

        public override void OnShow()
        {
            _modal.SetActive(false);
            _lastSignature = "";
            SetStatus("");
            _polling = StartCoroutine(PollRoutine());
        }

        public override void OnHide()
        {
            if (_polling != null)
            {
                StopCoroutine(_polling);
                _polling = null;
            }
        }

        IEnumerator PollRoutine()
        {
            while (true)
            {
                _ = RefreshAsync();
                yield return new WaitForSeconds(PollIntervalSeconds);
            }
        }

        async Task RefreshAsync()
        {
            if (_refreshing)
            {
                return;
            }
            _refreshing = true;
            try
            {
                var data = await AppConfig.CreateClient().GetRequestsAsync();
                int minutes = Mathf.CeilToInt(data.RemainingSeconds / 60f);
                _headerText.text = data.Total == 0
                    ? "予約はありません"
                    : $"全 {data.Total} 件 / 未再生 {data.RemainingCount} 件"
                      + (data.RemainingSeconds > 0 ? $" (約 {minutes} 分)" : "");
                SetStatus("");
                RebuildIfChanged(data.Items);
            }
            catch (System.Exception e)
            {
                SetStatus("更新に失敗: " + e.Message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>内容が変わったときだけ行を作り直す (スクロール位置の維持のため)。</summary>
        void RebuildIfChanged(List<RequestItemDto> items)
        {
            var sig = new StringBuilder();
            if (items != null)
            {
                foreach (var item in items)
                {
                    sig.Append(item.Id).Append(':').Append(item.Nowplaying)
                       .Append(':').Append(item.Reqorder).Append('|');
                }
            }
            string signature = sig.ToString();
            if (signature == _lastSignature)
            {
                return;
            }
            _lastSignature = signature;

            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
            if (items == null)
            {
                return;
            }
            foreach (var item in items)
            {
                AddRow(item);
            }
        }

        void AddRow(RequestItemDto item)
        {
            bool isPlaying = item.Nowplaying == "再生中";
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = isPlaying ? new Color(0.90f, 0.84f, 1.0f) : UiFactory.CardBg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 132f;
            var button = rowGo.AddComponent<Button>();
            button.onClick.AddListener(() => OpenOps(item));

            var nameText = UiFactory.CreateText(rowGo.transform, "Name", item.DisplayName, 30,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(24f, 52f);
            nameText.rectTransform.offsetMax = new Vector2(-24f, -10f);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            string subText = isPlaying
                ? "▶ 再生中"
                : (item.Nowplaying == "未再生" || item.Nowplaying == "1"
                    ? $"{item.Position} 番目"
                    : item.Nowplaying);
            if (!string.IsNullOrEmpty(item.Singer))
            {
                subText += "　うたう人: " + item.Singer;
            }
            var sub = UiFactory.CreateText(rowGo.transform, "Sub", subText, 24,
                isPlaying ? UiFactory.Primary : new Color(0.45f, 0.42f, 0.55f), TextAnchor.LowerLeft);
            UiFactory.StretchFull(sub.rectTransform);
            sub.rectTransform.offsetMin = new Vector2(24f, 10f);
            sub.rectTransform.offsetMax = new Vector2(-24f, -84f);

            _rows.Add(rowGo);
        }

        void OpenOps(RequestItemDto item)
        {
            Se.Play(Se.Tap);
            _selected = item;
            _modalSongText.text = item.DisplayName;
            _modalInfoText.text = string.IsNullOrEmpty(item.Singer) ? "" : "うたう人: " + item.Singer;
            ResetDeleteButton();
            _modal.SetActive(true);
        }

        void CloseModal()
        {
            _modal.SetActive(false);
        }

        void ResetDeleteButton()
        {
            _deleteArmed = false;
            _deleteLabel.text = "削除する";
        }

        async Task MoveAsync(string action)
        {
            if (_selected == null)
            {
                return;
            }
            try
            {
                string message = await AppConfig.CreateClient().MoveRequestAsync(_selected.Id, action);
                if (string.IsNullOrEmpty(message))
                {
                    Se.Play(Se.Confirm);
                    CloseModal();
                }
                else
                {
                    // 「すでに一番上です。」などの情報メッセージ
                    _modalInfoText.text = message;
                }
                _lastSignature = "";
                _ = RefreshAsync();
            }
            catch (System.Exception e)
            {
                _modalInfoText.text = "操作に失敗: " + e.Message;
                Se.Play(Se.Error);
            }
        }

        async Task DeleteAsync()
        {
            if (_selected == null)
            {
                return;
            }
            // 誤操作防止の2度押し確認
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                _deleteLabel.text = "もう一度押すと削除します";
                Se.Play(Se.Tap);
                return;
            }
            try
            {
                await AppConfig.CreateClient().DeleteRequestAsync(_selected.Id);
                Se.Play(Se.Confirm);
                CloseModal();
                _lastSignature = "";
                _ = RefreshAsync();
            }
            catch (System.Exception e)
            {
                _modalInfoText.text = "削除に失敗: " + e.Message;
                Se.Play(Se.Error);
                ResetDeleteButton();
            }
        }

        void SetStatus(string message)
        {
            _statusText.text = message;
            _statusText.color = UiFactory.Danger;
        }
    }
}
