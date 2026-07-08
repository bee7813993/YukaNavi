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

            // 行の高さと縦位置は文字の大きさ設定 (FontScale) に合わせて積み上げる
            float labelH = UiFactory.LineHeight(30);
            float y = -60f;

            // タイトル
            var title = UiFactory.CreateText(transform, "Title", "接続設定", 52, UiFactory.PrimaryDark);
            float titleH = UiFactory.ScaledFontSize(52) + 18f;
            SetTopRect(title.rectTransform, y, titleH);
            y -= titleH + 8f;

            var caption = UiFactory.CreateText(transform, "Caption",
                "ゆかりサーバーの URL を入力してください\n(ykr.moe はポート番号だけでもOK)", 28, UiFactory.TextDark);
            float captionH = UiFactory.LineHeight(28) * 2f;
            SetTopRect(caption.rectTransform, y, captionH);
            y -= captionH + 28f;

            // URL 入力
            var urlLabel = UiFactory.CreateText(transform, "UrlLabel", "サーバー URL", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(urlLabel.rectTransform, y, labelH);
            y -= labelH + 8f;
            _urlInput = UiFactory.CreateInputField(transform, "UrlInput", "http://192.168.x.x/ か ポート番号");
            SetTopRect(_urlInput.GetComponent<RectTransform>(), y, 84f);
            // URL 欄の右に QR 読み取りボタンを置く
            var urlRect = _urlInput.GetComponent<RectTransform>();
            urlRect.offsetMax = new Vector2(-310f, urlRect.offsetMax.y);
            var qrButton = UiFactory.CreateButton(transform, "QrButton", "QRで読取",
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
            var passLabel = UiFactory.CreateText(transform, "PassLabel",
                "かんたん認証パスワード (不要なら空欄)", 30, UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(passLabel.rectTransform, y, labelH);
            y -= labelH + 8f;
            _passInput = UiFactory.CreateInputField(transform, "PassInput", "");
            SetTopRect(_passInput.GetComponent<RectTransform>(), y, 84f);
            y -= 84f + 44f;

            // 接続テスト
            var testButton = UiFactory.CreateButton(transform, "TestButton", "接続テスト",
                UiFactory.Primary, Color.white, 38);
            SetTopRect(testButton.GetComponent<RectTransform>(), y, 96f);
            testButton.onClick.AddListener(() => _ = TestAsync());
            UiFactory.OnSubmit(_urlInput, () => _ = TestAsync()); // Enter でも接続テスト
            UiFactory.OnSubmit(_passInput, () => _ = TestAsync());
            y -= 96f + 20f;

            // 結果表示
            _resultText = UiFactory.CreateText(transform, "Result", "", 30, UiFactory.TextDark);
            float resultH = UiFactory.LineHeight(30) * 2f;
            SetTopRect(_resultText.rectTransform, y, resultH);
            y -= resultH + 24f;

            // 保存して戻る
            _saveButton = UiFactory.CreateButton(transform, "SaveButton", "保存して戻る",
                UiFactory.PrimaryDark, Color.white, 38);
            SetTopRect(_saveButton.GetComponent<RectTransform>(), y, 96f);
            _saveButton.onClick.AddListener(SaveAndBack);
            y -= 96f + 44f;

            // 文字の大きさ (全画面共通。変更すると即時反映して端末に保存される)
            var fontLabel = UiFactory.CreateText(transform, "FontLabel", "文字の大きさ", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(fontLabel.rectTransform, y, labelH);
            y -= labelH + 8f;
            var fontBar = UiFactory.CreatePanel(transform, "FontTabs");
            SetTopRect(fontBar, y, 84f);
            var fontTabs = UiFactory.CreateSegmentTabs(fontBar,
                new[] { "100%", "115%", "130%", "145%", "160%" }, 24);
            float[] scales = { 1f, 1.15f, 1.3f, 1.45f, 1.6f };
            int selected = 1;
            for (int i = 0; i < scales.Length; i++)
            {
                if (Mathf.Abs(UiFactory.FontScale - scales[i]) < 0.05f)
                {
                    selected = i;
                }
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
