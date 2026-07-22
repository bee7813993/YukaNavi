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
    /// タブレット据え置き用ライブダッシュボード (横向き)。
    /// 左はリモコン画面 (PlayerScreen) と同じデザインのプレイヤー (レコード盤+操作)、
    /// 右に予約リスト (再生済 → 再生中 → これから を1本の時系列で表示) と接続 QR を
    /// 表示する。Web 版 player_live_dashboard.php のアプリ版。
    /// 表示中は画面の向きを横に固定し、スリープも止める (OnHide で元に戻す)。
    /// </summary>
    public class DashboardScreen : ScreenBase
    {
        /// <summary>アプリ入手 QR の宛先。ストア公開後はストア URL に差し替える。</summary>
        const string AppDownloadUrl = "https://ykr.moe/apps/yukanavi/";

        /// <summary>
        /// ダッシュボードを案内する端末か (タブレット / PC / エディタ)。
        /// スマホは横向きにしても物理的な縦幅が足りず実用にならないため、
        /// メニューに入口を出さない (スマホはリモコン画面が同等の役割を持つ)。
        /// </summary>
        public static bool DeviceSupported
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                return true;
#else
                if (SystemInfo.deviceModel.StartsWith("iPad"))
                {
                    return true;
                }
                float dpi = Screen.dpi;
                if (dpi <= 0f)
                {
                    return true; // 物理サイズを判定できない端末は従来どおり出す
                }
                float w = Screen.width / dpi;
                float h = Screen.height / dpi;
                return Mathf.Sqrt(w * w + h * h) >= 7f; // 対角 7 インチ以上をタブレット扱い
#endif
            }
        }

        const float NowPollIntervalSeconds = 2f;
        const float ListPollIntervalSeconds = 3f;
        const float EndConfirmWindowSeconds = 3f;
        /// <summary>レコード盤の回転速度 (度/秒)。PlayerScreen と同じゆったり感。</summary>
        const float RecordSpinSpeed = 36f;
        /// <summary>左カラム (プレイヤー) の幅。リング 620px + 余白。</summary>
        const float LeftWidth = 660f;
        /// <summary>左カラム内の折り返し見積もり幅。</summary>
        const float WrapWidth = 604f;

        // 左: ビジュアル (PlayerScreen と同構造)
        RectTransform _leftContent;
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

        // 左: 操作
        readonly List<GameObject> _controlCards = new List<GameObject>();
        bool _controlsBuilt;
        bool _controlsForFoobar;
        bool _controlsWithKeychange;
        GameObject _playIcon;
        GameObject _pauseIcon;
        Text _endLabel;
        float _endArmedAt = -100f;
        Slider _volSlider;
        Text _volText;
        float _volSendAt = -1f;
        int _volPending;
        bool _volApplying;
        Text _keyText;
        GameObject _advancedCard;
        bool _advancedOpen;
        Text _compText;
        InputField _codeInput;

        // 右: 予約リスト (再生済〜再生中〜これから)
        RectTransform _queueContent;
        Text _queueCountText;
        readonly List<GameObject> _rows = new List<GameObject>();
        string _listSignature;
        /// <summary>統合リストに残す再生済みの件数 (コンパクト行で先頭に並べる)。</summary>
        const int MaxPlayedRows = 3;

        // ヘッダー
        Text _roomText;
        Text _clockText;
        int _shownClockMinute = -1;

        // QR (生成したテクスチャは自前で破棄する)
        RawImage _connectQrImage;
        RawImage _downloadQrImage;
        Text _connectUrlText;
        Texture2D _connectQrTex;
        Texture2D _downloadQrTex;
        string _connectQrPayload;

        // 進捗の補間 (ポーリング間もバーを滑らかに進める)
        float _basePosMs;
        float _totalMs;
        float _baseAt;
        bool _hasProgress;
        int _stateNum;
        int _shownSec = -1;
        string _totalTimeLabel = "";
        string _nowSignature;
        bool _nowRefreshing;
        bool _listRefreshing;
        bool _lastPollFailed;
        Coroutine _nowPolling;
        Coroutine _listPolling;

        public override void BuildUi()
        {
            bool landscape = Screen.width > Screen.height;
            if (!landscape)
            {
                BuildPortraitPlaceholder();
                return;
            }
            BuildLandscape();
        }

        /// <summary>
        /// 縦のまま開いた直後の仮表示。実機では OnShow の横固定 → 回転 →
        /// 向きの変化を AppRoot が検知して RebuildAll が走り、横レイアウトに置き換わる。
        /// </summary>
        void BuildPortraitPlaceholder()
        {
            var bg = UiFactory.CreatePanel(transform, "Bg", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);
            string message = "画面を横向きにしています...";
#if UNITY_EDITOR
            message = "エディタでは Game ビューの解像度を\n横向き (例: 1920x1080) にしてください";
#endif
            var text = UiFactory.CreateText(transform, "Message", message, 34, UiFactory.TextMuted);
            UiFactory.StretchFull(text.rectTransform);
        }

        /// <summary>横向きの本レイアウト (幅 1920 基準。16:9 で高さ 1080、iPad は 1440)。</summary>
        void BuildLandscape()
        {
            var bg = UiFactory.CreatePanel(transform, "Bg", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            // ---- ヘッダー (部屋名 / 時計) ----
            float headerH = Mathf.Max(76f, UiFactory.LineHeight(36) + 12f);
            var header = UiFactory.CreatePanel(transform, "Header");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(32f, -(10f + headerH));
            header.offsetMax = new Vector2(-32f, -10f);

            _roomText = UiFactory.CreateText(header, "Room", "ライブダッシュボード", 36,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(_roomText.rectTransform);
            _roomText.rectTransform.offsetMax = new Vector2(-320f, 0f);
            UiFactory.FitLabel(_roomText);

            _clockText = UiFactory.CreateText(header, "Clock", "", 46,
                UiFactory.PrimaryDark, TextAnchor.MiddleRight);
            UiFactory.StretchFull(_clockText.rectTransform);
            _shownClockMinute = -1;

            // ---- 本体 ----
            var body = UiFactory.CreatePanel(transform, "Body");
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(32f, GlobalNav.BarHeight + 12f);
            body.offsetMax = new Vector2(-32f, -(10f + headerH + 12f));

            // 左カラム: リモコンと同じ縦積み (スクロール可)
            var leftScroll = UiFactory.CreateScrollList(body, "Left", out _leftContent);
            leftScroll.anchorMin = new Vector2(0f, 0f);
            leftScroll.anchorMax = new Vector2(0f, 1f);
            leftScroll.pivot = new Vector2(0f, 0.5f);
            leftScroll.offsetMin = new Vector2(0f, 0f);
            leftScroll.offsetMax = new Vector2(LeftWidth, 0f);
            BuildVisualSection();
            // まずは MPC 想定で構築 (capabilities 取得後、差分があれば作り直す)
            BuildControls(isFoobar: false, useKeychange: false);

            // 右カラム: 予約リスト (再生済〜再生中〜これから、可変) / REQUEST URL
            const float reqH = 210f;
            var right = UiFactory.CreatePanel(body, "Right");
            right.anchorMin = new Vector2(0f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(LeftWidth + 20f, 0f);
            right.offsetMax = new Vector2(0f, 0f);

            var queueCard = CreateCard(right, "QueueCard");
            queueCard.anchorMin = new Vector2(0f, 0f);
            queueCard.anchorMax = new Vector2(1f, 1f);
            queueCard.offsetMin = new Vector2(0f, reqH + 16f);
            queueCard.offsetMax = new Vector2(0f, 0f);
            BuildQueueCard(queueCard);

            var requestCard = CreateCard(right, "RequestCard");
            requestCard.anchorMin = new Vector2(0f, 0f);
            requestCard.anchorMax = new Vector2(1f, 0f);
            requestCard.pivot = new Vector2(0.5f, 0f);
            requestCard.offsetMin = new Vector2(0f, 0f);
            requestCard.offsetMax = new Vector2(0f, reqH);
            BuildRequestCard(requestCard);
        }

        static RectTransform CreateCard(RectTransform parent, string name)
        {
            var card = UiFactory.CreatePanel(parent, name, UiFactory.CardBg);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 3f);
            return card;
        }

        // ==================== 左: ビジュアル (PlayerScreen と同デザイン) ====================

        void BuildVisualSection()
        {
            var section = UiFactory.CreatePanel(_leftContent, "Visual");
            _visualLayout = section.gameObject.AddComponent<LayoutElement>();
            _visualLayout.preferredHeight = 700f;

            // 音符 (再生中のみ表示)
            _noteParticles = NoteParticles.Create(section);
            _noteParticles.gameObject.SetActive(false);

            // 盤の中心をセクション上端より上に置き、下半分だけ覗かせる
            // (スクロールビューのマスクで上側は自然に切れる)
            const float ringSize = 620f;
            const float ringCenterY = -26f;

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
            tapButton.onClick.AddListener(() => _ = SendActionAsync("playpause"));
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

        /// <summary>レコード盤の画像を反映する (PlayerScreen と同じスキン対応)。</summary>
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

            var backdropRect = _textBackdrop.rectTransform;
            backdropRect.anchorMin = new Vector2(0f, 1f);
            backdropRect.anchorMax = new Vector2(1f, 1f);
            backdropRect.pivot = new Vector2(0.5f, 1f);
            backdropRect.anchoredPosition = new Vector2(0f, -top);
            backdropRect.offsetMin = new Vector2(16f, backdropRect.offsetMin.y);
            backdropRect.offsetMax = new Vector2(-16f, backdropRect.offsetMax.y);
            backdropRect.sizeDelta = new Vector2(backdropRect.sizeDelta.x, y - top + 10f);

            _visualLayout.preferredHeight = y + 12f;
        }

        // ==================== 左: 操作 (PlayerScreen と同デザイン) ====================

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
            _controlsBuilt = true;
            _controlsForFoobar = isFoobar;
            _controlsWithKeychange = useKeychange;

            // 再生開始 (待機中の曲から開始。リモコンには無いダッシュボード向けの主要操作)
            AddSection(section =>
            {
                var start = UiFactory.CreateButton(section, "Start", "▶ 再生開始",
                    UiFactory.Primary, Color.white, 30);
                var startRect = (RectTransform)start.transform;
                PlaceRow(startRect, 0f, 84f);
                start.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    _ = SendActionAsync("start");
                });
                return 84f + 4f;
            });

            // 主要操作 (カード無しの透明セクション): 丸ボタン段 + 補助ピル段
            AddSection(card =>
            {
                float y = 4f;
                var pp = CreateCircleButton(card, "PlayPause", UiFactory.Primary, 132f, 0f, y + 66f);
                pp.onClick.AddListener(() => _ = SendActionAsync("playpause"));
                BuildPlayPauseIcons(pp.transform);

                if (!isFoobar)
                {
                    var back = CreateCircleButton(card, "Seek5Back", Color.white, 92f, -190f, y + 66f);
                    AddCircleLabel(back.transform, "-5s", 26, UiFactory.Primary);
                    back.onClick.AddListener(() => _ = SendActionAsync("seek_back"));

                    var fwd = CreateCircleButton(card, "Seek5Fwd", Color.white, 92f, 190f, y + 66f);
                    AddCircleLabel(fwd.transform, "+5s", 26, UiFactory.Primary);
                    fwd.onClick.AddListener(() => _ = SendActionAsync("seek_forward"));
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

            // ボリューム + フェードアウト
            AddCard(card =>
            {
                float y = 16f;
                if (isFoobar)
                {
                    AddActionCell(card, "音量 -", y, 90f, 0f, 1f / 3f, "volume_down");
                    AddActionCell(card, "音量 +", y, 90f, 1f / 3f, 2f / 3f, "volume_up");
                    AddActionCell(card, "フェードアウト", y, 90f, 2f / 3f, 1f, "fadeout");
                    return y + 90f + 16f;
                }

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

            // 映像 + キー変更 (MPC 専用。PlayerScreen と同じカード構成)
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
                        down.onClick.AddListener(() =>
                        {
                            Se.Play(Se.Tap);
                            _ = SendActionAsync("keychange", "key=down");
                        });

                        // 中央の現在キーバッジはタップで原曲キーに戻せる (PlayerScreen と同じ)
                        _keyText = UiFactory.CreateBadge(card, "CurrentKey", "…", UiFactory.TextDark, Color.white);
                        _keyText.fontSize = UiFactory.ScaledFontSize(26);
                        var keyGo = _keyText.transform.parent.gameObject;
                        keyGo.GetComponent<Image>().raycastTarget = true;
                        var keyButton = keyGo.AddComponent<Button>();
                        keyGo.AddComponent<PressEffect>();
                        keyButton.onClick.AddListener(() =>
                        {
                            Se.Play(Se.Tap);
                            _ = SendActionAsync("keychange", "key=0");
                        });
                        PlaceCell((RectTransform)keyGo.transform, y + 10f, 70f, 1f / 3f + 0.04f, 2f / 3f - 0.04f);

                        var up = UiFactory.CreateSoftButton(card, "KeyUp", "キーアップ", 26);
                        PlaceCell((RectTransform)up.transform, y, 90f, 2f / 3f, 1f);
                        up.onClick.AddListener(() =>
                        {
                            Se.Play(Se.Tap);
                            _ = SendActionAsync("keychange", "key=up");
                        });
                        y += 90f;
                    }
                    return y + 16f;
                });
            }

            // 詳細設定 (MPC 専用、折りたたみ。PlayerScreen と同内容)
            if (!isFoobar)
            {
                BuildAdvancedCard();
            }
        }

        /// <summary>詳細設定の折りたたみカード (開閉のたびに作り直す)。</summary>
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

        void OnEndPressed()
        {
            Se.Play(Se.Tap);
            // 誤タップ防止の2度押し確認 (リモコン画面と同じ)
            if (Time.time - _endArmedAt <= EndConfirmWindowSeconds)
            {
                _endArmedAt = -100f;
                _endLabel.text = "曲終了";
                _ = SendActionAsync("next");
            }
            else
            {
                _endArmedAt = Time.time;
                _endLabel.text = "もう一度で終了";
            }
        }

        void OnVolumeSliderChanged(float value)
        {
            if (_volApplying)
            {
                return; // サーバー値の反映中は送信しない
            }
            _volPending = Mathf.RoundToInt(value);
            _volSendAt = Time.unscaledTime + 0.3f; // debounce
            if (_volText != null)
            {
                _volText.text = _volPending.ToString();
            }
        }

        /// <summary>プレイヤー種別・機能フラグに応じて操作カードを作り直す。</summary>
        async Task ConfigureControlsAsync()
        {
            if (_leftContent == null)
            {
                return; // 縦向きの仮表示中 (回転後の RebuildAll で組み直される)
            }
            bool isFoobar = false;
            bool useKeychange = false;
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                isFoobar = caps.Player != null && caps.Player.Mode == 2;
                useKeychange = caps.Features != null && caps.Features.Keychange;
            }
            catch (System.Exception)
            {
                // 取得失敗時は MPC 想定で全操作表示 (非対応操作はサーバーが 501 を返す)
            }
            if (!_controlsBuilt || _controlsForFoobar != isFoobar
                || _controlsWithKeychange != useKeychange)
            {
                BuildControls(isFoobar, useKeychange);
                _nowSignature = null; // 作り直したアイコン類を次のポーリングで最新化
            }
        }

        // ---- PlayerScreen と同じ配置ヘルパー ----

        GameObject AddCard(System.Func<RectTransform, float> build)
        {
            var cardGo = new GameObject("Card");
            cardGo.transform.SetParent(_leftContent, false);
            var img = cardGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            UiFactory.AddShadow(cardGo, 3f);
            var le = cardGo.AddComponent<LayoutElement>();
            le.preferredHeight = build((RectTransform)cardGo.transform);
            _controlCards.Add(cardGo);
            return cardGo;
        }

        GameObject AddSection(System.Func<RectTransform, float> build)
        {
            var go = new GameObject("Section");
            go.transform.SetParent(_leftContent, false);
            var rect = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = build(rect);
            _controlCards.Add(go);
            return go;
        }

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

        static void PlaceCell(RectTransform rect, float y, float height, float x0, float x1)
        {
            rect.anchorMin = new Vector2(x0, 1f);
            rect.anchorMax = new Vector2(x1, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(-16f, height);
        }

        static void PlaceCircle(RectTransform rect, float xOffset, float centerY, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(xOffset, -centerY);
            rect.sizeDelta = new Vector2(size, size);
        }

        void AddActionCell(RectTransform card, string label, float y, float height,
                           float x0, float x1, string action, string extraQuery = null, int fontSize = 26)
        {
            var button = UiFactory.CreateSoftButton(card, label, label, fontSize);
            PlaceCell((RectTransform)button.transform, y, height, x0, x1);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _ = SendActionAsync(action, extraQuery);
            });
        }

        Button CreateCircleButton(RectTransform parent, string name, Color bg, float size,
                                  float xOffset, float centerY)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = UiFactory.CircleSprite;
            img.color = bg;
            UiFactory.AddShadow(go);

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

        /// <summary>セクション見出し。次の行までのオフセットを返す (PlayerScreen と同じ)。</summary>
        float AddSectionLabel(RectTransform card, string label, float y)
        {
            var text = UiFactory.CreateText(card, "Section", label, 22, UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            PlaceRow(text.rectTransform, y, 32f);
            return 42f;
        }

        /// <summary>折り返しテキスト行。使った高さを返す (PlayerScreen と同じ)。</summary>
        float AddWrapped(RectTransform card, string label, float y, int fontSize, Color color)
        {
            int lines = UiFactory.EstimateWrapLines(label, fontSize, WrapWidth);
            float height = lines * UiFactory.LineHeight(fontSize) + 4f;
            var text = UiFactory.CreateText(card, "Text", UiFactory.NoWordWrap(label),
                fontSize, color, TextAnchor.UpperLeft);
            PlaceRow(text.rectTransform, y, height);
            return height;
        }

        void BuildPlayPauseIcons(Transform button)
        {
            _playIcon = new GameObject("PlayIcon");
            _playIcon.transform.SetParent(button, false);
            var playRect = _playIcon.AddComponent<RectTransform>();
            UiFactory.StretchFull(playRect);
            var playText = UiFactory.CreateText(_playIcon.transform, "Tri", "▶", 56, Color.white);
            UiFactory.StretchFull(playText.rectTransform);
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

        // ==================== 右: キュー / 履歴 / 接続 QR ====================

        void BuildQueueCard(RectTransform card)
        {
            float y = 18f;
            var caption = UiFactory.CreateText(card, "Caption", "予約リスト", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(caption.rectTransform, y, UiFactory.LineHeight(26));

            _queueCountText = UiFactory.CreateText(card, "Count", "", 22,
                UiFactory.TextMuted, TextAnchor.MiddleRight);
            SetTopRect(_queueCountText.rectTransform, y, UiFactory.LineHeight(26));
            y += UiFactory.LineHeight(26) + 10f;

            var scroll = UiFactory.CreateScrollList(card, "List", out _queueContent);
            scroll.anchorMin = new Vector2(0f, 0f);
            scroll.anchorMax = new Vector2(1f, 1f);
            scroll.offsetMin = new Vector2(12f, 12f);
            scroll.offsetMax = new Vector2(-12f, -y);
            _listSignature = null;
        }

        void BuildRequestCard(RectTransform card)
        {
            var caption = UiFactory.CreateText(card, "Caption", "この部屋に接続", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(caption.rectTransform, 14f, UiFactory.LineHeight(26));
            float qrTop = 14f + UiFactory.LineHeight(26) + 8f;
            float qrSize = 210f - qrTop - 14f;

            // 部屋接続 QR
            var connectGo = new GameObject("ConnectQr");
            connectGo.transform.SetParent(card, false);
            _connectQrImage = connectGo.AddComponent<RawImage>();
            var connectRect = _connectQrImage.rectTransform;
            connectRect.anchorMin = connectRect.anchorMax = new Vector2(0f, 1f);
            connectRect.pivot = new Vector2(0f, 1f);
            connectRect.anchoredPosition = new Vector2(24f, -qrTop);
            connectRect.sizeDelta = new Vector2(qrSize, qrSize);

            _connectUrlText = UiFactory.CreateText(card, "Url", "", 26,
                UiFactory.Primary, TextAnchor.MiddleLeft);
            var urlRect = _connectUrlText.rectTransform;
            urlRect.anchorMin = new Vector2(0f, 1f);
            urlRect.anchorMax = new Vector2(1f, 1f);
            urlRect.pivot = new Vector2(0.5f, 1f);
            urlRect.anchoredPosition = new Vector2((24f + qrSize + 20f) * 0.5f, -qrTop);
            urlRect.sizeDelta = new Vector2(-(24f + qrSize + 20f + qrSize + 200f + 24f),
                UiFactory.LineHeight(26));
            UiFactory.FitLabel(_connectUrlText);

            var urlCaption = UiFactory.CreateText(card, "UrlCaption",
                "ゆかナビの QR 読み取り、またはブラウザで開いて予約できます", 20,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            var urlCapRect = urlCaption.rectTransform;
            urlCapRect.anchorMin = new Vector2(0f, 1f);
            urlCapRect.anchorMax = new Vector2(1f, 1f);
            urlCapRect.pivot = new Vector2(0.5f, 1f);
            urlCapRect.anchoredPosition = new Vector2((24f + qrSize + 20f) * 0.5f,
                -(qrTop + UiFactory.LineHeight(26) + 6f));
            urlCapRect.sizeDelta = new Vector2(-(24f + qrSize + 20f + qrSize + 200f + 24f),
                UiFactory.LineHeight(20) * 2f);

            // アプリ入手 QR (右端)
            var dlGo = new GameObject("DownloadQr");
            dlGo.transform.SetParent(card, false);
            _downloadQrImage = dlGo.AddComponent<RawImage>();
            var dlRect = _downloadQrImage.rectTransform;
            dlRect.anchorMin = dlRect.anchorMax = new Vector2(1f, 1f);
            dlRect.pivot = new Vector2(1f, 1f);
            dlRect.anchoredPosition = new Vector2(-(180f + 24f), -qrTop);
            dlRect.sizeDelta = new Vector2(qrSize, qrSize);

            var dlCaption = UiFactory.CreateText(card, "DlCaption", "アプリを入手", 22,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            var dlCapRect = dlCaption.rectTransform;
            dlCapRect.anchorMin = dlCapRect.anchorMax = new Vector2(1f, 1f);
            dlCapRect.pivot = new Vector2(0f, 1f);
            dlCapRect.anchoredPosition = new Vector2(-(180f + 12f),
                -(qrTop + qrSize * 0.5f - UiFactory.LineHeight(22) * 0.5f));
            dlCapRect.sizeDelta = new Vector2(180f - 24f, UiFactory.LineHeight(22));
        }

        /// <summary>上端基準の帯配置 (カード内共通)。</summary>
        static void SetTopRect(RectTransform rect, float y, float height, float margin = 24f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(-margin * 2f, height);
        }

        // ==================== 表示の更新 ====================

        void Update()
        {
            if (_clockText != null)
            {
                var now = System.DateTime.Now;
                if (now.Minute != _shownClockMinute)
                {
                    _shownClockMinute = now.Minute;
                    _clockText.text = now.ToString("HH:mm");
                }
            }

            // 音量スライダーの debounce 送信
            if (_volSendAt > 0f && Time.unscaledTime >= _volSendAt)
            {
                _volSendAt = -1f;
                _ = SendVolumeActionAsync("volume_set", "value=" + _volPending);
            }

            // 曲終了の2度押し確認が期限切れになったらラベルを戻す
            if (_endArmedAt > 0f && Time.time - _endArmedAt > EndConfirmWindowSeconds
                && _endLabel != null)
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

            // 進捗の補間表示 (ポーリングの間も滑らかに進める)
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

        public override void OnShow()
        {
            ApplyLandscapeMode(true);
            // 据え置き掲示のため画面を消灯させない
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ApplyRecordSkin();
            RefreshQrCodes();
            _ = LoadRoomNameAsync();
            _ = ConfigureControlsAsync();
            _ = SyncVolumeAsync();
            _nowSignature = null;
            _listSignature = null;
            _nowPolling = StartCoroutine(NowPollRoutine());
            _listPolling = StartCoroutine(ListPollRoutine());
        }

        public override void OnHide()
        {
            if (_nowPolling != null)
            {
                StopCoroutine(_nowPolling);
                _nowPolling = null;
            }
            if (_listPolling != null)
            {
                StopCoroutine(_listPolling);
                _listPolling = null;
            }
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            // RebuildAll 中も OnHide → OnShow が同一フレームで対で呼ばれるため、
            // ここで縦へ戻しても表示継続時は直後の OnShow が横に設定し直す
            ApplyLandscapeMode(false);
        }

        public override void OnRebuild()
        {
            // 子 (行・QR 画像・盤スプライト) は RebuildAll が破棄するので、参照と自前リソースを片付ける
            _rows.Clear();
            _controlCards.Clear();
            _advancedCard = null;
            DestroyQrTextures();
            DestroyRecordResources();
        }

        void OnDestroy()
        {
            DestroyQrTextures();
            DestroyRecordResources();
        }

        void DestroyRecordResources()
        {
            if (_skinRecordTex != null)
            {
                Destroy(_skinRecordTex);
                _skinRecordTex = null;
            }
            _recordSkinKey = null;
        }

        void DestroyQrTextures()
        {
            if (_connectQrTex != null)
            {
                Destroy(_connectQrTex);
                _connectQrTex = null;
            }
            if (_downloadQrTex != null)
            {
                Destroy(_downloadQrTex);
                _downloadQrTex = null;
            }
            _connectQrPayload = null;
        }

        /// <summary>
        /// 画面の向きと Canvas の基準解像度を切り替える。
        /// 回転による向き・セーフエリアの変化を AppRoot が検知して RebuildAll し、
        /// 各画面は BuildUi で現在の向きに合わせて組み直される。
        /// </summary>
        void ApplyLandscapeMode(bool on)
        {
            if (on)
            {
                // 横のみの自動回転 (据え置き時にどちら向きでも置けるように)
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
            else
            {
                Screen.orientation = ScreenOrientation.Portrait;
            }
            var scaler = GetComponentInParent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = on ? new Vector2(1920f, 1080f) : new Vector2(1080f, 1920f);
            }
            AppRoot.CanvasRefWidth = on ? 1920f : 1080f;
            AppRoot.CanvasRefHeight = on ? 1080f : 1920f;
        }

        /// <summary>接続 QR とアプリ入手 QR を (必要なら) 生成し直す。</summary>
        void RefreshQrCodes()
        {
            // 部屋接続: Web 版 toolinfo の QR と同じ「URL + ?easypass=」形式。
            // アプリの QR 読み取り・スマホブラウザのどちらでも開ける
            string payload = AppConfig.ServerUrl;
            if (!string.IsNullOrEmpty(payload) && !string.IsNullOrEmpty(AppConfig.EasyPass))
            {
                payload = payload.TrimEnd('/') + "/?easypass="
                    + System.Uri.EscapeDataString(AppConfig.EasyPass);
            }
            if (_connectQrImage != null && !string.IsNullOrEmpty(payload)
                && payload != _connectQrPayload)
            {
                if (_connectQrTex != null)
                {
                    Destroy(_connectQrTex);
                }
                _connectQrTex = QrCodeTexture.Create(payload);
                _connectQrPayload = payload;
                _connectQrImage.texture = _connectQrTex;
            }
            if (_connectUrlText != null)
            {
                // 認証キーワードは文字では見せない (QR にのみ含める)
                _connectUrlText.text = string.IsNullOrEmpty(AppConfig.ServerUrl)
                    ? "接続設定が未設定です" : AppConfig.ServerUrl;
            }

            if (_downloadQrImage != null && _downloadQrTex == null)
            {
                _downloadQrTex = QrCodeTexture.Create(AppDownloadUrl);
                _downloadQrImage.texture = _downloadQrTex;
            }
        }

        async Task LoadRoomNameAsync()
        {
            if (_roomText == null)
            {
                return;
            }
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                string roomName = caps.Server != null ? caps.Server.RoomName : null;
                if (_roomText != null && !string.IsNullOrEmpty(roomName))
                {
                    _roomText.text = roomName + "部屋 : ライブダッシュボード";
                }
            }
            catch (System.Exception)
            {
                // 未接続時は既定の表示のまま
            }
        }

        // ==================== プレイヤー操作 ====================

        async Task SendActionAsync(string action, string extraQuery = null)
        {
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync(action, extraQuery);
                // 応答に現在値が含まれる操作は表示へ反映する (PlayerScreen と同じ)
                if (result.Volume.HasValue)
                {
                    ApplyVolumeUi(result.Volume.Value);
                }
                else if (result.CompLevel.HasValue && _compText != null)
                {
                    _compText.text = CompLabel(result.CompLevel.Value);
                }
                if (action == "playpause" || action == "next" || action == "start_first"
                    || action == "start" || action == "keychange")
                {
                    // 盤の回転・アイコン・バッジを早めに追従させる
                    _ = RefreshNowPlayingAsync();
                }
            }
            catch (ApiException e)
            {
                UiFactory.ShowToast(ActionErrorMessage(e), true);
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("操作に失敗しました: " + e.Message, true);
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
                UiFactory.ShowToast("コード番号を入力してください", true);
                Se.Play(Se.Error);
                return;
            }
            await SendActionAsync("command", "value=" + code);
        }

        static string CompLabel(int level)
        {
            return level > 0 ? "+" + level : level.ToString();
        }

        /// <summary>ボリューム系 (応答の現在値をスライダーへ反映する)。</summary>
        async Task SendVolumeActionAsync(string action, string extraQuery = null)
        {
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync(action, extraQuery);
                if (result.Volume.HasValue)
                {
                    ApplyVolumeUi(result.Volume.Value);
                }
            }
            catch (ApiException e)
            {
                UiFactory.ShowToast(ActionErrorMessage(e), true);
            }
            catch (System.Exception e)
            {
                UiFactory.ShowToast("操作に失敗しました: " + e.Message, true);
            }
        }

        async Task SyncVolumeAsync()
        {
            try
            {
                var result = await AppConfig.CreateClient().PlayerActionAsync("volume_get");
                if (result.Volume.HasValue)
                {
                    ApplyVolumeUi(result.Volume.Value);
                }
            }
            catch (System.Exception)
            {
                // 未接続・foobar 非対応などは表示 "-" のまま
            }
        }

        void ApplyVolumeUi(int volume)
        {
            if (_volSlider == null)
            {
                return;
            }
            _volApplying = true;
            _volSlider.value = volume;
            _volApplying = false;
            if (_volText != null)
            {
                _volText.text = volume.ToString();
            }
        }

        static string ActionErrorMessage(ApiException e)
        {
            if (e.HttpStatus == 501)
            {
                return "このプレイヤーでは使えない操作です";
            }
            if (e.HttpStatus == 502)
            {
                return "プレイヤーが起動していません";
            }
            return "操作に失敗しました: " + e.Message;
        }

        // ==================== ポーリング ====================

        IEnumerator NowPollRoutine()
        {
            while (true)
            {
                _ = RefreshNowPlayingAsync();
                yield return new WaitForSeconds(NextNowPollDelay());
            }
        }

        IEnumerator ListPollRoutine()
        {
            while (true)
            {
                _ = RefreshListAsync();
                yield return new WaitForSeconds(ListPollIntervalSeconds);
            }
        }

        /// <summary>曲の終わり際と曲間だけ間隔を詰める (PlayerScreen と同じ方針)。</summary>
        float NextNowPollDelay()
        {
            if (_lastPollFailed)
            {
                return 3f;
            }
            if (!_hasProgress)
            {
                return 0.8f;
            }
            float pos = _basePosMs;
            if (_stateNum == 2)
            {
                pos += (Time.realtimeSinceStartup - _baseAt) * 1000f;
            }
            if (_totalMs - pos < 8000f)
            {
                return 0.5f;
            }
            return NowPollIntervalSeconds;
        }

        async Task RefreshNowPlayingAsync()
        {
            if (_nowRefreshing)
            {
                return;
            }
            _nowRefreshing = true;
            try
            {
                var now = await AppConfig.CreateClient().GetNowPlayingAsync();

                int state = 0;
                if (now.Playing && !string.IsNullOrEmpty(now.Status))
                {
                    int.TryParse(now.Status, out state);
                }
                _stateNum = now.Playing ? state : 0;

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

                string sig = (now.Playing ? "1" : "0") + "|" + _stateNum
                    + "|" + (_hasProgress ? "p" : "-")
                    + "|" + now.PlayingTitle + "|" + now.PlayingSinger + "|" + now.Player
                    + "|" + now.Keychange;
                if (sig != _nowSignature)
                {
                    _nowSignature = sig;
                    UpdateNowVisual(now);
                }
                _lastPollFailed = false;
            }
            catch (System.Exception)
            {
                _lastPollFailed = true;
            }
            finally
            {
                _nowRefreshing = false;
            }
        }

        void UpdateNowVisual(NowPlayingDto now)
        {
            if (_stateBadgeText == null)
            {
                return;
            }
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

            string playerName = now.Player == "mpc" ? "MPC"
                : now.Player == "foobar" ? "foobar2000" : null;
            _playerBadgeRect.gameObject.SetActive(playerName != null);
            if (playerName != null)
            {
                _playerBadgeText.text = playerName;
                _playerBadgeRect.anchoredPosition = new Vector2(8f + stateRect.sizeDelta.x + 12f, -2f);
                _playerBadgeRect.sizeDelta =
                    new Vector2(UiFactory.EstimateTextWidth(playerName, 22) + 44f, 46f);
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

            if (_keyText != null)
            {
                _keyText.text = now.Keychange == 0 ? "♪ 原曲"
                    : (now.Keychange > 0 ? "+" + now.Keychange : now.Keychange.ToString());
            }
        }

        async Task RefreshListAsync()
        {
            if (_listRefreshing)
            {
                return;
            }
            _listRefreshing = true;
            try
            {
                var list = await AppConfig.CreateClient().GetRequestsAsync();
                UpdateListVisual(list);
            }
            catch (System.Exception)
            {
                // 次のポーリングで再試行
            }
            finally
            {
                _listRefreshing = false;
            }
        }

        void UpdateListVisual(RequestListDto list)
        {
            if (_queueContent == null || list == null || list.Items == null)
            {
                return;
            }

            if (_queueCountText != null)
            {
                string count = list.RemainingCount + " 曲待機中";
                if (list.RemainingSeconds > 0)
                {
                    count += " (約" + Mathf.CeilToInt(list.RemainingSeconds / 60f) + "分)";
                }
                _queueCountText.text = count;
            }

            // 再生済み (直近 MaxPlayedRows 件) → 再生中 → 未再生 (再生順) を1本のリストで表示する
            RequestItemDto playing = null;
            var pending = new List<RequestItemDto>();
            var played = new List<RequestItemDto>();
            foreach (var item in list.Items)
            {
                if (item.Nowplaying == "再生中")
                {
                    playing = item;
                }
                else if (IsPending(item))
                {
                    pending.Add(item);
                }
                else
                {
                    // Items は reqorder 降順 = 再生済みは新しく再生された順に並ぶ
                    played.Add(item);
                }
            }
            pending.Sort((a, b) => a.Position.CompareTo(b.Position));
            if (played.Count > MaxPlayedRows)
            {
                played.RemoveRange(MaxPlayedRows, played.Count - MaxPlayedRows);
            }
            played.Reverse(); // 表示は古い順 (上から時系列で 再生済 → 再生中 → 予約 と流れる)

            // 曲情報の修正 (song_name 等) も行へ反映されるようシグネチャに含める
            var sig = new System.Text.StringBuilder();
            sig.Append(playing != null ? playing.Id : 0).Append(':')
               .Append(playing != null ? playing.SongName : "").Append('|');
            foreach (var item in pending)
            {
                sig.Append(item.Id).Append(':').Append(item.Reqorder).Append(':')
                   .Append(item.Singer).Append(':').Append(item.SongName).Append(':')
                   .Append(item.Secret).Append('|');
            }
            sig.Append('#');
            foreach (var item in played)
            {
                sig.Append(item.Id).Append(':').Append(item.SongName).Append('|');
            }
            if (sig.ToString() == _listSignature)
            {
                return;
            }
            _listSignature = sig.ToString();

            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();

            if (playing == null && pending.Count == 0 && played.Count == 0)
            {
                var empty = UiFactory.CreateText(_queueContent, "Empty",
                    "予約はまだありません", 28, UiFactory.TextMuted);
                var layout = empty.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = UiFactory.LineHeight(28) + 40f;
                _rows.Add(empty.gameObject);
                return;
            }

            float playedHeight = 0f;
            foreach (var item in played)
            {
                playedHeight += AddPlayedRow(item) + 14f; // 14 = リストの行間
            }
            if (playing != null)
            {
                AddQueueRow(playing, 0);
            }
            // 番号は「つぎにうたう順」の連番 (サーバーの Position は再生中の曲が 1 を占有する)
            for (int i = 0; i < pending.Count; i++)
            {
                AddQueueRow(pending[i], i + 1);
            }

            // 再生済で埋まって「再生中・これから」が見えないままにならないよう、
            // リストが表示域に収まらないときは再生済ぶんを上へ送った位置で表示する
            // (上へスクロールすれば再生済も見える)
            Canvas.ForceUpdateCanvases();
            var viewport = (RectTransform)_queueContent.parent;
            float overflow = _queueContent.rect.height - viewport.rect.height;
            float scrollY = overflow > 0f ? Mathf.Min(playedHeight, overflow) : 0f;
            _queueContent.anchoredPosition = new Vector2(
                _queueContent.anchoredPosition.x, scrollY);
        }

        static bool IsPending(RequestItemDto item)
        {
            return item.Nowplaying == "未再生" || item.Nowplaying == "1";
        }

        /// <summary>行のサブ表示 (作品 [OP/ED] · 種別)。</summary>
        static string DescribeWorkKind(RequestItemDto item)
        {
            string work = "";
            if (!string.IsNullOrEmpty(item.ListerWork))
            {
                work = item.ListerWork;
                if (!string.IsNullOrEmpty(item.ListerOpEd))
                {
                    work += " [" + item.ListerOpEd + "]";
                }
            }
            if (!string.IsNullOrEmpty(item.Kind))
            {
                work = string.IsNullOrEmpty(work) ? item.Kind : work + " · " + item.Kind;
            }
            return work;
        }

        /// <summary>
        /// 再生済みのコンパクト行 (✓ 曲名 — うたう人)。使った行の高さを返す。
        /// 主役の「再生中・これから」を圧迫しないよう1行の薄い表示にする。
        /// </summary>
        float AddPlayedRow(RequestItemDto item)
        {
            string title = !string.IsNullOrEmpty(item.SongName)
                ? item.SongName : (item.DisplayName ?? item.Songfile ?? "");
            string label = "✓  " + title
                + (string.IsNullOrEmpty(item.Singer) ? "" : " — " + item.Singer);
            float rowH = 10f + UiFactory.LineHeight(22) + 10f;

            var row = UiFactory.CreatePanel(_queueContent, "Played" + item.Id,
                new Color(UiFactory.PanelBg.r, UiFactory.PanelBg.g, UiFactory.PanelBg.b, 0.45f));
            UiFactory.Roundify(row.GetComponent<Image>());
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = rowH;

            var text = UiFactory.CreateText(row, "Label", label, 22,
                UiFactory.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24f, 0f);
            text.rectTransform.offsetMax = new Vector2(-24f, 0f);
            UiFactory.FitLabel(text);

            _rows.Add(row.gameObject);
            return rowH;
        }

        /// <summary>キュー行 (order 0 = 再生中のハイライト行)。</summary>
        void AddQueueRow(RequestItemDto item, int order)
        {
            bool isPlaying = order == 0;
            bool masked = item.Secret == 1 && IsPending(item);
            string title = !masked && !string.IsNullOrEmpty(item.SongName)
                ? item.SongName : (item.DisplayName ?? item.Songfile ?? "");
            string sub = string.IsNullOrEmpty(item.Singer) ? "" : "うたう人: " + item.Singer;
            if (!masked)
            {
                string workKind = DescribeWorkKind(item);
                if (!string.IsNullOrEmpty(workKind))
                {
                    sub = string.IsNullOrEmpty(sub) ? workKind : sub + "  ·  " + workKind;
                }
            }

            float rowH = 16f + UiFactory.LineHeight(28)
                + (string.IsNullOrEmpty(sub) ? 0f : UiFactory.LineHeight(20) + 2f) + 16f;

            var row = UiFactory.CreatePanel(_queueContent, "Row" + item.Id,
                isPlaying ? UiFactory.PrimaryPale : UiFactory.PanelBg);
            UiFactory.Roundify(row.GetComponent<Image>());
            var layoutElem = row.gameObject.AddComponent<LayoutElement>();
            layoutElem.preferredHeight = rowH;

            // 順番の丸バッジ (再生中は ▶)
            const float badgeSize = 56f;
            var badge = UiFactory.CreatePanel(row, "Badge",
                isPlaying ? UiFactory.Primary : UiFactory.CardBg);
            UiFactory.Roundify(badge.GetComponent<Image>());
            badge.anchorMin = new Vector2(0f, 0.5f);
            badge.anchorMax = new Vector2(0f, 0.5f);
            badge.pivot = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(14f, 0f);
            badge.sizeDelta = new Vector2(badgeSize, badgeSize);
            var num = UiFactory.CreateText(badge, "Num", isPlaying ? "▶" : order.ToString(), 26,
                isPlaying ? Color.white : UiFactory.PrimaryDark);
            UiFactory.StretchFull(num.rectTransform);

            float textLeft = 14f + badgeSize + 14f;
            float textY = 10f;
            var titleText = UiFactory.CreateText(row, "Title", title, 28,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2((textLeft - 14f) * 0.5f, -textY);
            titleRect.sizeDelta = new Vector2(-(textLeft + 14f), UiFactory.LineHeight(28));
            UiFactory.FitLabel(titleText);

            if (!string.IsNullOrEmpty(sub))
            {
                var subText = UiFactory.CreateText(row, "Sub", sub, 20,
                    UiFactory.TextMuted, TextAnchor.MiddleLeft);
                var subRect = subText.rectTransform;
                subRect.anchorMin = new Vector2(0f, 1f);
                subRect.anchorMax = new Vector2(1f, 1f);
                subRect.pivot = new Vector2(0.5f, 1f);
                subRect.anchoredPosition = new Vector2((textLeft - 14f) * 0.5f,
                    -(textY + UiFactory.LineHeight(28) + 2f));
                subRect.sizeDelta = new Vector2(-(textLeft + 14f), UiFactory.LineHeight(20));
                UiFactory.FitLabel(subText);
            }

            _rows.Add(row.gameObject);
        }

        static string FormatMs(float ms)
        {
            int total = Mathf.Max(0, (int)(ms / 1000f));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
