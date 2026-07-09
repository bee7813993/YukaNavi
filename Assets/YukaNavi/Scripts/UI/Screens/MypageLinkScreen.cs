using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// マイページのサーバー連携画面。2つの独立した連携を扱う:
    /// - デバイスリンク: Web 版の マイページ > デバイスリンク で発行した6文字コードを
    ///   入力すると、このサーバーで Web 版と同じユーザーになり、以降マイページが同期される
    /// - Google 同期: アプリが直接 Google にログインし、マイページを Google Drive に
    ///   自動保存する (部屋と無関係にどこでも同じデータを使える)
    /// </summary>
    public class MypageLinkScreen : ScreenBase
    {
        InputField _codeInput;
        Button _linkButton;
        Text _stateText;
        GameObject _linkedGroup;
        GameObject _unlinkedGroup;
        GameObject _googleOutGroup;  // Google: 未ログイン
        GameObject _googleBusyGroup; // Google: ブラウザ認証待ち
        GameObject _googleInGroup;   // Google: ログイン済み
        Text _googleInfoText;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "サーバー連携");

            float y = 130f;

            // 現在の状態 (デバイスリンク)
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

            // ---- リンク済み時: 説明 + 解除 ----
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
            ly += 84f + 32f;

            // ---- Google 同期 (部屋と無関係の共通セクション。どちらのグループでも下に出る) ----
            float gy = Mathf.Max(uy, ly) + 40f;

            var googleLabel = UiFactory.CreateText(transform, "GoogleLabel", "Google 同期", 30,
                UiFactory.PrimaryDark, TextAnchor.MiddleLeft);
            SetRow(googleLabel.rectTransform, gy, UiFactory.LineHeight(30));
            gy += UiFactory.LineHeight(30) + 4f;

            const string googleGuide = "Google にログインすると、マイページを Google Drive に"
                + "自動保存し、どの部屋でも同じデータを使えます (Web版のGoogle連携と共通)。";
            float ggH = UiFactory.EstimateWrapLines(googleGuide, 24, 900f) * UiFactory.LineHeight(24);
            var googleGuideText = UiFactory.CreateText(transform, "GoogleGuide", googleGuide, 24,
                UiFactory.TextMuted, TextAnchor.UpperLeft);
            SetRow(googleGuideText.rectTransform, gy, ggH);
            gy += ggH + 16f;

            // 未ログイン: ログインボタン
            var gOut = UiFactory.CreatePanel(transform, "GoogleOut");
            UiFactory.StretchFull(gOut);
            _googleOutGroup = gOut.gameObject;
            var loginButton = UiFactory.CreateButton(gOut, "GoogleLogin", "Google にログイン",
                UiFactory.PrimaryDark, Color.white, 30);
            UiFactory.FitLabelOneLine(loginButton.GetComponentInChildren<Text>());
            SetRow(loginButton.GetComponent<RectTransform>(), gy, 96f);
            loginButton.onClick.AddListener(() => _ = GoogleLoginAsync());

            // 認証待ち: 案内 + 中止
            var gBusy = UiFactory.CreatePanel(transform, "GoogleBusy");
            UiFactory.StretchFull(gBusy);
            _googleBusyGroup = gBusy.gameObject;
            float by = gy;
            const string busyGuide = "ブラウザで Google の認証を進めてください...";
            float bh = UiFactory.EstimateWrapLines(busyGuide, 26, 900f) * UiFactory.LineHeight(26);
            var busyText = UiFactory.CreateText(gBusy, "BusyGuide", busyGuide, 26,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            SetRow(busyText.rectTransform, by, bh);
            by += bh + 12f;
            var cancelButton = UiFactory.CreateButton(gBusy, "Cancel", "中止する",
                UiFactory.PrimaryPale, UiFactory.Primary, 26);
            SetRow(cancelButton.GetComponent<RectTransform>(), by, 84f);
            cancelButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                GoogleAccount.CancelLogin();
            });

            // ログイン済み: アカウント情報 + 手動同期 + ログアウト
            var gIn = UiFactory.CreatePanel(transform, "GoogleIn");
            UiFactory.StretchFull(gIn);
            _googleInGroup = gIn.gameObject;
            float iy = gy;
            _googleInfoText = UiFactory.CreateText(gIn, "GoogleInfo", "", 26,
                UiFactory.TextDark, TextAnchor.UpperLeft);
            float ih = UiFactory.LineHeight(26) * 2f;
            SetRow(_googleInfoText.rectTransform, iy, ih);
            iy += ih + 12f;

            // 手動同期 (通常は自動で同期される。任意のタイミングで揃えたいとき用)
            var syncBar = UiFactory.CreatePanel(gIn, "SyncButtons");
            float rowH = Mathf.Max(84f, UiFactory.LineHeight(26) + 24f);
            SetRow(syncBar, iy, rowH);
            var syncLayout = syncBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            syncLayout.childForceExpandWidth = true;
            syncLayout.childForceExpandHeight = true;
            syncLayout.spacing = 10f;
            iy += rowH + 16f;

            var pushButton = UiFactory.CreateButton(syncBar, "SyncPush", "Driveへ保存",
                UiFactory.Primary, Color.white, 24);
            UiFactory.FitLabelOneLine(pushButton.GetComponentInChildren<Text>());
            pushButton.onClick.AddListener(() => _ = GoogleSyncAsync(true));

            var pullButton = UiFactory.CreateButton(syncBar, "SyncPull", "Driveから取込",
                UiFactory.Primary, Color.white, 24);
            UiFactory.FitLabelOneLine(pullButton.GetComponentInChildren<Text>());
            pullButton.onClick.AddListener(() => _ = GoogleSyncAsync(false));

            var logoutButton = UiFactory.CreateButton(gIn, "GoogleLogout", "ログアウト",
                UiFactory.PrimaryPale, UiFactory.Primary, 26);
            SetRow(logoutButton.GetComponent<RectTransform>(), iy, 84f);
            logoutButton.onClick.AddListener(() =>
            {
                Se.Play(Se.Tap);
                GoogleAccount.Logout();
                UiFactory.ShowToast("ログアウトしました (Drive 上のデータはそのまま残ります)");
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

            bool busy = GoogleAccount.IsLoginInProgress;
            bool loggedIn = GoogleAccount.IsLoggedIn;
            _googleOutGroup.SetActive(!busy && !loggedIn);
            _googleBusyGroup.SetActive(busy);
            _googleInGroup.SetActive(!busy && loggedIn);
            if (!busy && loggedIn)
            {
                long at = MypageService.LastDriveSyncAt;
                string last = at > 0
                    ? System.DateTimeOffset.FromUnixTimeSeconds(at)
                        .ToLocalTime().ToString("M/d HH:mm")
                    : "未同期";
                _googleInfoText.text = "ログイン中: " + GoogleAccount.Email + "\n最終同期: " + last;
            }
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

        /// <summary>
        /// Google ログイン。ブラウザを開いて認証完了をポーリングで待ち、
        /// 成功したら Drive と双方向に同期する (取込 → 保存)。
        /// </summary>
        async System.Threading.Tasks.Task GoogleLoginAsync()
        {
            Se.Play(Se.Tap);
            var login = GoogleAccount.LoginAsync();
            UpdateState(); // 認証待ち表示へ
            bool ok = await login;
            if (this == null)
            {
                return; // 画面が作り直された (状態は次の表示で反映される)
            }
            if (!ok)
            {
                if (!string.IsNullOrEmpty(GoogleAccount.LastLoginError))
                {
                    Se.Play(Se.Error);
                    UiFactory.ShowToast(GoogleAccount.LastLoginError, true);
                }
                else
                {
                    UiFactory.ShowToast("ログインを中止しました");
                }
                UpdateState();
                return;
            }
            Se.Play(Se.Confirm);
            UiFactory.ShowToast("Google にログインしました: " + GoogleAccount.Email);
            UpdateState();

            // 初回同期: Drive の内容を取り込み、端末の内容も Drive へ反映する
            ShowLoading("Drive と同期中...");
            try
            {
                await MypageService.PullFromDriveAsync(false);
                await MypageService.PushToDriveAsync();
                if (this == null)
                {
                    return;
                }
                UiFactory.ShowToast("Google Drive と同期しました");
            }
            catch (System.Exception e)
            {
                if (this == null)
                {
                    return;
                }
                UiFactory.ShowToast("同期に失敗: " + e.Message, true);
            }
            HideLoading();
            UpdateState();
        }

        async System.Threading.Tasks.Task GoogleSyncAsync(bool toDrive)
        {
            Se.Play(Se.Tap);
            ShowLoading("同期中...");
            try
            {
                if (toDrive)
                {
                    await MypageService.PushToDriveAsync();
                }
                else
                {
                    await MypageService.PullFromDriveAsync(false);
                }
                if (this == null)
                {
                    return;
                }
                HideLoading();
                Se.Play(Se.Confirm);
                UiFactory.ShowToast(toDrive
                    ? "Google Drive に保存しました"
                    : "Google Drive から取り込みました");
            }
            catch (System.Exception e)
            {
                if (this == null)
                {
                    return;
                }
                HideLoading();
                UiFactory.ShowToast("同期に失敗: " + e.Message, true);
                Se.Play(Se.Error);
            }
            UpdateState(); // 最終同期時刻や認証切れログアウトの反映
        }
    }
}
