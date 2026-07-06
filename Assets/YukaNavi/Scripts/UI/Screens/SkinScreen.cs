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

        // スキン作成モーダル
        GameObject _createModal;
        InputField _skinNameInput;
        Text _bgPickText;
        Text _charPickText;
        Text _createErrorText;
        Button _charNoneButton;
        Text _modalTitleText;
        Text _saveButtonLabel;
        string _pickedBg;
        string _pickedChar;
        /// <summary>キャラの扱い: 0=ゆかりちゃんのまま 1=画像 2=キャラなし</summary>
        int _charMode;
        /// <summary>編集対象 (null なら新規作成)</summary>
        SkinDef _editingSkin;

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
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreatePanel(transform, "TopBar", UiFactory.Primary);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 110f);
            var title = UiFactory.CreateText(topBar, "Title", "きせかえ", 42, Color.white);
            UiFactory.StretchFull(title.rectTransform);

            var caption = UiFactory.CreateText(transform, "Caption",
                "手持ちの画像や動画で、背景とキャラをカスタマイズできます", 26, UiFactory.TextDark);
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -122f);
            captionRect.sizeDelta = new Vector2(-40f, 40f);

            // 操作ボタン行 (スキンを作る / 取り込む / フォルダを開く)
            var buttonBar = UiFactory.CreatePanel(transform, "Buttons");
            buttonBar.anchorMin = new Vector2(0f, 1f);
            buttonBar.anchorMax = new Vector2(1f, 1f);
            buttonBar.pivot = new Vector2(0.5f, 1f);
            buttonBar.anchoredPosition = new Vector2(0f, -170f);
            buttonBar.offsetMin = new Vector2(20f, buttonBar.offsetMin.y);
            buttonBar.offsetMax = new Vector2(-20f, buttonBar.offsetMax.y);
            buttonBar.sizeDelta = new Vector2(buttonBar.sizeDelta.x, 80f);
            var buttonLayout = buttonBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childForceExpandHeight = true;
            buttonLayout.spacing = 10f;

            var createButton = UiFactory.CreateButton(buttonBar, "Create", "＋ 作る",
                UiFactory.Primary, Color.white, 28);
            createButton.onClick.AddListener(OpenCreateModal);

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
            var importButton = UiFactory.CreateButton(buttonBar, "Import", "取り込む (zip)",
                UiFactory.Primary, Color.white, 28);
            importButton.onClick.AddListener(ImportSkinFlow);
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var openButton = UiFactory.CreateButton(buttonBar, "OpenFolder", "フォルダを開く",
                UiFactory.PrimaryDark, Color.white, 28);
            openButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                SkinManager.EnsureRoot();
                Application.OpenURL("file:///" + SkinManager.SkinsRoot.Replace('\\', '/'));
            });
#endif

            _pathText = UiFactory.CreateText(transform, "Path", "", 20, new Color(0.5f, 0.47f, 0.6f));
            var pathRect = _pathText.rectTransform;
            pathRect.anchorMin = new Vector2(0f, 1f);
            pathRect.anchorMax = new Vector2(1f, 1f);
            pathRect.pivot = new Vector2(0.5f, 1f);
            pathRect.anchoredPosition = new Vector2(0f, -258f);
            pathRect.sizeDelta = new Vector2(-40f, 48f);

            var scrollRectT = UiFactory.CreateScrollList(transform, "SkinList", out _listContent);
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRectT.offsetMax = new Vector2(-20f, -314f);

            BuildCreateModal();
        }

        void BuildCreateModal()
        {
            _createModal = new GameObject("CreateModal");
            _createModal.transform.SetParent(transform, false);
            var overlayRect = _createModal.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _createModal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);

            var card = UiFactory.CreatePanel(_createModal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(940f, 1480f);
            // カード内タップがオーバーレイに抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            _modalTitleText = UiFactory.CreateText(card, "Title", "スキンを作る", 38, UiFactory.PrimaryDark);
            SetCardRow(_modalTitleText.rectTransform, -22f, 54f);

            var nameLabel = UiFactory.CreateText(card, "NameLabel", "スキンの名前", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(nameLabel.rectTransform, -88f, 34f);
            _skinNameInput = UiFactory.CreateInputField(card, "NameInput", "例: 夏フェス", 30);
            SetCardRow(_skinNameInput.GetComponent<RectTransform>(), -124f, 76f);

            var bgButton = UiFactory.CreateButton(card, "PickBg", "背景を選ぶ (画像 / 動画)",
                UiFactory.Primary, Color.white, 28);
            SetCardRow(bgButton.GetComponent<RectTransform>(), -222f, 80f);
            bgButton.onClick.AddListener(() => PickFile(true));
            _bgPickText = UiFactory.CreateText(card, "BgPicked", "未選択", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_bgPickText.rectTransform, -304f, 30f);

            BuildPreviewArea(card);

            var charButton = UiFactory.CreateButton(card, "PickChar", "キャラ画像を選ぶ (任意)",
                UiFactory.Primary, Color.white, 28);
            var charRect = charButton.GetComponent<RectTransform>();
            charRect.anchorMin = charRect.anchorMax = new Vector2(0f, 1f);
            charRect.pivot = new Vector2(0f, 1f);
            charRect.anchoredPosition = new Vector2(50f, -900f);
            charRect.sizeDelta = new Vector2(490f, 80f);
            charButton.onClick.AddListener(() => PickFile(false));

            _charNoneButton = UiFactory.CreateButton(card, "CharNone", "キャラなし",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 28);
            var noneRect = _charNoneButton.GetComponent<RectTransform>();
            noneRect.anchorMin = noneRect.anchorMax = new Vector2(1f, 1f);
            noneRect.pivot = new Vector2(1f, 1f);
            noneRect.anchoredPosition = new Vector2(-50f, -900f);
            noneRect.sizeDelta = new Vector2(320f, 80f);
            _charNoneButton.onClick.AddListener(ToggleCharNone);

            _charPickText = UiFactory.CreateText(card, "CharPicked", "未選択 (ゆかりちゃんのまま)", 22,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_charPickText.rectTransform, -982f, 30f);

            var hint = UiFactory.CreateText(card, "Hint",
                "※ 選んだファイルはアプリ内にコピーされます", 20, new Color(0.5f, 0.47f, 0.6f));
            SetCardRow(hint.rectTransform, -1020f, 28f);

            _createErrorText = UiFactory.CreateText(card, "Error", "", 24, UiFactory.Danger);
            SetCardRow(_createErrorText.rectTransform, -1056f, 36f);

            var saveButton = UiFactory.CreateButton(card, "Save", "作成する", UiFactory.Primary, Color.white, 34);
            _saveButtonLabel = saveButton.GetComponentInChildren<Text>();
            var saveRect = saveButton.GetComponent<RectTransform>();
            saveRect.anchorMin = saveRect.anchorMax = new Vector2(0.5f, 0f);
            saveRect.pivot = new Vector2(0.5f, 0f);
            saveRect.anchoredPosition = new Vector2(-190f, 36f);
            saveRect.sizeDelta = new Vector2(340f, 92f);
            saveButton.onClick.AddListener(CreateSkin);

            var cancelButton = UiFactory.CreateButton(card, "Cancel", "やめる",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 34);
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
        void BuildPreviewArea(RectTransform card)
        {
            // プレビュー枠 (左寄せ、9:16)
            var frame = UiFactory.CreatePanel(card, "PreviewFrame", new Color(0.15f, 0.13f, 0.2f));
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(60f, -346f);
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

            // 調整ボタン列 (プレビューの右)
            var buttons = UiFactory.CreatePanel(card, "AdjustButtons");
            buttons.anchorMin = buttons.anchorMax = new Vector2(0f, 1f);
            buttons.pivot = new Vector2(0f, 1f);
            buttons.anchoredPosition = new Vector2(380f, -346f);
            buttons.sizeDelta = new Vector2(500f, PreviewSize.y);
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
            dragHintLe.preferredHeight = 90f;

            _adjustButtons = buttons.gameObject;
            _adjustButtons.SetActive(false);
        }

        void AddAdjustButton(RectTransform parent, string label, System.Action onClick)
        {
            var button = UiFactory.CreateButton(parent, label, label, UiFactory.PrimaryDark, Color.white, 26);
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 78f;
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
                SetMessage("取り込めませんでした (skin.json 入りの zip を選んでください)");
                Se.Play(Se.Error);
                return;
            }
            AppConfig.SkinId = id;
            SetMessage("スキンを取り込みました");
            Se.Play(Se.Confirm);
            Rebuild();
        }

        /// <summary>スキンを zip に書き出して共有 (Android) またはフォルダ表示 (Windows/エディタ) する。</summary>
        void ExportSkinFlow(SkinDef skin)
        {
            Se.Play(Se.Tap);
            string zipPath = SkinManager.ExportSkin(skin);
            if (zipPath == null)
            {
                SetMessage("書き出しに失敗しました");
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

        void SetMessage(string message)
        {
            _pathText.text = message;
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

        void CreateSkin()
        {
            string name = (_skinNameInput.text ?? "").Trim();
            if (name == "")
            {
                _createErrorText.text = "スキンの名前を入力してください";
                Se.Play(Se.Error);
                return;
            }

            if (_editingSkin != null)
            {
                // 既存スキンの更新
                if (!SkinManager.UpdateSkin(_editingSkin, name, _pickedBg,
                        _charMode == 1 ? _pickedChar : null,
                        _adjRotation, _adjZoom, _adjOffset, _charMode))
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
                if (_pickedBg == null && _charMode == 0)
                {
                    _createErrorText.text = "背景を選ぶか、キャラの設定を変えてください";
                    Se.Play(Se.Error);
                    return;
                }
                string id = SkinManager.CreateSkin(name, _pickedBg,
                    _charMode == 1 ? _pickedChar : null,
                    _adjRotation, _adjZoom, _adjOffset,
                    _charMode == 2);
                if (id == null)
                {
                    _createErrorText.text = "スキンの作成に失敗しました";
                    Se.Play(Se.Error);
                    return;
                }
                AppConfig.SkinId = id;
            }
            SkinManager.BumpRevision();
            Se.Play(Se.Confirm);
            _createModal.SetActive(false);
            Rebuild();
        }

        public override void OnShow()
        {
            _createModal.SetActive(false);
            _pathText.text = SkinManager.SkinsRoot;
            Rebuild();
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
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 110f;
            var button = rowGo.AddComponent<Button>();
            string skinId = skin.Id;
            button.onClick.AddListener(() => Apply(skinId));

            string label = (selected ? "✓ " : "") + skin.Name;
            var text = UiFactory.CreateText(rowGo.transform, "Name", label, 32,
                selected ? UiFactory.PrimaryDark : UiFactory.TextDark, TextAnchor.MiddleLeft);
            UiFactory.StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24f, 6f);
            text.rectTransform.offsetMax = new Vector2(-330f, -6f);

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
                        Se.Play(Se.Confirm);
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
            Se.Play(Se.Confirm);
            Rebuild();
        }
    }
}
