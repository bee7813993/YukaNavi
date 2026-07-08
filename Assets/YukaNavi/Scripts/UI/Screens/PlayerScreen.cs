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
    /// リモコン画面。音楽プレイヤー風のビジュアル (回転するレコード盤 + プログレスリング +
    /// 大きな再生ボタン) を中心に、Web 版 (mpcctrl_bs5.php) と同等の操作を提供する。
    /// 再生中はレコードが回り音符が舞う。詳細設定 (音ズレ・字幕補正等) は折りたたみ。
    /// 一般のカラオケ端末の慣習に合わせ全ユーザーが使える (曲終了のみ2度押し確認)。
    /// foobar2000 サーバー (playmode=2) では MPC 専用の操作を出さない。
    /// </summary>
    public class PlayerScreen : ScreenBase
    {
        const float PollIntervalSeconds = 2f;
        const float EndConfirmWindowSeconds = 3f;
        /// <summary>カード内テキストの実効幅 (折り返し行数の見積もりに使う)</summary>
        const float WrapWidth = 940f;
        /// <summary>レコード盤の回転速度 (度/秒)。10秒で1回転のゆったり感</summary>
        const float RecordSpinSpeed = 36f;

        RectTransform _listContent;
        Text _statusText;

        // ビジュアルセクション (構造は固定、テキストだけ動的に更新)
        LayoutElement _visualLayout;
        NoteParticles _noteParticles;
        Image _ringFill;
        RectTransform _recordRect;
        Image _recordImage;
        GameObject _recordLabel;
        string _recordSkinKey;
        Texture2D _skinRecordTex;
        Text _stateBadgeText;
        Image _stateBadgeBg;
        Text _playerBadgeText;
        RectTransform _playerBadgeRect;
        Image _textBackdrop;
        Text _titleText;
        Text _singerText;
        Text _timeText;
        string _totalTimeLabel = "";

        // NEXT カード
        GameObject _nextCard;
        LayoutElement _nextLayout;
        Text _nextTitle;

        // 進捗 (ポーリングの合間はクライアント側で補間して滑らかに進める)
        bool _hasProgress;
        float _basePosMs;
        float _totalMs;
        float _baseAt;
        int _stateNum;   // MPC 状態番号: 2=再生中 / 1=一時停止
        int _shownSec = -1;

        // 操作部 (プレイヤー種別が変わった時だけ作り直す)
        readonly List<GameObject> _controlCards = new List<GameObject>();
        bool _controlsBuilt;
        bool _controlsForFoobar;
        bool _controlsWithKeychange;
        GameObject _advancedCard;
        bool _advancedOpen;

        GameObject _playIcon;
        GameObject _pauseIcon;
        Text _endLabel;
        Text _keyText;
        Text _volText;
        Slider _volSlider;
        float _volSendAt = -1f;   // スライダー操作の送信予定時刻 (debounce)
        int _volPending;
        Text _compText;
        InputField _codeInput;

        string _infoSignature = null;
        float _endArmedAt = -100f;
        Coroutine _polling;
        bool _refreshing;
        bool _lastPollFailed;

        public override void BuildUi()
        {
            _controlCards.Clear();
            _infoSignature = null;
            _controlsBuilt = false;
            _advancedCard = null;
            _playIcon = null;
            _pauseIcon = null;
            _endLabel = null;
            _keyText = null;
            _volText = null;
            _volSlider = null;
            _compText = null;
            _codeInput = null;
            _hasProgress = false;

            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "リモコン");

            var scrollRectT = UiFactory.CreateScrollList(transform, "PlayerList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 64f);
            scrollRectT.offsetMax = new Vector2(-20f, -126f);

            BuildVisualSection();
            BuildNextCard();

            // 操作結果メッセージ (ナビバーの上に固定)
            _statusText = UiFactory.CreateText(transform, "Status", "", 26, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 10f);
            statusRect.sizeDelta = new Vector2(-40f, 44f);
        }

        public override void OnShow()
        {
            SetStatus("", false);
            _endArmedAt = -100f;
            _shownSec = -1;
            ApplyRecordSkin(); // スキンが切り替わっていたら盤を差し替える
            _ = InitControlsAsync();
            _polling = StartCoroutine(PollRoutine());
        }

        public override void OnHide()
        {
            if (_polling != null)
            {
                StopCoroutine(_polling);
                _polling = null;
            }
            _volSendAt = -1f;
        }

        void Update()
        {
            // 音量スライダーの debounce 送信
            if (_volSendAt > 0f && Time.unscaledTime >= _volSendAt)
            {
                _volSendAt = -1f;
                _ = SendVolumeAsync(_volPending);
            }

            // 曲終了の2度押し確認が期限切れになったらラベルを戻す
            if (_endArmedAt > 0f && Time.time - _endArmedAt > EndConfirmWindowSeconds && _endLabel != null)
            {
                _endArmedAt = -100f;
                _endLabel.text = "曲終了";
            }

            bool spinning = _stateNum == 2 && _hasProgress;

            // レコード盤の回転と音符
            if (_recordRect != null && spinning)
            {
                _recordRect.Rotate(0f, 0f, -RecordSpinSpeed * Time.deltaTime);
            }
            if (_noteParticles != null && _noteParticles.gameObject.activeSelf != spinning)
            {
                _noteParticles.gameObject.SetActive(spinning);
            }

            // 進捗の補間表示 (2秒ポーリングの間も滑らかに進める)
            if (!_hasProgress || _ringFill == null)
            {
                return;
            }
            float pos = _basePosMs;
            if (_stateNum == 2)
            {
                pos += (Time.realtimeSinceStartup - _baseAt) * 1000f;
            }
            pos = Mathf.Clamp(pos, 0f, _totalMs);
            _ringFill.fillAmount = _totalMs > 0f ? pos / _totalMs : 0f;
            int sec = (int)(pos / 1000f);
            if (sec != _shownSec && _timeText != null)
            {
                _shownSec = sec;
                _timeText.text = FormatMs(pos) + " / " + _totalTimeLabel;
            }
        }

        IEnumerator PollRoutine()
        {
            while (true)
            {
                _ = RefreshNowPlayingAsync();
                yield return new WaitForSeconds(NextPollDelay());
            }
        }

        /// <summary>
        /// 次のポーリングまでの待ち時間。Web 版は SSE で曲の切り替わりを即座に拾うため、
        /// アプリ側も曲の終わり際と曲間だけ間隔を詰めて、曲名の切り替わり遅れを目立たなくする。
        /// </summary>
        float NextPollDelay()
        {
            if (_lastPollFailed)
            {
                return 3f;  // サーバーに繋がらない時は無駄打ちしない
            }
            if (!_hasProgress)
            {
                return 0.8f;  // 曲間・停止中: 次の曲の開始をすぐ拾う
            }
            float pos = _basePosMs;
            if (_stateNum == 2)
            {
                pos += (Time.realtimeSinceStartup - _baseAt) * 1000f;
            }
            if (_totalMs - pos < 8000f)
            {
                return 0.5f;  // 曲の終わり際: 曲替わりを素早く検知
            }
            return PollIntervalSeconds;
        }

        /// <summary>プレイヤー種別と機能フラグに合わせて操作カードを構築する。</summary>
        async Task InitControlsAsync()
        {
            bool isFoobar = false;
            bool useKeychange = false;
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                isFoobar = caps.Player.Mode == 2;
                useKeychange = caps.Features != null && caps.Features.Keychange;
            }
            catch (System.Exception)
            {
                // 取得失敗時は MPC 想定で全操作表示 (非対応操作はサーバーが 501 を返す)
            }
            if (!_controlsBuilt || _controlsForFoobar != isFoobar || _controlsWithKeychange != useKeychange)
            {
                BuildControls(isFoobar, useKeychange);
                // 作り直したアイコン類を次のポーリングで必ず最新状態にする
                _infoSignature = null;
            }
            if (!isFoobar)
            {
                _ = SyncVolumeAsync();
            }
        }

        // ==================== ビジュアル (レコード盤 + 曲情報) ====================

        void BuildVisualSection()
        {
            var section = UiFactory.CreatePanel(_listContent, "Visual");
            _visualLayout = section.gameObject.AddComponent<LayoutElement>();
            _visualLayout.preferredHeight = 700f;

            // 音符 (再生中のみ表示)
            _noteParticles = NoteParticles.Create(section);
            _noteParticles.gameObject.SetActive(false);

            // 盤の中心をセクション上端より上に置き、下半分だけ覗かせる
            // (スクロールビューのマスクで上側は自然に切れる)
            const float ringSize = 620f;
            const float ringCenterY = -26f;

            // プログレス弧 (背景リング + 下半円の塗り)
            var ringBg = new GameObject("RingBg");
            ringBg.transform.SetParent(section, false);
            var ringBgImg = ringBg.AddComponent<Image>();
            ringBgImg.sprite = UiFactory.RingSprite;
            ringBgImg.color = new Color(0.5f, 0.47f, 0.58f, 0.25f);
            ringBgImg.raycastTarget = false;
            PlaceCircle(ringBgImg.rectTransform, 0f, ringCenterY, ringSize);

            var ringFillGo = new GameObject("RingFill");
            ringFillGo.transform.SetParent(section, false);
            _ringFill = ringFillGo.AddComponent<Image>();
            _ringFill.sprite = UiFactory.RingSprite;
            _ringFill.color = UiFactory.Primary;
            _ringFill.raycastTarget = false;
            _ringFill.type = Image.Type.Filled;
            // 見えている下半分に沿って左端 → 右端へ進む弧
            _ringFill.fillMethod = Image.FillMethod.Radial180;
            _ringFill.fillOrigin = (int)Image.Origin180.Bottom;
            _ringFill.fillClockwise = false;
            _ringFill.fillAmount = 0f;
            PlaceCircle(_ringFill.rectTransform, 0f, ringCenterY, ringSize);

            // レコード盤 (スキン → 同梱素材 → 実行時生成プレースホルダ の順で解決)
            var recordGo = new GameObject("Record");
            recordGo.transform.SetParent(section, false);
            _recordImage = recordGo.AddComponent<Image>();
            _recordImage.sprite = UiFactory.VinylSprite;
            _recordImage.raycastTarget = false;
            _recordRect = _recordImage.rectTransform;
            PlaceCircle(_recordRect, 0f, ringCenterY, 566f);

            // プレースホルダ盤用の中心ラベル (テーマ色 + ♪)。
            // 画像素材にはラベルがデザイン済みなので ApplyRecordSkin が隠す
            _recordLabel = new GameObject("Label");
            _recordLabel.transform.SetParent(recordGo.transform, false);
            var recordLabel = _recordLabel.AddComponent<Image>();
            recordLabel.sprite = UiFactory.CircleSprite;
            recordLabel.color = UiFactory.Primary;
            recordLabel.raycastTarget = false;
            var labelRect = recordLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(176f, 176f);
            var labelNote = UiFactory.CreateText(_recordLabel.transform, "Note", "♪", 72, Color.white);
            UiFactory.StretchFull(labelNote.rectTransform);

            _recordSkinKey = null;
            ApplyRecordSkin();

            // 盤タップでも再生/一時停止できる (見えている下半分だけを判定領域にする)
            var tapGo = new GameObject("RecordTap");
            tapGo.transform.SetParent(section, false);
            var tapImg = tapGo.AddComponent<Image>();
            tapImg.color = new Color(1f, 1f, 1f, 0f);
            var tapButton = tapGo.AddComponent<Button>();
            tapButton.transition = Selectable.Transition.None;
            tapButton.onClick.AddListener(() => _ = RunActionAsync("playpause"));
            var tapRect = (RectTransform)tapGo.transform;
            tapRect.anchorMin = tapRect.anchorMax = new Vector2(0.5f, 1f);
            tapRect.pivot = new Vector2(0.5f, 1f);
            tapRect.anchoredPosition = Vector2.zero;
            tapRect.sizeDelta = new Vector2(566f, 250f);

            // 状態バッジ + プレイヤー種別バッジ (左上に浮かせる)
            _stateBadgeText = UiFactory.CreateBadge(section, "State", "…", UiFactory.TextMuted, Color.white);
            _stateBadgeBg = _stateBadgeText.transform.parent.GetComponent<Image>();
            var stateRect = (RectTransform)_stateBadgeText.transform.parent;
            stateRect.anchorMin = stateRect.anchorMax = new Vector2(0f, 1f);
            stateRect.pivot = new Vector2(0f, 1f);
            stateRect.anchoredPosition = new Vector2(8f, -2f);
            stateRect.sizeDelta = new Vector2(150f, 46f);

            _playerBadgeText = UiFactory.CreateBadge(section, "Player", "", UiFactory.PanelBg, UiFactory.TextMuted);
            _playerBadgeRect = (RectTransform)_playerBadgeText.transform.parent;
            _playerBadgeRect.anchorMin = _playerBadgeRect.anchorMax = new Vector2(0f, 1f);
            _playerBadgeRect.pivot = new Vector2(0f, 1f);
            _playerBadgeRect.anchoredPosition = new Vector2(170f, -2f);
            _playerBadgeRect.sizeDelta = new Vector2(120f, 46f);
            _playerBadgeRect.gameObject.SetActive(false);

            // 曲情報 (リングの下、中央寄せ)。背景が透けても読めるよう半透明の帯を敷く
            var backdropGo = new GameObject("TextBackdrop");
            backdropGo.transform.SetParent(section, false);
            _textBackdrop = backdropGo.AddComponent<Image>();
            _textBackdrop.color = new Color(1f, 1f, 1f, 0.55f);
            UiFactory.Roundify(_textBackdrop);
            _textBackdrop.raycastTarget = false;

            _titleText = UiFactory.CreateText(section, "Title", "", 38, UiFactory.TextDark, TextAnchor.UpperCenter);
            _titleText.fontStyle = FontStyle.Bold;
            _singerText = UiFactory.CreateText(section, "Singer", "", 25, UiFactory.TextMuted, TextAnchor.UpperCenter);
            _timeText = UiFactory.CreateText(section, "Time", "", 24, UiFactory.TextMuted, TextAnchor.UpperCenter);
            LayoutVisualTexts("", "", false);
        }

        /// <summary>
        /// レコード盤の画像を反映する。スキンの盤 → 同梱素材 → 生成プレースホルダの順。
        /// スキンの切り替え・編集 (Revision) を検知した時だけ読み直す。
        /// </summary>
        void ApplyRecordSkin()
        {
            if (_recordImage == null)
            {
                return;
            }
            var skin = SkinManager.Current();
            string key = skin.Id + "#" + SkinManager.Revision;
            if (key == _recordSkinKey)
            {
                return;
            }
            _recordSkinKey = key;

            Texture2D tex = null;
            if (skin.Record != null && !string.IsNullOrEmpty(skin.Record.File))
            {
                tex = SkinManager.LoadTexture(skin, skin.Record.File);
            }
            bool fromSkin = tex != null;
            if (tex == null)
            {
                tex = Resources.Load<Texture2D>("Art/UI/yukanavi_record_disc");
            }

            var oldSprite = _recordImage.sprite;
            if (tex != null)
            {
                _recordImage.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _recordLabel.SetActive(false);
            }
            else
            {
                _recordImage.sprite = UiFactory.VinylSprite;
                _recordLabel.SetActive(true);
            }
            if (oldSprite != null && oldSprite != UiFactory.VinylSprite && oldSprite != _recordImage.sprite)
            {
                Destroy(oldSprite);
            }
            // スキン由来のテクスチャは差し替え時に破棄する (Resources 由来は破棄しない)
            if (_skinRecordTex != null && _skinRecordTex != tex)
            {
                Destroy(_skinRecordTex);
            }
            _skinRecordTex = fromSkin ? tex : null;
        }

        /// <summary>曲名の行数に合わせてテキスト位置とセクション高さを更新する。</summary>
        void LayoutVisualTexts(string title, string singer, bool showTime)
        {
            const float top = 302f;
            float y = top + 12f;
            int lines = UiFactory.EstimateWrapLines(title, 38, WrapWidth);
            float titleHeight = lines * UiFactory.LineHeight(38) + 4f;
            _titleText.text = UiFactory.NoWordWrap(title);
            PlaceRow(_titleText.rectTransform, y, titleHeight);
            y += titleHeight + 6f;

            bool hasSinger = !string.IsNullOrEmpty(singer);
            _singerText.gameObject.SetActive(hasSinger);
            if (hasSinger)
            {
                _singerText.text = singer;
                PlaceRow(_singerText.rectTransform, y, 34f);
                y += 40f;
            }

            _timeText.gameObject.SetActive(showTime);
            if (showTime)
            {
                PlaceRow(_timeText.rectTransform, y, 34f);
                y += 40f;
            }

            // 帯をテキスト群のぴったり背後に合わせる
            var backdropRect = _textBackdrop.rectTransform;
            backdropRect.anchorMin = new Vector2(0f, 1f);
            backdropRect.anchorMax = new Vector2(1f, 1f);
            backdropRect.pivot = new Vector2(0.5f, 1f);
            backdropRect.anchoredPosition = new Vector2(0f, -top);
            backdropRect.offsetMin = new Vector2(16f, backdropRect.offsetMin.y);
            backdropRect.offsetMax = new Vector2(-16f, backdropRect.offsetMax.y);
            backdropRect.sizeDelta = new Vector2(backdropRect.sizeDelta.x, y - top + 10f);

            _visualLayout.preferredHeight = y + 18f;
        }

        void BuildNextCard()
        {
            // 現在の曲より目立たないよう、半透明で影なしの控えめなカードにする
            _nextCard = new GameObject("NextCard");
            _nextCard.transform.SetParent(_listContent, false);
            var img = _nextCard.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.45f);
            UiFactory.Roundify(img);
            _nextLayout = _nextCard.AddComponent<LayoutElement>();

            var card = (RectTransform)_nextCard.transform;
            var badge = UiFactory.CreateBadge(card, "NextBadge", "NEXT", UiFactory.PrimaryPale, UiFactory.Primary);
            badge.fontStyle = FontStyle.Bold;
            var badgeRect = (RectTransform)badge.transform.parent;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(24f, -18f);
            badgeRect.sizeDelta = new Vector2(110f, 44f);

            _nextTitle = UiFactory.CreateText(card, "NextTitle", "", 27, UiFactory.TextDark, TextAnchor.UpperLeft);
            _nextTitle.fontStyle = FontStyle.Bold;
            _nextCard.SetActive(false);
        }

        void UpdateNextCard(NextSongDto next)
        {
            if (next == null)
            {
                _nextCard.SetActive(false);
                return;
            }
            _nextCard.SetActive(true);

            // 「曲名 (うたう人)」を1本のテキストにまとめてコンパクトに
            const float textLeft = 156f;
            float textWidth = WrapWidth - textLeft + 24f;
            string label = next.Title ?? "";
            if (!string.IsNullOrEmpty(next.Singer))
            {
                label += " (" + next.Singer + ")";
            }
            int lines = UiFactory.EstimateWrapLines(label, 27, textWidth);
            float titleHeight = lines * UiFactory.LineHeight(27) + 4f;
            _nextTitle.text = UiFactory.NoWordWrap(label);
            var titleRect = _nextTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -19f);
            titleRect.offsetMin = new Vector2(textLeft, titleRect.offsetMin.y);
            titleRect.offsetMax = new Vector2(-24f, titleRect.offsetMax.y);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, titleHeight);

            _nextLayout.preferredHeight = Mathf.Max(19f + titleHeight + 15f, 76f);
        }

        // ==================== 再生状態の反映 ====================

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

                int state = 0;
                if (now.Playing && !string.IsNullOrEmpty(now.Status))
                {
                    int.TryParse(now.Status, out state);
                }
                _stateNum = now.Playing ? state : 0;

                // 進捗 (毎回ベース値を更新して補間の起点にする)
                _basePosMs = now.Playtime;
                _totalMs = now.Totaltime;
                _baseAt = Time.realtimeSinceStartup;
                _hasProgress = now.Playing && now.Totaltime > 0f;
                _totalTimeLabel = _hasProgress ? FormatMs(_totalMs) : "";
                _shownSec = -1;
                if (!_hasProgress && _ringFill != null)
                {
                    _ringFill.fillAmount = 0f;
                }

                string sig = (now.Playing ? "1" : "0") + "|" + _stateNum + "|" + (_hasProgress ? "p" : "-")
                    + "|" + now.PlayingTitle + "|" + now.PlayingSinger + "|" + now.Player
                    + "|" + (now.NextSong != null ? now.NextSong.Title + "|" + now.NextSong.Singer : "none");
                if (sig != _infoSignature)
                {
                    _infoSignature = sig;
                    UpdateVisual(now);
                    UpdateNextCard(now.NextSong);
                }

                if (_keyText != null)
                {
                    _keyText.text = KeyLabel(now.Keychange);
                }
                _lastPollFailed = false;
            }
            catch (System.Exception e)
            {
                _lastPollFailed = true;
                SetStatus("状態の取得に失敗: " + e.Message, true);
            }
            finally
            {
                _refreshing = false;
            }
        }

        void UpdateVisual(NowPlayingDto now)
        {
            // 状態バッジ
            string stateLabel;
            Color stateBg;
            if (!now.Playing)
            {
                stateLabel = "停止中";
                stateBg = UiFactory.TextMuted;
            }
            else if (_stateNum == 1)
            {
                stateLabel = "一時停止";
                stateBg = new Color(0.85f, 0.55f, 0.20f);
            }
            else
            {
                stateLabel = "♪ 再生中";
                stateBg = UiFactory.Primary;
            }
            _stateBadgeText.text = stateLabel;
            _stateBadgeBg.color = stateBg;
            var stateRect = (RectTransform)_stateBadgeText.transform.parent;
            stateRect.sizeDelta = new Vector2(UiFactory.EstimateTextWidth(stateLabel, 22) + 44f, 46f);

            // プレイヤー種別バッジ
            string playerName = now.Player == "mpc" ? "MPC" : now.Player == "foobar" ? "foobar2000" : null;
            _playerBadgeRect.gameObject.SetActive(playerName != null);
            if (playerName != null)
            {
                _playerBadgeText.text = playerName;
                _playerBadgeRect.anchoredPosition = new Vector2(stateRect.sizeDelta.x + 22f, -2f);
                _playerBadgeRect.sizeDelta = new Vector2(UiFactory.EstimateTextWidth(playerName, 22) + 44f, 46f);
            }

            // 再生/一時停止アイコン
            bool playing = _stateNum == 2;
            if (_playIcon != null)
            {
                _playIcon.SetActive(!playing);
                _pauseIcon.SetActive(playing);
            }

            // 曲情報テキスト
            string title;
            string singer = "";
            if (!now.Playing)
            {
                title = "プレイヤー停止中";
            }
            else
            {
                title = !string.IsNullOrEmpty(now.PlayingTitle) ? now.PlayingTitle : (now.PlayingFile ?? "");
                if (!string.IsNullOrEmpty(now.PlayingSinger))
                {
                    singer = "うたう人: " + now.PlayingSinger;
                }
            }
            LayoutVisualTexts(title, singer, _hasProgress);
        }

        // ==================== 操作部 ====================

        void BuildControls(bool isFoobar, bool useKeychange)
        {
            foreach (var go in _controlCards)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _controlCards.Clear();
            _advancedCard = null;
            _controlsBuilt = true;
            _controlsForFoobar = isFoobar;
            _controlsWithKeychange = useKeychange;

            // 主要操作 (カード無しの透明セクション): 丸ボタン段 + 補助ピル段
            AddSection(card =>
            {
                float y = 4f;
                // 大きな再生/一時停止ボタン (中央)
                var pp = CreateCircleButton(card, "PlayPause", UiFactory.Primary, 132f, 0f, y + 66f);
                pp.onClick.AddListener(() => _ = RunActionAsync("playpause"));
                BuildPlayPauseIcons(pp.transform);

                if (!isFoobar)
                {
                    var back = CreateCircleButton(card, "Seek5Back", Color.white, 92f, -210f, y + 66f);
                    AddCircleLabel(back.transform, "-5s", 26, UiFactory.Primary);
                    back.onClick.AddListener(() => _ = RunActionAsync("seek_back"));

                    var fwd = CreateCircleButton(card, "Seek5Fwd", Color.white, 92f, 210f, y + 66f);
                    AddCircleLabel(fwd.transform, "+5s", 26, UiFactory.Primary);
                    fwd.onClick.AddListener(() => _ = RunActionAsync("seek_forward"));
                }
                y += 148f;

                // 補助段: 曲の最初から / -20s / +20s / 曲終了
                if (isFoobar)
                {
                    AddActionCell(card, "曲の最初から", y, 78f, 0f, 0.5f, "start_first", null, 24);
                    var endBtn = UiFactory.CreateSoftButton(card, "End", "曲終了", 24);
                    PlaceCell((RectTransform)endBtn.transform, y, 78f, 0.5f, 1f);
                    _endLabel = endBtn.GetComponentInChildren<Text>();
                    _endLabel.color = UiFactory.Danger;
                    endBtn.onClick.AddListener(OnEndPressed);
                }
                else
                {
                    AddActionCell(card, "曲の最初から", y, 78f, 0f, 0.31f, "start_first", null, 24);
                    AddActionCell(card, "-20s", y, 78f, 0.31f, 0.5f, "seek_back_large", null, 24);
                    AddActionCell(card, "+20s", y, 78f, 0.5f, 0.69f, "seek_forward_large", null, 24);
                    var endBtn = UiFactory.CreateSoftButton(card, "End", "曲終了", 24);
                    PlaceCell((RectTransform)endBtn.transform, y, 78f, 0.69f, 1f);
                    _endLabel = endBtn.GetComponentInChildren<Text>();
                    _endLabel.color = UiFactory.Danger;
                    endBtn.onClick.AddListener(OnEndPressed);
                }
                return y + 78f + 8f;
            });

            // ボリューム
            AddCard(card =>
            {
                float y = 16f;
                if (isFoobar)
                {
                    AddActionCell(card, "音量 -", y, 90f, 0f, 0.5f, "volume_down");
                    AddActionCell(card, "音量 +", y, 90f, 0.5f, 1f, "volume_up");
                    return y + 90f + 16f;
                }

                // − / スライダー / ＋ / 数値
                AddActionCell(card, "-", y, 90f, 0f, 0.12f, "volume_down");
                _volSlider = UiFactory.CreateSlider(card, "VolSlider", 0f, 100f);
                var sliderRect = (RectTransform)_volSlider.transform;
                PlaceCell(sliderRect, y, 90f, 0.13f, 0.72f);
                _volSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
                AddActionCell(card, "+", y, 90f, 0.73f, 0.85f, "volume_up");
                _volText = UiFactory.CreateText(card, "VolNum", "-", 30, UiFactory.TextDark);
                PlaceCell(_volText.rectTransform, y, 90f, 0.86f, 1f);
                y += 90f + 12f;

                AddActionCell(card, "ミュート", y, 90f, 0f, 1f / 3f, "mute");
                AddActionCell(card, "初期値", y, 90f, 1f / 3f, 2f / 3f, "volume_reset");
                AddActionCell(card, "フェードアウト", y, 90f, 2f / 3f, 1f, "fadeout");
                return y + 90f + 16f;
            });

            // 映像 + キー変更 (MPC 専用)
            if (!isFoobar)
            {
                AddCard(card =>
                {
                    float y = 16f;
                    AddActionCell(card, "字幕ON/OFF", y, 90f, 0f, 0.5f, "subtitle_toggle");
                    AddActionCell(card, "音声トラック変更", y, 90f, 0.5f, 1f, "audiotrack_next");
                    y += 90f;

                    if (useKeychange)
                    {
                        y += 12f;
                        var down = UiFactory.CreateSoftButton(card, "KeyDown", "キーダウン", 26);
                        PlaceCell((RectTransform)down.transform, y, 90f, 0f, 1f / 3f);
                        down.onClick.AddListener(() => _ = RunKeyAsync("down"));

                        // 中央の現在キーバッジはタップで原曲キーに戻せる
                        _keyText = UiFactory.CreateBadge(card, "CurrentKey", "…", UiFactory.TextDark, Color.white);
                        _keyText.fontSize = UiFactory.ScaledFontSize(26);
                        var keyGo = _keyText.transform.parent.gameObject;
                        keyGo.GetComponent<Image>().raycastTarget = true;
                        var keyButton = keyGo.AddComponent<Button>();
                        keyGo.AddComponent<PressEffect>();
                        keyButton.onClick.AddListener(() => _ = RunKeyAsync("0"));
                        PlaceCell((RectTransform)keyGo.transform, y + 10f, 70f, 1f / 3f + 0.04f, 2f / 3f - 0.04f);

                        var up = UiFactory.CreateSoftButton(card, "KeyUp", "キーアップ", 26);
                        PlaceCell((RectTransform)up.transform, y, 90f, 2f / 3f, 1f);
                        up.onClick.AddListener(() => _ = RunKeyAsync("up"));
                        y += 90f;
                    }
                    return y + 16f;
                });
            }

            // 詳細設定 (MPC 専用、折りたたみ)
            if (!isFoobar)
            {
                BuildAdvancedCard();
            }
        }

        /// <summary>丸ボタン (円スプライト + 上部ツヤ + 影)。中心座標指定で置く。</summary>
        Button CreateCircleButton(RectTransform parent, string name, Color bg, float size, float xOffset, float centerY)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = UiFactory.CircleSprite;
            img.color = bg;
            UiFactory.AddShadow(go);

            // 上部のツヤでぷっくりした立体感を出す
            var glossGo = new GameObject("Gloss");
            glossGo.transform.SetParent(go.transform, false);
            var gloss = glossGo.AddComponent<Image>();
            gloss.sprite = UiFactory.CircleGlossSprite;
            gloss.raycastTarget = false;
            UiFactory.StretchFull(gloss.rectTransform);

            var button = go.AddComponent<Button>();
            go.AddComponent<PressEffect>();
            PlaceCircle((RectTransform)go.transform, xOffset, centerY, size);
            return button;
        }

        void AddCircleLabel(Transform parent, string label, int fontSize, Color color)
        {
            var text = UiFactory.CreateText(parent, "Label", label, fontSize, color);
            UiFactory.StretchFull(text.rectTransform);
        }

        /// <summary>再生 (▶) と一時停止 (バー2本) のアイコンを作り、状態で切り替える。</summary>
        void BuildPlayPauseIcons(Transform button)
        {
            _playIcon = new GameObject("PlayIcon");
            _playIcon.transform.SetParent(button, false);
            var playRect = _playIcon.AddComponent<RectTransform>();
            UiFactory.StretchFull(playRect);
            var playText = UiFactory.CreateText(_playIcon.transform, "Tri", "▶", 56, Color.white);
            UiFactory.StretchFull(playText.rectTransform);
            // 三角は光学的に左に寄って見えるので少し右へ
            playText.rectTransform.offsetMin = new Vector2(10f, 0f);
            playText.rectTransform.offsetMax = new Vector2(10f, 0f);

            _pauseIcon = new GameObject("PauseIcon");
            _pauseIcon.transform.SetParent(button, false);
            var pauseRect = _pauseIcon.AddComponent<RectTransform>();
            UiFactory.StretchFull(pauseRect);
            for (int i = 0; i < 2; i++)
            {
                var barGo = new GameObject("Bar" + i);
                barGo.transform.SetParent(_pauseIcon.transform, false);
                var bar = barGo.AddComponent<Image>();
                bar.color = Color.white;
                UiFactory.Roundify(bar);
                bar.raycastTarget = false;
                var barRect = bar.rectTransform;
                barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
                barRect.pivot = new Vector2(0.5f, 0.5f);
                barRect.anchoredPosition = new Vector2(i == 0 ? -17f : 17f, 0f);
                barRect.sizeDelta = new Vector2(15f, 54f);
            }
            _pauseIcon.SetActive(false);
        }

        void BuildAdvancedCard()
        {
            if (_advancedCard != null)
            {
                _controlCards.Remove(_advancedCard);
                Destroy(_advancedCard);
                _advancedCard = null;
                _compText = null;
                _codeInput = null;
            }

            _advancedCard = AddCard(card =>
            {
                // ヘッダー (タップで開閉)
                var header = UiFactory.CreateButton(card, "AdvHeader", "", new Color(1f, 1f, 1f, 0f), UiFactory.Primary, 28);
                var headerRect = (RectTransform)header.transform;
                headerRect.anchorMin = new Vector2(0f, 1f);
                headerRect.anchorMax = new Vector2(1f, 1f);
                headerRect.pivot = new Vector2(0.5f, 1f);
                headerRect.anchoredPosition = Vector2.zero;
                headerRect.sizeDelta = new Vector2(0f, 86f);
                var headerLabel = UiFactory.CreateText(header.transform, "Title", "詳細設定", 28, UiFactory.Primary, TextAnchor.MiddleLeft);
                UiFactory.StretchFull(headerLabel.rectTransform);
                headerLabel.rectTransform.offsetMin = new Vector2(28f, 0f);
                var arrow = UiFactory.CreateText(header.transform, "Arrow", _advancedOpen ? "▲" : "▼", 26, UiFactory.Primary, TextAnchor.MiddleRight);
                UiFactory.StretchFull(arrow.rectTransform);
                arrow.rectTransform.offsetMax = new Vector2(-28f, 0f);
                header.onClick.AddListener(() =>
                {
                    _advancedOpen = !_advancedOpen;
                    Se.Play(Se.Tap);
                    BuildAdvancedCard();
                    if (_advancedOpen)
                    {
                        _ = SyncCompAsync();
                    }
                });

                float y = 86f;
                if (!_advancedOpen)
                {
                    return y;
                }
                y += 8f;

                y += AddSectionLabel(card, "音ズレ修正 ー映像を遅く／映像を早くー", y);
                AddActionCell(card, "-100ms", y, 90f, 0f, 0.25f, "audiodelay_down", "step=100");
                AddActionCell(card, "-10ms", y, 90f, 0.25f, 0.5f, "audiodelay_down");
                AddActionCell(card, "+10ms", y, 90f, 0.5f, 0.75f, "audiodelay_up");
                AddActionCell(card, "+100ms", y, 90f, 0.75f, 1f, "audiodelay_up", "step=100");
                y += 90f + 20f;

                y += AddSectionLabel(card, "再生スピード", y);
                AddActionCell(card, "スピードダウン", y, 90f, 0f, 1f / 3f, "speed_down");
                AddActionCell(card, "標準スピード", y, 90f, 1f / 3f, 2f / 3f, "speed_normal");
                AddActionCell(card, "スピードアップ", y, 90f, 2f / 3f, 1f, "speed_up");
                y += 90f + 20f;

                y += AddSectionLabel(card, "映像オプション", y);
                AddActionCell(card, "サイズ縮小", y, 90f, 0f, 1f / 3f, "size_small");
                AddActionCell(card, "サイズ標準", y, 90f, 1f / 3f, 2f / 3f, "size_normal");
                AddActionCell(card, "サイズ拡大", y, 90f, 2f / 3f, 1f, "size_large");
                y += 90f + 14f;
                AddActionCell(card, "D3Dフルスクリーン", y, 90f, 0f, 1f / 3f, "d3d_fullscreen", null, 22);
                AddActionCell(card, "ミュートON/OFF", y, 90f, 1f / 3f, 2f / 3f, "mute", null, 22);
                AddActionCell(card, "左右反転", y, 90f, 2f / 3f, 1f, "mirror");
                y += 90f + 14f;
                AddActionCell(card, "時刻表示", y, 90f, 0f, 1f / 3f, "show_time");
                y += 90f + 20f;

                y += AddSectionLabel(card, "字幕補正（白飛び対策）", y);
                y += AddWrapped(card,
                    "明るさ・コントラスト・彩度を一括で調整します。設定値は保存され、次の曲にも引き継がれます。",
                    y, 22, UiFactory.TextMuted);
                y += 10f;
                AddActionCell(card, "- 弱める", y, 90f, 0f, 1f / 3f, "comp_down");
                _compText = UiFactory.CreateBadge(card, "CompLevel", "…", UiFactory.TextDark, Color.white);
                _compText.fontSize = UiFactory.ScaledFontSize(28);
                PlaceCell((RectTransform)_compText.transform.parent, y + 10f, 70f, 1f / 3f + 0.04f, 2f / 3f - 0.04f);
                AddActionCell(card, "強める +", y, 90f, 2f / 3f, 1f, "comp_up");
                y += 90f + 14f;
                AddActionCell(card, "リセット（0 に戻す）", y, 84f, 0f, 1f, "comp_reset");
                y += 84f + 20f;

                y += AddSectionLabel(card, "任意コード送出", y);
                _codeInput = UiFactory.CreateInputField(card, "Code", "コード番号", 28);
                _codeInput.contentType = InputField.ContentType.IntegerNumber;
                PlaceCell((RectTransform)_codeInput.transform, y, 84f, 0f, 0.6f);
                var send = UiFactory.CreateSoftButton(card, "SendCode", "送出", 26);
                PlaceCell((RectTransform)send.transform, y, 84f, 0.62f, 1f);
                send.onClick.AddListener(() => _ = SendCodeAsync());
                return y + 84f + 24f;
            });
            if (_advancedOpen)
            {
                _ = SyncCompAsync();
            }
        }

        // ==================== 操作の実行 ====================

        void OnEndPressed()
        {
            // 演奏中の曲を終わらせる操作なので2度押しで確認する
            if (Time.time - _endArmedAt > EndConfirmWindowSeconds)
            {
                _endArmedAt = Time.time;
                _endLabel.text = "もう一度で終了";
                Se.Play(Se.Tap);
                return;
            }
            _endArmedAt = -100f;
            _endLabel.text = "曲終了";
            _ = RunActionAsync("next");
        }

        async Task RunKeyAsync(string key)
        {
            await RunActionAsync("keychange", "key=" + key);
            await RefreshNowPlayingAsync();  // 現在キー表示を更新
        }

        void OnVolumeSliderChanged(float value)
        {
            if (_volText != null)
            {
                _volText.text = ((int)value).ToString();
            }
            // 操作終了から 0.35 秒待って送信 (ドラッグ中の連続送信を避ける)
            _volPending = (int)value;
            _volSendAt = Time.unscaledTime + 0.35f;
        }

        async Task SendVolumeAsync(int value)
        {
            try
            {
                await AppConfig.CreateClient().PlayerActionAsync("volume_set", "value=" + value);
                SetStatus("音量: " + value, false);
            }
            catch (System.Exception e)
            {
                SetStatus("音量の設定に失敗: " + e.Message, true);
            }
        }

        async Task SyncVolumeAsync()
        {
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync("volume_get");
                if (result.Volume.HasValue)
                {
                    ApplyVolume(result.Volume.Value);
                }
            }
            catch (System.Exception)
            {
                // プレイヤー停止中などで取れない時は「-」表示のまま
            }
        }

        async Task SyncCompAsync()
        {
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync("comp_get");
                if (result.CompLevel.HasValue && _compText != null)
                {
                    _compText.text = CompLabel(result.CompLevel.Value);
                }
            }
            catch (System.Exception)
            {
                // 旧サーバー (comp_* 未対応) では「…」表示のまま
            }
        }

        async Task SendCodeAsync()
        {
            int code;
            if (_codeInput == null || !int.TryParse(_codeInput.text, out code))
            {
                SetStatus("コード番号を入力してください", true);
                Se.Play(Se.Error);
                return;
            }
            await RunActionAsync("command", "value=" + code);
        }

        async Task RunActionAsync(string action, string extraQuery = null)
        {
            Se.Play(Se.Tap);
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync(action, extraQuery);
                if (result.Volume.HasValue)
                {
                    ApplyVolume(result.Volume.Value);
                    SetStatus("音量: " + result.Volume.Value, false);
                }
                else if (result.CompLevel.HasValue)
                {
                    if (_compText != null)
                    {
                        _compText.text = CompLabel(result.CompLevel.Value);
                    }
                    SetStatus("字幕補正: " + CompLabel(result.CompLevel.Value), false);
                }
                else if (!string.IsNullOrEmpty(result.Message))
                {
                    SetStatus(result.Message, false);
                }
                else
                {
                    SetStatus("OK (" + result.Player + ")", false);
                }
                if (action == "playpause" || action == "next" || action == "start_first")
                {
                    // 盤の回転・アイコン・バッジを早めに追従させる
                    _ = RefreshNowPlayingAsync();
                }
            }
            catch (ApiException e)
            {
                SetStatus(e.HttpStatus == 501 ? "このプレイヤーでは使えない操作です"
                    : e.HttpStatus == 502 ? "プレイヤー側が応答しません (未起動?)"
                    : "操作に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            catch (System.Exception e)
            {
                SetStatus("操作に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }

        void ApplyVolume(int volume)
        {
            if (_volText != null)
            {
                _volText.text = volume.ToString();
            }
            if (_volSlider != null)
            {
                _volSlider.SetValueWithoutNotify(volume);
            }
        }

        // ==================== 部品 ====================

        /// <summary>可変高さのカードをリストに足す。build は内容を配置して高さを返す。</summary>
        GameObject AddCard(System.Func<RectTransform, float> build)
        {
            var cardGo = new GameObject("Card");
            cardGo.transform.SetParent(_listContent, false);
            var img = cardGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(cardGo, 3f);
            var le = cardGo.AddComponent<LayoutElement>();
            le.preferredHeight = build((RectTransform)cardGo.transform);
            _controlCards.Add(cardGo);
            return cardGo;
        }

        /// <summary>カード枠なしの透明セクションをリストに足す。</summary>
        GameObject AddSection(System.Func<RectTransform, float> build)
        {
            var go = new GameObject("Section");
            go.transform.SetParent(_listContent, false);
            var rect = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = build(rect);
            _controlCards.Add(go);
            return go;
        }

        /// <summary>カード内の全幅行 (左右 28px マージン) を y 位置に置く。</summary>
        static void PlaceRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.offsetMin = new Vector2(28f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-28f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        /// <summary>カード内を横分割 (x0〜x1 は 0〜1) したセルに置く。</summary>
        static void PlaceCell(RectTransform rect, float y, float height, float x0, float x1)
        {
            rect.anchorMin = new Vector2(x0, 1f);
            rect.anchorMax = new Vector2(x1, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(-16f, height);
        }

        /// <summary>中央基準 (xOffset) + 上からの中心位置 (centerY) で正方形要素を置く。回転にも使える。</summary>
        static void PlaceCircle(RectTransform rect, float xOffset, float centerY, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(xOffset, -centerY);
            rect.sizeDelta = new Vector2(size, size);
        }

        /// <summary>ソフトボタンのセル。action 指定で player.php をそのまま呼ぶ。</summary>
        void AddActionCell(RectTransform card, string label, float y, float height,
                           float x0, float x1, string action, string extraQuery = null, int fontSize = 26)
        {
            var button = UiFactory.CreateSoftButton(card, label, label, fontSize);
            PlaceCell((RectTransform)button.transform, y, height, x0, x1);
            button.onClick.AddListener(() => _ = RunActionAsync(action, extraQuery));
        }

        /// <summary>セクション見出し。次の行までのオフセットを返す。</summary>
        float AddSectionLabel(RectTransform card, string label, float y)
        {
            var text = UiFactory.CreateText(card, "Section", label, 22, UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            PlaceRow(text.rectTransform, y, 32f);
            return 42f;
        }

        /// <summary>折り返しテキスト行。使った高さを返す。</summary>
        float AddWrapped(RectTransform card, string label, float y, int fontSize, Color color)
        {
            int lines = UiFactory.EstimateWrapLines(label, fontSize, WrapWidth);
            float height = lines * UiFactory.LineHeight(fontSize) + 4f;
            var text = UiFactory.CreateText(card, "Text", UiFactory.NoWordWrap(label),
                fontSize, color, TextAnchor.UpperLeft);
            PlaceRow(text.rectTransform, y, height);
            return height;
        }

        static string KeyLabel(int key)
        {
            if (key == 0)
            {
                return "♪ 原曲";
            }
            return key > 0 ? "+" + key : key.ToString();
        }

        static string CompLabel(int level)
        {
            return level > 0 ? "+" + level : level.ToString();
        }

        static string FormatMs(float ms)
        {
            int total = Mathf.Max(0, (int)(ms / 1000f));
            int h = total / 3600;
            int m = (total / 60) % 60;
            int s = total % 60;
            return string.Format("{0:00}:{1:00}:{2:00}", h, m, s);
        }

        void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
        }
    }
}
