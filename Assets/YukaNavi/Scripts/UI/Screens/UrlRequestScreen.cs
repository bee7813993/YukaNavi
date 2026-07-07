using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// URL 指定リクエスト画面。YouTube などの URL を入力してプレイヤーで直接再生する
    /// 予約 (kind=URL指定) を作る。Web 版の request_confirm_url_bs5.php 相当で、
    /// 名前やオプションは予約確認画面に任せる。Android の共有メニューからの URL も
    /// ここに流れてくる (OpenWithUrl)。
    /// サーバーの connectinternet が無効のときは検索トップに入口が出ない。
    /// </summary>
    public class UrlRequestScreen : ScreenBase
    {
        static string _pendingUrl;

        InputField _urlInput;
        InputField _titleInput;
        Text _errorText;

        public static void Open(ScreenManager manager)
        {
            manager.Show<UrlRequestScreen>();
        }

        /// <summary>共有メニュー等から受け取った URL を入力済みの状態で開く。</summary>
        public static void OpenWithUrl(ScreenManager manager, string url)
        {
            _pendingUrl = url;
            manager.Show<UrlRequestScreen>();
        }

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "URLでリクエスト");

            var caption = UiFactory.CreateText(transform, "Caption",
                "YouTube などの URL を、プレイヤーで直接再生します。",
                26, UiFactory.TextDark, TextAnchor.UpperLeft);
            PlaceRow(caption.rectTransform, -140f, 76f);

            var urlLabel = UiFactory.CreateText(transform, "UrlLabel", "URL", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            PlaceRow(urlLabel.rectTransform, -232f, 36f);
            _urlInput = UiFactory.CreateInputField(transform, "UrlInput",
                "https://... (直接再生できるURL)", 28);
            PlaceRow((RectTransform)_urlInput.transform, -272f, 92f);

            var titleLabel = UiFactory.CreateText(transform, "TitleLabel",
                "曲名 (あとで見返す用。なくてもOK)", 26,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            PlaceRow(titleLabel.rectTransform, -392f, 36f);
            _titleInput = UiFactory.CreateInputField(transform, "TitleInput",
                "例: 曲名 / アーティスト", 28);
            PlaceRow((RectTransform)_titleInput.transform, -432f, 92f);

            _errorText = UiFactory.CreateText(transform, "Error", "", 26, UiFactory.Danger);
            PlaceRow(_errorText.rectTransform, -548f, 40f);

            var nextButton = UiFactory.CreateButton(transform, "Next", "よやくの確認へ",
                UiFactory.Primary, Color.white, 34);
            PlaceRow((RectTransform)nextButton.transform, -604f, 96f);
            nextButton.onClick.AddListener(GoConfirm);
        }

        static void PlaceRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(28f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-28f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        public override void OnShow()
        {
            _urlInput.text = _pendingUrl ?? "";
            _pendingUrl = null;
            _titleInput.text = "";
            _errorText.text = "";
        }

        void GoConfirm()
        {
            string url = (_urlInput.text ?? "").Trim();
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                _errorText.text = "http:// か https:// で始まる URL を入れてください";
                Se.Play(Se.Error);
                return;
            }
            string title = (_titleInput.text ?? "").Trim();
            Se.Play(Se.Transition);
            ReserveScreen.Open(Manager, new ReserveScreen.Entry
            {
                Line1 = title != "" ? title : url,
                Line2 = "URL指定 (インターネット再生)",
                Filename = title != "" ? title : url,
                FullPath = url,
                Kind = "URL指定",
            });
        }
    }
}
