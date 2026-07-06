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
    /// リモコン画面。/api/player.php の操作を UI 化し、再生中情報をポーリング表示する。
    /// 一般のカラオケ端末の慣習に合わせ全ユーザーが使える (次の曲へ のみ2度押し確認)。
    /// foobar2000 サーバー (playmode=2) では非対応の MPC 専用ボタンを隠す。
    /// </summary>
    public class PlayerScreen : ScreenBase
    {
        const float PollIntervalSeconds = 2f;
        const float NextConfirmWindowSeconds = 3f;

        Text _titleText;
        Text _singerText;
        Text _timeText;
        Text _nextSongText;
        Text _statusText;
        Text _nextButtonLabel;
        readonly List<GameObject> _mpcOnlyButtons = new List<GameObject>();
        Coroutine _polling;
        bool _refreshing;
        float _nextArmedAt = -100f;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            // 上部バー
            UiFactory.CreateTopBar(transform, "リモコン");

            // 再生中カード
            var card = UiFactory.CreatePanel(transform, "NowPlaying", UiFactory.CardBg);
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -126f);
            card.offsetMin = new Vector2(20f, card.offsetMin.y);
            card.offsetMax = new Vector2(-20f, card.offsetMax.y);
            card.sizeDelta = new Vector2(card.sizeDelta.x, 285f);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject);

            _titleText = UiFactory.CreateText(card, "Song", "", 33, UiFactory.TextDark);
            SetCardRow(_titleText.rectTransform, -14f, 96f);
            _singerText = UiFactory.CreateText(card, "Singer", "", 27, UiFactory.PrimaryDark);
            SetCardRow(_singerText.rectTransform, -116f, 38f);
            _timeText = UiFactory.CreateText(card, "Time", "", 28, UiFactory.TextDark);
            SetCardRow(_timeText.rectTransform, -160f, 40f);
            _nextSongText = UiFactory.CreateText(card, "Next", "", 25, new Color(0.45f, 0.42f, 0.55f));
            SetCardRow(_nextSongText.rectTransform, -206f, 66f);

            // ステータス行
            _statusText = UiFactory.CreateText(transform, "Status", "", 27, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -428f);
            statusRect.sizeDelta = new Vector2(-40f, 42f);

            // 操作ボタン: 主要操作 (1段目・大) / シーク・音量 (中段) / その他 (下段)
            var mainGrid = CreateButtonGrid("MainGrid", -486f, new Vector2(508f, 150f), 150f);
            AddActionButton(mainGrid.transform, "再生 / 一時停止", "playpause", false, 36);
            var nextButton = AddActionButton(mainGrid.transform, "次の曲へ", null, false, 36);
            _nextButtonLabel = nextButton.GetComponentInChildren<Text>();
            nextButton.onClick.AddListener(OnNextPressed);

            var midGrid = CreateButtonGrid("SeekVolumeGrid", -668f, new Vector2(508f, 110f), 236f);
            AddOutlineActionButton(midGrid.transform, "少し戻る", "seek_back", true);
            AddOutlineActionButton(midGrid.transform, "少し進む", "seek_forward", true);
            AddOutlineActionButton(midGrid.transform, "音量 −", "volume_down", false);
            AddOutlineActionButton(midGrid.transform, "音量 ＋", "volume_up", false);

            var subGrid = CreateButtonGrid("OtherGrid", -936f, new Vector2(508f, 110f), 236f);
            AddOutlineActionButton(subGrid.transform, "曲の頭から", "start_first", false);
            AddOutlineActionButton(subGrid.transform, "停止", "stop", true);
            AddOutlineActionButton(subGrid.transform, "ミュート", "mute", true);
            AddOutlineActionButton(subGrid.transform, "フェードアウト", "fadeout", true);
        }

        GridLayoutGroup CreateButtonGrid(string name, float y, Vector2 cell, float height)
        {
            var panel = UiFactory.CreatePanel(transform, name);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, y);
            panel.offsetMin = new Vector2(20f, panel.offsetMin.y);
            panel.offsetMax = new Vector2(-20f, panel.offsetMax.y);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
            var grid = panel.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.cellSize = cell;
            grid.spacing = new Vector2(16f, 16f);
            grid.childAlignment = TextAnchor.UpperCenter;
            return grid;
        }

        static void SetCardRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(28f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-28f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        Button AddActionButton(Transform grid, string label, string action, bool mpcOnly, int fontSize = 32)
        {
            var button = UiFactory.CreateButton(grid, label, label, UiFactory.Primary, Color.white, fontSize);
            RegisterActionButton(button, action, mpcOnly);
            return button;
        }

        Button AddOutlineActionButton(Transform grid, string label, string action, bool mpcOnly)
        {
            var button = UiFactory.CreateOutlineButton(grid, label, label, 30);
            RegisterActionButton(button, action, mpcOnly);
            return button;
        }

        void RegisterActionButton(Button button, string action, bool mpcOnly)
        {
            if (action != null)
            {
                button.onClick.AddListener(() => _ = RunActionAsync(action));
            }
            if (mpcOnly)
            {
                _mpcOnlyButtons.Add(button.gameObject);
            }
        }

        public override void OnShow()
        {
            SetStatus("", false);
            ResetNextButton();
            _ = ApplyCapabilitiesAsync();
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

        /// <summary>foobar2000 サーバーでは MPC 専用ボタンを隠す。</summary>
        async Task ApplyCapabilitiesAsync()
        {
            try
            {
                var caps = await AppConfig.CreateClient().GetCapabilitiesAsync();
                bool isFoobar = caps.Player.Mode == 2;
                foreach (var go in _mpcOnlyButtons)
                {
                    go.SetActive(!isFoobar);
                }
            }
            catch (System.Exception)
            {
                // 取得失敗時は全ボタン表示のまま (非対応操作はサーバーが 501 を返す)
            }
        }

        IEnumerator PollRoutine()
        {
            while (true)
            {
                _ = RefreshNowPlayingAsync();
                yield return new WaitForSeconds(PollIntervalSeconds);
            }
        }

        async Task RefreshNowPlayingAsync()
        {
            if (_refreshing)
            {
                return;
            }
            _refreshing = true;
            try
            {
                var now = await AppConfig.CreateClient().GetNowPlayingAsync();
                if (!now.Playing)
                {
                    _titleText.text = "(プレイヤー停止中)";
                    _singerText.text = "";
                    _timeText.text = "";
                }
                else
                {
                    _titleText.text = now.PlayingTitle ?? "";
                    _singerText.text = string.IsNullOrEmpty(now.PlayingSinger) ? "" : "うたう人: " + now.PlayingSinger;
                    _timeText.text = (now.PlaytimeText ?? "") + " / " + (now.TotaltimeText ?? "");
                }
                if (now.NextSong != null)
                {
                    string next = "次: " + now.NextSong.Title;
                    if (!string.IsNullOrEmpty(now.NextSong.Singer))
                    {
                        next += " (" + now.NextSong.Singer + ")";
                    }
                    _nextSongText.text = next;
                }
                else
                {
                    _nextSongText.text = now.Playing ? "次の予約はありません" : "";
                }
            }
            catch (System.Exception e)
            {
                _titleText.text = "";
                _singerText.text = "";
                _timeText.text = "";
                _nextSongText.text = "";
                SetStatus("状態の取得に失敗: " + e.Message, true);
            }
            finally
            {
                _refreshing = false;
            }
        }

        void OnNextPressed()
        {
            // 演奏中の曲を終わらせる操作なので2度押しで確認する
            if (Time.time - _nextArmedAt > NextConfirmWindowSeconds)
            {
                _nextArmedAt = Time.time;
                _nextButtonLabel.text = "もう一度押すと次の曲へ";
                Se.Play(Se.Tap);
                return;
            }
            ResetNextButton();
            _ = RunActionAsync("next");
        }

        void ResetNextButton()
        {
            _nextArmedAt = -100f;
            if (_nextButtonLabel != null)
            {
                _nextButtonLabel.text = "次の曲へ";
            }
        }

        async Task RunActionAsync(string action)
        {
            Se.Play(Se.Tap);
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync(action);
                if (result.Volume.HasValue)
                {
                    SetStatus("音量: " + result.Volume.Value, false);
                }
                else if (!string.IsNullOrEmpty(result.Message))
                {
                    SetStatus(result.Message, false);
                }
                else
                {
                    SetStatus("OK (" + result.Player + ")", false);
                }
            }
            catch (ApiException e)
            {
                SetStatus(e.HttpStatus == 501
                    ? "このプレイヤーでは使えない操作です"
                    : "操作に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            catch (System.Exception e)
            {
                SetStatus("操作に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
        }
    }
}
