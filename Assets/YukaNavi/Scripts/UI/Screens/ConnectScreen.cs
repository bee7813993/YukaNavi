using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 接続設定画面。サーバー URL と easyauth パスワードを入力し、
    /// server_info (認証不要) → capabilities (認証込み) の順で疎通確認して保存する。
    /// 初回起動時はこの画面から始まる。QR コード読み取りは次スライスで追加予定。
    /// </summary>
    public class ConnectScreen : ScreenBase
    {
        InputField _urlInput;
        InputField _passInput;
        Text _resultText;
        Button _saveButton;
        bool _tested;

        public override void BuildUi()
        {
            // 背景
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            // 接続設定用イラスト (中央が明るく UI を重ねられる素材)。画面比に合わせて覆う
            var artSprite = UiFactory.LoadSprite("Art/ScreenArt/yukanavi_connect_illustration");
            if (artSprite != null)
            {
                var artGo = new GameObject("BgArt");
                artGo.transform.SetParent(transform, false);
                var art = artGo.AddComponent<Image>();
                art.sprite = artSprite;
                art.raycastTarget = false;
                UiFactory.StretchFull(art.rectTransform);
                var fitter = artGo.AddComponent<AspectRatioFitter>();
                fitter.aspectRatio = 1080f / 1920f;
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            }

            // 大きい文字サイズ設定でも全項目に届くよう、設定項目は縦スクロールに積む
            var scrollGo = new GameObject("SettingsScroll");
            scrollGo.transform.SetParent(transform, false);
            var scrollRectT = scrollGo.AddComponent<RectTransform>();
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(0f, GlobalNav.BarHeight + 10f);
            scrollRectT.offsetMax = new Vector2(0f, 0f);
            scrollGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // タッチ判定用
            var scroll = scrollGo.AddComponent<ScrollRect>();
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            UiFactory.StretchFull(viewportRect);
            viewportGo.AddComponent<Image>().color = Color.white;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            // 行の高さと縦位置は文字の大きさ設定 (FontScale) に合わせて積み上げる
            // (折り返しうる文言は行数を見積もって確保し、重なりを防ぐ)
            float labelH = UiFactory.LineHeight(30);
            float y = -48f;

            // タイトル
            var title = UiFactory.CreateText(content, "Title", "設定", 52, UiFactory.PrimaryDark);
            float titleH = UiFactory.ScaledFontSize(52) + 18f;
            SetTopRect(title.rectTransform, y, titleH);
            y -= titleH + 8f;

            const string captionLine1 = "ゆかりサーバーの URL を入力してください";
            const string captionLine2 = "(ykr.moe はポート番号だけでもOK)";
            var caption = UiFactory.CreateText(content, "Caption",
                captionLine1 + "\n" + captionLine2, 28, UiFactory.TextDark);
            float captionH = (UiFactory.EstimateWrapLines(captionLine1, 28, 900f)
                + UiFactory.EstimateWrapLines(captionLine2, 28, 900f)) * UiFactory.LineHeight(28);
            SetTopRect(caption.rectTransform, y, captionH);
            y -= captionH + 28f;

            // URL 入力
            var urlLabel = UiFactory.CreateText(content, "UrlLabel", "サーバー URL", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(urlLabel.rectTransform, y, labelH);
            y -= labelH + 8f;
            _urlInput = UiFactory.CreateInputField(content, "UrlInput", "http://192.168.x.x/ か ポート番号");
            SetTopRect(_urlInput.GetComponent<RectTransform>(), y, 84f);
            // URL 欄の右に QR 読み取りボタンを置く
            var urlRect = _urlInput.GetComponent<RectTransform>();
            urlRect.offsetMax = new Vector2(-310f, urlRect.offsetMax.y);
            var qrButton = UiFactory.CreateButton(content, "QrButton", "QRで読取",
                UiFactory.Primary, Color.white, 30);
            var qrRect = qrButton.GetComponent<RectTransform>();
            qrRect.anchorMin = qrRect.anchorMax = new Vector2(1f, 1f);
            qrRect.pivot = new Vector2(1f, 1f);
            qrRect.anchoredPosition = new Vector2(-90f, y);
            qrRect.sizeDelta = new Vector2(200f, 84f);
            UiFactory.FitLabel(qrButton.GetComponentInChildren<Text>());
            qrButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                Manager.Show<QrScanScreen>();
            });
            y -= 84f + 20f;

            // easyauth パスワード入力
            const string passLabelText = "かんたん認証パスワード (不要なら空欄)";
            var passLabel = UiFactory.CreateText(content, "PassLabel",
                passLabelText, 30, UiFactory.PrimaryDark, TextAnchor.UpperLeft);
            float passLabelH = UiFactory.EstimateWrapLines(passLabelText, 30, 900f)
                * UiFactory.LineHeight(30);
            SetTopRect(passLabel.rectTransform, y, passLabelH);
            y -= passLabelH + 8f;
            _passInput = UiFactory.CreateInputField(content, "PassInput", "");
            SetTopRect(_passInput.GetComponent<RectTransform>(), y, 84f);
            y -= 84f + 44f;

            // 接続テスト
            var testButton = UiFactory.CreateButton(content, "TestButton", "接続テスト",
                UiFactory.Primary, Color.white, 38);
            SetTopRect(testButton.GetComponent<RectTransform>(), y, 96f);
            testButton.onClick.AddListener(() => _ = TestAsync());
            UiFactory.OnSubmit(_urlInput, () => _ = TestAsync()); // Enter でも接続テスト
            UiFactory.OnSubmit(_passInput, () => _ = TestAsync());
            y -= 96f + 20f;

            // 結果表示
            _resultText = UiFactory.CreateText(content, "Result", "", 30, UiFactory.TextDark);
            float resultH = UiFactory.LineHeight(30) * 2f;
            SetTopRect(_resultText.rectTransform, y, resultH);
            y -= resultH + 24f;

            // 保存して戻る
            _saveButton = UiFactory.CreateButton(content, "SaveButton", "保存して戻る",
                UiFactory.PrimaryDark, Color.white, 38);
            SetTopRect(_saveButton.GetComponent<RectTransform>(), y, 96f);
            _saveButton.onClick.AddListener(SaveAndBack);
            y -= 96f + 44f;

            // 文字の大きさ (全画面共通。変更すると即時反映して端末に保存される)
            var fontLabel = UiFactory.CreateText(content, "FontLabel", "文字の大きさ", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(fontLabel.rectTransform, y, labelH);
            y -= labelH + 8f;
            var fontBar = UiFactory.CreatePanel(content, "FontTabs");
            SetTopRect(fontBar, y, 84f);
            y -= 84f + 28f;

            // アプリバージョン (ProjectSettings の bundleVersion)
            var versionText = UiFactory.CreateText(content, "Version",
                "ゆかナビ v" + Application.version, 22, UiFactory.TextMuted);
            float versionH = UiFactory.LineHeight(22);
            SetTopRect(versionText.rectTransform, y, versionH);
            y -= versionH;

            content.sizeDelta = new Vector2(0f, -y + 24f);
            var fontTabs = UiFactory.CreateSegmentTabs(fontBar,
                new[] { "100%", "130%", "160%", "190%" }, 24);
            float[] scales = { 1f, 1.3f, 1.6f, 1.9f };
            // 現在値に最も近いタブを選択表示 (旧5段階で保存した中間値もどれかに寄せる)
            int selected = 1;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < scales.Length; i++)
            {
                float diff = Mathf.Abs(UiFactory.FontScale - scales[i]);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    selected = i;
                }
            }
            for (int i = 0; i < scales.Length; i++)
            {
                float scale = scales[i];
                fontTabs[i].onClick.AddListener(() =>
                {
                    Se.Play(Se.Tap);
                    if (Mathf.Abs(UiFactory.FontScale - scale) < 0.01f)
                    {
                        return;
                    }
                    UiFactory.FontScale = scale;
                    Manager.RebuildAll(); // 全画面を作り直して反映 (この画面も含む)
                    if (GlobalNav.Instance != null)
                    {
                        GlobalNav.Instance.Rebuild(); // メニューの文字・行の高さも作り直す
                    }
                });
            }
            UiFactory.SetSegmentSelected(fontTabs, selected);
        }

        /// <summary>上端基準・左右 90px マージンの行配置。</summary>
        static void SetTopRect(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.offsetMin = new Vector2(90f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-90f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        public override void OnShow()
        {
            // QR 読み取りから戻ってきた場合は読み取った URL を反映する
            if (!string.IsNullOrEmpty(QrScanScreen.LastScannedText))
            {
                _urlInput.text = QrScanScreen.LastScannedText;
                QrScanScreen.LastScannedText = null;
                _resultText.text = "QR を読み取りました。接続テストをしてください";
                _resultText.color = UiFactory.TextDark;
                _tested = false;
                return;
            }
            _urlInput.text = AppConfig.ServerUrl;
            _passInput.text = AppConfig.EasyPass;
            _resultText.text = AppConfig.IsConfigured
                ? ""
                : "はじめに接続テストをして、保存してください";
            _tested = false;
        }

        /// <summary>URL の体裁を整え、?easypass=XXXX が付いていれば取り出す (YukariUrl に委譲)。</summary>
        static string NormalizeUrl(string raw, out string easypass)
        {
            return YukariUrl.Normalize(raw, out easypass);
        }

        async Task TestAsync()
        {
            string url = NormalizeUrl(_urlInput.text, out string urlPass);
            if (url == "")
            {
                SetResult("URL を入力してください", true);
                return;
            }
            _urlInput.text = url;
            if (!string.IsNullOrEmpty(urlPass))
            {
                _passInput.text = urlPass; // URL に付いていた認証キーワードを取り込む
            }
            string pass = (_passInput.text ?? "").Trim();
            var client = new ApiClient(url, pass);

            SetResult("接続確認中...", false);

            // server_info (認証不要)。旧サーバーには無いので 404 は許容して先へ進む
            ServerInfoDto info = null;
            try
            {
                info = await client.GetServerInfoAsync();
            }
            catch (ApiException e)
            {
                if (e.HttpStatus != 404)
                {
                    SetResult("接続できません: " + e.Message, true);
                    return;
                }
            }

            // capabilities (認証込み) で疎通確認
            try
            {
                await client.GetCapabilitiesAsync();
                string version = info != null ? " " + info.Version : "";
                SetResult("接続OK: ゆかり" + version + " に接続できました", false);
                Se.Play(Se.Confirm);
                _tested = true;
            }
            catch (ApiException e)
            {
                if (info != null && info.EasyauthRequired && pass == "")
                {
                    SetResult("このサーバーはかんたん認証パスワードが必要です", true);
                }
                else if (e.HttpStatus == 401)
                {
                    SetResult("認証に失敗しました。パスワードを確認してください", true);
                }
                else
                {
                    SetResult("接続できません: " + e.Message, true);
                }
            }
        }

        void SaveAndBack()
        {
            string url = NormalizeUrl(_urlInput.text, out string urlPass);
            if (url == "")
            {
                SetResult("URL を入力してください", true);
                return;
            }
            if (!string.IsNullOrEmpty(urlPass))
            {
                _passInput.text = urlPass;
            }
            if (!_tested)
            {
                SetResult("先に接続テストをしてください", true);
                return;
            }
            AppConfig.ServerUrl = url;
            AppConfig.EasyPass = (_passInput.text ?? "").Trim();
            AppConfig.IsConfigured = true;
            AppState.Invalidate(); // capabilities を取り直す
            Se.Play(Se.Transition);
            Manager.ShowAsRoot<HomeScreen>();
        }

        void SetResult(string message, bool isError)
        {
            _resultText.text = message;
            _resultText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
            if (isError)
            {
                Se.Play(Se.Error);
            }
        }
    }
}
