using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// マイページのサーバー連携 (デバイスリンク) 画面。
    /// Web 版のマイページ > デバイスリンクで発行した6文字コードを入力すると、
    /// このサーバーで Web 版と同じユーザーになり、以降マイページが同期される。
    /// リンク済みのときは Google 連携の状態表示と「今すぐ同期」も行える。
    /// </summary>
    public class MypageLinkScreen : ScreenBase
    {
        InputField _codeInput;
        Button _linkButton;
        Button _googleLinkButton;
        Text _stateText;
        GameObject _linkedGroup;
        GameObject _unlinkedGroup;
        Text _googleText;
        Button _carryButton;
        Text _carryButtonLabel;
        int _refreshSerial;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "サーバー連携");

            float y = 130f;
            float labelH = UiFactory.LineHeight(26);

            // 現在の状態
            _stateText = UiFactory.CreateText(transform, "State", "", 28, UiFactory.PrimaryDark);
            SetRow(_stateText.rectTransform, y, UiFactory.LineHeight(28));
            y += UiFactory.LineHeight(28) + 20f;

            // ---- 未リンク時: 説明 + コード入力 ----
            var unlinked = UiFactory.CreatePanel(transform, "Unlinked");
            UiFactory.StretchFull(unlinked);
            _unlinkedGroup = unlinked.gameObject;
            float uy = y;

            const string guide1 = "Web版の マイページ > デバイスリンク で";
            const string guide2 = "「コードを発行」し、6文字のコードを入力してください。";
            const string guide3 = "連携すると、このサーバーではWeb版と同じマイページになります。";
            foreach (string line in new[] { guide1, guide2, guide3 })
            {
                float h = UiFactory.EstimateWrapLines(line, 26, 900f) * UiFactory.LineHeight(26);
                var text = UiFactory.CreateText(unlinked, "Guide", line, 26, UiFactory.TextDark,
                    TextAnchor.MiddleLeft);
                SetRow(text.rectTransform, uy, h);
                uy += h + 4f;
            }
            uy += 24f;

            _codeInput = UiFactory.CreateInputField(unlinked, "CodeInput", "XXXXXX", 40);
            var codeRect = _codeInput.GetComponent<RectTransform>();
            SetRow(codeRect, uy, 96f);
            codeRect.offsetMin = new Vector2(240f, codeRect.offsetMin.y);
            codeRect.offsetMax = new Vector2(-240f, codeRect.offsetMax.y);
            _codeInput.characterLimit = 6;
            _codeInput.textComponent.alignment = TextAnchor.MiddleCenter;
            uy += 96f + 24f;

            _linkButton = UiFactory.CreateButton(unlinked, "Link", "連携する",
                UiFactory.Primary, Color.white, 34);
            SetRow(_linkButton.GetComponent<RectTransform>(), uy, 96f);
            _linkButton.onClick.AddListener(() => _ = LinkAsync());
            UiFactory.OnSubmit(_codeInput, () => _ = LinkAsync());
            uy += 96f + 32f;

            // Google 同期を持ち歩いているときは、コード入力なしで連携できるボタンを出す
            _googleLinkButton = UiFactory.CreateButton(unlinked, "GoogleLink",
                "Google 同期で連携する", UiFactory.PrimaryDark, Color.white, 28);
            UiFactory.FitLabelOneLine(_googleLinkButton.GetComponentInChildren<Text>());
            SetRow(_googleLinkButton.GetComponent<RectTransform>(), uy, 84f);
            _googleLinkButton.onClick.AddListener(() => _ = GoogleLinkAsync());
            _googleLinkButton.gameObject.SetActive(false);

            // ---- リンク済み時: 説明 + Google 連携 + 解除 ----
            var linked = UiFactory.CreatePanel(transform, "Linked");
            UiFactory.StretchFull(linked);
            _linkedGroup = linked.gameObject;
            float ly = y;

            const string linkedGuide = "このサーバーではWeb版と同じマイページを使っています。"
                + "履歴・お気に入りの追加は自動で同期されます。";
            float lg = UiFactory.EstimateWrapLines(linkedGuide, 26, 900f) * UiFactory.LineHeight(26);
            var linkedText = UiFactory.CreateText(linked, "Guide", linkedGuide, 26,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            SetRow(linkedText.rectTransform, ly, lg);
            ly += lg + 32f;

            // Google 連携 (状態は OnShow で取得)
            var googleLabel = UiFactory.CreateText(linked, "GoogleLabel", "Google同期", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetRow(googleLabel.rectTransform, ly, UiFactory.LineHeight(30));
            ly += UiFactory.LineHeight(30) + 4f;

            _googleText = UiFactory.CreateText(linked, "GoogleState", "確認中...", 26,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            float gh = UiFactory.LineHeight(26) * 2f;
            SetRow(_googleText.rectTransform, ly, gh);
            ly += gh + 12f;

            // Google 操作ボタン行 (連携ページを開く / Driveへ保存 / Driveから取込)
            var googleBar = UiFactory.CreatePanel(linked, "GoogleButtons");
            float rowH = Mathf.Max(84f, UiFactory.LineHeight(26) + 24f);
            SetRow(googleBar, ly, rowH);
            var googleLayout = googleBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            googleLayout.childForceExpandWidth = true;
            googleLayout.childForceExpandHeight = true;
            googleLayout.spacing = 10f;
            ly += rowH + 24f;

            var openButton = UiFactory.CreateButton(googleBar, "OpenGoogle", "連携ページを開く",
                UiFactory.PrimaryDark, Color.white, 24);
            UiFactory.FitLabelOneLine(openButton.GetComponentInChildren<Text>());
            openButton.onClick.AddListener(OpenGooglePage);

            var pushButton = UiFactory.CreateButton(googleBar, "SyncPush", "Driveへ保存",
                UiFactory.Primary, Color.white, 24);
            UiFactory.FitLabelOneLine(pushButton.GetComponentInChildren<Text>());
            pushButton.onClick.AddListener(() => _ = GoogleSyncAsync("to_drive"));

            var pullButton = UiFactory.CreateButton(googleBar, "SyncPull", "Driveから取込",
                UiFactory.Primary, Color.white, 24);
            UiFactory.FitLabelOneLine(pullButton.GetComponentInChildren<Text>());
            pullButton.onClick.AddListener(() => _ = GoogleSyncAsync("from_drive"));

            // 持ち歩き (別の部屋に接続したとき Google アカウントで自動的に同期を引き継ぐ)
            const string carryGuide = "同期の持ち歩き: オンにすると、別の部屋に接続したとき"
                + "Google アカウントで自動的にマイページを引き継ぎます。";
            float cgH = UiFactory.EstimateWrapLines(carryGuide, 24, 900f) * UiFactory.LineHeight(24);
            var carryText = UiFactory.CreateText(linked, "CarryGuide", carryGuide, 24,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetRow(carryText.rectTransform, ly, cgH);
            ly += cgH + 8f;

            _carryButton = UiFactory.CreateButton(linked, "Carry", "", UiFactory.PrimaryPale,
                UiFactory.Primary, 26);
            _carryButtonLabel = _carryButton.GetComponentInChildren<Text>();
            UiFactory.FitLabelOneLine(_carryButtonLabel);
            SetRow(_carryButton.GetComponent<RectTransform>(), ly, 84f);
            _carryButton.onClick.AddListener(() => _ = ToggleCarryAsync());
            ly += 84f + 48f;

            // リンク解除 (2度押し確認)
            var unlinkButton = UiFactory.CreateButton(linked, "Unlink", "連携を解除する",
                UiFactory.Danger, Color.white, 28);
            SetRow(unlinkButton.GetComponent<RectTransform>(), ly, 84f);
            var unlinkLabel = unlinkButton.GetComponentInChildren<Text>();
            bool armed = false;
            unlinkButton.onClick.AddListener(() =>
            {
                if (!armed)
                {
                    armed = true;
                    unlinkLabel.text = "本当に解除する？";
                    Se.Play(Se.Tap);
                    return;
                }
                MypageService.Unlink();
                Se.Play(Se.Confirm);
                UiFactory.ShowToast("連携を解除しました (端末内のデータはそのまま使えます)");
                armed = false;
                unlinkLabel.text = "連携を解除する";
                UpdateState();
            });

            UpdateState();
        }

        /// <summary>上端基準・左右 90px マージンの行配置。</summary>
        static void SetRow(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.offsetMin = new Vector2(90f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-90f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        public override void OnShow()
        {
            UpdateState();
        }

        void UpdateState()
        {
            bool linked = MypageService.IsLinked;
            _stateText.text = linked ? "✓ このサーバーと連携中です" : "未連携 (端末内データを使用中)";
            _stateText.color = linked ? UiFactory.Primary : UiFactory.TextMuted;
            _unlinkedGroup.SetActive(!linked);
            _linkedGroup.SetActive(linked);
            if (!linked)
            {
                _googleLinkButton.gameObject.SetActive(MypageService.HasGoogleCarry);
            }
            UpdateCarryButton();
            if (linked)
            {
                _ = RefreshGoogleAsync();
            }
        }

        /// <summary>持ち歩き中の Google 同期でこの部屋と連携する (自動リンク拒否も解除)。</summary>
        async System.Threading.Tasks.Task GoogleLinkAsync()
        {
            Se.Play(Se.Tap);
            ShowLoading("連携中...");
            bool linked = await MypageService.GoogleLinkNowAsync();
            HideLoading();
            if (linked)
            {
                Se.Play(Se.Confirm);
                UiFactory.ShowToast("Google アカウントで連携しました");
                UpdateState();
            }
            else
            {
                Se.Play(Se.Error);
                string message = !string.IsNullOrEmpty(MypageService.LastAutoLinkError)
                    ? "連携できませんでした: " + MypageService.LastAutoLinkError
                    : (MypageService.HasGoogleCarry
                        ? "連携できませんでした (この部屋は Google 同期が未設定かもしれません)"
                        : "Google の認証が切れています。連携をやり直してください");
                UiFactory.ShowToast(message, true);
                UpdateState();
            }
        }

        void UpdateCarryButton()
        {
            bool carrying = MypageService.HasGoogleCarry;
            _carryButtonLabel.text = carrying
                ? "✓ 持ち歩き中 (" + MypageService.GoogleCarryEmail + ") - タップでやめる"
                : "同期の持ち歩きを開始する";
            _carryButton.image.color = carrying ? UiFactory.Primary : UiFactory.PrimaryPale;
            _carryButtonLabel.color = carrying ? Color.white : UiFactory.Primary;
        }

        /// <summary>持ち歩きの開始 (トークン取得) / 停止 (端末から破棄)。</summary>
        async System.Threading.Tasks.Task ToggleCarryAsync()
        {
            Se.Play(Se.Tap);
            if (MypageService.HasGoogleCarry)
            {
                MypageService.ClearGoogleCarry();
                UiFactory.ShowToast("同期の持ち歩きをやめました (この端末からトークンを消しました)");
                UpdateCarryButton();
                return;
            }
            bool ok = await MypageService.FetchGoogleCarryAsync();
            if (ok)
            {
                Se.Play(Se.Confirm);
                UiFactory.ShowToast("同期の持ち歩きを開始しました。別の部屋でも自動で引き継がれます");
            }
            else
            {
                Se.Play(Se.Error);
                UiFactory.ShowToast("先にこの部屋で Google 連携を済ませてください", true);
            }
            UpdateCarryButton();
        }

        async System.Threading.Tasks.Task LinkAsync()
        {
            string code = (_codeInput.text ?? "").Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                UiFactory.ShowToast("6文字のコードを入力してください", true);
                Se.Play(Se.Error);
                return;
            }
            Se.Play(Se.Tap);
            _linkButton.interactable = false;
            ShowLoading("連携中...");
            try
            {
                await MypageService.LinkAsync(code);
                HideLoading();
                Se.Play(Se.Confirm);
                UiFactory.ShowToast("連携しました！端末の履歴・お気に入りをサーバーと統合しました");
                _codeInput.text = "";
                UpdateState();
            }
            catch (System.Exception e)
            {
                HideLoading();
                UiFactory.ShowToast("連携に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            finally
            {
                _linkButton.interactable = true;
            }
        }

        async System.Threading.Tasks.Task RefreshGoogleAsync()
        {
            int serial = ++_refreshSerial;
            _googleText.text = "確認中...";
            try
            {
                var status = await AppConfig.CreateClient()
                    .MypageGoogleStatusAsync(AppConfig.LinkedMypageUserId);
                if (serial != _refreshSerial)
                {
                    return;
                }
                if (!status.Linked)
                {
                    _googleText.text = "未連携です。「連携ページを開く」からWeb版で認証すると、"
                        + "マイページを Google Drive に保存して別の部屋でも復元できます。";
                }
                else
                {
                    string last = status.LastSyncedAt > 0
                        ? System.DateTimeOffset.FromUnixTimeSeconds(status.LastSyncedAt)
                            .ToLocalTime().ToString("M/d HH:mm")
                        : "未同期";
                    _googleText.text = "連携中: " + status.Email + "\n最終同期: " + last
                        + (status.AutoSync ? " (自動同期オン)" : "");
                }
            }
            catch (System.Exception e)
            {
                if (serial == _refreshSerial)
                {
                    _googleText.text = "状態の取得に失敗: " + e.Message;
                }
            }
        }

        /// <summary>Web 版の Google 連携ページをブラウザで開く (認証は Web 側で行う)。</summary>
        void OpenGooglePage()
        {
            Se.Play(Se.Tap);
            string url = AppConfig.ServerUrl.TrimEnd('/') + "/mypage_google_sync.php";
            if (!string.IsNullOrEmpty(AppConfig.EasyPass))
            {
                url += "?easypass=" + System.Uri.EscapeDataString(AppConfig.EasyPass);
            }
            Application.OpenURL(url);
        }

        async System.Threading.Tasks.Task GoogleSyncAsync(string direction)
        {
            Se.Play(Se.Tap);
            ShowLoading("同期中...");
            try
            {
                await AppConfig.CreateClient()
                    .MypageGoogleSyncAsync(AppConfig.LinkedMypageUserId, direction);
                HideLoading();
                Se.Play(Se.Confirm);
                UiFactory.ShowToast(direction == "to_drive"
                    ? "Google Drive に保存しました"
                    : "Google Drive から取り込みました");
                _ = RefreshGoogleAsync();
            }
            catch (System.Exception e)
            {
                HideLoading();
                UiFactory.ShowToast("同期に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
        }
    }
}
