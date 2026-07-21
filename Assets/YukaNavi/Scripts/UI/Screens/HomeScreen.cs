using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// ホーム画面。動画背景 + 音符パーティクル + ゆかりちゃん + 再生中ティッカー。
    /// 背景とキャラはスキン (きせかえ) で差し替えられる。
    /// 自分の予約の順番が近づくと「そろそろ出番」バナーを出す。
    /// </summary>
    public class HomeScreen : ScreenBase
    {
        /// <summary>他画面の背後に残し、半透明背景から壁紙・キャラ・時計を透かして見せる。</summary>
        public override bool KeepVisibleInBackground
        {
            get { return true; }
        }

        const float PollIntervalSeconds = 5f;

        // 時計・メッセージ・マスコットの表示/位置/大きさはスキンごとに保存する (HomeLayoutStore)。
        // 表示のオン/オフはきせかえ画面に統合した。

        /// <summary>長押しで移動・拡縮できるホーム上のパーツ。</summary>
        enum HomeItem { None, Clock, Ticker, Mascot }

        class MovableItem
        {
            public RectTransform Group;
            public Image HitArea;
            public GameObject EditControls;  // 移動モード中の ×/拡縮ハンドル
            public string LayoutKey;         // HomeLayoutStore のキー (clock / ticker / mascot)
        }

        Text _tickerNowText;
        Text _tickerNextText;
        Text _clockText;
        Text _dateText;
        Text _roomNameText;
        GameObject _roomNameGo;
        GameObject _roomModal;
        List<RoomDto> _rooms;
        MovableItem _clockItem;
        MovableItem _tickerItem;
        MovableItem _mascotItem;
        HomeItem _editing = HomeItem.None;
        GameObject _editOverlay;
        GameObject _editHint;
        Button _bgmButton;
        Image _bgmIcon;
        Sprite _speakerOnSprite;
        Sprite _speakerMuteSprite;
        SkinDef _currentSkin;
        int _editingSiblingIndex; // 移動モード中に最前面へ出す前の描画順
        int _lastClockMinute = -1;
        RectTransform _tickerBgRect;
        RectTransform _bannerRect;
        RectTransform _tickerGroupRect;
        Text _statusClockText;
        Image _batteryFill;
        GameObject _batteryGo;
        Text _nameText;
        GameObject _banner;
        Text _bannerText;
        GameObject _backgroundGo;
        MascotView _mascot;
        VideoPlayer _videoPlayer;
        RenderTexture _videoTexture;
        string _appliedSkinId;
        int _appliedSkinRevision = -1;
        Coroutine _polling;
        bool _refreshing;

        public override void BuildUi()
        {
            // 背景とマスコットはスキン依存のため OnShow の ApplySkin() で構築する。
            // ここでは常設要素のみ作る (描画順: 背景[0] → パーティクル → マスコット → ロゴ以降)

            NoteParticles.Create(transform);

            // 最上部の白帯 (リンクラ風のステータスバー)。壁紙の対象外として不透明の白にする。
            // 左から 時刻・バッテリー / NAME (自分の名前) / 部屋名 (タップで部屋移動)。
            // 高さは NAME/ROOM ボックス (文字の大きさ設定に追従) + ゆとりのある上下余白
            float capH = UiFactory.ScaledFontSize(14) + 6f;
            float valueH = UiFactory.LineHeight(25);
            float boxH = 8f + capH + valueH + 8f;
            float statusBarH = Mathf.Max(110f, boxH + 64f);
            var statusBar = UiFactory.CreatePanel(transform, "StatusBar", Color.white);
            statusBar.anchorMin = new Vector2(0f, 1f);
            statusBar.anchorMax = new Vector2(1f, 1f);
            statusBar.pivot = new Vector2(0.5f, 1f);
            statusBar.sizeDelta = new Vector2(0f, statusBarH);
            UiFactory.AddShadow(statusBar.gameObject, 4f);
            UiFactory.ExtendBarIntoSafeArea(statusBar, Color.white); // ノッチの裏まで白を敷く

            // 時刻 (上) + バッテリー (下) の縦2段 (帯の縦中央に揃える)
            _statusClockText = UiFactory.CreateText(statusBar, "Clock", "", 30,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            _statusClockText.fontStyle = FontStyle.Bold;
            UiFactory.FitLabel(_statusClockText); // 大きい文字サイズ設定でも枠内で1行に収める
            var scRect = _statusClockText.rectTransform;
            scRect.anchorMin = new Vector2(0f, 0.5f);
            scRect.anchorMax = new Vector2(0f, 0.5f);
            scRect.pivot = new Vector2(0f, 0.5f);
            scRect.anchoredPosition = new Vector2(28f, 21f);
            scRect.sizeDelta = new Vector2(140f, 40f);

            // バッテリー (枠 + 塗り + 先端の凸)。残量が取れない環境 (エディタ等) では非表示
            var batteryGo = new GameObject("Battery");
            batteryGo.transform.SetParent(statusBar, false);
            var batteryFrame = batteryGo.AddComponent<Image>();
            batteryFrame.color = new Color(0.55f, 0.52f, 0.65f);
            UiFactory.Roundify(batteryFrame);
            batteryFrame.raycastTarget = false;
            var batteryRect = (RectTransform)batteryGo.transform;
            batteryRect.anchorMin = batteryRect.anchorMax = new Vector2(0f, 0.5f);
            batteryRect.pivot = new Vector2(0f, 0.5f);
            batteryRect.anchoredPosition = new Vector2(28f, -22f);
            batteryRect.sizeDelta = new Vector2(64f, 30f);
            var batteryInner = new GameObject("Inner");
            batteryInner.transform.SetParent(batteryGo.transform, false);
            var batteryInnerImg = batteryInner.AddComponent<Image>();
            batteryInnerImg.color = Color.white;
            UiFactory.Roundify(batteryInnerImg);
            batteryInnerImg.raycastTarget = false;
            UiFactory.StretchFull(batteryInnerImg.rectTransform);
            batteryInnerImg.rectTransform.offsetMin = new Vector2(3f, 3f);
            batteryInnerImg.rectTransform.offsetMax = new Vector2(-3f, -3f);
            var batteryFillGo = new GameObject("Fill");
            batteryFillGo.transform.SetParent(batteryGo.transform, false);
            _batteryFill = batteryFillGo.AddComponent<Image>();
            _batteryFill.color = new Color(0.55f, 0.52f, 0.65f);
            UiFactory.Roundify(_batteryFill);
            _batteryFill.raycastTarget = false;
            var bfRect = _batteryFill.rectTransform;
            bfRect.anchorMin = new Vector2(0f, 0f);
            bfRect.anchorMax = new Vector2(1f, 1f);
            bfRect.offsetMin = new Vector2(6f, 6f);
            bfRect.offsetMax = new Vector2(-6f, -6f);
            var batteryTipGo = new GameObject("Tip");
            batteryTipGo.transform.SetParent(batteryGo.transform, false);
            var batteryTip = batteryTipGo.AddComponent<Image>();
            batteryTip.color = new Color(0.55f, 0.52f, 0.65f);
            UiFactory.Roundify(batteryTip);
            batteryTip.raycastTarget = false;
            var tipRect = batteryTip.rectTransform;
            tipRect.anchorMin = tipRect.anchorMax = new Vector2(1f, 0.5f);
            tipRect.pivot = new Vector2(0f, 0.5f);
            tipRect.anchoredPosition = new Vector2(3f, 0f);
            tipRect.sizeDelta = new Vector2(7f, 14f);
            _batteryGo = batteryGo;
            _batteryGo.SetActive(false);

            // NAME プレート (自分の名前。予約時の名前が入る)。
            // リンクラ風の「灰色の縁 + 白地、上にキャプション・下に黒文字」ボックス
            var namePlate = CreateOutlinedBox(statusBar, "NamePlate", new Vector2(190f, 0f),
                new Vector2(400f, boxH), false);
            var nameCaption = UiFactory.CreateText(namePlate, "Caption", "NAME", 14,
                UiFactory.Primary, TextAnchor.UpperLeft);
            nameCaption.fontStyle = FontStyle.Bold;
            SetBoxCaption(nameCaption.rectTransform, 22f, capH);
            _nameText = UiFactory.CreateText(namePlate, "Name", "", 25,
                UiFactory.TextDark, TextAnchor.LowerLeft);
            SetBoxValue(_nameText.rectTransform, 22f, valueH);
            UiFactory.FitLabel(_nameText, 16); // 枠に入らないときは縮めて必ず見せる

            // 部屋名 (タップで部屋移動モーダル。Web 版の部屋ドロップダウンと同じ動作)。
            // 左にドアアイコン、右に「ROOM」キャプション + 黒文字の部屋名
            var roomPill = CreateOutlinedBox(statusBar, "RoomName", new Vector2(-20f, 0f),
                new Vector2(440f, boxH), true);
            var roomPillButton = roomPill.gameObject.AddComponent<Button>();
            roomPill.gameObject.AddComponent<PressEffect>();
            roomPillButton.onClick.AddListener(OpenRoomModal);
            var roomIcon = UiFactory.CreateImage(roomPill, "Icon",
                "Art/UI/Icons/yukanavi_icon_room_door_256");
            roomIcon.color = UiFactory.PrimaryDark; // 白単色素材を着色
            roomIcon.preserveAspect = true;
            roomIcon.raycastTarget = false;
            var roomIconRect = roomIcon.rectTransform;
            roomIconRect.anchorMin = roomIconRect.anchorMax = new Vector2(0f, 0.5f);
            roomIconRect.pivot = new Vector2(0f, 0.5f);
            roomIconRect.anchoredPosition = new Vector2(16f, 0f);
            roomIconRect.sizeDelta = new Vector2(40f, 40f);
            var roomCaption = UiFactory.CreateText(roomPill, "Caption", "ROOM", 14,
                UiFactory.Primary, TextAnchor.UpperLeft);
            roomCaption.fontStyle = FontStyle.Bold;
            SetBoxCaption(roomCaption.rectTransform, 70f, capH);
            _roomNameText = UiFactory.CreateText(roomPill, "Text", "", 25,
                UiFactory.TextDark, TextAnchor.LowerLeft);
            SetBoxValue(_roomNameText.rectTransform, 70f, valueH);
            UiFactory.FitLabel(_roomNameText, 16); // 枠に入らないときは縮めて必ず見せる
            _roomNameGo = roomPill.gameObject;
            _roomNameGo.SetActive(false); // 部屋名 (または URL) が取れたら表示

            // 移動モードの確定用オーバーレイ。移動対象より奥に置き「まわりをタップ」で確定する
            _editOverlay = new GameObject("EditOverlay");
            _editOverlay.transform.SetParent(transform, false);
            UiFactory.StretchFull(_editOverlay.AddComponent<RectTransform>());
            var editOverlayImg = _editOverlay.AddComponent<Image>();
            editOverlayImg.color = new Color(0f, 0f, 0f, 0f);
            var editOverlayButton = _editOverlay.AddComponent<Button>();
            editOverlayButton.transition = Selectable.Transition.None;
            editOverlayButton.onClick.AddListener(() => EndEdit(true));
            _editOverlay.SetActive(false);

            // 再生中ティッカー + 出番バナー (ひとまとまりで長押し移動できる)。
            // 高さは文字の大きさ設定 (FontScale) に追従させる
            float tickerLineH = UiFactory.LineHeight(26) + 6f;
            float tickerH = tickerLineH * 2f + 10f;
            var tickerGroup = UiFactory.CreatePanel(transform, "TickerGroup");
            tickerGroup.anchorMin = new Vector2(0f, 1f);
            tickerGroup.anchorMax = new Vector2(1f, 1f);
            tickerGroup.pivot = new Vector2(0.5f, 1f);
            tickerGroup.sizeDelta = new Vector2(-40f, tickerH + 80f);

            var tickerBg = UiFactory.CreatePanel(tickerGroup, "Ticker", new Color(1f, 1f, 1f, 0.78f));
            tickerBg.anchorMin = new Vector2(0f, 1f);
            tickerBg.anchorMax = new Vector2(1f, 1f);
            tickerBg.pivot = new Vector2(0.5f, 1f);
            tickerBg.anchoredPosition = Vector2.zero;
            tickerBg.sizeDelta = new Vector2(0f, tickerH);
            UiFactory.Roundify(tickerBg.GetComponent<Image>());
            tickerBg.GetComponent<Image>().raycastTarget = false;
            UiFactory.AddShadow(tickerBg.gameObject);
            _tickerBgRect = tickerBg;
            _tickerGroupRect = tickerGroup;
            _tickerNowText = CreateTickerLine(tickerBg, "Now", -4f);
            _tickerNextText = CreateTickerLine(tickerBg, "Next", -4f - tickerLineH);

            // そろそろ出番バナー (ティッカーの下)
            var banner = UiFactory.CreatePanel(tickerGroup, "Banner", UiFactory.Primary);
            banner.anchorMin = new Vector2(0f, 1f);
            banner.anchorMax = new Vector2(1f, 1f);
            banner.pivot = new Vector2(0.5f, 1f);
            banner.anchoredPosition = new Vector2(0f, -(tickerH + 10f));
            banner.sizeDelta = new Vector2(-80f, 66f);
            _bannerRect = banner;
            UiFactory.Roundify(banner.GetComponent<Image>());
            banner.GetComponent<Image>().raycastTarget = false;
            UiFactory.AddShadow(banner.gameObject);
            _bannerText = UiFactory.CreateText(banner, "Text", "", 32, Color.white);
            UiFactory.StretchFull(_bannerText.rectTransform);
            _banner = banner.gameObject;
            _banner.SetActive(false);

            _tickerItem = SetupMovable(tickerGroup, HomeLayoutStore.Ticker, HomeItem.Ticker);

            // 大きな時刻表示 (リンクラ風)。長押しで移動できる
            var clockGroup = UiFactory.CreatePanel(transform, "ClockGroup");
            clockGroup.anchorMin = clockGroup.anchorMax = new Vector2(1f, 1f);
            clockGroup.pivot = new Vector2(1f, 1f);
            clockGroup.sizeDelta = new Vector2(420f, 164f);

            _clockText = UiFactory.CreateText(clockGroup, "Clock", "", 96, Color.white, TextAnchor.MiddleRight);
            UiFactory.FitLabel(_clockText); // 飾りの大時計は文字サイズ設定によらず枠内に収める
            var clockRect = _clockText.rectTransform;
            clockRect.anchorMin = new Vector2(0f, 1f);
            clockRect.anchorMax = new Vector2(1f, 1f);
            clockRect.pivot = new Vector2(0.5f, 1f);
            clockRect.anchoredPosition = new Vector2(0f, 0f);
            clockRect.sizeDelta = new Vector2(0f, 110f);
            UiFactory.AddShadow(_clockText.gameObject, 3f);
            _dateText = UiFactory.CreateText(clockGroup, "Date", "", 30, new Color(1f, 1f, 1f, 0.92f),
                TextAnchor.MiddleRight);
            UiFactory.FitLabel(_dateText);
            var dateRect = _dateText.rectTransform;
            dateRect.anchorMin = new Vector2(0f, 1f);
            dateRect.anchorMax = new Vector2(1f, 1f);
            dateRect.pivot = new Vector2(0.5f, 1f);
            dateRect.anchoredPosition = new Vector2(-4f, -112f);
            dateRect.sizeDelta = new Vector2(-8f, 40f);
            UiFactory.AddShadow(_dateText.gameObject, 2f);

            _clockItem = SetupMovable(clockGroup, HomeLayoutStore.Clock, HomeItem.Clock);

            // ホーム設定 (歯車) ボタン: 左上の半透明丸
            var settingsButton = UiFactory.CreateButton(transform, "HomeSettings", "",
                new Color(1f, 1f, 1f, 0.55f), UiFactory.PrimaryDark, 24);
            settingsButton.image.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            float buttonTop = statusBarH + 20f; // 白帯 (高さ可変) の下に置く
            var settingsRect = settingsButton.GetComponent<RectTransform>();
            settingsRect.anchorMin = settingsRect.anchorMax = new Vector2(0f, 1f);
            settingsRect.pivot = new Vector2(0f, 1f);
            settingsRect.anchoredPosition = new Vector2(24f, -buttonTop);
            settingsRect.sizeDelta = new Vector2(88f, 88f);
            var settingsIcon = UiFactory.CreateImage(settingsButton.transform, "Icon",
                "Art/UI/Icons/yukanavi_icon_settings_256");
            settingsIcon.color = UiFactory.PrimaryDark; // 白単色素材を着色
            settingsIcon.preserveAspect = true;
            settingsIcon.raycastTarget = false;
            var settingsIconRect = settingsIcon.rectTransform;
            settingsIconRect.anchorMin = settingsIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            settingsIconRect.pivot = new Vector2(0.5f, 0.5f);
            settingsIconRect.anchoredPosition = Vector2.zero;
            settingsIconRect.sizeDelta = new Vector2(52f, 52f);
            // ホームの表示設定はきせかえ画面に統合した
            settingsButton.onClick.AddListener(() =>
            {
                EndEdit(false);
                Se.Play(Se.Transition);
                Manager.Show<SkinScreen>();
            });

            // サウンド (BGM+操作音) ミュート切替ボタン (右上)。カラオケ用アプリなので既定はミュート
            _bgmButton = UiFactory.CreateButton(transform, "BgmToggle", "",
                new Color(1f, 1f, 1f, 0.55f), UiFactory.PrimaryDark, 22);
            _bgmButton.image.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            _speakerOnSprite = UiFactory.LoadSprite("Art/UI/Icons/yukanavi_icon_speaker_256");
            _speakerMuteSprite = UiFactory.LoadSprite("Art/UI/Icons/yukanavi_icon_speaker_mute_256");
            _bgmIcon = UiFactory.CreateImage(_bgmButton.transform, "Icon",
                "Art/UI/Icons/yukanavi_icon_speaker_mute_256");
            _bgmIcon.preserveAspect = true;
            _bgmIcon.raycastTarget = false;
            var bgmIconRect = _bgmIcon.rectTransform;
            bgmIconRect.anchorMin = bgmIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgmIconRect.pivot = new Vector2(0.5f, 0.5f);
            bgmIconRect.anchoredPosition = Vector2.zero;
            bgmIconRect.sizeDelta = new Vector2(52f, 52f);
            var bgmRect = _bgmButton.GetComponent<RectTransform>();
            bgmRect.anchorMin = bgmRect.anchorMax = new Vector2(1f, 1f);
            bgmRect.pivot = new Vector2(1f, 1f);
            bgmRect.anchoredPosition = new Vector2(-24f, -buttonTop);
            bgmRect.sizeDelta = new Vector2(88f, 88f);
            _bgmButton.onClick.AddListener(ToggleSoundPanel);
            UpdateBgmButton();
            BuildSoundPanel(buttonTop + 88f + 12f);

            // 移動モードのヒント (ナビバーの上)
            var hint = UiFactory.CreateText(transform, "EditHint",
                "ドラッグで移動 / ↔で大きさ / ×で非表示 / まわりをタップで決定", 26, Color.white);
            UiFactory.AddShadow(hint.gameObject, 2f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 24f);
            hintRect.sizeDelta = new Vector2(-40f, 44f);
            _editHint = hint.gameObject;
            _editHint.SetActive(false);
        }

        /// <summary>ボックス上段のキャプション (NAME / ROOM) の配置。</summary>
        static void SetBoxCaption(RectTransform rect, float left, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-16f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        /// <summary>ボックス下段の値 (名前 / 部屋名) の配置。</summary>
        static void SetBoxValue(RectTransform rect, float left, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 8f);
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-16f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        /// <summary>
        /// 白帯用の「外側にふわっとぼける縁 + 白地」の角丸ボックス。anchorRight=true で右寄せ配置。
        /// </summary>
        static RectTransform CreateOutlinedBox(RectTransform parent, string name,
                                               Vector2 position, Vector2 size, bool anchorRight)
        {
            // 透明コンテナ (タップ判定用の Image 付き)
            var box = UiFactory.CreatePanel(parent, name, new Color(1f, 1f, 1f, 0f));
            float ax = anchorRight ? 1f : 0f;
            box.anchorMin = box.anchorMax = new Vector2(ax, 0.5f);
            box.pivot = new Vector2(ax, 0.5f);
            box.anchoredPosition = position;
            box.sizeDelta = size;

            // 外側にぼけるグロー (箱より一回り大きく敷く)
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(box, false);
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = UiFactory.SoftGlowSprite;
            glow.type = Image.Type.Sliced;
            glow.color = new Color(0.72f, 0.70f, 0.80f, 0.8f);
            glow.raycastTarget = false;
            UiFactory.StretchFull(glow.rectTransform);
            glow.rectTransform.offsetMin = new Vector2(-14f, -14f);
            glow.rectTransform.offsetMax = new Vector2(14f, 14f);

            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(box, false);
            var inner = innerGo.AddComponent<Image>();
            inner.color = Color.white;
            UiFactory.Roundify(inner);
            inner.raycastTarget = false;
            UiFactory.StretchFull(inner.rectTransform);
            return box;
        }

        static void SetModalRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(50f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-50f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        void UpdateBgmButton()
        {
            bool on = !Bgm.Muted;
            _bgmButton.image.color = on ? UiFactory.Primary : new Color(1f, 1f, 1f, 0.55f);
            _bgmIcon.sprite = on ? _speakerOnSprite : _speakerMuteSprite;
            _bgmIcon.color = on ? Color.white : UiFactory.PrimaryDark;
            if (_soundToggleLabel != null)
            {
                _soundToggleLabel.text = on ? "♪ 音を鳴らす: オン" : "音を鳴らす: オフ";
                _soundToggle.image.color = on ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            }
        }

        // ---- 音量パネル (スピーカーボタンで開閉) ----

        GameObject _soundPanel;
        Button _soundToggle;
        Text _soundToggleLabel;

        void ToggleSoundPanel()
        {
            Se.Play(Se.Tap);
            _soundPanel.SetActive(!_soundPanel.activeSelf);
        }

        /// <summary>音 ON/OFF と BGM・効果音の音量スライダーを持つ小パネル。</summary>
        void BuildSoundPanel(float top)
        {
            // 外側タップで閉じる透明オーバーレイ
            _soundPanel = new GameObject("SoundPanel");
            _soundPanel.transform.SetParent(transform, false);
            UiFactory.StretchFull(_soundPanel.AddComponent<RectTransform>());
            var overlay = _soundPanel.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0f);
            var overlayButton = _soundPanel.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(() => _soundPanel.SetActive(false));

            float labelH = UiFactory.LineHeight(24);
            float rowH = Mathf.Max(64f, labelH + 8f);
            float y = 16f;
            float cardH = 16f + rowH + 10f + (labelH + 52f) * 2f + 16f;

            var card = UiFactory.CreatePanel(_soundPanel.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(-24f, -top);
            card.sizeDelta = new Vector2(560f, cardH);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None; // カード内タップで閉じない

            // 音 ON/OFF
            _soundToggle = UiFactory.CreateButton(card, "Toggle", "", UiFactory.Primary, Color.white, 24);
            _soundToggleLabel = _soundToggle.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_soundToggleLabel);
            var toggleRect = _soundToggle.GetComponent<RectTransform>();
            SetSoundRow(toggleRect, y, rowH);
            _soundToggle.onClick.AddListener(() =>
            {
                Bgm.Muted = !Bgm.Muted;
                Se.Play(Se.Tap); // 解除した瞬間から操作音が鳴る
                UpdateBgmButton();
            });
            y += rowH + 10f;

            // BGM 音量
            y += AddVolumeRow(card, y, labelH, "BGM の音量", Bgm.Volume, value => Bgm.Volume = value);

            // 効果音 音量
            AddVolumeRow(card, y, labelH, "効果音の音量", Se.Volume, value =>
            {
                Se.Volume = value;
            });

            UpdateBgmButton();
            _soundPanel.SetActive(false);
        }

        float AddVolumeRow(RectTransform card, float y, float labelH, string label,
                           float value, System.Action<float> onChanged)
        {
            var text = UiFactory.CreateText(card, "Label", label, 24,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            SetSoundRow(text.rectTransform, y, labelH);

            var slider = UiFactory.CreateSlider(card, "Slider", 0f, 1f, wholeNumbers: false);
            var sliderRect = slider.GetComponent<RectTransform>();
            SetSoundRow(sliderRect, y + labelH + 4f, 40f);
            slider.value = value;
            slider.onValueChanged.AddListener(v => onChanged(v));
            return labelH + 52f;
        }

        static void SetSoundRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        // ---- 長押し移動モード ----

        /// <summary>
        /// グループに HitArea (長押し・ドラッグ受け) と ×/拡縮ハンドルを付ける。
        /// 位置・大きさ・表示は OnShow の ApplyHomeLayout() が現在のスキンから復元する。
        /// eventTarget を渡すと長押し・ドラッグをそちらで受ける (マスコットのように自前で raycast を持つもの用)。
        /// </summary>
        MovableItem SetupMovable(RectTransform group, string layoutKey, HomeItem kind,
                                 GameObject eventTarget = null)
        {
            var item = new MovableItem
            {
                Group = group,
                LayoutKey = layoutKey,
            };
            group.anchoredPosition = HomeLayoutStore.DefaultPos(layoutKey);

            // 長押し検出とドラッグの受け皿。移動モード中だけ白くハイライトされる
            var hitGo = new GameObject("HitArea");
            hitGo.transform.SetParent(group, false);
            hitGo.transform.SetAsFirstSibling();
            item.HitArea = hitGo.AddComponent<Image>();
            item.HitArea.sprite = UiFactory.RoundedSprite;
            item.HitArea.type = Image.Type.Sliced;
            item.HitArea.color = new Color(1f, 1f, 1f, 0f);
            UiFactory.StretchFull(item.HitArea.rectTransform);
            if (eventTarget != null)
            {
                item.HitArea.raycastTarget = false; // ハイライト表示専用にする
            }
            var drag = (eventTarget != null ? eventTarget : hitGo).AddComponent<HomeDraggable>();
            drag.Target = group;
            drag.Bounds = (RectTransform)transform;
            drag.IsEditing = () => _editing == kind;
            drag.OnLongPress = () => BeginEdit(kind);

            // 移動モード中のコントロール (左上に非表示 ×、右上に拡縮ハンドル)
            var controls = new GameObject("EditControls");
            controls.transform.SetParent(group, false);
            var controlsRect = controls.AddComponent<RectTransform>();
            UiFactory.StretchFull(controlsRect);

            var hideButton = UiFactory.CreateButton(controls.transform, "Hide", "×", Color.white,
                UiFactory.PrimaryDark, 40);
            hideButton.image.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            var hideRect = hideButton.GetComponent<RectTransform>();
            hideRect.anchorMin = hideRect.anchorMax = new Vector2(0f, 1f);
            hideRect.pivot = new Vector2(0.5f, 0.5f);
            hideRect.anchoredPosition = Vector2.zero;
            hideRect.sizeDelta = new Vector2(80f, 80f);
            hideButton.onClick.AddListener(() => HideItem(item));

            // 拡縮ハンドル (右上角。外側へドラッグで拡大、内側へで縮小)
            var handleGo = new GameObject("ScaleHandle");
            handleGo.transform.SetParent(controls.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = UiFactory.RoundedSprite;
            handleImg.type = Image.Type.Sliced;
            handleImg.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            handleImg.color = Color.white;
            UiFactory.AddShadow(handleGo, 3f);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(88f, 88f);
            var handleText = UiFactory.CreateText(handleGo.transform, "Icon", "↔", 44, UiFactory.PrimaryDark);
            UiFactory.StretchFull(handleText.rectTransform);
            handleText.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f); // 斜め (右上-左下) 向きに
            var scaleHandle = handleGo.AddComponent<ScaleHandle>();
            scaleHandle.Target = group;
            scaleHandle.Bounds = (RectTransform)transform;

            item.EditControls = controls;
            item.EditControls.SetActive(false);
            return item;
        }

        /// <summary>現在のスキンに保存された配置を全パーツへ適用する。</summary>
        void ApplyHomeLayout()
        {
            ApplyItemLayout(_clockItem);
            ApplyItemLayout(_tickerItem);
            if (_mascotItem != null)
            {
                ApplyItemLayout(_mascotItem);
            }
        }

        void ApplyItemLayout(MovableItem item)
        {
            var skin = _currentSkin ?? SkinManager.Current();
            var saved = HomeLayoutStore.Load(skin, item.LayoutKey);
            if (saved == null)
            {
                item.Group.anchoredPosition = HomeLayoutStore.DefaultPos(item.LayoutKey);
                item.Group.localScale = Vector3.one;
                item.Group.gameObject.SetActive(true);
                return;
            }
            item.Group.anchoredPosition = new Vector2(saved.X, saved.Y);
            float s = Mathf.Clamp(saved.Scale, HomeLayoutStore.MinScale, HomeLayoutStore.MaxScale);
            item.Group.localScale = new Vector3(s, s, 1f);
            item.Group.gameObject.SetActive(saved.Visible);
            if (saved.Visible)
            {
                // キャンバス幅が端末で異なる (iPad/スマホ) ため、別端末やスキン配布で保存された
                // 座標は画面外になりうる。ドラッグ時と同じクランプで画面内へ引き戻す
                Canvas.ForceUpdateCanvases(); // 直後の GetWorldCorners を今の配置で評価させる
                var canvas = GetComponentInParent<Canvas>();
                float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
                HomeDraggable.ClampIntoBounds(item.Group, (RectTransform)transform, scale);
            }
        }

        /// <summary>パーツの今の位置・大きさ・表示を現在のスキンに保存する。</summary>
        void PersistItemLayout(MovableItem item)
        {
            var skin = _currentSkin ?? SkinManager.Current();
            HomeLayoutStore.Save(skin, item.LayoutKey, new SkinLayoutItem
            {
                Visible = item.Group.gameObject.activeSelf,
                X = item.Group.anchoredPosition.x,
                Y = item.Group.anchoredPosition.y,
                Scale = item.Group.localScale.x,
            });
        }

        MovableItem ItemOf(HomeItem kind)
        {
            switch (kind)
            {
                case HomeItem.Clock:
                    return _clockItem;
                case HomeItem.Mascot:
                    return _mascotItem;
                default:
                    return _tickerItem;
            }
        }

        void BeginEdit(HomeItem kind)
        {
            if (_editing == kind)
            {
                return;
            }
            EndEdit(false); // 別パーツの移動中なら確定してから切り替える
            _editing = kind;
            var item = ItemOf(kind);
            item.HitArea.color = new Color(1f, 1f, 1f, 0.28f);
            item.EditControls.SetActive(true);
            // 確定用オーバーレイより前面に出す (マスコットは通常オーバーレイより奥にいるため、
            // これがないと2回目以降のタッチをオーバーレイに取られて操作できない)
            _editingSiblingIndex = item.Group.GetSiblingIndex();
            item.Group.SetAsLastSibling();
            _editOverlay.SetActive(true);
            _editHint.SetActive(true);
            Se.Play(Se.Tap); // 長押し成立の合図
        }

        void EndEdit(bool playSe)
        {
            if (_editing == HomeItem.None)
            {
                return;
            }
            var item = ItemOf(_editing);
            _editing = HomeItem.None;
            _editOverlay.SetActive(false);
            _editHint.SetActive(false);
            if (item != null)
            {
                item.HitArea.color = new Color(1f, 1f, 1f, 0f);
                item.EditControls.SetActive(false);
                item.Group.SetSiblingIndex(_editingSiblingIndex); // 描画順を元に戻す
                PersistItemLayout(item);
            }
            if (playSe)
            {
                Se.Play(Se.Confirm);
            }
        }

        void HideItem(MovableItem item)
        {
            EndEdit(false);
            item.Group.gameObject.SetActive(false);
            PersistItemLayout(item); // 再表示はきせかえ画面のトグルから
            Se.Play(Se.Tap);
        }

        /// <summary>
        /// 移動モード中の拡縮ハンドル。パーツ中心から外側へドラッグすると拡大、内側へで縮小。
        /// </summary>
        class ScaleHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target;
            public RectTransform Bounds;

            Vector2 _startCenter;
            float _startDist;
            float _startScale;

            public void OnBeginDrag(PointerEventData eventData)
            {
                var corners = new Vector3[4];
                Target.GetWorldCorners(corners);
                Vector3 worldCenter = (corners[0] + corners[2]) * 0.5f;
                var canvas = GetComponentInParent<Canvas>();
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera : null;
                _startCenter = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
                _startDist = Mathf.Max(Vector2.Distance(eventData.position, _startCenter), 1f);
                _startScale = Target.localScale.x;
            }

            public void OnDrag(PointerEventData eventData)
            {
                float k = Vector2.Distance(eventData.position, _startCenter) / _startDist;
                float s = Mathf.Clamp(_startScale * k, HomeLayoutStore.MinScale, HomeLayoutStore.MaxScale);
                Target.localScale = new Vector3(s, s, 1f);
                var canvas = GetComponentInParent<Canvas>();
                float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
                HomeDraggable.ClampIntoBounds(Target, Bounds, scale);
            }
        }

        /// <summary>ホーム上パーツの長押し検出とドラッグ移動。</summary>
        class HomeDraggable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
        {
            const float LongPressSeconds = 0.55f;

            public RectTransform Target;
            public RectTransform Bounds;
            public System.Func<bool> IsEditing;
            public System.Action OnLongPress;

            bool _pressed;
            float _downTime;

            public void OnPointerDown(PointerEventData eventData)
            {
                _pressed = true;
                _downTime = Time.unscaledTime;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _pressed = false;
            }

            void Update()
            {
                if (_pressed && !IsEditing() && Time.unscaledTime - _downTime >= LongPressSeconds)
                {
                    _pressed = false;
                    OnLongPress();
                }
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!IsEditing())
                {
                    _pressed = false; // 長押し成立前に動いたら長押しをキャンセル
                    return;
                }
                var canvas = GetComponentInParent<Canvas>();
                float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
                Target.anchoredPosition += eventData.delta / scale;
                ClampIntoBounds(Target, Bounds, scale);
            }

            /// <summary>はみ出した分だけ引き戻す (アンカー形式や拡縮に依存しないよう画面座標で判定)。</summary>
            public static void ClampIntoBounds(RectTransform target, RectTransform bounds, float scale)
            {
                var t = new Vector3[4];
                var b = new Vector3[4];
                target.GetWorldCorners(t);
                bounds.GetWorldCorners(b);
                float dx = 0f;
                float dy = 0f;
                if (t[0].x < b[0].x)
                {
                    dx = b[0].x - t[0].x;
                }
                else if (t[2].x > b[2].x)
                {
                    dx = b[2].x - t[2].x;
                }
                if (t[0].y < b[0].y)
                {
                    dy = b[0].y - t[0].y;
                }
                else if (t[2].y > b[2].y)
                {
                    dy = b[2].y - t[2].y;
                }
                if (dx != 0f || dy != 0f)
                {
                    target.anchoredPosition += new Vector2(dx, dy) / scale;
                }
            }
        }

        /// <summary>現在のスキンに合わせて背景とマスコットを構築する (変更時のみ)。</summary>
        void ApplySkin()
        {
            var skin = SkinManager.Current();
            _currentSkin = skin; // 配置 (ApplyHomeLayout / PersistItemLayout) もこのスキンを見る
            if (_appliedSkinId == skin.Id && _appliedSkinRevision == SkinManager.Revision)
            {
                return;
            }
            _appliedSkinId = skin.Id;
            _appliedSkinRevision = SkinManager.Revision;

            // 既存の背景・マスコット・動画リソースを破棄
            DestroyBackgroundResources();
            if (_mascotItem != null)
            {
                Destroy(_mascotItem.Group.gameObject); // グループごと破棄 (MascotView も子)
                _mascotItem = null;
                _mascot = null;
            }

            BuildBackground(skin);
            BuildMascot(skin);
        }

        void DestroyBackgroundResources()
        {
            if (_backgroundGo != null)
            {
                Destroy(_backgroundGo);
                _backgroundGo = null;
            }
            if (_videoPlayer != null)
            {
                Destroy(_videoPlayer);
                _videoPlayer = null;
            }
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
                _videoTexture = null;
            }
        }

        static string BgIndexKey(SkinDef skin)
        {
            return "home.bg_index." + skin.Id;
        }

        /// <summary>
        /// 背景タップ (前面に UI が無いところ) で次の背景へ。複数背景 (backgrounds) の
        /// スキンだけで動く。選んだ背景はスキンごとに保存され、次回起動でも続く。
        /// パーツ移動モード中は編集オーバーレイがタップを受けるためここには届かない。
        /// </summary>
        void CycleBackground()
        {
            var skin = _currentSkin ?? SkinManager.Current();
            var layers = SkinManager.GetBackgrounds(skin);
            if (skin.Folder == null || layers.Count <= 1)
            {
                return;
            }
            int index = (PlayerPrefs.GetInt(BgIndexKey(skin), 0) + 1) % layers.Count;
            PlayerPrefs.SetInt(BgIndexKey(skin), index);
            PlayerPrefs.Save();
            Se.Play(Se.Tap);
            DestroyBackgroundResources();
            BuildBackground(skin);
        }

        void BuildBackground(SkinDef skin)
        {
            var view = BackgroundView.Create(transform, "Background");
            // 背景タップで切替できるようにする (透明の当たり判定)
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            var bgButton = view.gameObject.AddComponent<Button>();
            bgButton.transition = Selectable.Transition.None;
            bgButton.onClick.AddListener(CycleBackground);

            bool built = false;
            var layers = SkinManager.GetBackgrounds(skin);
            if (skin.Folder != null && layers.Count > 0)
            {
                int index = Mathf.Clamp(PlayerPrefs.GetInt(BgIndexKey(skin), 0), 0, layers.Count - 1);
                var bgDef = layers[index];
                if (bgDef.Type == "video")
                {
                    string path = SkinManager.GetFilePath(skin, bgDef.File);
                    if (path != null)
                    {
                        SetupVideo(view, null, path);
                        built = true;
                    }
                }
                else if (bgDef.Type == "image")
                {
                    var tex = SkinManager.LoadTexture(skin, bgDef.File);
                    if (tex != null)
                    {
                        view.SetTexture(tex, (float)tex.width / tex.height);
                        built = true;
                    }
                }
                if (built)
                {
                    view.SetAdjust(bgDef.Rotation, bgDef.Zoom, new Vector2(bgDef.OffsetX, bgDef.OffsetY));
                }
            }
            if (!built)
            {
                // 組み込みデフォルト: rich 動画 → 静止画の順で試す
                var clip = Resources.Load<VideoClip>("Videos/yukanavi_home_background_loop_rich_portrait_1080x1920");
                if (clip != null)
                {
                    SetupVideo(view, clip, null);
                }
                else
                {
                    var tex = Resources.Load<Texture2D>("Art/Backgrounds/yukanavi_home_background_no_character_1080x1920");
                    if (tex != null)
                    {
                        view.SetTexture(tex, (float)tex.width / tex.height);
                    }
                }
            }
            view.transform.SetSiblingIndex(0);
            _backgroundGo = view.gameObject;
        }

        /// <summary>動画背景。clip (組み込み) または filePath (スキン) のどちらかを指定する。</summary>
        void SetupVideo(BackgroundView view, VideoClip clip, string filePath)
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            if (clip != null)
            {
                _videoPlayer.clip = clip;
                CreateVideoTexture(view, (int)clip.width, (int)clip.height);
            }
            else
            {
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = filePath;
                // 動画の実サイズは prepare 後に判明するため、それから RenderTexture を作る
                // (固定サイズの RenderTexture に描くとアスペクト比が崩れるため)
                _videoPlayer.prepareCompleted += vp =>
                {
                    CreateVideoTexture(view, (int)vp.width, (int)vp.height);
                    vp.Play();
                };
                _videoPlayer.Prepare();
            }
        }

        void CreateVideoTexture(BackgroundView view, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                width = 1080;
                height = 1920;
            }
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }
            _videoTexture = new RenderTexture(width, height, 0);
            _videoPlayer.targetTexture = _videoTexture;
            view.SetTexture(_videoTexture, (float)width / height);
        }

        void BuildMascot(SkinDef skin)
        {
            Sprite[] customs = null;
            string[][] mascotTalks = null;
            float scale = 1f;
            if (skin.Folder != null)
            {
                if (skin.Character != null && skin.Character.Type == "none")
                {
                    return; // キャラなしスキン
                }
                // 複数キャラ (characters) 対応。タップで次の画像に切り替わる
                var sprites = new List<Sprite>();
                var talks = new List<string[]>(); // sprites と同じ並びのキャラ別セリフ
                foreach (var layer in SkinManager.GetCharacters(skin))
                {
                    if (layer.Type != "image")
                    {
                        continue; // 未対応 type (live2d 等) は読み飛ばす
                    }
                    var tex = SkinManager.LoadTexture(skin, layer.File);
                    if (tex == null)
                    {
                        continue;
                    }
                    sprites.Add(Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f));
                    talks.Add(layer.Talk != null && layer.Talk.Count > 0 ? layer.Talk.ToArray() : null);
                    if (sprites.Count == 1)
                    {
                        scale = Mathf.Clamp(layer.Scale, 0.3f, 2f);
                    }
                }
                if (sprites.Count > 0)
                {
                    customs = sprites.ToArray();
                    mascotTalks = talks.ToArray();
                }
                // 1枚も読めなければデフォルトのゆかりちゃんにフォールバック
            }
            var size = new Vector2(740f, 1110f) * scale;

            // MascotView は内部で浮遊・スクイーズのため自身の位置/スケールを動かすので、
            // ユーザーの移動・拡縮は親グループ側で行う
            var groupGo = new GameObject("MascotGroup");
            groupGo.transform.SetParent(transform, false);
            var group = groupGo.AddComponent<RectTransform>();
            group.anchorMin = group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0f);
            group.sizeDelta = size;
            _mascot = MascotView.Create(group, size, 0f, customs);
            // スキンにセリフが設定されていればタップ時にランダムで表示する
            // (キャラごとの talk があれば表示中のキャラのものが優先される)
            _mascot.CustomLines = (skin.Talk != null && skin.Talk.Count > 0)
                ? skin.Talk.ToArray() : null;
            _mascot.CustomLinesPerCharacter = mascotTalks;
            _mascotItem = SetupMovable(group, HomeLayoutStore.Mascot, HomeItem.Mascot, _mascot.gameObject);
            // 移動モード中はタップ演出 (表情切替・セリフ) を止める
            _mascot.SuppressTap = () => _editing == HomeItem.Mascot;
            // 描画順: 背景[0] → パーティクル[1] → マスコット[2]
            group.SetSiblingIndex(2);
        }

        /// <summary>ティッカーの1行分のテキストを作る (長い曲名は行内で切り詰め)。</summary>
        Text CreateTickerLine(RectTransform parent, string name, float y)
        {
            // 高さと位置は SetTickerLines() が内容の行数に合わせて更新する
            var text = UiFactory.CreateText(parent, name, "", 26, UiFactory.PrimaryDark, TextAnchor.UpperLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, UiFactory.LineHeight(26) + 6f);
            return text;
        }

        /// <summary>
        /// ティッカーの2行分のテキストを反映し、長い曲名は折り返して全文出す
        /// (バーとバナーの位置も行数に合わせて動かす)。
        /// </summary>
        void SetTickerLines(string nowLine, string nextLine)
        {
            _tickerNowText.text = UiFactory.NoWordWrap(nowLine);
            _tickerNextText.text = UiFactory.NoWordWrap(nextLine);

            float h1 = MeasureTickerLine(_tickerNowText, nowLine);
            float h2 = MeasureTickerLine(_tickerNextText, nextLine);

            var nowRect = _tickerNowText.rectTransform;
            nowRect.anchoredPosition = new Vector2(0f, -6f);
            nowRect.sizeDelta = new Vector2(nowRect.sizeDelta.x, h1);

            var nextRect = _tickerNextText.rectTransform;
            nextRect.anchoredPosition = new Vector2(0f, -6f - h1);
            nextRect.sizeDelta = new Vector2(nextRect.sizeDelta.x, h2);

            float tickerH = 6f + h1 + h2 + 8f;
            _tickerBgRect.sizeDelta = new Vector2(0f, tickerH);
            _bannerRect.anchoredPosition = new Vector2(0f, -(tickerH + 10f));
            _tickerGroupRect.sizeDelta = new Vector2(-40f, tickerH + 80f);
        }

        /// <summary>
        /// ティッカー1行分の高さ。概算の行数見積もりは大きい文字サイズで1行分ずれるため、
        /// 実測 (preferredHeight) を使う。幅が未確定の初回だけ概算にフォールバック。
        /// </summary>
        static float MeasureTickerLine(Text text, string content)
        {
            if (text.rectTransform.rect.width > 10f)
            {
                return Mathf.Ceil(text.preferredHeight) + 6f;
            }
            return UiFactory.EstimateWrapLines(content, 26, 990f) * UiFactory.LineHeight(26) + 4f;
        }

        public override void OnShow()
        {
            ReserveScreen.ClearEditSession(); // 曲えらびなおし途中の離脱はホームで無かったことにする
            ApplySkin();
            ApplyHomeLayout(); // スキンに保存された時計などの配置を反映
            if (_videoPlayer != null)
            {
                _videoPlayer.Play();
            }
            // 予約で入力した名前を NAME 枠に反映する
            string username = AppConfig.Username;
            _nameText.text = string.IsNullOrEmpty(username) ? "(よやくすると入ります)" : username;
            _ = LoadRoomNameAsync();
            if (_polling != null)
            {
                StopCoroutine(_polling); // 背後表示のまま OnShow が再度呼ばれても二重ポーリングしない
            }
            _polling = StartCoroutine(PollRoutine());
        }

        /// <summary>RebuildAll 用: 子と一緒に消えないリソースとスキン適用状態を片付ける。</summary>
        public override void OnRebuild()
        {
            if (_videoPlayer != null)
            {
                Destroy(_videoPlayer);
                _videoPlayer = null;
            }
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
                _videoTexture = null;
            }
            _backgroundGo = null;
            _mascot = null;
            _mascotItem = null;
            _roomModal = null;
            _appliedSkinId = null;
            _appliedSkinRevision = -1;
            _editing = HomeItem.None;
        }

        /// <summary>部屋名 (Web 版の「〇〇部屋」) を取得して上部に表示する。名前未設定時は接続先 URL。</summary>
        async Task LoadRoomNameAsync()
        {
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                string roomName = caps.Server != null ? caps.Server.RoomName : null;
                var rooms = caps.Server != null ? caps.Server.Rooms : null;
                if (rooms != null && rooms.Count > 0)
                {
                    _rooms = rooms;
                    SaveKnownRooms(rooms); // 移動先で一覧が取れなくても戻れるように記憶しておく
                }
                else
                {
                    _rooms = LoadKnownRooms();
                }
                string label = string.IsNullOrEmpty(roomName) ? AppConfig.ServerUrl : roomName + "部屋";
                if (!string.IsNullOrEmpty(label))
                {
                    _roomNameText.text = label;
                    _roomNameGo.SetActive(true);
                }
            }
            catch
            {
                // 未接続でも部屋一覧から元の部屋へ戻れるように、ピルは URL 表示で出しておく
                _rooms = LoadKnownRooms();
                _roomNameText.text = AppConfig.ServerUrl;
                _roomNameGo.SetActive(true);
            }
        }

        const string KnownRoomsKey = "home.known_rooms";

        static void SaveKnownRooms(List<RoomDto> rooms)
        {
            try
            {
                PlayerPrefs.SetString(KnownRoomsKey, JsonConvert.SerializeObject(rooms));
                PlayerPrefs.Save();
            }
            catch
            {
            }
        }

        static List<RoomDto> LoadKnownRooms()
        {
            try
            {
                string json = PlayerPrefs.GetString(KnownRoomsKey, "");
                return json == "" ? null : JsonConvert.DeserializeObject<List<RoomDto>>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>部屋移動モーダル (Web 版の部屋ドロップダウン相当)。部屋一覧から毎回作り直す。</summary>
        void OpenRoomModal()
        {
            if (_rooms == null || _rooms.Count == 0)
            {
                return; // 別部屋未設定なら何もしない
            }
            Se.Play(Se.Tap);
            if (_roomModal != null)
            {
                Destroy(_roomModal);
            }
            _roomModal = new GameObject("RoomModal");
            _roomModal.transform.SetParent(transform, false);
            UiFactory.StretchFull(_roomModal.AddComponent<RectTransform>());
            var overlay = _roomModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);
            var overlayButton = _roomModal.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(() => _roomModal.SetActive(false));

            const float rowHeight = 112f;
            var card = UiFactory.CreatePanel(_roomModal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880f, 264f + _rooms.Count * rowHeight);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            // カード内タップがオーバーレイの「閉じる」に抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            var title = UiFactory.CreateText(card, "Title", "部屋を移動", 34, UiFactory.PrimaryDark);
            SetModalRow(title.rectTransform, -32f, 60f);

            string currentUrl = NormalizeUrl(AppConfig.ServerUrl);
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                bool isCurrent = NormalizeUrl(room.Url) == currentUrl;
                // 名前が無い部屋は URL 表示 (認証キーワードは見せない)
                string label = string.IsNullOrEmpty(room.Name)
                    ? YukariUrl.Normalize(room.Url, out _)
                    : room.Name + "部屋";
                Button button;
                if (isCurrent)
                {
                    button = UiFactory.CreateButton(card, "Room" + i, "✓ " + label + " (いまここ)",
                        UiFactory.Primary, Color.white, 30);
                    button.onClick.AddListener(() => _roomModal.SetActive(false));
                }
                else
                {
                    button = UiFactory.CreateSoftButton(card, "Room" + i, label, 30);
                    string url = room.Url;
                    button.onClick.AddListener(() => SwitchRoom(url));
                }
                SetModalRow(button.GetComponent<RectTransform>(), -(120f + i * rowHeight), 96f);
            }

            var closeButton = UiFactory.CreateSoftButton(card, "Close", "閉じる", 30);
            SetModalRow(closeButton.GetComponent<RectTransform>(),
                -(120f + _rooms.Count * rowHeight + 16f), 96f);
            closeButton.onClick.AddListener(() => _roomModal.SetActive(false));
        }

        /// <summary>別の部屋 (サーバー) に接続を切り替える。</summary>
        void SwitchRoom(string rawUrl)
        {
            Se.Play(Se.Confirm);
            _roomModal.SetActive(false);
            // 別部屋 URL に ?easypass=XXXX が付いていれば認証キーワードも引き継ぐ
            string url = YukariUrl.Normalize(rawUrl, out string easypass);
            AppConfig.ServerUrl = url;
            if (!string.IsNullOrEmpty(easypass))
            {
                AppConfig.EasyPass = easypass;
            }
            AppState.Invalidate();
            // 部屋一覧 (_rooms) は消さない: 移動先で取得できなくても元の部屋に戻れるように
            _roomNameText.text = url; // 部屋名が取れたら置き換わる
            _ = LoadRoomNameAsync();
            _ = RefreshAsync();
        }

        static string NormalizeUrl(string url)
        {
            // easypass 付き URL でも同じ部屋として比較できるようにクエリを除去して比べる
            return YukariUrl.Normalize(url, out _).TrimEnd('/').ToLowerInvariant();
        }

        public override void OnHide()
        {
            EndEdit(false); // 移動モード中に画面遷移したら現在位置で確定
            if (_roomModal != null)
            {
                _roomModal.SetActive(false);
            }
            if (_videoPlayer != null)
            {
                _videoPlayer.Pause();
            }
            if (_polling != null)
            {
                StopCoroutine(_polling);
                _polling = null;
            }
        }

        void Update()
        {
            // 時刻表示 (分が変わったときだけ文字列を更新)
            var now = System.DateTime.Now;
            if (now.Minute != _lastClockMinute)
            {
                _lastClockMinute = now.Minute;
                _clockText.text = now.ToString("HH:mm");
                _dateText.text = now.ToString("MM/dd ddd").ToUpperInvariant();
                _statusClockText.text = now.ToString("HH:mm");
                UpdateBattery();
                Bgm.RefreshForCurrentSkin(); // 昼夜 BGM の時間帯またぎ (変化がなければ何もしない)
            }
        }

        /// <summary>上部白帯のバッテリー表示。残量が取れない環境 (エディタ等) では出さない。</summary>
        void UpdateBattery()
        {
            float level = SystemInfo.batteryLevel;
            if (level < 0f)
            {
                _batteryGo.SetActive(false);
                return;
            }
            _batteryGo.SetActive(true);
            // 塗りの幅を残量に合わせる (枠の内側 6px を基準に anchorMax.x で削る)
            _batteryFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(level), 1f);
            _batteryFill.color = level <= 0.2f
                ? UiFactory.Danger
                : new Color(0.55f, 0.52f, 0.65f);
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
                var client = AppConfig.CreateClient();
                var now = await client.GetNowPlayingAsync();
                var requests = await client.GetRequestsAsync();
                UpdateTicker(now, requests);
                UpdateTurnBanner(requests.Items);
            }
            catch (System.Exception e)
            {
                SetTickerLines("未接続: " + e.Message, "");
                _banner.SetActive(false);
            }
            finally
            {
                _refreshing = false;
            }
        }

        void UpdateTicker(NowPlayingDto now, RequestListDto requests)
        {
            string nowLine;
            string nextLine;
            if (now.Playing)
            {
                nowLine = "♪ いま: " + now.PlayingTitle;
                if (!string.IsNullOrEmpty(now.PlayingSinger))
                {
                    nowLine += " (" + now.PlayingSinger + ")";
                }
                if (now.NextSong != null)
                {
                    nextLine = "♪ 次: " + now.NextSong.Title;
                    if (!string.IsNullOrEmpty(now.NextSong.Singer))
                    {
                        nextLine += " (" + now.NextSong.Singer + ")";
                    }
                }
                else
                {
                    nextLine = "♪ 次: (予約なし)";
                }
            }
            else
            {
                nowLine = "再生停止中";
                nextLine = requests.RemainingCount > 0
                    ? "♪ 予約 " + requests.RemainingCount + " 件待ち"
                    : "♪ 予約を待っています";
            }
            SetTickerLines(nowLine, nextLine);
        }

        /// <summary>自分の予約 (名前一致) が未再生の先頭2件に入っていたらバナーを出す。</summary>
        void UpdateTurnBanner(List<RequestItemDto> items)
        {
            string username = AppConfig.Username;
            if (string.IsNullOrEmpty(username) || items == null)
            {
                _banner.SetActive(false);
                return;
            }
            // position 昇順 = 次に再生される順
            var pending = new List<RequestItemDto>();
            foreach (var item in items)
            {
                if (item.Nowplaying == "未再生" || item.Nowplaying == "1")
                {
                    pending.Add(item);
                }
            }
            pending.Sort((a, b) => a.Position.CompareTo(b.Position));
            int mine = pending.FindIndex(i => i.Singer == username);
            if (mine == 0)
            {
                _bannerText.text = "♪ 次はあなたの番だよ！";
                _banner.SetActive(true);
            }
            else if (mine == 1)
            {
                _bannerText.text = "♪ そろそろ出番！あと1曲だよ";
                _banner.SetActive(true);
            }
            else
            {
                _banner.SetActive(false);
            }
        }
    }
}
