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
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            // 上部バー
            UiFactory.CreateTopBar(transform, "予約一覧");

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
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            // カード内タップがオーバーレイの「閉じる」に抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            _modalSongText = UiFactory.CreateText(card, "Song", "", 34, UiFactory.TextDark);
            SetCardRow(_modalSongText.rectTransform, -40f, 130f);

            _modalInfoText = UiFactory.CreateText(card, "Info", "", 28, UiFactory.PrimaryDark);
            SetCardRow(_modalInfoText.rectTransform, -180f, 40f);

            var warikomi = AddOpsButton(card, "次に再生 (割り込み)", UiFactory.Primary, -240f);
            warikomi.onClick.AddListener(() => _ = MoveAsync("warikomi"));
            var up = AddOpsOutlineButton(card, "上へ", -350f);
            up.onClick.AddListener(() => _ = MoveAsync("up"));
            var down = AddOpsOutlineButton(card, "下へ", -460f);
            down.onClick.AddListener(() => _ = MoveAsync("down"));

            _deleteButton = AddOpsButton(card, "削除する", UiFactory.Danger, -570f);
            _deleteLabel = _deleteButton.GetComponentInChildren<Text>();
            _deleteButton.onClick.AddListener(() => _ = DeleteAsync());

            var close = AddOpsOutlineButton(card, "閉じる", -680f);
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

        Button AddOpsOutlineButton(RectTransform card, string label, float y)
        {
            var button = UiFactory.CreateOutlineButton(card, label, label, 34);
            SetCardRow(button.GetComponent<RectTransform>(), y, 96f);
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
            bool isPending = item.Nowplaying == "未再生" || item.Nowplaying == "1";
            bool isDone = !isPlaying && !isPending;
            var img = rowGo.AddComponent<Image>();
            img.color = isPlaying ? UiFactory.PrimaryPale
                : (isDone ? new Color(0.965f, 0.955f, 0.985f) : UiFactory.CardBg);
            UiFactory.Roundify(img);
            UiFactory.AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 136f;
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();
            button.onClick.AddListener(() => OpenOps(item));

            // 左: 順番の丸バッジ (再生中 ▶ / 再生済 ✓ / 未再生は再生順)
            var circleGo = new GameObject("Order");
            circleGo.transform.SetParent(rowGo.transform, false);
            var circleImg = circleGo.AddComponent<Image>();
            circleImg.sprite = UiFactory.RoundedSprite;
            circleImg.type = Image.Type.Sliced;
            circleImg.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に見せる
            circleImg.color = isPlaying ? UiFactory.Primary
                : (isDone ? new Color(0.85f, 0.83f, 0.90f) : UiFactory.PrimaryPale);
            circleImg.raycastTarget = false;
            var circleRect = circleGo.GetComponent<RectTransform>();
            circleRect.anchorMin = circleRect.anchorMax = new Vector2(0f, 0.5f);
            circleRect.pivot = new Vector2(0f, 0.5f);
            circleRect.anchoredPosition = new Vector2(20f, 0f);
            circleRect.sizeDelta = new Vector2(78f, 78f);
            string mark = isPlaying ? "▶" : (isDone ? "✓" : item.Position.ToString());
            var circleText = UiFactory.CreateText(circleGo.transform, "Mark", mark, 32,
                isPlaying ? Color.white : UiFactory.PrimaryDark);
            UiFactory.StretchFull(circleText.rectTransform);

            // 中央: 曲名 (2行まで) + 歌う人
            var nameText = UiFactory.CreateText(rowGo.transform, "Name", item.DisplayName, 29,
                isDone ? UiFactory.TextMuted : UiFactory.TextDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(118f, 46f);
            nameText.rectTransform.offsetMax = new Vector2(-24f, -12f);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            string subText = isPlaying ? "♪ うたっています"
                : (isDone ? "うたい終わりました" : "");
            if (!string.IsNullOrEmpty(item.Singer))
            {
                subText = (subText != "" ? subText + "　" : "") + "うたう人: " + item.Singer;
            }
            var sub = UiFactory.CreateText(rowGo.transform, "Sub", subText, 22,
                isPlaying ? UiFactory.Primary : UiFactory.TextMuted, TextAnchor.LowerLeft);
            UiFactory.StretchFull(sub.rectTransform);
            sub.rectTransform.offsetMin = new Vector2(118f, 12f);
            sub.rectTransform.offsetMax = new Vector2(-24f, -92f);

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
