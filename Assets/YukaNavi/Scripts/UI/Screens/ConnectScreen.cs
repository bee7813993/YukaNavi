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
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            // タイトル
            var title = UiFactory.CreateText(transform, "Title", "接続設定", 52, UiFactory.PrimaryDark);
            SetTopRect(title.rectTransform, -80f, 70f);

            var caption = UiFactory.CreateText(transform, "Caption",
                "ゆかりサーバーの URL を入力してください", 30, UiFactory.TextDark);
            SetTopRect(caption.rectTransform, -170f, 40f);

            // URL 入力
            var urlLabel = UiFactory.CreateText(transform, "UrlLabel", "サーバー URL", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(urlLabel.rectTransform, -260f, 40f);
            _urlInput = UiFactory.CreateInputField(transform, "UrlInput", "http://192.168.x.x/");
            SetTopRect(_urlInput.GetComponent<RectTransform>(), -310f, 84f);
            // URL 欄の右に QR 読み取りボタンを置く
            var urlRect = _urlInput.GetComponent<RectTransform>();
            urlRect.offsetMax = new Vector2(-310f, urlRect.offsetMax.y);
            var qrButton = UiFactory.CreateButton(transform, "QrButton", "QRで読取",
                UiFactory.Primary, Color.white, 30);
            var qrRect = qrButton.GetComponent<RectTransform>();
            qrRect.anchorMin = qrRect.anchorMax = new Vector2(1f, 1f);
            qrRect.pivot = new Vector2(1f, 1f);
            qrRect.anchoredPosition = new Vector2(-90f, -310f);
            qrRect.sizeDelta = new Vector2(200f, 84f);
            qrButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Transition);
                Manager.Show<QrScanScreen>();
            });

            // easyauth パスワード入力
            var passLabel = UiFactory.CreateText(transform, "PassLabel",
                "かんたん認証パスワード (不要なら空欄)", 30, UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetTopRect(passLabel.rectTransform, -430f, 40f);
            _passInput = UiFactory.CreateInputField(transform, "PassInput", "");
            SetTopRect(_passInput.GetComponent<RectTransform>(), -480f, 84f);

            // 接続テスト
            var testButton = UiFactory.CreateButton(transform, "TestButton", "接続テスト",
                UiFactory.Primary, Color.white, 38);
            SetTopRect(testButton.GetComponent<RectTransform>(), -610f, 96f);
            testButton.onClick.AddListener(() => _ = TestAsync());

            // 結果表示
            _resultText = UiFactory.CreateText(transform, "Result", "", 30, UiFactory.TextDark);
            SetTopRect(_resultText.rectTransform, -730f, 120f);

            // 保存して戻る
            _saveButton = UiFactory.CreateButton(transform, "SaveButton", "保存して戻る",
                UiFactory.PrimaryDark, Color.white, 38);
            SetTopRect(_saveButton.GetComponent<RectTransform>(), -880f, 96f);
            _saveButton.onClick.AddListener(SaveAndBack);
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

        /// <summary>URL の体裁を整える (http:// 前置、末尾 / 補完)。</summary>
        static string NormalizeUrl(string raw)
        {
            string url = (raw ?? "").Trim();
            if (url == "")
            {
                return "";
            }
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "http://" + url;
            }
            if (!url.EndsWith("/"))
            {
                url += "/";
            }
            return url;
        }

        async Task TestAsync()
        {
            string url = NormalizeUrl(_urlInput.text);
            if (url == "")
            {
                SetResult("URL を入力してください", true);
                return;
            }
            _urlInput.text = url;
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
            string url = NormalizeUrl(_urlInput.text);
            if (url == "")
            {
                SetResult("URL を入力してください", true);
                return;
            }
            if (!_tested)
            {
                SetResult("先に接続テストをしてください", true);
                return;
            }
            AppConfig.ServerUrl = url;
            AppConfig.EasyPass = (_passInput.text ?? "").Trim();
            AppConfig.IsConfigured = true;
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
