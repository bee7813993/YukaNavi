using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 予約の詳細画面。予約一覧の行タップで開き、曲情報とオプションの現在値を表示して
    /// 操作 (次に再生 / 上へ / 下へ / 予約の変更 / 削除) を行う。
    /// 表示中は最新状態を取り直し、他の端末からの変更にも追従する。
    /// </summary>
    public class RequestDetailScreen : ScreenBase
    {
        static RequestItemDto _pendingItem;

        RequestItemDto _item;
        RectTransform _infoContent;
        readonly List<GameObject> _infoRows = new List<GameObject>();
        Text _statusText;
        Button _warikomiButton;
        Button _upButton;
        Button _downButton;
        Button _stateButton;
        Text _stateLabel;
        Button _editButton;
        Button _deleteButton;
        Text _deleteLabel;
        Text _warikomiLabel;
        bool _deleteArmed;
        bool _endArmed;
        bool _refreshing;
        InputField _commentInput;

        /// <summary>予約を指定して詳細を開く。</summary>
        public static void Open(ScreenManager manager, RequestItemDto item)
        {
            _pendingItem = item;
            manager.Show<RequestDetailScreen>();
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "予約の詳細");

            // 曲情報 (内容に応じて可変なのでスクロールリストに積む)
            var scrollRectT = UiFactory.CreateScrollList(transform, "Info", out _infoContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 344f);
            scrollRectT.offsetMax = new Vector2(-20f, -126f);

            // 操作結果メッセージ
            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.Danger);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 300f);
            statusRect.sizeDelta = new Vector2(-40f, 36f);

            // 操作ボタン群 (下部固定)。主操作は「予約の変更」、移動系は1行にまとめて控えめに
            _editButton = AddBottomButton("予約の変更", UiFactory.Primary, 192f, 96f);
            _editButton.onClick.AddListener(() =>
            {
                if (_item == null)
                {
                    return;
                }
                Se.Play(Se.Transition);
                ReserveScreen.OpenForEdit(Manager, _item);
            });

            // 移動・状態系 (あまり使われないので小さく1行)。先頭は再生中なら「曲を終了」に変わる
            _warikomiButton = UiFactory.CreateSoftButton(transform, "Primary", "次に再生", 26);
            SetBottomRect(_warikomiButton.GetComponent<RectTransform>(), 104f, 76f, 0f, 0.25f);
            _warikomiLabel = _warikomiButton.GetComponentInChildren<Text>();
            UiFactory.FitLabel(_warikomiLabel);
            _warikomiButton.onClick.AddListener(() => _ = PrimaryActionAsync());

            _upButton = UiFactory.CreateSoftButton(transform, "Up", "上へ", 26);
            SetBottomRect(_upButton.GetComponent<RectTransform>(), 104f, 76f, 0.25f, 0.5f);
            UiFactory.FitLabel(_upButton.GetComponentInChildren<Text>());
            _upButton.onClick.AddListener(() => _ = MoveAsync("up"));

            _downButton = UiFactory.CreateSoftButton(transform, "Down", "下へ", 26);
            SetBottomRect(_downButton.GetComponent<RectTransform>(), 104f, 76f, 0.5f, 0.75f);
            UiFactory.FitLabel(_downButton.GetComponentInChildren<Text>());
            _downButton.onClick.AddListener(() => _ = MoveAsync("down"));

            // 再生状況の変更 (未再生 → 再生済 / 再生済 → 未再生)
            _stateButton = UiFactory.CreateSoftButton(transform, "PlayStatus", "再生済に", 26);
            SetBottomRect(_stateButton.GetComponent<RectTransform>(), 104f, 76f, 0.75f, 1f);
            _stateLabel = _stateButton.GetComponentInChildren<Text>();
            UiFactory.FitLabel(_stateLabel);
            _stateButton.onClick.AddListener(() => _ = TogglePlayStatusAsync());

            _deleteButton = AddBottomButton("削除する", UiFactory.Danger, 12f, 80f);
            _deleteLabel = _deleteButton.GetComponentInChildren<Text>();
            _deleteButton.onClick.AddListener(() => _ = DeleteAsync());
        }

        Button AddBottomButton(string label, Color color, float bottom, float height)
        {
            var button = UiFactory.CreateButton(transform, label, label, color, Color.white, 34);
            SetBottomRect(button.GetComponent<RectTransform>(), bottom, height, 0f, 1f);
            return button;
        }

        /// <summary>ナビバー上の下部固定配置 (xMin/xMax は 0〜1 の分割率)。</summary>
        static void SetBottomRect(RectTransform rect, float bottom, float height, float xMin, float xMax)
        {
            rect.anchorMin = new Vector2(xMin, 0f);
            rect.anchorMax = new Vector2(xMax, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + bottom);
            rect.offsetMin = new Vector2(xMin == 0f ? 20f : 8f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(xMax == 1f ? -20f : -8f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        public override void OnShow()
        {
            _item = _pendingItem ?? _item;
            _pendingItem = null;
            if (_item == null)
            {
                Manager.ShowAsRoot<HomeScreen>();
                return;
            }
            ResetDeleteButton();
            SetStatus("");
            RebuildInfo();
            UpdateButtons(true);
            _ = RefreshAsync(); // 最新状態 (変更・移動後の反映) を取り直す
        }

        /// <summary>状態に応じてボタンを出し分ける (再生中は移動不可・曲終了が主操作)。</summary>
        void UpdateButtons(bool exists)
        {
            _endArmed = false;
            bool isPlaying = exists && _item != null && _item.Nowplaying == "再生中";
            bool isPending = exists && _item != null
                && (_item.Nowplaying == "未再生" || _item.Nowplaying == "1");
            _warikomiLabel.text = isPlaying ? "曲を終了" : "次に再生";
            _warikomiLabel.color = isPlaying ? UiFactory.Danger : UiFactory.Primary;
            _warikomiButton.interactable = isPlaying || isPending;
            _upButton.interactable = isPending;
            _downButton.interactable = isPending;
            // 未再生 → 再生済に / 再生済 (停止中含む) → 未再生に。再生中は変更しない
            _stateLabel.text = isPending ? "再生済に" : "未再生に";
            _stateButton.interactable = exists && !isPlaying;
            _editButton.interactable = exists;
            _deleteButton.interactable = exists;
        }

        /// <summary>再生状況のトグル (未再生 ⇔ 再生済)。変更後は一覧の該当カードへ。</summary>
        async Task TogglePlayStatusAsync()
        {
            if (_item == null)
            {
                return;
            }
            bool isPending = _item.Nowplaying == "未再生" || _item.Nowplaying == "1";
            Se.Play(Se.Tap);
            try
            {
                await AppConfig.CreateClient().SetPlayStatusAsync(
                    _item.Id, isPending ? "再生済" : "未再生");
                Se.Play(Se.Confirm);
                // 一覧上の位置づけ (未再生の並び・色) が変わるため該当カードを見せる
                QueueScreen.OpenAndFocus(Manager, _item.Id);
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("再生状況の変更に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        /// <summary>先頭ボタン: 未再生なら割り込み、再生中なら曲を終了して次へ (2度押し確認)。</summary>
        async Task PrimaryActionAsync()
        {
            if (_item == null)
            {
                return;
            }
            if (_item.Nowplaying != "再生中")
            {
                await MoveAsync("warikomi");
                return;
            }
            // BGV・小休止は終了させないと次に進まないため、ここから直接終了できる
            if (!_endArmed)
            {
                _endArmed = true;
                _warikomiLabel.text = "もう一度で終了";
                Se.Play(Se.Tap);
                return;
            }
            try
            {
                await AppConfig.CreateClient().PlayerActionAsync("next");
                Se.Play(Se.Confirm);
                UiFactory.ShowToast("次の曲へ送りました");
                await RefreshAsync();
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("曲の終了に失敗: " + e.Message, true);
                Se.Play(Se.Error);
                _endArmed = false;
                _warikomiLabel.text = "曲を終了";
            }
        }

        void SetStatus(string message)
        {
            _statusText.text = message;
        }

        void ResetDeleteButton()
        {
            _deleteArmed = false;
            if (_deleteLabel != null)
            {
                _deleteLabel.text = "削除する";
            }
        }

        // ---- 表示 ----

        void RebuildInfo()
        {
            foreach (var row in _infoRows)
            {
                Destroy(row);
            }
            _infoRows.Clear();
            if (_item == null)
            {
                return;
            }

            // 曲カード (曲名 + 状態 + 歌う人)
            AddCard(card =>
            {
                float y = 16f;
                y += AddWrapped(card, _item.DisplayName, y, 34, UiFactory.TextDark);
                y += 6f;
                y += AddWrapped(card, StateLine(_item), y, 26, UiFactory.Primary);
                if (!string.IsNullOrEmpty(_item.Singer))
                {
                    y += AddWrapped(card, "うたう人: " + _item.Singer, y, 26, UiFactory.TextDark);
                }
                string lister = ListerLine(_item);
                if (lister != "")
                {
                    y += AddWrapped(card, lister, y, 24, UiFactory.TextMuted);
                }
                return y + 10f;
            });

            // コメントカード (全文表示 + みんなで追記できる)
            AddCard(card =>
            {
                float y = 14f;
                y += AddWrapped(card, "コメント", y, 22, UiFactory.PrimaryDark);
                string comment = (_item.Comment ?? "").Trim();
                if (comment != "")
                {
                    y += AddWrapped(card, comment, y, 24, UiFactory.TextDark);
                }
                y += 6f;

                _commentInput = UiFactory.CreateInputField(card, "CommentInput", "コメントを追記...");
                var inputRect = _commentInput.GetComponent<RectTransform>();
                inputRect.anchorMin = new Vector2(0f, 1f);
                inputRect.anchorMax = new Vector2(1f, 1f);
                inputRect.pivot = new Vector2(0.5f, 1f);
                inputRect.anchoredPosition = new Vector2(0f, -y);
                inputRect.offsetMin = new Vector2(24f, inputRect.offsetMin.y);
                inputRect.offsetMax = new Vector2(-244f, inputRect.offsetMax.y);
                inputRect.sizeDelta = new Vector2(inputRect.sizeDelta.x, 76f);

                var sendButton = UiFactory.CreateButton(card, "SendComment", "追記する",
                    UiFactory.Primary, Color.white, 26);
                var sendRect = sendButton.GetComponent<RectTransform>();
                sendRect.anchorMin = sendRect.anchorMax = new Vector2(1f, 1f);
                sendRect.pivot = new Vector2(1f, 1f);
                sendRect.anchoredPosition = new Vector2(-24f, -y);
                sendRect.sizeDelta = new Vector2(200f, 76f);
                sendButton.onClick.AddListener(() => _ = AddCommentAsync());
                y += 84f;
                return y + 8f;
            });

            // オプションの現在値 (初期値以外のものだけ)
            var options = OptionLines(_item);
            if (options.Count > 0)
            {
                AddCard(card =>
                {
                    float y = 14f;
                    y += AddWrapped(card, "オプション", y, 22, UiFactory.PrimaryDark);
                    foreach (var line in options)
                    {
                        y += AddWrapped(card, line, y, 26, UiFactory.TextDark);
                    }
                    return y + 10f;
                });
            }
        }

        /// <summary>可変高さのカードを情報リストに足す。build は内容を配置して高さを返す。</summary>
        void AddCard(System.Func<RectTransform, float> build)
        {
            var cardGo = new GameObject("Card");
            cardGo.transform.SetParent(_infoContent, false);
            var img = cardGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(cardGo, 3f);
            var le = cardGo.AddComponent<LayoutElement>();
            le.preferredHeight = build((RectTransform)cardGo.transform);
            _infoRows.Add(cardGo);
        }

        float AddWrapped(RectTransform card, string label, float y, int fontSize, Color color)
        {
            int lines = UiFactory.EstimateWrapLines(label, fontSize, 960f);
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

        static string StateLine(RequestItemDto item)
        {
            if (item.Nowplaying == "再生中")
            {
                return "♪ うたっています";
            }
            if (item.Nowplaying == "未再生" || item.Nowplaying == "1")
            {
                return item.Position + " 番目に再生予定";
            }
            return "うたい終わりました";
        }

        static string ListerLine(RequestItemDto item)
        {
            string line = item.ListerArtist ?? "";
            if (!string.IsNullOrEmpty(item.ListerWork))
            {
                line += (line != "" ? "　／　" : "") + item.ListerWork;
                if (!string.IsNullOrEmpty(item.ListerOpEd))
                {
                    line += " [" + item.ListerOpEd + "]";
                }
            }
            return line;
        }

        static List<string> OptionLines(RequestItemDto item)
        {
            var lines = new List<string>();
            if (item.Keychange != 0)
            {
                lines.Add("キー変更: " + (item.Keychange > 0 ? "+" + item.Keychange : item.Keychange.ToString()));
            }
            if (item.Secret == 1)
            {
                lines.Add("シークレット");
            }
            if (item.Loop == 1)
            {
                lines.Add("BGVモード (ループ再生)");
            }
            if (item.Kind == "動画_別プ")
            {
                lines.Add("別プレイヤー再生");
            }
            if (item.Pause == 1)
            {
                lines.Add("小休止リクエスト");
            }
            if (item.Volume > 0)
            {
                lines.Add("音量: +" + item.Volume + " %");
            }
            if (item.Audiodelay != 0)
            {
                lines.Add("音ズレ補正: " + item.Audiodelay + " ms");
            }
            if (item.Track > 0)
            {
                lines.Add("音声トラック: " + (item.Track + 1) + "トラック目");
            }
            return lines;
        }

        // ---- 最新化・操作 ----

        async Task RefreshAsync()
        {
            if (_refreshing || _item == null)
            {
                return;
            }
            _refreshing = true;
            int id = _item.Id;
            try
            {
                var data = await AppConfig.CreateClient().GetRequestsAsync();
                var latest = data.Items?.Find(i => i.Id == id);
                if (latest == null)
                {
                    SetStatus("この予約はもうありません (削除された可能性があります)");
                    UpdateButtons(false);
                }
                else
                {
                    _item = latest;
                    RebuildInfo();
                    UpdateButtons(true);
                }
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

        /// <summary>コメントを追記する (Web 版と同じ「>> コメント by 名前」形式でつながる)。</summary>
        async Task AddCommentAsync()
        {
            string text = (_commentInput.text ?? "").Trim();
            if (text == "" || _item == null)
            {
                Se.Play(Se.Error);
                return;
            }
            Se.Play(Se.Tap);
            try
            {
                await AppConfig.CreateClient().AddRequestCommentAsync(
                    _item.Id, text, AppConfig.Username);
                Se.Play(Se.Confirm);
                await RefreshAsync(); // 追記結果を表示に反映 (入力欄も作り直される)
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("コメントの追記に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        async Task MoveAsync(string action)
        {
            if (_item == null)
            {
                return;
            }
            Se.Play(Se.Tap);
            try
            {
                string message = await AppConfig.CreateClient().MoveRequestAsync(_item.Id, action);
                if (string.IsNullOrEmpty(message))
                {
                    Se.Play(Se.Confirm);
                    // 並べ替えの結果 (どこに動いたか) が見えるよう、一覧の該当カードへ遷移する
                    QueueScreen.OpenAndFocus(Manager, _item.Id);
                    return;
                }
                UiFactory.ShowToast(message); // 「すでに一番上です。」等
                _ = RefreshAsync();
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("操作に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        async Task DeleteAsync()
        {
            if (_item == null)
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
                await AppConfig.CreateClient().DeleteRequestAsync(_item.Id);
                Se.Play(Se.Confirm);
                Manager.Back(); // 一覧へ戻る
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("削除に失敗: " + e.Message, true);
                Se.Play(Se.Error);
                ResetDeleteButton();
            }
        }
    }
}
