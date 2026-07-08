using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// きせかえ (スキン選択) 画面。skins/ フォルダのスキンを一覧し、タップで適用する。
    /// 「スキンを作る」から端末の画像/動画を選び、プレビュー上で位置・ズーム・回転を
    /// 調整してスキンを新規作成できる (サーバーには何も置かない、端末内で完結)。
    /// </summary>
    public class SkinScreen : ScreenBase
    {
        // プレビュー枠のサイズ (9:16、基準解像度系)
        static readonly Vector2 PreviewSize = new Vector2(280f, 498f);

        RectTransform _listContent;
        Text _pathText;
        readonly List<GameObject> _rows = new List<GameObject>();

        // ホームの表示トグル (時計/メッセージ/マスコット)。配置は現在のスキンに保存される
        Button _clockToggle;
        Button _tickerToggle;
        Button _mascotToggle;

        // スキン作成モーダル
        GameObject _createModal;
        InputField _skinNameInput;
        Text _bgPickText;
        Text _charPickText;
        Text _bgmPickText;
        Text _recordPickText;
        Button _recordDefaultButton;
        string _pickedRecord;
        /// <summary>0=そのまま (新規時はアプリ標準) / 1=画像を選択 / 2=アプリ標準の盤に戻す</summary>
        int _recordMode;
        InputField _talkInput;
        Text _createErrorText;
        Button _charNoneButton;
        Button _bgmNoneButton;
        Text _modalTitleText;
        Text _saveButtonLabel;
        string _pickedBg;
        string _pickedChar;
        string _pickedBgm;
        /// <summary>キャラの扱い: 0=ゆかりちゃんのまま 1=画像 2=キャラなし</summary>
        int _charMode;
        /// <summary>BGM の扱い: 0=現状維持 (既存 or デフォルト) 1=新しいファイル 2=BGMなし</summary>
        int _bgmMode;
        /// <summary>編集対象 (null なら新規作成)</summary>
        SkinDef _editingSkin;

        // テーマ色プリセット (null = 既定の紫)。基準色1色から派生色を自動生成する
        static readonly string[] ThemePresets =
        {
            null,       // 既定 (紫)
            "#E06BA8",  // ピンク
            "#D65C5C",  // 赤
            "#E08A3C",  // オレンジ
            "#4CAF6E",  // 緑
            "#3CAAB4",  // ティール
            "#4C7FD6",  // 青
            "#6B7280",  // グレー
        };
        readonly List<GameObject> _themeChecks = new List<GameObject>();
        string _pickedThemeHex;

        // 任意色ピッカー (H/S/V スライダーのサブモーダル)
        GameObject _colorModal;
        Slider _hueSlider;
        Slider _satSlider;
        Slider _valSlider;
        Image _colorPreview;
        Image _customChipImage;
        Text _customChipLabel;
        GameObject _customCheck;
        bool _suppressSliderEvent;

        // 背景調整プレビュー
        BackgroundView _previewView;
        GameObject _previewPlaceholder;
        Text _previewPlaceholderText;
        GameObject _adjustButtons;
        float _adjRotation;
        float _adjZoom = 1f;
        Vector2 _adjOffset;
        Canvas _canvas;

        /// <summary>プレビュー枠のドラッグを拾う小部品。</summary>
        class DragCatcher : MonoBehaviour, IDragHandler
        {
            public System.Action<Vector2> Dragged;

            public void OnDrag(PointerEventData eventData)
            {
                Dragged?.Invoke(eventData.delta);
            }
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "きせかえ");

            // 上部は文字の大きさ設定 (FontScale) に合わせて積み上げる (固定座標だと重なる)
            float y = 122f;

            const string captionLabel = "手持ちの画像や動画で、背景とキャラをカスタマイズできます";
            var caption = UiFactory.CreateText(transform, "Caption", captionLabel, 26,
                UiFactory.TextDark);
            float captionH = UiFactory.EstimateWrapLines(captionLabel, 26, 1040f)
                * UiFactory.LineHeight(26);
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -y);
            captionRect.sizeDelta = new Vector2(-40f, captionH);
            y += captionH + 8f;

            // 操作ボタン行 (スキンを作る / 取り込む / フォルダを開く)
            float rowH = Mathf.Max(80f, UiFactory.LineHeight(28) + 24f);
            var buttonBar = UiFactory.CreatePanel(transform, "Buttons");
            buttonBar.anchorMin = new Vector2(0f, 1f);
            buttonBar.anchorMax = new Vector2(1f, 1f);
            buttonBar.pivot = new Vector2(0.5f, 1f);
            buttonBar.anchoredPosition = new Vector2(0f, -y);
            buttonBar.offsetMin = new Vector2(20f, buttonBar.offsetMin.y);
            buttonBar.offsetMax = new Vector2(-20f, buttonBar.offsetMax.y);
            buttonBar.sizeDelta = new Vector2(buttonBar.sizeDelta.x, rowH);
            var buttonLayout = buttonBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childForceExpandHeight = true;
            buttonLayout.spacing = 10f;
            y += rowH + 14f;

            var createButton = UiFactory.CreateButton(buttonBar, "Create", "＋ 作る",
                UiFactory.Primary, Color.white, 28);
            UiFactory.FitLabel(createButton.GetComponentInChildren<Text>());
            createButton.onClick.AddListener(OpenCreateModal);

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
            var importButton = UiFactory.CreateButton(buttonBar, "Import", "取り込む (zip)",
                UiFactory.Primary, Color.white, 28);
            UiFactory.FitLabel(importButton.GetComponentInChildren<Text>());
            importButton.onClick.AddListener(ImportSkinFlow);
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var openButton = UiFactory.CreateButton(buttonBar, "OpenFolder", "フォルダを開く",
                UiFactory.PrimaryDark, Color.white, 28);
            UiFactory.FitLabel(openButton.GetComponentInChildren<Text>());
            openButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                SkinManager.EnsureRoot();
                Application.OpenURL("file:///" + SkinManager.SkinsRoot.Replace('\\', '/'));
            });
#endif

            // ホームの表示 (きせかえに統合したホーム設定)
            const string homeLabelText = "ホーム画面の表示 (ホームで長押しすると移動・拡縮できます)";
            var homeLabel = UiFactory.CreateText(transform, "HomeLabel", homeLabelText, 22,
                UiFactory.TextMuted, TextAnchor.MiddleLeft);
            float homeLabelH = UiFactory.EstimateWrapLines(homeLabelText, 22, 1030f)
                * UiFactory.LineHeight(22);
            var homeLabelRect = homeLabel.rectTransform;
            homeLabelRect.anchorMin = new Vector2(0f, 1f);
            homeLabelRect.anchorMax = new Vector2(1f, 1f);
            homeLabelRect.pivot = new Vector2(0.5f, 1f);
            homeLabelRect.anchoredPosition = new Vector2(0f, -y);
            homeLabelRect.offsetMin = new Vector2(24f, homeLabelRect.offsetMin.y);
            homeLabelRect.offsetMax = new Vector2(-24f, homeLabelRect.offsetMax.y);
            homeLabelRect.sizeDelta = new Vector2(homeLabelRect.sizeDelta.x, homeLabelH);
            y += homeLabelH + 4f;

            var homeBar = UiFactory.CreatePanel(transform, "HomeToggles");
            homeBar.anchorMin = new Vector2(0f, 1f);
            homeBar.anchorMax = new Vector2(1f, 1f);
            homeBar.pivot = new Vector2(0.5f, 1f);
            homeBar.anchoredPosition = new Vector2(0f, -y);
            homeBar.offsetMin = new Vector2(20f, homeBar.offsetMin.y);
            homeBar.offsetMax = new Vector2(-20f, homeBar.offsetMax.y);
            homeBar.sizeDelta = new Vector2(homeBar.sizeDelta.x, rowH);
            var homeLayoutGroup = homeBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            homeLayoutGroup.childForceExpandWidth = true;
            homeLayoutGroup.childForceExpandHeight = true;
            homeLayoutGroup.spacing = 10f;
            y += rowH + 12f;

            _clockToggle = AddHomeToggle(homeBar, "時計", HomeLayoutStore.Clock);
            _tickerToggle = AddHomeToggle(homeBar, "メッセージ", HomeLayoutStore.Ticker);
            _mascotToggle = AddHomeToggle(homeBar, "マスコット", HomeLayoutStore.Mascot);

            var resetButton = UiFactory.CreateButton(homeBar, "ResetLayout", "配置リセット",
                UiFactory.PrimaryDark, Color.white, 24);
            UiFactory.FitLabel(resetButton.GetComponentInChildren<Text>());
            resetButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Confirm);
                HomeLayoutStore.ResetAll(SkinManager.Current());
                UpdateHomeToggles();
                SetMessage("ホームの配置を初期状態に戻しました");
            });

            _pathText = UiFactory.CreateText(transform, "Path", "", 20, new Color(0.5f, 0.47f, 0.6f));
            float pathH = UiFactory.LineHeight(20) * 2f;
            var pathRect = _pathText.rectTransform;
            pathRect.anchorMin = new Vector2(0f, 1f);
            pathRect.anchorMax = new Vector2(1f, 1f);
            pathRect.pivot = new Vector2(0.5f, 1f);
            pathRect.anchoredPosition = new Vector2(0f, -y);
            pathRect.sizeDelta = new Vector2(-40f, pathH);
            y += pathH + 8f;

            var scrollRectT = UiFactory.CreateScrollList(transform, "SkinList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -y);

            BuildCreateModal();
            BuildColorModal();
        }

        Button AddHomeToggle(RectTransform bar, string label, string key)
        {
            var button = UiFactory.CreateButton(bar, key, label,
                new Color(0.75f, 0.73f, 0.80f), Color.white, 24);
            UiFactory.FitLabel(button.GetComponentInChildren<Text>());
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                var skin = SkinManager.Current();
                HomeLayoutStore.SetVisible(skin, key, !HomeLayoutStore.GetVisible(skin, key));
                UpdateHomeToggles();
            });
            return button;
        }

        /// <summary>ホーム表示トグルの見た目を現在のスキンの保存値に合わせる。</summary>
        void UpdateHomeToggles()
        {
            var skin = SkinManager.Current();
            UpdateHomeToggle(_clockToggle, "時計", HomeLayoutStore.GetVisible(skin, HomeLayoutStore.Clock));
            UpdateHomeToggle(_tickerToggle, "メッセージ", HomeLayoutStore.GetVisible(skin, HomeLayoutStore.Ticker));
            UpdateHomeToggle(_mascotToggle, "マスコット", HomeLayoutStore.GetVisible(skin, HomeLayoutStore.Mascot));
        }

        static void UpdateHomeToggle(Button button, string label, bool visible)
        {
            button.image.color = visible ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            button.GetComponentInChildren<Text>().text = visible ? "✓ " + label : label;
        }

        void BuildCreateModal()
        {
            _createModal = new GameObject("CreateModal");
            _createModal.transform.SetParent(transform, false);
            var overlayRect = _createModal.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _createModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);

            // 外枠は画面の高さに合わせ、中身 (card) は縦スクロールできるようにする
            // (項目が増えて 9:16 の画面や大きい文字サイズでは収まらないため)
            var cardFrame = UiFactory.CreatePanel(_createModal.transform, "Card", Color.white);
            cardFrame.anchorMin = new Vector2(0.5f, 0f);
            cardFrame.anchorMax = new Vector2(0.5f, 1f);
            cardFrame.pivot = new Vector2(0.5f, 0.5f);
            // 上 40px / 下はナビバー + 20px を空ける (下部の保存ボタンがナビバーに隠れないように)
            float topMargin = 40f;
            float bottomMargin = GlobalNav.BarHeight + 20f;
            cardFrame.anchoredPosition = new Vector2(0f, (bottomMargin - topMargin) / 2f);
            cardFrame.sizeDelta = new Vector2(940f, -(topMargin + bottomMargin));
            UiFactory.Roundify(cardFrame.GetComponent<Image>());
            UiFactory.AddShadow(cardFrame.gameObject, 8f);
            // カード内タップがオーバーレイに抜けないようにする
            var cardButton = cardFrame.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            var modalScroll = cardFrame.gameObject.AddComponent<ScrollRect>();
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(cardFrame, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            UiFactory.StretchFull(viewportRect);
            viewportRect.offsetMin = new Vector2(0f, 150f); // 下部の保存/やめるボタン行は固定
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = Color.white;
            UiFactory.Roundify(viewportImg);
            var viewportMask = viewportGo.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var card = contentGo.AddComponent<RectTransform>();
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            // 高さは行を積み上げた後に確定する (BuildCreateModal 末尾)

            modalScroll.content = card;
            modalScroll.viewport = viewportRect;
            modalScroll.horizontal = false;
            modalScroll.movementType = ScrollRect.MovementType.Elastic;
            modalScroll.scrollSensitivity = 30f;

            // 各行の縦位置・高さは文字の大きさ設定 (FontScale) に合わせて積み上げる
            float y = 22f;
            float labelH = UiFactory.LineHeight(26);
            float pickH = UiFactory.LineHeight(22);
            float btnH = Mathf.Max(80f, UiFactory.LineHeight(28) + 20f);
            float inputH = Mathf.Max(76f, UiFactory.LineHeight(30) + 20f);

            _modalTitleText = UiFactory.CreateText(card, "Title", "スキンを作る", 38, UiFactory.PrimaryDark);
            float titleH = UiFactory.LineHeight(38);
            SetCardRow(_modalTitleText.rectTransform, -y, titleH);
            y += titleH + 12f;

            var nameLabel = UiFactory.CreateText(card, "NameLabel", "スキンの名前", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(nameLabel.rectTransform, -y, labelH);
            y += labelH + 2f;
            _skinNameInput = UiFactory.CreateInputField(card, "NameInput", "例: 夏フェス", 30);
            SetCardRow(_skinNameInput.GetComponent<RectTransform>(), -y, inputH);
            y += inputH + 22f;

            var bgButton = UiFactory.CreateButton(card, "PickBg", "背景を選ぶ (画像 / 動画)",
                UiFactory.Primary, Color.white, 28);
            SetCardRow(bgButton.GetComponent<RectTransform>(), -y, btnH);
            UiFactory.FitLabelOneLine(bgButton.GetComponentInChildren<Text>());
            bgButton.onClick.AddListener(() => PickFile(true));
            y += btnH + 2f;
            _bgPickText = UiFactory.CreateText(card, "BgPicked", "未選択", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_bgPickText.rectTransform, -y, pickH);
            UiFactory.FitLabel(_bgPickText);
            y += pickH + 6f;

            float previewH = BuildPreviewArea(card, y);
            y += previewH + 24f;

            var charButton = UiFactory.CreateButton(card, "PickChar", "キャラ画像を選ぶ (任意)",
                UiFactory.Primary, Color.white, 28);
            var charRect = charButton.GetComponent<RectTransform>();
            charRect.anchorMin = charRect.anchorMax = new Vector2(0f, 1f);
            charRect.pivot = new Vector2(0f, 1f);
            charRect.anchoredPosition = new Vector2(50f, -y);
            charRect.sizeDelta = new Vector2(490f, btnH);
            UiFactory.FitLabelOneLine(charButton.GetComponentInChildren<Text>());
            charButton.onClick.AddListener(() => PickFile(false));

            _charNoneButton = UiFactory.CreateButton(card, "CharNone", "キャラなし",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 28);
            var noneRect = _charNoneButton.GetComponent<RectTransform>();
            noneRect.anchorMin = noneRect.anchorMax = new Vector2(1f, 1f);
            noneRect.pivot = new Vector2(1f, 1f);
            noneRect.anchoredPosition = new Vector2(-50f, -y);
            noneRect.sizeDelta = new Vector2(320f, btnH);
            UiFactory.FitLabelOneLine(_charNoneButton.GetComponentInChildren<Text>());
            _charNoneButton.onClick.AddListener(ToggleCharNone);
            y += btnH + 2f;

            _charPickText = UiFactory.CreateText(card, "CharPicked", "未選択 (ゆかりちゃんのまま)", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_charPickText.rectTransform, -y, pickH);
            UiFactory.FitLabel(_charPickText);
            y += pickH + 18f;

            // BGM (任意)。登録するとこのスキン適用中はデフォルト BGM の代わりに流れる
            var bgmButton = UiFactory.CreateButton(card, "PickBgm", "BGM を選ぶ (任意)",
                UiFactory.Primary, Color.white, 28);
            var bgmRect = bgmButton.GetComponent<RectTransform>();
            bgmRect.anchorMin = bgmRect.anchorMax = new Vector2(0f, 1f);
            bgmRect.pivot = new Vector2(0f, 1f);
            bgmRect.anchoredPosition = new Vector2(50f, -y);
            bgmRect.sizeDelta = new Vector2(490f, btnH);
            UiFactory.FitLabelOneLine(bgmButton.GetComponentInChildren<Text>());
            bgmButton.onClick.AddListener(PickBgmFile);

            _bgmNoneButton = UiFactory.CreateButton(card, "BgmNone", "BGMなし",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 28);
            var bgmNoneRect = _bgmNoneButton.GetComponent<RectTransform>();
            bgmNoneRect.anchorMin = bgmNoneRect.anchorMax = new Vector2(1f, 1f);
            bgmNoneRect.pivot = new Vector2(1f, 1f);
            bgmNoneRect.anchoredPosition = new Vector2(-50f, -y);
            bgmNoneRect.sizeDelta = new Vector2(320f, btnH);
            UiFactory.FitLabelOneLine(_bgmNoneButton.GetComponentInChildren<Text>());
            _bgmNoneButton.onClick.AddListener(ToggleBgmNone);
            y += btnH + 2f;

            _bgmPickText = UiFactory.CreateText(card, "BgmPicked", "未選択 (アプリのBGM)", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_bgmPickText.rectTransform, -y, pickH);
            UiFactory.FitLabel(_bgmPickText);
            y += pickH + 18f;

            // リモコンのレコード盤 (任意)。円形の透過 PNG を想定
            var recordButton = UiFactory.CreateButton(card, "PickRecord", "レコード盤を選ぶ (任意)",
                UiFactory.Primary, Color.white, 28);
            var recordRect = recordButton.GetComponent<RectTransform>();
            recordRect.anchorMin = recordRect.anchorMax = new Vector2(0f, 1f);
            recordRect.pivot = new Vector2(0f, 1f);
            recordRect.anchoredPosition = new Vector2(50f, -y);
            recordRect.sizeDelta = new Vector2(490f, btnH);
            UiFactory.FitLabelOneLine(recordButton.GetComponentInChildren<Text>());
            recordButton.onClick.AddListener(PickRecordFile);

            _recordDefaultButton = UiFactory.CreateButton(card, "RecordDefault", "標準の盤",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 28);
            var recordDefaultRect = _recordDefaultButton.GetComponent<RectTransform>();
            recordDefaultRect.anchorMin = recordDefaultRect.anchorMax = new Vector2(1f, 1f);
            recordDefaultRect.pivot = new Vector2(1f, 1f);
            recordDefaultRect.anchoredPosition = new Vector2(-50f, -y);
            recordDefaultRect.sizeDelta = new Vector2(320f, btnH);
            UiFactory.FitLabelOneLine(_recordDefaultButton.GetComponentInChildren<Text>());
            _recordDefaultButton.onClick.AddListener(ToggleRecordDefault);
            y += btnH + 2f;

            _recordPickText = UiFactory.CreateText(card, "RecordPicked", "未選択 (アプリ標準の盤)", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_recordPickText.rectTransform, -y, pickH);
            UiFactory.FitLabel(_recordPickText);
            y += pickH + 18f;

            // テーマ色 (ボタンや文字の色)。基準色から派生色を自動生成する
            var themeLabel = UiFactory.CreateText(card, "ThemeLabel", "テーマ色 (ボタンや文字の色)", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(themeLabel.rectTransform, -y, labelH);
            y += labelH + 6f;
            BuildThemeChips(card, y);
            y += 72f + 12f;

            // キャラのセリフ (1行に1つ。タップでランダム表示)
            const string talkLabelText = "キャラのセリフ (1行に1つ、タップでランダム表示)";
            var talkLabel = UiFactory.CreateText(card, "TalkLabel", talkLabelText, 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            float talkLabelH = UiFactory.EstimateWrapLines(talkLabelText, 26, 840f)
                * UiFactory.LineHeight(26);
            SetCardRow(talkLabel.rectTransform, -y, talkLabelH);
            y += talkLabelH + 6f;
            _talkInput = UiFactory.CreateInputField(card, "TalkInput",
                "例: うたっていこ〜♪ (空ならアプリ標準のセリフ)", 26);
            _talkInput.lineType = InputField.LineType.MultiLineNewline;
            ((Text)_talkInput.textComponent).alignment = TextAnchor.UpperLeft;
            ((Text)_talkInput.placeholder).alignment = TextAnchor.UpperLeft;
            float talkInputH = Mathf.Max(130f, UiFactory.LineHeight(26) * 2f + 30f);
            SetCardRow(_talkInput.GetComponent<RectTransform>(), -y, talkInputH);
            y += talkInputH + 14f;

            var hint = UiFactory.CreateText(card, "Hint",
                "※ 選んだファイルはアプリ内にコピーされます", 20, new Color(0.5f, 0.47f, 0.6f));
            float hintH = UiFactory.LineHeight(20);
            SetCardRow(hint.rectTransform, -y, hintH);
            y += hintH + 6f;

            _createErrorText = UiFactory.CreateText(card, "Error", "", 24, UiFactory.Danger);
            float errorH = UiFactory.LineHeight(24);
            SetCardRow(_createErrorText.rectTransform, -y, errorH);
            y += errorH;

            card.sizeDelta = new Vector2(0f, y + 24f);

            var saveButton = UiFactory.CreateButton(cardFrame, "Save", "作成する", UiFactory.Primary, Color.white, 34);
            _saveButtonLabel = saveButton.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_saveButtonLabel);
            var saveRect = saveButton.GetComponent<RectTransform>();
            saveRect.anchorMin = saveRect.anchorMax = new Vector2(0.5f, 0f);
            saveRect.pivot = new Vector2(0.5f, 0f);
            saveRect.anchoredPosition = new Vector2(-190f, 36f);
            saveRect.sizeDelta = new Vector2(340f, 92f);
            saveButton.onClick.AddListener(CreateSkin);

            var cancelButton = UiFactory.CreateSoftButton(cardFrame, "Cancel", "やめる", 34);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(190f, 36f);
            cancelRect.sizeDelta = new Vector2(340f, 92f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _createModal.SetActive(false);
            });

            _createModal.SetActive(false);
        }

        /// <summary>テーマ色の選択チップ列 (丸い色見本。選択中は ✓)。高さは 72px 固定。</summary>
        void BuildThemeChips(RectTransform card, float y)
        {
            _themeChecks.Clear();
            var row = UiFactory.CreatePanel(card, "ThemeChips");
            SetCardRow(row, -y, 72f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 20f;

            foreach (var preset in ThemePresets)
            {
                string hex = preset;
                Color color = new Color(0.48f, 0.36f, 0.84f); // 既定 (紫)
                if (hex != null)
                {
                    ColorUtility.TryParseHtmlString(hex, out color);
                }
                var chipGo = new GameObject("Chip");
                chipGo.transform.SetParent(row, false);
                var img = chipGo.AddComponent<Image>();
                img.sprite = UiFactory.RoundedSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
                img.color = color;
                chipGo.GetComponent<RectTransform>().sizeDelta = new Vector2(72f, 72f);
                var button = chipGo.AddComponent<Button>();
                chipGo.AddComponent<PressEffect>();
                button.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    _pickedThemeHex = hex;
                    UpdateThemeChips();
                });
                var check = UiFactory.CreateText(chipGo.transform, "Check", "✓", 36, Color.white);
                UiFactory.StretchFull(check.rectTransform);
                check.gameObject.SetActive(false);
                _themeChecks.Add(check.gameObject);
            }

            // カスタム (任意色) チップ: タップでカラーピッカーを開く
            var customGo = new GameObject("Custom");
            customGo.transform.SetParent(row, false);
            _customChipImage = customGo.AddComponent<Image>();
            _customChipImage.sprite = UiFactory.RoundedSprite;
            _customChipImage.type = Image.Type.Sliced;
            _customChipImage.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            _customChipImage.color = Color.white;
            customGo.GetComponent<RectTransform>().sizeDelta = new Vector2(72f, 72f);
            var customButton = customGo.AddComponent<Button>();
            customGo.AddComponent<PressEffect>();
            customButton.onClick.AddListener(OpenColorModal);
            _customChipLabel = UiFactory.CreateText(customGo.transform, "Plus", "＋", 40, UiFactory.PrimaryDark);
            UiFactory.StretchFull(_customChipLabel.rectTransform);
            var customCheck = UiFactory.CreateText(customGo.transform, "Check", "✓", 36, Color.white);
            UiFactory.StretchFull(customCheck.rectTransform);
            _customCheck = customCheck.gameObject;
            _customCheck.SetActive(false);
        }

        void UpdateThemeChips()
        {
            bool isPreset = false;
            for (int i = 0; i < _themeChecks.Count; i++)
            {
                bool selected = ThemePresets[i] == _pickedThemeHex;
                isPreset |= selected;
                _themeChecks[i].SetActive(selected);
            }
            // プリセットに無い色はカスタムチップに色見本として表示する
            var customColor = Color.white;
            bool custom = !isPreset && !string.IsNullOrEmpty(_pickedThemeHex)
                && ColorUtility.TryParseHtmlString(_pickedThemeHex, out customColor);
            _customChipImage.color = custom ? customColor : Color.white;
            _customChipLabel.gameObject.SetActive(!custom);
            _customCheck.SetActive(custom);
        }

        // ---- 任意色ピッカー ----

        void BuildColorModal()
        {
            _colorModal = new GameObject("ColorModal");
            _colorModal.transform.SetParent(transform, false);
            UiFactory.StretchFull(_colorModal.AddComponent<RectTransform>());
            var overlay = _colorModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);
            var overlayButton = _colorModal.AddComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(() => _colorModal.SetActive(false));

            var card = UiFactory.CreatePanel(_colorModal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880f, 640f);
            UiFactory.Roundify(card.GetComponent<Image>());
            UiFactory.AddShadow(card.gameObject, 6f);
            // カード内タップがオーバーレイの「閉じる」に抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            var title = UiFactory.CreateText(card, "Title", "テーマ色をつくる", 34, UiFactory.PrimaryDark);
            SetCardRow(title.rectTransform, -28f, 54f);

            // プレビュー (中央の丸)
            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(card, false);
            _colorPreview = previewGo.AddComponent<Image>();
            _colorPreview.sprite = UiFactory.RoundedSprite;
            _colorPreview.type = Image.Type.Sliced;
            _colorPreview.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            _colorPreview.raycastTarget = false;
            UiFactory.AddShadow(previewGo, 3f);
            var previewRect = previewGo.GetComponent<RectTransform>();
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 1f);
            previewRect.pivot = new Vector2(0.5f, 1f);
            previewRect.anchoredPosition = new Vector2(0f, -94f);
            previewRect.sizeDelta = new Vector2(110f, 110f);

            _hueSlider = AddColorSlider(card, "色あい", -240f, 360f);
            _satSlider = AddColorSlider(card, "あざやかさ", -324f, 100f);
            _valSlider = AddColorSlider(card, "明るさ", -408f, 100f);

            var okButton = UiFactory.CreateButton(card, "Ok", "この色にする", UiFactory.Primary, Color.white, 30);
            var okRect = okButton.GetComponent<RectTransform>();
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 0f);
            okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(-180f, 36f);
            okRect.sizeDelta = new Vector2(320f, 92f);
            okButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Confirm);
                _pickedThemeHex = "#" + ColorUtility.ToHtmlStringRGB(CurrentSliderColor());
                UpdateThemeChips();
                _colorModal.SetActive(false);
            });

            var cancelButton = UiFactory.CreateSoftButton(card, "Cancel", "やめる", 30);
            var colorCancelRect = cancelButton.GetComponent<RectTransform>();
            colorCancelRect.anchorMin = colorCancelRect.anchorMax = new Vector2(0.5f, 0f);
            colorCancelRect.pivot = new Vector2(0.5f, 0f);
            colorCancelRect.anchoredPosition = new Vector2(180f, 36f);
            colorCancelRect.sizeDelta = new Vector2(320f, 92f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _colorModal.SetActive(false);
            });

            _colorModal.SetActive(false);
        }

        /// <summary>「ラベル + スライダー」の1行を作る。</summary>
        Slider AddColorSlider(RectTransform card, string label, float y, float maxValue)
        {
            var labelText = UiFactory.CreateText(card, label, label, 26, UiFactory.TextDark, TextAnchor.MiddleLeft);
            SetCardRow(labelText.rectTransform, y, 64f);
            labelText.rectTransform.offsetMax = new Vector2(-620f, labelText.rectTransform.offsetMax.y);

            var sliderGo = new GameObject(label + "Slider");
            sliderGo.transform.SetParent(card, false);
            var sliderRect = sliderGo.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 1f);
            sliderRect.anchorMax = new Vector2(1f, 1f);
            sliderRect.pivot = new Vector2(0.5f, 1f);
            sliderRect.anchoredPosition = new Vector2(0f, y);
            sliderRect.offsetMin = new Vector2(280f, sliderRect.offsetMin.y);
            sliderRect.offsetMax = new Vector2(-60f, sliderRect.offsetMax.y);
            sliderRect.sizeDelta = new Vector2(sliderRect.sizeDelta.x, 64f);

            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = maxValue;

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = UiFactory.RoundedSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.pixelsPerUnitMultiplier = 2.5f; // 細いバーでも角丸が潰れないように
            bgImg.color = new Color(0.85f, 0.83f, 0.90f);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(1f, 0.5f);
            bgRect.sizeDelta = new Vector2(0f, 16f);

            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            UiFactory.StretchFull(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(22f, 0f);
            handleAreaRect.offsetMax = new Vector2(-22f, 0f);
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = UiFactory.RoundedSprite;
            handleImg.type = Image.Type.Sliced;
            handleImg.pixelsPerUnitMultiplier = 0.55f; // ほぼ円形に
            handleImg.color = Color.white;
            UiFactory.AddShadow(handleGo, 2f);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(44f, 44f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener(_ => OnColorSliderChanged());
            return slider;
        }

        Color CurrentSliderColor()
        {
            return Color.HSVToRGB(_hueSlider.value / 360f, _satSlider.value / 100f, _valSlider.value / 100f);
        }

        void OnColorSliderChanged()
        {
            if (_suppressSliderEvent)
            {
                return;
            }
            _colorPreview.color = CurrentSliderColor();
        }

        void OpenColorModal()
        {
            Se.Play(Se.Tap);
            // いま選んでいる色 (無ければ既定の紫) から始める
            var initial = new Color(0.48f, 0.36f, 0.84f);
            if (!string.IsNullOrEmpty(_pickedThemeHex))
            {
                ColorUtility.TryParseHtmlString(_pickedThemeHex, out initial);
            }
            Color.RGBToHSV(initial, out float h, out float s, out float v);
            _suppressSliderEvent = true;
            _hueSlider.value = h * 360f;
            _satSlider.value = s * 100f;
            _valSlider.value = v * 100f;
            _suppressSliderEvent = false;
            _colorPreview.color = CurrentSliderColor();
            _colorModal.SetActive(true);
        }

        /// <summary>テーマ色が変わっていれば UI 全体を作り直す (戻り値 true)。以後 UI 参照は無効。</summary>
        bool ApplyThemeAndRebuild()
        {
            if (!YukariTheme.ApplyFromSkin(SkinManager.Current()))
            {
                return false;
            }
            if (GlobalNav.Instance != null)
            {
                GlobalNav.Instance.Rebuild();
            }
            Manager.RebuildAll(); // この画面も作り直され OnShow まで呼ばれる
            return true;
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

        /// <summary>背景調整プレビュー (枠 + ドラッグ + 回転/ズームボタン)。</summary>
        /// <summary>背景プレビューと調整ボタン列。使った高さを返す。</summary>
        float BuildPreviewArea(RectTransform card, float y)
        {
            // プレビュー枠 (左寄せ、9:16)
            var frame = UiFactory.CreatePanel(card, "PreviewFrame", new Color(0.15f, 0.13f, 0.2f));
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(60f, -y);
            frame.sizeDelta = PreviewSize;

            _previewView = BackgroundView.Create(frame, "Preview");

            // ドラッグで位置調整
            var catcher = frame.gameObject.AddComponent<DragCatcher>();
            catcher.Dragged += OnPreviewDragged;

            // 未選択時のプレースホルダ
            _previewPlaceholder = new GameObject("Placeholder");
            _previewPlaceholder.transform.SetParent(frame, false);
            var phRect = _previewPlaceholder.AddComponent<RectTransform>();
            UiFactory.StretchFull(phRect);
            _previewPlaceholderText = UiFactory.CreateText(_previewPlaceholder.transform, "Text",
                "背景を選ぶと\nプレビューが出ます", 24, new Color(0.8f, 0.78f, 0.88f));
            UiFactory.StretchFull(_previewPlaceholderText.rectTransform);

            // 調整ボタン列 (プレビューの右)。文字サイズ設定によってはプレビューより縦に長くなる
            float adjBtnH = Mathf.Max(78f, UiFactory.LineHeight(26) + 16f);
            float dragHintH = UiFactory.LineHeight(22) * 2f;
            float buttonsH = adjBtnH * 4f + 12f * 4f + dragHintH;
            var buttons = UiFactory.CreatePanel(card, "AdjustButtons");
            buttons.anchorMin = buttons.anchorMax = new Vector2(0f, 1f);
            buttons.pivot = new Vector2(0f, 1f);
            buttons.anchoredPosition = new Vector2(380f, -y);
            buttons.sizeDelta = new Vector2(500f, buttonsH);
            var layout = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 12f;

            AddAdjustButton(buttons, "回転 (90°)", () =>
            {
                _adjRotation = Mathf.Repeat(_adjRotation + 90f, 360f);
                ApplyAdjust();
            });
            AddAdjustButton(buttons, "拡大 ＋", () =>
            {
                _adjZoom = Mathf.Min(_adjZoom + 0.1f, 4f);
                ApplyAdjust();
            });
            AddAdjustButton(buttons, "縮小 −", () =>
            {
                _adjZoom = Mathf.Max(_adjZoom - 0.1f, 1f);
                ApplyAdjust();
            });
            AddAdjustButton(buttons, "リセット", () =>
            {
                _adjRotation = 0f;
                _adjZoom = 1f;
                _adjOffset = Vector2.zero;
                ApplyAdjust();
            });
            var dragHint = UiFactory.CreateText(buttons, "DragHint",
                "プレビューをドラッグ\nすると位置を動かせます", 22, new Color(0.5f, 0.47f, 0.6f));
            var dragHintLe = dragHint.gameObject.AddComponent<LayoutElement>();
            dragHintLe.preferredHeight = dragHintH;

            _adjustButtons = buttons.gameObject;
            _adjustButtons.SetActive(false);
            return Mathf.Max(PreviewSize.y, buttonsH);
        }

        void AddAdjustButton(RectTransform parent, string label, System.Action onClick)
        {
            var button = UiFactory.CreateButton(parent, label, label, UiFactory.PrimaryDark, Color.white, 26);
            UiFactory.FitLabelOneLine(button.GetComponentInChildren<Text>());
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = Mathf.Max(78f, UiFactory.LineHeight(26) + 16f);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onClick();
            });
        }

        void OnPreviewDragged(Vector2 screenDelta)
        {
            if (_previewView == null || !_adjustButtons.activeSelf)
            {
                return;
            }
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _adjOffset += new Vector2(
                screenDelta.x / scale / PreviewSize.x,
                screenDelta.y / scale / PreviewSize.y);
            // cover からの移動量として妥当な範囲にクランプ
            _adjOffset.x = Mathf.Clamp(_adjOffset.x, -2f, 2f);
            _adjOffset.y = Mathf.Clamp(_adjOffset.y, -2f, 2f);
            ApplyAdjust();
        }

        void ApplyAdjust()
        {
            _previewView.SetAdjust(_adjRotation, _adjZoom, _adjOffset);
        }

        static Texture2D LoadTextureFromFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes))
                {
                    Destroy(tex);
                    return null;
                }
                return tex;
            }
            catch
            {
                return null;
            }
        }

        static bool IsVideoFile(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".mov";
        }

        /// <summary>zip を選んでスキンを取り込む。</summary>
        void ImportSkinFlow()
        {
            Se.Play(Se.Tap);
#if UNITY_EDITOR
            string picked = UnityEditor.EditorUtility.OpenFilePanel("スキン zip を選ぶ", "", "zip");
            OnImportPicked(string.IsNullOrEmpty(picked) ? null : picked);
#else
            NativeFilePicker.PickFile(OnImportPicked, new string[] { "application/zip" });
#endif
        }

        void OnImportPicked(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return; // キャンセル
            }
            string id = SkinManager.ImportSkin(path);
            if (id == null)
            {
                SetMessage("取り込めませんでした (skin.json 入りの zip を選んでください)", true);
                Se.Play(Se.Error);
                return;
            }
            AppConfig.SkinId = id;
            Bgm.RefreshForCurrentSkin();
            SetMessage("スキンを取り込みました");
            Se.Play(Se.Confirm);
            if (ApplyThemeAndRebuild())
            {
                return;
            }
            Rebuild();
        }

        /// <summary>スキンを zip に書き出して共有 (Android) またはフォルダ表示 (Windows/エディタ) する。</summary>
        void ExportSkinFlow(SkinDef skin)
        {
            Se.Play(Se.Tap);
            string zipPath = SkinManager.ExportSkin(skin);
            if (zipPath == null)
            {
                SetMessage("書き出しに失敗しました", true);
                Se.Play(Se.Error);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(zipPath);
            SetMessage("書き出しました: " + zipPath);
#elif UNITY_STANDALONE_WIN
            Application.OpenURL("file:///" + Path.GetDirectoryName(zipPath).Replace('\\', '/'));
            SetMessage("書き出しました: " + zipPath);
#else
            NativeFilePicker.ExportFile(zipPath, success =>
                SetMessage(success ? "書き出しました" : "書き出しをキャンセルしました"));
#endif
            Se.Play(Se.Confirm);
        }

        void SetMessage(string message, bool isError = false)
        {
            UiFactory.ShowToast(message, isError);
        }

        /// <summary>端末のファイルピッカーで BGM (音声ファイル) を選ぶ。</summary>
        void PickBgmFile()
        {
            Se.Play(Se.Tap);
#if UNITY_EDITOR
            string picked = UnityEditor.EditorUtility.OpenFilePanel("BGM を選ぶ", "", "mp3,ogg,wav");
            OnBgmPicked(string.IsNullOrEmpty(picked) ? null : picked);
#else
            NativeFilePicker.PickFile(OnBgmPicked, new string[] { "audio/*" });
#endif
        }

        void OnBgmPicked(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return; // キャンセル
            }
            // m4a/aac は Unity のランタイム読み込みが非対応 (Android のピッカーでは選べてしまう)
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".m4a" || ext == ".aac")
            {
                _createErrorText.text = "m4a は使えません (mp3 / ogg / wav に変換してください)";
                Se.Play(Se.Error);
                return;
            }
            _createErrorText.text = "";
            _pickedBgm = path;
            _bgmMode = 1;
            UpdateBgmUi();
        }

        /// <summary>「BGMなし」のトグル (スキン BGM を外してアプリの BGM に戻す)。もう一度押すと解除。</summary>
        void ToggleBgmNone()
        {
            Se.Play(Se.Tap);
            if (_bgmMode == 2)
            {
                _bgmMode = _pickedBgm != null ? 1 : 0;
            }
            else
            {
                _bgmMode = 2;
            }
            UpdateBgmUi();
        }

        void UpdateBgmUi()
        {
            _bgmNoneButton.image.color = _bgmMode == 2 ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            switch (_bgmMode)
            {
                case 2:
                    _bgmPickText.text = "スキンの BGM を使いません (アプリのBGM)";
                    break;
                case 1:
                    _bgmPickText.text = "選択済み: " + Path.GetFileName(_pickedBgm);
                    break;
                default:
                    bool hasExisting = _editingSkin != null && _editingSkin.Bgm != null
                        && !string.IsNullOrEmpty(_editingSkin.Bgm.File);
                    _bgmPickText.text = hasExisting
                        ? "現在の BGM: " + _editingSkin.Bgm.File
                        : "未選択 (アプリのBGM)";
                    break;
            }
        }

        /// <summary>端末のファイルピッカーでレコード盤の画像を選ぶ。</summary>
        void PickRecordFile()
        {
            Se.Play(Se.Tap);
#if UNITY_EDITOR
            string picked = UnityEditor.EditorUtility.OpenFilePanel("レコード盤の画像を選ぶ", "", "png,jpg,jpeg");
            OnRecordPicked(string.IsNullOrEmpty(picked) ? null : picked);
#else
            NativeFilePicker.PickFile(OnRecordPicked, new string[] { "image/*" });
#endif
        }

        void OnRecordPicked(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return; // キャンセル
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            {
                _createErrorText.text = "画像ファイル (png / jpg) を選んでください";
                Se.Play(Se.Error);
                return;
            }
            _createErrorText.text = "";
            _pickedRecord = path;
            _recordMode = 1;
            UpdateRecordUi();
        }

        /// <summary>「標準の盤」のトグル (スキンのレコード盤を外してアプリ標準に戻す)。もう一度押すと解除。</summary>
        void ToggleRecordDefault()
        {
            Se.Play(Se.Tap);
            if (_recordMode == 2)
            {
                _recordMode = _pickedRecord != null ? 1 : 0;
            }
            else
            {
                _recordMode = 2;
            }
            UpdateRecordUi();
        }

        void UpdateRecordUi()
        {
            _recordDefaultButton.image.color = _recordMode == 2 ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            switch (_recordMode)
            {
                case 2:
                    _recordPickText.text = "アプリ標準のレコード盤を使います";
                    break;
                case 1:
                    _recordPickText.text = "選択済み: " + Path.GetFileName(_pickedRecord);
                    break;
                default:
                    bool hasExisting = _editingSkin != null && _editingSkin.Record != null
                        && !string.IsNullOrEmpty(_editingSkin.Record.File);
                    _recordPickText.text = hasExisting
                        ? "現在のレコード盤: " + _editingSkin.Record.File
                        : "未選択 (アプリ標準の盤)";
                    break;
            }
        }

        /// <summary>「キャラなし」のトグル。もう一度押すと「ゆかりちゃんのまま」に戻る。</summary>
        void ToggleCharNone()
        {
            Se.Play(Se.Tap);
            if (_charMode == 2)
            {
                _charMode = _pickedChar != null ? 1 : 0;
            }
            else
            {
                _charMode = 2;
            }
            UpdateCharUi();
        }

        void UpdateCharUi()
        {
            _charNoneButton.image.color = _charMode == 2 ? UiFactory.Primary : new Color(0.75f, 0.73f, 0.80f);
            switch (_charMode)
            {
                case 2:
                    _charPickText.text = "キャラを表示しません";
                    break;
                case 1:
                    _charPickText.text = _pickedChar != null
                        ? "選択済み: " + Path.GetFileName(_pickedChar)
                        : "現在のキャラ画像を使用";
                    break;
                default:
                    _charPickText.text = "未選択 (ゆかりちゃんのまま)";
                    break;
            }
        }

        void OpenCreateModal()
        {
            Se.Play(Se.Tap);
            _editingSkin = null;
            _modalTitleText.text = "スキンを作る";
            _saveButtonLabel.text = "作成する";
            _skinNameInput.text = "";
            _pickedBg = null;
            _pickedChar = null;
            _charMode = 0;
            UpdateCharUi();
            _pickedBgm = null;
            _bgmMode = 0;
            UpdateBgmUi();
            _pickedRecord = null;
            _recordMode = 0;
            UpdateRecordUi();
            _talkInput.text = "";
            _pickedThemeHex = null;
            UpdateThemeChips();
            _bgPickText.text = "未選択";
            _createErrorText.text = "";
            _adjRotation = 0f;
            _adjZoom = 1f;
            _adjOffset = Vector2.zero;
            _previewView.SetTexture(null, 9f / 16f);
            _previewPlaceholder.SetActive(true);
            _previewPlaceholderText.text = "背景を選ぶと\nプレビューが出ます";
            _adjustButtons.SetActive(false);
            _createModal.SetActive(true);
        }

        /// <summary>既存スキンを編集モードでモーダルに読み込む。</summary>
        void OpenEditModal(SkinDef skin)
        {
            Se.Play(Se.Tap);
            _editingSkin = skin;
            _modalTitleText.text = "スキンを編集";
            _saveButtonLabel.text = "保存する";
            _skinNameInput.text = skin.Name;
            _pickedBg = null;
            _pickedChar = null;
            _pickedBgm = null;
            _bgmMode = 0; // 既存 BGM は維持
            _pickedRecord = null;
            _recordMode = 0; // 既存のレコード盤は維持
            _talkInput.text = skin.Talk != null ? string.Join("\n", skin.Talk) : "";
            _pickedThemeHex = skin.Theme != null ? skin.Theme.Primary : null;
            UpdateThemeChips();
            _createErrorText.text = "";

            // キャラ設定の復元
            if (skin.Character == null)
            {
                _charMode = 0;
            }
            else if (skin.Character.Type == "none")
            {
                _charMode = 2;
            }
            else
            {
                _charMode = 1;
            }
            UpdateCharUi();
            UpdateBgmUi();
            UpdateRecordUi();

            // 背景の復元 (画像はプレビュー + 調整値も復元)
            _adjRotation = 0f;
            _adjZoom = 1f;
            _adjOffset = Vector2.zero;
            _previewView.SetTexture(null, 9f / 16f);
            _previewPlaceholder.SetActive(true);
            _adjustButtons.SetActive(false);
            if (skin.Background != null && !string.IsNullOrEmpty(skin.Background.File))
            {
                _bgPickText.text = "現在の背景: " + skin.Background.File;
                _adjRotation = skin.Background.Rotation;
                _adjZoom = skin.Background.Zoom <= 0f ? 1f : skin.Background.Zoom;
                _adjOffset = new Vector2(skin.Background.OffsetX, skin.Background.OffsetY);
                if (skin.Background.Type == "image")
                {
                    var tex = SkinManager.LoadTexture(skin, skin.Background.File);
                    if (tex != null)
                    {
                        _previewView.SetTexture(tex, (float)tex.width / tex.height);
                        _previewPlaceholder.SetActive(false);
                        _adjustButtons.SetActive(true);
                        ApplyAdjust();
                    }
                }
                else
                {
                    _previewPlaceholderText.text = "動画はプレビューできません\n(画面に合わせて自動調整)";
                }
            }
            else
            {
                _bgPickText.text = "未選択";
                _previewPlaceholderText.text = "背景を選ぶと\nプレビューが出ます";
            }
            _createModal.SetActive(true);
        }

        /// <summary>端末のファイルピッカーで画像/動画を選ぶ。</summary>
        void PickFile(bool forBackground)
        {
            Se.Play(Se.Tap);
#if UNITY_EDITOR
            string extensions = forBackground ? "png,jpg,jpeg,mp4,webm" : "png,jpg,jpeg";
            string picked = UnityEditor.EditorUtility.OpenFilePanel(
                forBackground ? "背景を選ぶ" : "キャラ画像を選ぶ", "", extensions);
            OnFilePicked(forBackground, string.IsNullOrEmpty(picked) ? null : picked);
#else
            string[] fileTypes = forBackground
                ? new string[] { "image/*", "video/*" }
                : new string[] { "image/*" };
            NativeFilePicker.PickFile(path => OnFilePicked(forBackground, path), fileTypes);
#endif
        }

        void OnFilePicked(bool forBackground, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return; // キャンセル
            }
            if (forBackground)
            {
                _pickedBg = path;
                _bgPickText.text = "選択済み: " + Path.GetFileName(path);
                _adjRotation = 0f;
                _adjZoom = 1f;
                _adjOffset = Vector2.zero;
                if (IsVideoFile(path))
                {
                    // 動画はプレビュー非対応 (画面に合わせて自動クロップされる)
                    _previewView.SetTexture(null, 9f / 16f);
                    _previewPlaceholder.SetActive(true);
                    _previewPlaceholderText.text = "動画はプレビューできません\n(画面に合わせて自動調整)";
                    _adjustButtons.SetActive(false);
                }
                else
                {
                    var tex = LoadTextureFromFile(path);
                    if (tex != null)
                    {
                        _previewView.SetTexture(tex, (float)tex.width / tex.height);
                        _previewPlaceholder.SetActive(false);
                        _adjustButtons.SetActive(true);
                        ApplyAdjust();
                    }
                }
            }
            else
            {
                _pickedChar = path;
                _charMode = 1;
                UpdateCharUi();
            }
        }

        /// <summary>セリフ入力欄を1行=1セリフのリストにする (空行は除く)。</summary>
        List<string> ParseTalkLines()
        {
            var lines = new List<string>();
            foreach (var line in (_talkInput.text ?? "").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed != "")
                {
                    lines.Add(trimmed);
                }
            }
            return lines;
        }

        void CreateSkin()
        {
            string name = (_skinNameInput.text ?? "").Trim();
            if (name == "")
            {
                _createErrorText.text = "スキンの名前を入力してください";
                Se.Play(Se.Error);
                return;
            }
            var talkLines = ParseTalkLines();

            if (_editingSkin != null)
            {
                // 既存スキンの更新
                if (!SkinManager.UpdateSkin(_editingSkin, name, _pickedBg,
                        _charMode == 1 ? _pickedChar : null,
                        _adjRotation, _adjZoom, _adjOffset, _charMode,
                        _bgmMode == 1 ? _pickedBgm : null, _bgmMode == 2,
                        _pickedThemeHex,
                        _recordMode == 1 ? _pickedRecord : null, _recordMode == 2,
                        talkLines))
                {
                    _createErrorText.text = "スキンの保存に失敗しました";
                    Se.Play(Se.Error);
                    return;
                }
                AppConfig.SkinId = _editingSkin.Id;
            }
            else
            {
                // 新規作成
                if (_pickedBg == null && _charMode == 0 && _bgmMode != 1 && _recordMode != 1
                    && talkLines.Count == 0)
                {
                    _createErrorText.text = "背景を選ぶか、キャラ・BGM・レコード盤などの設定を変えてください";
                    Se.Play(Se.Error);
                    return;
                }
                string id = SkinManager.CreateSkin(name, _pickedBg,
                    _charMode == 1 ? _pickedChar : null,
                    _adjRotation, _adjZoom, _adjOffset,
                    _charMode == 2,
                    _bgmMode == 1 ? _pickedBgm : null,
                    _pickedThemeHex,
                    _recordMode == 1 ? _pickedRecord : null,
                    talkLines);
                if (id == null)
                {
                    _createErrorText.text = "スキンの作成に失敗しました";
                    Se.Play(Se.Error);
                    return;
                }
                AppConfig.SkinId = id;
            }
            SkinManager.BumpRevision();
            Bgm.RefreshForCurrentSkin();
            Se.Play(Se.Confirm);
            _createModal.SetActive(false);
            if (ApplyThemeAndRebuild())
            {
                return;
            }
            Rebuild();
        }

        public override void OnShow()
        {
            _createModal.SetActive(false);
            _colorModal.SetActive(false);
            _pathText.text = SkinManager.SkinsRoot;
            Rebuild();
            UpdateHomeToggles();
        }

        void Rebuild()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();

            string currentId = SkinManager.Current().Id;
            foreach (var skin in SkinManager.ListSkins())
            {
                AddRow(skin, skin.Id == currentId);
            }
        }

        void AddRow(SkinDef skin, bool selected)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = selected ? new Color(0.90f, 0.84f, 1.0f) : UiFactory.CardBg;
            UiFactory.Roundify(img);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = Mathf.Max(110f, UiFactory.LineHeight(32) + 40f);
            var button = rowGo.AddComponent<Button>();
            string skinId = skin.Id;
            button.onClick.AddListener(() => Apply(skinId));

            string label = (selected ? "✓ " : "") + skin.Name;
            var text = UiFactory.CreateText(rowGo.transform, "Name", label, 32,
                selected ? UiFactory.PrimaryDark : UiFactory.TextDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24f, 6f);
            text.rectTransform.offsetMax = new Vector2(-330f, -6f);
            UiFactory.FitLabelOneLine(text, 20); // 長い名前は1行に収まるよう縮める

            // ユーザースキンには編集/共有/削除ボタンを付ける
            if (skin.Folder != null)
            {
                var editButton = UiFactory.CreateButton(rowGo.transform, "Edit", "編集",
                    UiFactory.Primary, Color.white, 24);
                var editRect = editButton.GetComponent<RectTransform>();
                editRect.anchorMin = new Vector2(1f, 0.5f);
                editRect.anchorMax = new Vector2(1f, 0.5f);
                editRect.pivot = new Vector2(1f, 0.5f);
                editRect.anchoredPosition = new Vector2(-256f, 0f);
                editRect.sizeDelta = new Vector2(110f, 76f);
                editButton.onClick.AddListener(() => OpenEditModal(skin));

                var exportButton = UiFactory.CreateButton(rowGo.transform, "Export", "共有",
                    UiFactory.PrimaryDark, Color.white, 24);
                var expRect = exportButton.GetComponent<RectTransform>();
                expRect.anchorMin = new Vector2(1f, 0.5f);
                expRect.anchorMax = new Vector2(1f, 0.5f);
                expRect.pivot = new Vector2(1f, 0.5f);
                expRect.anchoredPosition = new Vector2(-136f, 0f);
                expRect.sizeDelta = new Vector2(110f, 76f);
                exportButton.onClick.AddListener(() => ExportSkinFlow(skin));

                var deleteButton = UiFactory.CreateButton(rowGo.transform, "Delete", "削除",
                    UiFactory.Danger, Color.white, 26);
                var delRect = deleteButton.GetComponent<RectTransform>();
                delRect.anchorMin = new Vector2(1f, 0.5f);
                delRect.anchorMax = new Vector2(1f, 0.5f);
                delRect.pivot = new Vector2(1f, 0.5f);
                delRect.anchoredPosition = new Vector2(-16f, 0f);
                delRect.sizeDelta = new Vector2(110f, 76f);
                var delLabel = deleteButton.GetComponentInChildren<Text>();
                UiFactory.FitLabel(delLabel); // 「本当に？」も枠内に収める
                bool armed = false;
                deleteButton.onClick.AddListener(() =>
                {
                    if (!armed)
                    {
                        armed = true;
                        delLabel.text = "本当に？";
                        Se.Play(Se.Tap);
                        return;
                    }
                    if (SkinManager.DeleteSkin(skin))
                    {
                        if (AppConfig.SkinId == skinId)
                        {
                            AppConfig.SkinId = "";
                        }
                        Bgm.RefreshForCurrentSkin();
                        Se.Play(Se.Confirm);
                        if (ApplyThemeAndRebuild())
                        {
                            return;
                        }
                        Rebuild();
                    }
                    else
                    {
                        Se.Play(Se.Error);
                    }
                });
            }

            _rows.Add(rowGo);
        }

        void Apply(string skinId)
        {
            AppConfig.SkinId = skinId;
            Bgm.RefreshForCurrentSkin(); // スキン BGM も切り替える
            Se.Play(Se.Confirm);
            if (ApplyThemeAndRebuild())
            {
                return; // テーマ色が変わった → 全 UI 再構築済み
            }
            Rebuild();
            UpdateHomeToggles(); // 表示トグルは選択中スキンの保存値を映す
        }
    }
}
