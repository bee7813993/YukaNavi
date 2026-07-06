using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 予約確認モーダル + 完了演出の共通部品 (検索画面・マイページで共用)。
    /// Open() で確認を開き、「予約する」で exec.php へ投稿して完了演出を出す。
    /// </summary>
    public class ReserveDialog : MonoBehaviour
    {
        /// <summary>予約候補の共通形。</summary>
        public class Entry
        {
            public string Line1;     // 曲名 (またはファイル名)
            public string Line2;     // 歌手 / 作品などの補足 (無ければ空)
            public string Filename;  // 予約に使う表示ファイル名
            public string FullPath;  // 予約に使うフルパス
        }

        GameObject _modal;
        Text _songText;
        Text _errorText;
        InputField _nameInput;
        InputField _commentInput;
        Button _submitButton;
        Button _favoriteButton;
        Button _laterButton;
        Entry _entry;

        static readonly Color ToggleOffColor = new Color(0.75f, 0.73f, 0.80f);

        GameObject _completeOverlay;
        RectTransform _completePose;

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

        public static ReserveDialog Create(Transform parent)
        {
            var go = new GameObject("ReserveDialog");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            var dialog = go.AddComponent<ReserveDialog>();
            dialog.BuildModal();
            dialog.BuildCompleteOverlay();
            return dialog;
        }

        void BuildModal()
        {
            _modal = new GameObject("ConfirmModal");
            _modal.transform.SetParent(transform, false);
            var overlayRect = _modal.AddComponent<RectTransform>();
            UiFactory.StretchFull(overlayRect);
            var overlay = _modal.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);

            var card = UiFactory.CreatePanel(_modal.transform, "Card", Color.white);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.55f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(940f, 920f);

            var title = UiFactory.CreateText(card, "Title", "この曲を予約しますか？", 40, UiFactory.PrimaryDark);
            SetCardRow(title.rectTransform, -30f, 60f);

            _songText = UiFactory.CreateText(card, "Song", "", 32, UiFactory.TextDark);
            SetCardRow(_songText.rectTransform, -105f, 130f);

            var nameLabel = UiFactory.CreateText(card, "NameLabel", "歌う人の名前", 28,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(nameLabel.rectTransform, -255f, 36f);
            _nameInput = UiFactory.CreateInputField(card, "NameInput", "名前 (必須)");
            SetCardRow(_nameInput.GetComponent<RectTransform>(), -295f, 80f);

            var commentLabel = UiFactory.CreateText(card, "CommentLabel", "コメント (任意)", 28,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetCardRow(commentLabel.rectTransform, -395f, 36f);
            _commentInput = UiFactory.CreateInputField(card, "CommentInput", "");
            SetCardRow(_commentInput.GetComponent<RectTransform>(), -435f, 80f);

            // お気に入り / あとで歌う のトグル (端末ローカル保存)
            _favoriteButton = UiFactory.CreateButton(card, "Favorite", "お気に入り",
                ToggleOffColor, Color.white, 28);
            var favRect = _favoriteButton.GetComponent<RectTransform>();
            favRect.anchorMin = favRect.anchorMax = new Vector2(0f, 1f);
            favRect.pivot = new Vector2(0f, 1f);
            favRect.anchoredPosition = new Vector2(50f, -540f);
            favRect.sizeDelta = new Vector2(410f, 76f);
            _favoriteButton.onClick.AddListener(() =>
            {
                if (_entry == null)
                {
                    return;
                }
                bool added = LocalMypage.ToggleFavorite(_entry.FullPath, _entry.Line1, "動画");
                Se.Play(added ? Se.Confirm : Se.Tap);
                UpdateToggleButtons();
            });

            _laterButton = UiFactory.CreateButton(card, "Later", "あとで歌う",
                ToggleOffColor, Color.white, 28);
            var laterRect = _laterButton.GetComponent<RectTransform>();
            laterRect.anchorMin = laterRect.anchorMax = new Vector2(1f, 1f);
            laterRect.pivot = new Vector2(1f, 1f);
            laterRect.anchoredPosition = new Vector2(-50f, -540f);
            laterRect.sizeDelta = new Vector2(410f, 76f);
            _laterButton.onClick.AddListener(() =>
            {
                if (_entry == null)
                {
                    return;
                }
                bool added = LocalMypage.ToggleLater(_entry.FullPath, _entry.Line1, "動画");
                Se.Play(added ? Se.Confirm : Se.Tap);
                UpdateToggleButtons();
            });

            _errorText = UiFactory.CreateText(card, "Error", "", 26, UiFactory.Danger);
            SetCardRow(_errorText.rectTransform, -630f, 40f);

            _submitButton = UiFactory.CreateButton(card, "Submit", "予約する", UiFactory.Primary, Color.white, 38);
            var submitRect = _submitButton.GetComponent<RectTransform>();
            submitRect.anchorMin = submitRect.anchorMax = new Vector2(0.5f, 0f);
            submitRect.pivot = new Vector2(0.5f, 0f);
            submitRect.anchoredPosition = new Vector2(-190f, 40f);
            submitRect.sizeDelta = new Vector2(340f, 96f);
            _submitButton.onClick.AddListener(() => _ = SubmitAsync());

            var cancelButton = UiFactory.CreateButton(card, "Cancel", "やめる",
                new Color(0.75f, 0.73f, 0.80f), Color.white, 38);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(190f, 40f);
            cancelRect.sizeDelta = new Vector2(340f, 96f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                _modal.SetActive(false);
            });

            _modal.SetActive(false);
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
            });

            var message = UiFactory.CreateText(_completeOverlay.transform, "Message", "予約したよ♪", 64, UiFactory.Primary);
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

            var hint = UiFactory.CreateText(_completeOverlay.transform, "Hint", "タップで閉じる", 30, UiFactory.PrimaryDark);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 60f);
            hintRect.sizeDelta = new Vector2(600f, 44f);

            _completeOverlay.SetActive(false);
        }

        /// <summary>予約確認を開く。</summary>
        public void Open(Entry entry)
        {
            Se.Play(Se.Tap);
            _entry = entry;
            _songText.text = string.IsNullOrEmpty(entry.Line2)
                ? entry.Line1
                : entry.Line1 + "\n" + entry.Line2;
            _errorText.text = "";
            _nameInput.text = AppConfig.Username;
            _commentInput.text = "";
            _submitButton.interactable = true;
            UpdateToggleButtons();
            _modal.SetActive(true);
        }

        void UpdateToggleButtons()
        {
            bool inFavorite = _entry != null && LocalMypage.IsInFavorite(_entry.FullPath);
            bool inLater = _entry != null && LocalMypage.IsInLater(_entry.FullPath);
            _favoriteButton.image.color = inFavorite ? UiFactory.Primary : ToggleOffColor;
            _favoriteButton.GetComponentInChildren<Text>().text = inFavorite ? "✓ お気に入り" : "お気に入り";
            _laterButton.image.color = inLater ? UiFactory.Primary : ToggleOffColor;
            _laterButton.GetComponentInChildren<Text>().text = inLater ? "✓ あとで歌う" : "あとで歌う";
        }

        /// <summary>モーダルと完了演出を閉じる (画面の OnShow 用)。</summary>
        public void HideAll()
        {
            _modal.SetActive(false);
            _completeOverlay.SetActive(false);
        }

        async Task SubmitAsync()
        {
            if (_entry == null)
            {
                return;
            }
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
            try
            {
                await AppConfig.CreateClient().PostRequestAsync(
                    _entry.Filename, _entry.FullPath, name, (_commentInput.text ?? "").Trim());
                LocalMypage.AddHistory(_entry.FullPath, _entry.Line1, "動画");
                _modal.SetActive(false);
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
            Se.Play(Se.ReservationComplete);
            _completeOverlay.SetActive(true);
            StartCoroutine(CompletePopRoutine());
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
