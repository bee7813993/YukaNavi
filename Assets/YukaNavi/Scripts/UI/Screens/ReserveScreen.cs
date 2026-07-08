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
            /// <summary>予約の種別 (null = 動画)。URL指定 / 小休止 で使う</summary>
            public string Kind;
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
        int _completedId = -1; // 直前に予約 (新規・変更) した id。完了画面から一覧の該当カードへ飛ぶ
        /// <summary>変更対象の予約 (曲えらびなおしに渡すため保持)。</summary>
        RequestItemDto _editSource;
        GameObject _changeSongRow;
        Text _topBarTitle;
        Text _submitLabel;
        Text _completeMessage;
        LayoutElement _songPanelLe;
        Text _songText;
        Text _songSubText;
        Text _kindBadgeText;
        RectTransform _kindBadgeRect;
        GameObject _detailsRow;
        LayoutElement _detailsLe;
        Text _detailsHeaderText;
        Text _detailsText;
        bool _detailsOpen;
        VideoDetailsDto _videoDetails;
        int _durationSeconds;
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
        Image _completePoseImage;
        Sprite _defaultPoseSprite;
        string _poseSkinKey;
        Texture2D _poseSkinTex;

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
                // 動画_別プ / カラオケ配信 はトグルで表現するため、それ以外の特殊種別だけ引き継ぐ
                Kind = (item.Kind == "URL指定" || item.Kind == "小休止") ? item.Kind : null,
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
            // 曲名 (PrimaryPale で強調)。高さは曲名の行数に合わせて OnShow で更新する
            var songPanel = AddPanel(form, 170f, color: UiFactory.PrimaryPale);
            UiFactory.AddShadow(songPanel.gameObject, 3f);
            _songPanelLe = songPanel.GetComponent<LayoutElement>();
            var songCaption = UiFactory.CreateText(songPanel, "Caption", "よやくする曲", 24,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var capRect = songCaption.rectTransform;
            capRect.anchorMin = new Vector2(0f, 1f);
            capRect.anchorMax = new Vector2(1f, 1f);
            capRect.pivot = new Vector2(0.5f, 1f);
            capRect.anchoredPosition = new Vector2(0f, -12f);
            capRect.offsetMin = new Vector2(24f, capRect.offsetMin.y);
            capRect.offsetMax = new Vector2(-24f, capRect.offsetMax.y);
            capRect.sizeDelta = new Vector2(capRect.sizeDelta.x, 34f);

            // 種別バッジ (URL指定 / 小休止 のときだけ表示)
            _kindBadgeText = UiFactory.CreateBadge(songPanel, "KindBadge", "", UiFactory.PrimaryDark, Color.white);
            _kindBadgeRect = (RectTransform)_kindBadgeText.transform.parent;
            _kindBadgeRect.anchorMin = _kindBadgeRect.anchorMax = new Vector2(1f, 1f);
            _kindBadgeRect.pivot = new Vector2(1f, 1f);
            _kindBadgeRect.anchoredPosition = new Vector2(-20f, -10f);
            _kindBadgeRect.sizeDelta = new Vector2(150f, 40f);
            _kindBadgeRect.gameObject.SetActive(false);

            _songText = UiFactory.CreateText(songPanel, "Song", "", 32, UiFactory.TextDark, TextAnchor.UpperLeft);
            _songText.fontStyle = FontStyle.Bold;
            _songSubText = UiFactory.CreateText(songPanel, "SongSub", "", 25, UiFactory.TextMuted, TextAnchor.UpperLeft);

            // 動画詳細情報 (タップで開閉。解析できたファイルだけ表示される)
            var detailsPanel = AddPanel(form, 64f);
            _detailsRow = detailsPanel.gameObject;
            _detailsLe = detailsPanel.GetComponent<LayoutElement>();
            var detailsHeader = UiFactory.CreateButton(detailsPanel, "DetailsHeader", "",
                new Color(1f, 1f, 1f, 0f), UiFactory.Primary, 26);
            var dhRect = (RectTransform)detailsHeader.transform;
            dhRect.anchorMin = new Vector2(0f, 1f);
            dhRect.anchorMax = new Vector2(1f, 1f);
            dhRect.pivot = new Vector2(0.5f, 1f);
            dhRect.anchoredPosition = Vector2.zero;
            dhRect.sizeDelta = new Vector2(0f, 64f);
            _detailsHeaderText = UiFactory.CreateText(detailsHeader.transform, "Label",
                "動画詳細情報 ▼", 26, UiFactory.Primary, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(_detailsHeaderText.rectTransform);
            _detailsHeaderText.rectTransform.offsetMin = new Vector2(24f, 0f);
            detailsHeader.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _detailsOpen = !_detailsOpen;
                UpdateDetailsRow();
            });
            _detailsText = UiFactory.CreateText(detailsPanel, "Details", "", 25,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            var dtRect = _detailsText.rectTransform;
            dtRect.anchorMin = new Vector2(0f, 1f);
            dtRect.anchorMax = new Vector2(1f, 1f);
            dtRect.pivot = new Vector2(0.5f, 1f);
            dtRect.anchoredPosition = new Vector2(0f, -60f);
            dtRect.offsetMin = new Vector2(24f, dtRect.offsetMin.y);
            dtRect.offsetMax = new Vector2(-24f, dtRect.offsetMax.y);
            dtRect.sizeDelta = new Vector2(dtRect.sizeDelta.x, 300f);
            _detailsText.verticalOverflow = VerticalWrapMode.Overflow;
            _detailsRow.SetActive(false);

            // 予約の変更中のみ: 曲そのものを差し替える (検索画面へ)
            var changeSongPanel = AddPanel(form, 84f, transparent: true);
            _changeSongRow = changeSongPanel.gameObject;
            var changeSongButton = UiFactory.CreateSoftButton(changeSongPanel,
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

            // ラベル・入力欄の高さは文字の大きさ設定に追従させる (固定だと大きい設定で重なる)
            float panelLabelH = UiFactory.LineHeight(24);
            float inputH = Mathf.Max(78f, UiFactory.LineHeight(34) + 20f);

            // 名前
            var namePanel = AddPanel(form, 12f + inputH + 8f + panelLabelH + 8f);
            AddPanelLabel(namePanel, "歌う人の名前");
            _nameInput = UiFactory.CreateInputField(namePanel, "NameInput", "名前 (必須)");
            SetPanelControl(_nameInput.GetComponent<RectTransform>(), inputH);

            // 予約中の歌う人チップ (横スクロール)
            BuildSingerChips(form);

            // コメント (Enter で改行して複数行書ける)
            float commentInputH = inputH + UiFactory.LineHeight(34);
            var commentPanel = AddPanel(form, 12f + commentInputH + 8f + panelLabelH + 8f);
            AddPanelLabel(commentPanel, "コメント (任意)");
            _commentInput = UiFactory.CreateInputField(commentPanel, "CommentInput", "");
            _commentInput.lineType = InputField.LineType.MultiLineNewline;
            _commentInput.textComponent.alignment = TextAnchor.UpperLeft;
            SetPanelControl(_commentInput.GetComponent<RectTransform>(), commentInputH);

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
            _favoriteButton.onClick.AddListener(async () =>
            {
                try
                {
                    bool added = await MypageService.ToggleFavoriteAsync(
                        _entry.FullPath, _entry.Line1, "動画");
                    Se.Play(added ? Se.Confirm : Se.Tap);
                }
                catch (System.Exception e)
                {
                    UiFactory.ShowToast("同期に失敗: " + e.Message, true);
                }
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
            _laterButton.onClick.AddListener(async () =>
            {
                try
                {
                    bool added = await MypageService.ToggleLaterAsync(
                        _entry.FullPath, _entry.Line1, "動画");
                    Se.Play(added ? Se.Confirm : Se.Tap);
                }
                catch (System.Exception e)
                {
                    UiFactory.ShowToast("同期に失敗: " + e.Message, true);
                }
                UpdateMypageButtons();
            });

            AddSectionHeader(form, "オプション");

            // 使う頻度が高い順に上から並べる: 音声トラック → キー変更 → シークレット・小休止 → それ以外

            // 音声トラック (on/off vocal の切替。トラックが複数ある動画でだけ表示される)
            _trackValueText = AddStepperRow(form, out _trackRow, "音声トラック",
                () => { _track = Mathf.Max(_track - 1, 0); UpdateOptionTexts(); },
                () => { _track = Mathf.Min(_track + 1, _trackMax - 1); UpdateOptionTexts(); });

            // キー変更
            _keyValueText = AddStepperRow(form, out _keyRow, "キー変更",
                () => { _keychange = Mathf.Max(_keychange - 1, -6); UpdateOptionTexts(); },
                () => { _keychange = Mathf.Min(_keychange + 1, 6); UpdateOptionTexts(); });

            // シークレット / 小休止 (よく使うので半幅で横に並べる)
            var togglePair = AddPanel(form, 100f, transparent: true);
            _secretButton = AddHalfToggle(togglePair, 0f, 0.5f, "シークレット (曲名をふせる)",
                () => { _secret = !_secret; UpdateToggles(); });
            _secretRow = _secretButton.gameObject;
            _pauseButton = AddHalfToggle(togglePair, 0.5f, 1f, "小休止 (ループ再生)",
                () => { _pause = !_pause; UpdateToggles(); });
            _pauseRow = _pauseButton.gameObject;

            _bgvButton = AddToggleRow(form, out _bgvRow, "BGVモード (配信のBGVとして再生)",
                () => { _bgv = !_bgv; UpdateToggles(); });
            _otherButton = AddToggleRow(form, out _otherRow, "別プレイヤー再生",
                () => { _otherplayer = !_otherplayer; UpdateToggles(); });
            _otherButtonLabel = _otherButton.GetComponentInChildren<Text>();

            // 音量 / 音ズレ (半幅のミニステッパーで横に並べる)
            var finePair = AddPanel(form, 158f, transparent: true);
            _volumeValueText = AddHalfStepper(finePair, 0f, 0.5f, "音量増減",
                () => { _volume = Mathf.Max(_volume - 10, -100); UpdateOptionTexts(); },
                () => { _volume = Mathf.Min(_volume + 10, 100); UpdateOptionTexts(); });
            _delayValueText = AddHalfStepper(finePair, 0.5f, 1f, "音ズレ補正",
                () => { _audioDelay = Mathf.Max(_audioDelay - 100, -9900); UpdateOptionTexts(); },
                () => { _audioDelay = Mathf.Min(_audioDelay + 100, 9900); UpdateOptionTexts(); });

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
            UiFactory.AddHorizontalMoreIndicator(scroll); // 右に続きがあるとき「›」を出す

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

            void AddChip(string name, Color bg)
            {
                var chip = UiFactory.CreateButton(_singerChipContent, name, name, bg, Color.white, 26);
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Max(UiFactory.EstimateTextWidth(name, 26) + 44f, 120f);
                chip.onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    _nameInput.text = name;
                });
                _singerChips.Add(chip.gameObject);
            }

            // 小休止の予約は歌う人を「小休止」にできる (確認画面を通らず挿入されるため変更時用)
            bool isPause = _entry != null && _entry.Kind == "小休止";
            if (isPause)
            {
                AddChip("小休止", UiFactory.TextMuted);
            }
            foreach (var singer in singers)
            {
                AddChip(singer, UiFactory.PrimaryDark);
            }
            _singerRow.SetActive(singers.Count > 0 || isPause);
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
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, UiFactory.LineHeight(24));
        }

        static void SetPanelControl(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 12f);
            rect.offsetMin = new Vector2(24f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-24f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
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

            var minus = UiFactory.CreateSoftButton(panel, "Minus", "−", 34);
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
            UiFactory.FitLabel(value, 18); // トラック名など長い値でも枠内に収める

            var plus = UiFactory.CreateSoftButton(panel, "Plus", "＋", 34);
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

        /// <summary>半幅トグルボタン (x0〜x1 は 0〜1 の分割率)。</summary>
        Button AddHalfToggle(RectTransform panel, float x0, float x1, string label, System.Action onToggle)
        {
            var button = UiFactory.CreateButton(panel, "Toggle", label, ToggleOffColor, Color.white, 24);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(x0, 0f);
            rect.anchorMax = new Vector2(x1, 1f);
            rect.offsetMin = new Vector2(x0 == 0f ? 0f : 8f, 0f);
            rect.offsetMax = new Vector2(x1 == 1f ? 0f : -8f, 0f);
            button.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onToggle();
            });
            return button;
        }

        /// <summary>半幅のミニステッパー (ラベル上段 + [−] 値 [＋] 下段)。値表示 Text を返す。</summary>
        Text AddHalfStepper(RectTransform panel, float x0, float x1, string label,
                            System.Action onMinus, System.Action onPlus)
        {
            var cardGo = new GameObject(label);
            cardGo.transform.SetParent(panel, false);
            var img = cardGo.AddComponent<Image>();
            img.color = UiFactory.CardBg;
            UiFactory.Roundify(img);
            var card = (RectTransform)cardGo.transform;
            card.anchorMin = new Vector2(x0, 0f);
            card.anchorMax = new Vector2(x1, 1f);
            card.offsetMin = new Vector2(x0 == 0f ? 0f : 8f, 0f);
            card.offsetMax = new Vector2(x1 == 1f ? 0f : -8f, 0f);

            var labelText = UiFactory.CreateText(card, "Label", label, 24, UiFactory.PrimaryDark);
            var labelRect = labelText.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -10f);
            labelRect.sizeDelta = new Vector2(0f, 32f);

            var minus = UiFactory.CreateSoftButton(card, "Minus", "−", 30);
            var minusRect = (RectTransform)minus.transform;
            minusRect.anchorMin = minusRect.anchorMax = new Vector2(0f, 0f);
            minusRect.pivot = new Vector2(0f, 0f);
            minusRect.anchoredPosition = new Vector2(14f, 12f);
            minusRect.sizeDelta = new Vector2(100f, 66f);
            minus.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onMinus();
            });

            var value = UiFactory.CreateText(card, "Value", "", 26, UiFactory.TextDark);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 0f);
            valueRect.pivot = new Vector2(0.5f, 0f);
            valueRect.anchoredPosition = new Vector2(0f, 12f);
            valueRect.offsetMin = new Vector2(118f, valueRect.offsetMin.y);
            valueRect.offsetMax = new Vector2(-118f, valueRect.offsetMax.y);
            valueRect.sizeDelta = new Vector2(valueRect.sizeDelta.x, 66f);
            UiFactory.FitLabel(value, 18);

            var plus = UiFactory.CreateSoftButton(card, "Plus", "＋", 30);
            var plusRect = (RectTransform)plus.transform;
            plusRect.anchorMin = plusRect.anchorMax = new Vector2(1f, 0f);
            plusRect.pivot = new Vector2(1f, 0f);
            plusRect.anchoredPosition = new Vector2(-14f, 12f);
            plusRect.sizeDelta = new Vector2(100f, 66f);
            plus.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                onPlus();
            });

            return value;
        }

        // ---- 表示更新 ----

        /// <summary>曲名・補足の行数に合わせて曲カードの中身と高さを更新する。</summary>
        void LayoutSongCard()
        {
            const float wrapWidth = 940f;
            string title = _entry.Line1 ?? "";
            float y = 52f;

            int titleLines = UiFactory.EstimateWrapLines(title, 32, wrapWidth);
            float titleHeight = titleLines * UiFactory.LineHeight(32) + 4f;
            _songText.text = UiFactory.NoWordWrap(title);
            var titleRect = _songText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -y);
            titleRect.offsetMin = new Vector2(24f, titleRect.offsetMin.y);
            titleRect.offsetMax = new Vector2(-24f, titleRect.offsetMax.y);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, titleHeight);
            y += titleHeight;

            bool hasSub = !string.IsNullOrEmpty(_entry.Line2);
            _songSubText.gameObject.SetActive(hasSub);
            if (hasSub)
            {
                y += 6f;
                int subLines = UiFactory.EstimateWrapLines(_entry.Line2, 25, wrapWidth);
                float subHeight = subLines * UiFactory.LineHeight(25) + 4f;
                _songSubText.text = UiFactory.NoWordWrap(_entry.Line2);
                var subRect = _songSubText.rectTransform;
                subRect.anchorMin = new Vector2(0f, 1f);
                subRect.anchorMax = new Vector2(1f, 1f);
                subRect.pivot = new Vector2(0.5f, 1f);
                subRect.anchoredPosition = new Vector2(0f, -y);
                subRect.offsetMin = new Vector2(24f, subRect.offsetMin.y);
                subRect.offsetMax = new Vector2(-24f, subRect.offsetMax.y);
                subRect.sizeDelta = new Vector2(subRect.sizeDelta.x, subHeight);
                y += subHeight;
            }

            // 種別バッジ (URL指定 / 小休止)
            bool hasKind = !string.IsNullOrEmpty(_entry.Kind);
            _kindBadgeRect.gameObject.SetActive(hasKind);
            if (hasKind)
            {
                _kindBadgeText.text = _entry.Kind;
                _kindBadgeRect.sizeDelta = new Vector2(
                    UiFactory.EstimateTextWidth(_entry.Kind, 22) + 40f, 40f);
            }

            _songPanelLe.preferredHeight = y + 16f;
        }

        void UpdateMypageButtons()
        {
            bool inFavorite = _entry != null && LocalMypage.IsInFavorite(_entry.FullPath);
            bool inLater = _entry != null && LocalMypage.IsInLater(_entry.FullPath);
            _favoriteButton.image.color = inFavorite ? UiFactory.Primary : ToggleOffColor;
            _favoriteButton.GetComponentInChildren<Text>().text = inFavorite ? "✓ お気に入り" : "お気に入り";
            _laterButton.image.color = inLater ? UiFactory.Primary : ToggleOffColor;
            _laterButton.GetComponentInChildren<Text>().text = inLater ? "✓ あとで歌う" : "あとで歌う";
        }

        /// <summary>トグルの見た目 (色 + 先頭の ✓) を状態に合わせる。</summary>
        static void SetToggle(Button button, bool on)
        {
            button.image.color = on ? UiFactory.Primary : ToggleOffColor;
            var label = button.GetComponentInChildren<Text>();
            string baseLabel = label.text.StartsWith("✓ ") ? label.text.Substring(2) : label.text;
            label.text = on ? "✓ " + baseLabel : baseLabel;
        }

        void UpdateToggles()
        {
            SetToggle(_secretButton, _secret);
            SetToggle(_bgvButton, _bgv);
            SetToggle(_otherButton, _otherplayer);
            SetToggle(_pauseButton, _pause);
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

            LayoutSongCard();
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

            // ファイル詳細は取得できるまで隠しておく
            _videoDetails = null;
            _durationSeconds = 0;
            _detailsOpen = false;
            UpdateDetailsRow();

            _ = ApplyCapabilitiesAsync();
            _ = LoadFileDetailsAsync();
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

        /// <summary>
        /// ファイル詳細 (音声トラック + 動画詳細情報) を取得して、
        /// トラック選択行の出し分け (Web 版と同じ規則) と詳細プルダウンの表示を行う。
        /// </summary>
        async Task LoadFileDetailsAsync()
        {
            if (!IsVideoFile(_entry.FullPath))
            {
                return;
            }
            var entryAtLoad = _entry;
            List<string> labels;
            try
            {
                var details = await AppConfig.CreateClient().GetFileDetailsAsync(_entry.FullPath);
                labels = details.Tracks ?? new List<string>();
                if (entryAtLoad != _entry)
                {
                    return; // 画面が別の曲に切り替わっていたら破棄
                }
                _videoDetails = details.Details;
                _durationSeconds = details.Details != null ? details.Details.DurationSeconds : 0;
                UpdateDetailsRow();
            }
            catch
            {
                labels = new List<string>();
            }
            if (entryAtLoad != _entry)
            {
                return;
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

        /// <summary>動画詳細情報プルダウンの中身と開閉状態を反映する。</summary>
        void UpdateDetailsRow()
        {
            if (_videoDetails == null)
            {
                _detailsRow.SetActive(false);
                return;
            }
            _detailsRow.SetActive(true);
            _detailsHeaderText.text = _detailsOpen ? "動画詳細情報 ▲" : "動画詳細情報 ▼";

            var lines = new List<string>();
            var d = _videoDetails;
            if (!string.IsNullOrEmpty(d.Duration))
            {
                lines.Add("曲の長さ: " + d.Duration);
            }
            if (!string.IsNullOrEmpty(d.Resolution))
            {
                lines.Add("解像度: " + d.Resolution);
            }
            if (d.FrameRate > 0f)
            {
                lines.Add("フレームレート: " + d.FrameRate + " fps");
            }
            if (!string.IsNullOrEmpty(d.VideoCodec))
            {
                lines.Add("映像コーデック: " + d.VideoCodec);
            }
            if (!string.IsNullOrEmpty(d.AudioCodec))
            {
                string audio = d.AudioCodec;
                if (!string.IsNullOrEmpty(d.AudioChannels))
                {
                    audio += " / " + d.AudioChannels;
                }
                if (!string.IsNullOrEmpty(d.AudioSampleRate))
                {
                    audio += " / " + d.AudioSampleRate;
                }
                lines.Add("音声コーデック: " + audio);
            }
            if (!string.IsNullOrEmpty(d.Bitrate))
            {
                lines.Add("ビットレート: " + d.Bitrate);
            }

            _detailsText.text = string.Join("\n", lines);
            _detailsText.gameObject.SetActive(_detailsOpen);
            _detailsLe.preferredHeight = 64f + (_detailsOpen ? lines.Count * UiFactory.LineHeight(25) + 16f : 0f);
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
            // 「小休止」名義は自分の名前として覚えない (次回の予約者名を汚さない)
            if (name != "小休止")
            {
                AppConfig.Username = name;
            }
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
                Duration = _durationSeconds, // 残り時間の計算用 (Web 版と同じ)
                SelectId = _editId, // 負なら新規、既存 id なら差し替え (変更)
            };
            string kind = string.IsNullOrEmpty(_entry.Kind) ? "動画" : _entry.Kind;
            try
            {
                int newId = await AppConfig.CreateClient().PostRequestAsync(
                    _entry.Filename, _entry.FullPath, name,
                    (_commentInput.text ?? "").Trim(), kind, options);
                _completedId = newId > 0 ? newId : _editId;
                if (_editId < 0)
                {
                    LocalMypage.AddHistory(_entry.FullPath, _entry.Line1, kind);
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
            ApplyCompletePose();
            _completeMessage.text = _editId >= 0 ? "変更したよ♪" : "予約したよ♪";
            Se.Play(Se.ReservationComplete);
            _completeOverlay.SetActive(true);
            StartCoroutine(CompletePopRoutine());
        }

        /// <summary>完了画面のマスコット: スキンにキャラ画像があればそれ、無ければゆかりちゃん。</summary>
        void ApplyCompletePose()
        {
            var skin = SkinManager.Current();
            string key = skin.Id + "#" + SkinManager.Revision;
            if (key == _poseSkinKey)
            {
                return;
            }
            _poseSkinKey = key;

            Texture2D tex = null;
            var characters = SkinManager.GetCharacters(skin);
            if (characters.Count > 0 && characters[0].Type == "image")
            {
                tex = SkinManager.LoadTexture(skin, characters[0].File); // 完了画面は1枚目のキャラ
            }
            var oldSprite = _completePoseImage.sprite;
            if (tex != null)
            {
                _completePoseImage.sprite = Sprite.Create(tex,
                    new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                _completePoseImage.sprite = _defaultPoseSprite;
            }
            // スキン由来の旧スプライト・テクスチャは差し替え時に破棄する
            if (oldSprite != null && oldSprite != _defaultPoseSprite && oldSprite != _completePoseImage.sprite)
            {
                Destroy(oldSprite);
            }
            if (_poseSkinTex != null && _poseSkinTex != tex)
            {
                Destroy(_poseSkinTex);
            }
            _poseSkinTex = tex;
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
                // 新規・変更とも予約一覧へ遷移し、いま予約したカードの位置を見せる
                if (_completedId >= 0)
                {
                    QueueScreen.OpenAndFocus(Manager, _completedId);
                }
                else
                {
                    Manager.BackTo<QueueScreen>();
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
            _completePoseImage = pose;
            _defaultPoseSprite = pose.sprite;
            _poseSkinKey = null;
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
