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
    /// 予約確認画面。カラオケ機の端末と同じく独立した画面として、
    /// 名前・コメント・予約オプション (キー変更/シークレット/BGV/別プレイヤー/小休止/
    /// 音量/音ズレ/トラック) を設定して予約する。オプションは capabilities で出し分ける。
    /// </summary>
    public class ReserveScreen : ScreenBase
    {
        /// <summary>予約候補の共通形。</summary>
        public class Entry
        {
            public string Line1;     // 曲名 (またはファイル名)
            public string Line2;     // 歌手 / 作品などの補足 (無ければ空)
            public string Filename;  // 予約に使う表示ファイル名
            public string FullPath;  // 予約に使うフルパス
        }

        /// <summary>次に開くエントリ (Open() 経由で渡す)。</summary>
        static Entry _pending;

        /// <summary>変更対象の既存予約 (OpenForEdit() 経由で渡す)。null なら新規予約。</summary>
        static RequestItemDto _pendingEdit;

        /// <summary>
        /// 「曲をえらびなおす」で検索画面へ行っている間の変更対象。
        /// 検索から曲を選ぶ (Open) と消費され、変更モードとして続行する。
        /// </summary>
        public static RequestItemDto EditSession { get; private set; }

        /// <summary>曲えらびなおしを中断する (ホームに戻ったときなど)。</summary>
        public static void ClearEditSession()
        {
            EditSession = null;
        }

        static readonly Color ToggleOffColor = new Color(0.75f, 0.73f, 0.80f);

        Entry _entry;
        /// <summary>変更対象の予約 id (負なら新規予約)。</summary>
        int _editId = -1;
        /// <summary>変更対象の予約 (曲えらびなおしに渡すため保持)。</summary>
        RequestItemDto _editSource;
        GameObject _changeSongRow;
        Text _topBarTitle;
        Text _submitLabel;
        Text _completeMessage;
        Text _songText;
        InputField _nameInput;
        InputField _commentInput;
        Button _favoriteButton;
        Button _laterButton;
        Text _errorText;
        Button _submitButton;

        // オプション状態
        int _keychange;
        bool _secret;
        bool _bgv;
        bool _otherplayer;
        bool _pause;
        int _volume;
        int _audioDelay;
        int _track;
        int _trackMax;

        // オプション行 (capabilities / ファイル種別で出し分け)
        GameObject _keyRow;
        GameObject _secretRow;
        GameObject _bgvRow;
        GameObject _otherRow;
        GameObject _pauseRow;
        GameObject _trackRow;
        Text _keyValueText;
        Button _secretButton;
        Button _bgvButton;
        Button _otherButton;
        Button _pauseButton;
        Text _otherButtonLabel;
        Text _volumeValueText;
        Text _delayValueText;
        Text _trackValueText;
        List<string> _trackLabels = new List<string>();

        GameObject _completeOverlay;
        RectTransform _completePose;

        // 予約中の歌う人チップ (タップで名前欄に入力)
        GameObject _singerRow;
        RectTransform _singerChipContent;
        readonly List<GameObject> _singerChips = new List<GameObject>();

        /// <summary>パス区切り (\ と /) を考慮した basename。</summary>
        public static string BaseName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            int cut = Mathf.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }

        /// <summary>動画ファイルか (音声ファイル以外は動画扱い。exec.php と同じ判定)。</summary>
        static bool IsVideoFile(string path)
        {
            string ext = BaseName(path);
            int dot = ext.LastIndexOf('.');
            ext = dot >= 0 ? ext.Substring(dot + 1).ToLowerInvariant() : "";
            switch (ext)
            {
                case "mp3":
                case "m4a":
                case "wav":
                case "ogg":
                case "flac":
                case "wma":
                case "aac":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>予約確認画面を開く。曲えらびなおし中なら変更モードとして続行する。</summary>
        public static void Open(ScreenManager manager, Entry entry)
        {
            _pending = entry;
            _pendingEdit = EditSession; // 変更対象があれば新しい曲でオプションを引き継ぐ
            EditSession = null;
            manager.Show<ReserveScreen>();
        }

        /// <summary>既存予約の「変更」として開く (現在の値をフォームに読み込む)。</summary>
        public static void OpenForEdit(ScreenManager manager, RequestItemDto item)
        {
            string line2 = item.ListerArtist ?? "";
            if (!string.IsNullOrEmpty(item.ListerWork))
            {
                line2 += (line2 != "" ? "　／　" : "") + item.ListerWork;
                if (!string.IsNullOrEmpty(item.ListerOpEd))
                {
                    line2 += " [" + item.ListerOpEd + "]";
                }
            }
            _pending = new Entry
            {
                Line1 = item.Songfile,
                Line2 = line2,
                Filename = item.Songfile,
                FullPath = item.FullPath,
            };
            _pendingEdit = item;
            EditSession = null;
            manager.Show<ReserveScreen>();
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreateTopBar(transform, "予約の確認");
            _topBarTitle = topBar.GetComponentInChildren<Text>();

            // フォーム (スクロール)
            var scrollRectT = UiFactory.CreateScrollList(transform, "Form", out var form);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 132f);
            scrollRectT.offsetMax = new Vector2(-20f, -126f);

            BuildForm(form);

            // 予約ボタン (下部固定、ナビバーの上)
            _submitButton = UiFactory.CreateButton(transform, "Submit", "この内容で予約する",
                UiFactory.Primary, Color.white, 38);
            _submitLabel = _submitButton.GetComponentInChildren<Text>();
            var submitRect = _submitButton.GetComponent<RectTransform>();
            submitRect.anchorMin = new Vector2(0f, 0f);
            submitRect.anchorMax = new Vector2(1f, 0f);
            submitRect.pivot = new Vector2(0.5f, 0f);
            submitRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 12f);
            submitRect.offsetMin = new Vector2(20f, submitRect.offsetMin.y);
            submitRect.offsetMax = new Vector2(-20f, submitRect.offsetMax.y);
            submitRect.sizeDelta = new Vector2(submitRect.sizeDelta.x, 108f);
            _submitButton.onClick.AddListener(() => _ = SubmitAsync());

            BuildCompleteOverlay();
        }

        // ---- フォーム構築 ----

        void BuildForm(RectTransform form)
        {
            // 曲名 (PrimaryPale で強調)
            var songPanel = AddPanel(form, 170f, color: UiFactory.PrimaryPale);
            UiFactory.AddShadow(songPanel.gameObject, 3f);
            var songCaption = UiFactory.CreateText(songPanel, "Caption", "よやくする曲", 24,
                UiFactory.PrimaryDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(songCaption.rectTransform);
            songCaption.rectTransform.offsetMin = new Vector2(24f, 110f);
            songCaption.rectTransform.offsetMax = new Vector2(-24f, -10f);
            _songText = UiFactory.CreateText(songPanel, "Song", "", 30, UiFactory.TextDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(_songText.rectTransform);
            _songText.rectTransform.offsetMin = new Vector2(24f, 8f);
            _songText.rectTransform.offsetMax = new Vector2(-24f, -52f);
            _songText.verticalOverflow = VerticalWrapMode.Truncate;

            // 予約の変更中のみ: 曲そのものを差し替える (検索画面へ)
            var changeSongPanel = AddPanel(form, 84f, transparent: true);
            _changeSongRow = changeSongPanel.gameObject;
            var changeSongButton = UiFactory.CreateOutlineButton(changeSongPanel,
                "ChangeSong", "曲をえらびなおす (検索へ)", 28);
            UiFactory.StretchFull(changeSongButton.GetComponent<RectTransform>());
            changeSongButton.onClick.AddListener(() =>
            {
                if (_editSource == null)
                {
                    return;
                }
                EditSession = _editSource; // 検索から曲を選ぶと変更モードとして戻ってくる
                Se.Play(Se.Transition);
                Manager.Show<SearchScreen>();
            });
            _changeSongRow.SetActive(false);

            AddSectionHeader(form, "きほん");

            // 名前
            var namePanel = AddPanel(form, 140f);
            AddPanelLabel(namePanel, "歌う人の名前");
            _nameInput = UiFactory.CreateInputField(namePanel, "NameInput", "名前 (必須)");
            SetPanelControl(_nameInput.GetComponent<RectTransform>());

            // 予約中の歌う人チップ (横スクロール)
            BuildSingerChips(form);

            // コメント
            var commentPanel = AddPanel(form, 140f);
            AddPanelLabel(commentPanel, "コメント (任意)");
            _commentInput = UiFactory.CreateInputField(commentPanel, "CommentInput", "");
            SetPanelControl(_commentInput.GetComponent<RectTransform>());

            // お気に入り / あとで歌う
            var mypagePanel = AddPanel(form, 104f);
            _favoriteButton = UiFactory.CreateButton(mypagePanel, "Favorite", "お気に入り",
                ToggleOffColor, Color.white, 28);
            var favRect = _favoriteButton.GetComponent<RectTransform>();
            favRect.anchorMin = new Vector2(0f, 0.5f);
            favRect.anchorMax = new Vector2(0f, 0.5f);
            favRect.pivot = new Vector2(0f, 0.5f);
            favRect.anchoredPosition = new Vector2(24f, 0f);
            favRect.sizeDelta = new Vector2(460f, 76f);
            _favoriteButton.onClick.AddListener(() =>
            {
                bool added = LocalMypage.ToggleFavorite(_entry.FullPath, _entry.Line1, "動画");
                Se.Play(added ? Se.Confirm : Se.Tap);
                UpdateMypageButtons();
            });
            _laterButton = UiFactory.CreateButton(mypagePanel, "Later", "あとで歌う",
                ToggleOffColor, Color.white, 28);
            var laterRect = _laterButton.GetComponent<RectTransform>();
            laterRect.anchorMin = new Vector2(1f, 0.5f);
            laterRect.anchorMax = new Vector2(1f, 0.5f);
            laterRect.pivot = new Vector2(1f, 0.5f);
            laterRect.anchoredPosition = new Vector2(-24f, 0f);
            laterRect.sizeDelta = new Vector2(460f, 76f);
            _laterButton.onClick.AddListener(() =>
            {
                bool added = LocalMypage.ToggleLater(_entry.FullPath, _entry.Line1, "動画");
                Se.Play(added ? Se.Confirm : Se.Tap);
                UpdateMypageButtons();
            });

            AddSectionHeader(form, "オプション");

            // キー変更
            _keyValueText = AddStepperRow(form, out _keyRow, "キー変更",
                () => { _keychange = Mathf.Max(_keychange - 1, -6); UpdateOptionTexts(); },
                () => { _keychange = Mathf.Min(_keychange + 1, 6); UpdateOptionTexts(); });

            // トグル群
            _secretButton = AddToggleRow(form, out _secretRow, "シークレット (歌うまで曲名を表示しない)",
                () => { _secret = !_secret; UpdateToggles(); });
            _bgvButton = AddToggleRow(form, out _bgvRow, "BGVモード (配信のBGVとして再生)",
                () => { _bgv = !_bgv; UpdateToggles(); });
            _otherButton = AddToggleRow(form, out _otherRow, "別プレイヤー再生",
                () => { _otherplayer = !_otherplayer; UpdateToggles(); });
            _otherButtonLabel = _otherButton.GetComponentInChildren<Text>();
            _pauseButton = AddToggleRow(form, out _pauseRow, "小休止リクエスト (止めるまでループ再生)",
                () => { _pause = !_pause; UpdateToggles(); });

            // 音量 / 音ズレ
            _volumeValueText = AddStepperRow(form, out _, "音量増減",
                () => { _volume = Mathf.Max(_volume - 10, -100); UpdateOptionTexts(); },
                () => { _volume = Mathf.Min(_volume + 10, 100); UpdateOptionTexts(); });
            _delayValueText = AddStepperRow(form, out _, "音ズレ補正",
                () => { _audioDelay = Mathf.Max(_audioDelay - 100, -9900); UpdateOptionTexts(); },
                () => { _audioDelay = Mathf.Min(_audioDelay + 100, 9900); UpdateOptionTexts(); });

            // 音声トラック
            _trackValueText = AddStepperRow(form, out _trackRow, "音声トラック",
                () => { _track = Mathf.Max(_track - 1, 0); UpdateOptionTexts(); },
                () => { _track = Mathf.Min(_track + 1, _trackMax - 1); UpdateOptionTexts(); });

            // エラー表示
            var errorPanel = AddPanel(form, 56f, transparent: true);
            _errorText = UiFactory.CreateText(errorPanel, "Error", "", 26, UiFactory.Danger);
            UiFactory.StretchFull(_errorText.rectTransform);
        }

        /// <summary>「予約中の歌う人」の横スクロールチップ行。タップで名前欄に入る。</summary>
        void BuildSingerChips(RectTransform form)
        {
            var panel = AddPanel(form, 96f, transparent: true);
            _singerRow = panel.gameObject;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(panel, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            UiFactory.StretchFull(viewportRect);
            viewportGo.AddComponent<Image>().color = Color.white;
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _singerChipContent = contentGo.AddComponent<RectTransform>();
            _singerChipContent.anchorMin = new Vector2(0f, 0f);
            _singerChipContent.anchorMax = new Vector2(0f, 1f);
            _singerChipContent.pivot = new Vector2(0f, 0.5f);
            _singerChipContent.sizeDelta = Vector2.zero;
            _singerChipContent.anchoredPosition = Vector2.zero;
            var layout = contentGo.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 10f;
            layout.padding = new RectOffset(4, 4, 10, 10);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = _singerChipContent;
            scroll.viewport = viewportRect;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _singerRow.SetActive(false);
        }

        /// <summary>予約に入っている歌う人からチップを作る。</summary>
        async Task LoadSingersAsync()
        {
            var entryAtLoad = _entry;
            List<string> singers;
            try
            {
                singers = await AppConfig.CreateClient().GetSingerListAsync();
            }
            catch
            {
                return;
            }
            if (entryAtLoad != _entry)
            {
                return; // 画面が切り替わっていたら破棄
            }
            foreach (var chip in _singerChips)
            {
                Destroy(chip);
            }
            _singerChips.Clear();
            foreach (var singer in singers)
            {
                string name = singer;
                var chip = UiFactory.CreateButton(_singerChipContent, name, name,
                    UiFactory.PrimaryDark, Color.white, 26);
                var text = chip.GetComponentInChildren<Text>();
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Max(text.preferredWidth + 44f, 120f);
                chip.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    _nameInput.text = name;
                });
                _singerChips.Add(chip.gameObject);
            }
            _singerRow.SetActive(singers.Count > 0);
        }

        RectTransform AddPanel(RectTransform form, float height, bool transparent = false, Color? color = null)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(form, false);
            var rect = go.AddComponent<RectTransform>();
            if (!transparent)
            {
                var img = go.AddComponent<Image>();
                img.color = color ?? UiFactory.CardBg;
                UiFactory.Roundify(img);
            }
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            return rect;
        }

        /// <summary>「| きほん」形式のセクション見出し行。</summary>
        void AddSectionHeader(RectTransform form, string label)
        {
            var panel = AddPanel(form, 52f, transparent: true);

            var barGo = new GameObject("Bar");
            barGo.transform.SetParent(panel, false);
            var barImg = barGo.AddComponent<Image>();
            barImg.sprite = UiFactory.RoundedSprite;
            barImg.type = Image.Type.Sliced;
            barImg.pixelsPerUnitMultiplier = 5f; // 細いバーでも角丸が潰れないように
            barImg.color = UiFactory.Primary;
            barImg.raycastTarget = false;
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 0.5f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.anchoredPosition = new Vector2(8f, -4f);
            barRect.sizeDelta = new Vector2(10f, 34f);

            var text = UiFactory.CreateText(panel, "Header", label, 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(36f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, -8f);
        }

        void AddPanelLabel(RectTransform panel, string label)
        {
            var text = UiFactory.CreateText(panel, "Label", label, 24,
                UiFactory.PrimaryDark, TextAnchor.UpperLeft);
            UiFactory.StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24f, 92f);
            text.rectTransform.offsetMax = new Vector2(-24f, -8f);
        }

        static void SetPanelControl(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 12f);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 78f);
        }

        /// <summary>「ラベル [−] 値 [＋]」の1行を作り、値表示 Text を返す。</summary>
        Text AddStepperRow(RectTransform form, out GameObject row, string label,
                           System.Action onMinus, System.Action onPlus)
        {
            var panel = AddPanel(form, 104f);
            row = panel.gameObject;

            var labelText = UiFactory.CreateText(panel, "Label", label, 27,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(labelText.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(24f, 4f);
            labelText.rectTransform.offsetMax = new Vector2(-560f, -4f);

            var minus = UiFactory.CreateButton(panel, "Minus", "−", UiFactory.PrimaryDark, Color.white, 34);
            var minusRect = minus.GetComponent<RectTransform>();
            minusRect.anchorMin = minusRect.anchorMax = new Vector2(1f, 0.5f);
            minusRect.pivot = new Vector2(1f, 0.5f);
            minusRect.anchoredPosition = new Vector2(-410f, 0f);
            minusRect.sizeDelta = new Vector2(110f, 76f);
            minus.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onMinus();
            });

            var value = UiFactory.CreateText(panel, "Value", "", 28, UiFactory.TextDark);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.anchoredPosition = new Vector2(-140f, 0f);
            valueRect.sizeDelta = new Vector2(266f, 80f);

            var plus = UiFactory.CreateButton(panel, "Plus", "＋", UiFactory.PrimaryDark, Color.white, 34);
            var plusRect = plus.GetComponent<RectTransform>();
            plusRect.anchorMin = plusRect.anchorMax = new Vector2(1f, 0.5f);
            plusRect.pivot = new Vector2(1f, 0.5f);
            plusRect.anchoredPosition = new Vector2(-24f, 0f);
            plusRect.sizeDelta = new Vector2(110f, 76f);
            plus.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onPlus();
            });

            return value;
        }

        /// <summary>全幅トグルボタンの1行を作る。</summary>
        Button AddToggleRow(RectTransform form, out GameObject row, string label, System.Action onToggle)
        {
            var panel = AddPanel(form, 100f, transparent: true);
            row = panel.gameObject;
            var button = UiFactory.CreateButton(panel, "Toggle", label, ToggleOffColor, Color.white, 27);
            UiFactory.StretchFull(button.GetComponent<RectTransform>());
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onToggle();
            });
            return button;
        }

        // ---- 表示更新 ----

        void UpdateMypageButtons()
        {
            bool inFavorite = _entry != null && LocalMypage.IsInFavorite(_entry.FullPath);
            bool inLater = _entry != null && LocalMypage.IsInLater(_entry.FullPath);
            _favoriteButton.image.color = inFavorite ? UiFactory.Primary : ToggleOffColor;
            _favoriteButton.GetComponentInChildren<Text>().text = inFavorite ? "✓ お気に入り" : "お気に入り";
            _laterButton.image.color = inLater ? UiFactory.Primary : ToggleOffColor;
            _laterButton.GetComponentInChildren<Text>().text = inLater ? "✓ あとで歌う" : "あとで歌う";
        }

        void UpdateToggles()
        {
            _secretButton.image.color = _secret ? UiFactory.Primary : ToggleOffColor;
            _bgvButton.image.color = _bgv ? UiFactory.Primary : ToggleOffColor;
            _otherButton.image.color = _otherplayer ? UiFactory.Primary : ToggleOffColor;
            _pauseButton.image.color = _pause ? UiFactory.Primary : ToggleOffColor;
        }

        void UpdateOptionTexts()
        {
            _keyValueText.text = _keychange > 0 ? "+" + _keychange : _keychange.ToString();
            _volumeValueText.text = (_volume > 0 ? "+" + _volume : _volume.ToString()) + " %";
            _delayValueText.text = _audioDelay + " ms";
            // トラック名があればそれだけを表示し、無ければ番号にフォールバック
            _trackValueText.text = (_track < _trackLabels.Count && !string.IsNullOrEmpty(_trackLabels[_track]))
                ? _trackLabels[_track]
                : (_track + 1) + "トラック目";
        }

        // ---- 画面ライフサイクル ----

        public override void OnShow()
        {
            var edit = _pendingEdit;
            _pendingEdit = null;
            _entry = _pending ?? _entry;
            _pending = null;
            if (_entry == null)
            {
                Manager.ShowAsRoot<HomeScreen>();
                return;
            }

            _editId = edit != null ? edit.Id : -1;
            _editSource = edit;
            _topBarTitle.text = _editId >= 0 ? "予約の変更" : "予約の確認";
            _submitLabel.text = _editId >= 0 ? "この内容で変更する" : "この内容で予約する";
            _changeSongRow.SetActive(_editId >= 0);

            _songText.text = string.IsNullOrEmpty(_entry.Line2)
                ? _entry.Line1
                : _entry.Line1 + "\n" + _entry.Line2;
            _errorText.text = "";
            _submitButton.interactable = true;
            _completeOverlay.SetActive(false);

            if (edit != null)
            {
                // 既存予約の現在値をフォームに読み込む
                _nameInput.text = edit.Singer ?? "";
                _commentInput.text = edit.Comment ?? "";
                _keychange = Mathf.Clamp(edit.Keychange, -6, 6);
                _secret = edit.Secret == 1;
                _bgv = edit.Loop == 1;
                _pause = edit.Pause == 1;
                _otherplayer = edit.Kind == "動画_別プ";
                _volume = edit.Volume > 0 ? Mathf.Clamp(edit.Volume, -100, 100) : 0;
                _audioDelay = Mathf.Clamp(edit.Audiodelay, -9900, 9900);
                _track = Mathf.Max(edit.Track, 0);
            }
            else
            {
                // 新規予約: オプションのリセット
                _nameInput.text = AppConfig.Username;
                _commentInput.text = "";
                _keychange = 0;
                _secret = false;
                _bgv = false;
                _otherplayer = false;
                _pause = false;
                _volume = 0;
                _audioDelay = 0;
                _track = 0;
            }
            _trackMax = Mathf.Max(_track + 1, 1);
            _trackLabels.Clear();
            _trackRow.SetActive(false);
            UpdateMypageButtons();
            UpdateToggles();
            UpdateOptionTexts();

            _ = ApplyCapabilitiesAsync();
            _ = LoadTracksAsync();
            _ = LoadSingersAsync();
        }

        /// <summary>capabilities とファイル種別でオプション行を出し分ける。</summary>
        async Task ApplyCapabilitiesAsync()
        {
            bool isVideo = IsVideoFile(_entry.FullPath);
            try
            {
                var caps = await AppState.EnsureCapabilitiesAsync();
                _keyRow.SetActive(caps.Features.Keychange && isVideo);
                _secretRow.SetActive(caps.Features.Secret);
                _bgvRow.SetActive(caps.Features.Bgv && isVideo);
                _otherRow.SetActive(caps.Features.Otherplayer && isVideo);
                _pauseRow.SetActive(caps.Features.Userpause);
                if (!string.IsNullOrEmpty(caps.Request.OtherplayerDisc))
                {
                    _otherButtonLabel.text = caps.Request.OtherplayerDisc;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[YukaNavi] capabilities 取得失敗 (全オプション表示): " + e.Message);
                // capabilities が取れない場合は全オプションを表示 (サーバー側で弾かれる)
                _keyRow.SetActive(isVideo);
                _secretRow.SetActive(true);
                _bgvRow.SetActive(isVideo);
                _otherRow.SetActive(isVideo);
                _pauseRow.SetActive(true);
            }
        }

        /// <summary>音声トラックの一覧を取得して選択行を出し分ける (Web 版と同じ規則)。</summary>
        async Task LoadTracksAsync()
        {
            if (!IsVideoFile(_entry.FullPath))
            {
                return;
            }
            var entryAtLoad = _entry;
            List<string> labels;
            try
            {
                labels = await AppConfig.CreateClient().GetTrackListAsync(_entry.FullPath);
            }
            catch
            {
                labels = new List<string>();
            }
            if (entryAtLoad != _entry)
            {
                return; // 画面が別の曲に切り替わっていたら破棄
            }
            if (labels.Count == 1)
            {
                return; // 1トラックのみ → 選択不要
            }
            _trackLabels = labels;
            _trackMax = labels.Count == 0 ? 3 : labels.Count; // 判別不能時は3トラック仮 (Web と同じ)
            _track = Mathf.Clamp(_track, 0, _trackMax - 1); // 変更時は現在のトラックを保つ
            UpdateOptionTexts();
            _trackRow.SetActive(true);
        }

        // ---- 予約 ----

        async Task SubmitAsync()
        {
            string name = (_nameInput.text ?? "").Trim();
            if (name == "")
            {
                _errorText.text = "名前を入力してください";
                Se.Play(Se.Error);
                return;
            }
            AppConfig.Username = name;
            _submitButton.interactable = false;
            _errorText.text = "";
            var options = new ApiClient.RequestOptions
            {
                Keychange = _keychange,
                Secret = _secret,
                Loop = _bgv,
                OtherPlayer = _otherplayer,
                Pause = _pause,
                Volume = _volume,
                AudioDelay = _audioDelay,
                Track = _track,
                SelectId = _editId, // 負なら新規、既存 id なら差し替え (変更)
            };
            try
            {
                await AppConfig.CreateClient().PostRequestAsync(
                    _entry.Filename, _entry.FullPath, name,
                    (_commentInput.text ?? "").Trim(), "動画", options);
                if (_editId < 0)
                {
                    LocalMypage.AddHistory(_entry.FullPath, _entry.Line1, "動画");
                }
                ShowComplete();
            }
            catch (System.Exception e)
            {
                _errorText.text = "予約に失敗: " + e.Message;
                Se.Play(Se.Error);
                _submitButton.interactable = true;
            }
        }

        void ShowComplete()
        {
            _completeMessage.text = _editId >= 0 ? "変更したよ♪" : "予約したよ♪";
            Se.Play(Se.ReservationComplete);
            _completeOverlay.SetActive(true);
            StartCoroutine(CompletePopRoutine());
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
                if (_editId >= 0)
                {
                    Manager.BackTo<QueueScreen>(); // 変更は予約一覧へ戻る (検索経由でも)
                }
                else
                {
                    Manager.Back(); // 新規予約は検索/マイページへ戻る
                }
            });

            var message = UiFactory.CreateText(_completeOverlay.transform, "Message", "予約したよ♪", 64, UiFactory.Primary);
            _completeMessage = message;
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

            var hint = UiFactory.CreateText(_completeOverlay.transform, "Hint", "タップで戻る", 30, UiFactory.PrimaryDark);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 60f);
            hintRect.sizeDelta = new Vector2(600f, 44f);

            _completeOverlay.SetActive(false);
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
