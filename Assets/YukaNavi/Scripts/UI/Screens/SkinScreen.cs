using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// きせかえ (スキン選択) 画面。skins/ フォルダのスキンを一覧し、タップで適用する。
    /// 「スキンを作る」から端末の画像/動画を選んでスキンを新規作成できる
    /// (サーバーには何も置かない、端末内で完結するカスタマイズ)。
    /// </summary>
    public class SkinScreen : ScreenBase
    {
        RectTransform _listContent;
        Text _pathText;
        readonly List<GameObject> _rows = new List<GameObject>();

        // スキン作成モーダル
        GameObject _createModal;
        InputField _skinNameInput;
        Text _bgPickText;
        Text _charPickText;
        Text _createErrorText;
        string _pickedBg;
        string _pickedChar;

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

            // 操作ボタン行 (スキンを作る / フォルダを開く)
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
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.55f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(940f, 860f);
            // カード内タップがオーバーレイに抜けないようにする
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.transition = Selectable.Transition.None;

            var title = UiFactory.CreateText(card, "Title", "スキンを作る", 40, UiFactory.PrimaryDark);
            SetCardRow(title.rectTransform, -30f, 60f);

            var nameLabel = UiFactory.CreateText(card, "NameLabel", "スキンの名前", 28,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(nameLabel.rectTransform, -110f, 36f);
            _skinNameInput = UiFactory.CreateInputField(card, "NameInput", "例: 夏フェス");
            SetCardRow(_skinNameInput.GetComponent<RectTransform>(), -150f, 80f);

            var bgButton = UiFactory.CreateButton(card, "PickBg", "背景を選ぶ (画像 / 動画)",
                UiFactory.Primary, Color.white, 30);
            SetCardRow(bgButton.GetComponent<RectTransform>(), -260f, 84f);
            bgButton.onClick.AddListener(() => PickFile(true));
            _bgPickText = UiFactory.CreateText(card, "BgPicked", "未選択", 24,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_bgPickText.rectTransform, -348f, 34f);

            var charButton = UiFactory.CreateButton(card, "PickChar", "キャラ画像を選ぶ (任意)",
                UiFactory.Primary, Color.white, 30);
            SetCardRow(charButton.GetComponent<RectTransform>(), -400f, 84f);
            charButton.onClick.AddListener(() => PickFile(false));
            _charPickText = UiFactory.CreateText(card, "CharPicked", "未選択 (ゆかりちゃんのまま)", 24,
                new Color(0.5f, 0.47f, 0.6f), TextAnchor.MiddleLeft);
            SetCardRow(_charPickText.rectTransform, -488f, 34f);

            var hint = UiFactory.CreateText(card, "Hint",
                "※ 選んだファイルはアプリ内にコピーされます", 22, new Color(0.5f, 0.47f, 0.6f));
            SetCardRow(hint.rectTransform, -534f, 32f);

            _createErrorText = UiFactory.CreateText(card, "Error", "", 26, UiFactory.Danger);
            SetCardRow(_createErrorText.rectTransform, -576f, 40f);

            var saveButton = UiFactory.CreateButton(card, "Save", "作成する", UiFactory.Primary, Color.white, 36);
            var saveRect = saveButton.GetComponent<RectTransform>();
            saveRect.anchorMin = saveRect.anchorMax = new Vector2(0.5f, 0f);
            saveRect.pivot = new Vector2(0.5f, 0f);
            saveRect.anchoredPosition = new Vector2(-190f, 40f);
            saveRect.sizeDelta = new Vector2(340f, 96f);
            saveButton.onClick.AddListener(CreateSkin);

            var cancelButton = UiFactory.CreateButton(card, "Cancel", "やめる",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 36);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(190f, 40f);
            cancelRect.sizeDelta = new Vector2(340f, 96f);
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
            Application.OpenURL("file:///" + System.IO.Path.GetDirectoryName(zipPath).Replace('\\', '/'));
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

        void OpenCreateModal()
        {
            Se.Play(Se.Tap);
            _skinNameInput.text = "";
            _pickedBg = null;
            _pickedChar = null;
            _bgPickText.text = "未選択";
            _charPickText.text = "未選択 (ゆかりちゃんのまま)";
            _createErrorText.text = "";
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
            }
            else
            {
                _pickedChar = path;
                _charPickText.text = "選択済み: " + Path.GetFileName(path);
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
            if (_pickedBg == null && _pickedChar == null)
            {
                _createErrorText.text = "背景かキャラ画像のどちらかを選んでください";
                Se.Play(Se.Error);
                return;
            }
            string id = SkinManager.CreateSkin(name, _pickedBg, _pickedChar);
            if (id == null)
            {
                _createErrorText.text = "スキンの作成に失敗しました";
                Se.Play(Se.Error);
                return;
            }
            AppConfig.SkinId = id;
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

            // ユーザースキンには書き出し/削除ボタンを付ける
            if (skin.Folder != null)
            {
                var exportButton = UiFactory.CreateButton(rowGo.transform, "Export", "書き出し",
                    UiFactory.PrimaryDark, Color.white, 24);
                var expRect = exportButton.GetComponent<RectTransform>();
                expRect.anchorMin = new Vector2(1f, 0.5f);
                expRect.anchorMax = new Vector2(1f, 0.5f);
                expRect.pivot = new Vector2(1f, 0.5f);
                expRect.anchoredPosition = new Vector2(-172f, 0f);
                expRect.sizeDelta = new Vector2(146f, 76f);
                exportButton.onClick.AddListener(() => ExportSkinFlow(skin));

                var deleteButton = UiFactory.CreateButton(rowGo.transform, "Delete", "削除",
                    UiFactory.Danger, Color.white, 26);
                var delRect = deleteButton.GetComponent<RectTransform>();
                delRect.anchorMin = new Vector2(1f, 0.5f);
                delRect.anchorMax = new Vector2(1f, 0.5f);
                delRect.pivot = new Vector2(1f, 0.5f);
                delRect.anchoredPosition = new Vector2(-16f, 0f);
                delRect.sizeDelta = new Vector2(146f, 76f);
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
