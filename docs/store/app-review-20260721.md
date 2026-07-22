# App Store 審査却下 (2026-07-21) への対応

Submission ID: c25a1f62-8d49-49a0-9fd8-d6c4fd867301 / 対象: 1.0.0 (5) /
審査デバイス: iPad Air 11-inch (M3)・iPhone 17 Pro Max (iOS/iPadOS 26.5.2)

## 指摘と対応の対応表

| # | 指摘 | 原因 | 対応 |
|---|------|------|------|
| 1 | **4.8 Login Services** — サードパーティログイン (Google) のみで、プライバシー配慮型の同等ログインが無い | Google ログインを提供している (用途は Google Drive バックアップ専用・完全任意) | 適用除外 (「特定のサードパーティサービスのクライアント」) を返信で主張。実装面でもアプリ内認証化・データ削除を追加して補強 |
| 2 | **4 Design** — サインインで外部ブラウザに遷移する | `GoogleAccount` が `Application.OpenURL` で既定ブラウザを開いていた | iOS は SFSafariViewController のアプリ内シートで認証 (`Assets/Plugins/iOS/YukaNaviSafariView.mm` + `InAppBrowser.cs`)。完了・中止・タイムアウトで自動クローズ |
| 3 | **4 Design** — アカウント作成があるなら削除機能が必要 | アプリにアカウント制度は無い (匿名 UUID のみ) が、Google 連携データの削除手段がアプリ内に無かった | マイページ > 連携 に「連携データを削除」を追加 (Drive 上の `mypage_data.json` 削除 → OAuth トークン失効 → ログアウト) |
| 4 | **2.1(a)** — `http://ykr.moe:11004` に接続できない (Request timeout) | 審査時間帯にデモサーバーが手違いでダウンしていた (構成自体は IPv4/IPv6 両対応) | サーバー復旧・監視。アプリ側の不具合ではないため修正なし。下のチェックリストで再発防止 |
| 5 | **4 Design** — iPad で UI が crowded / 崩れて見える | ① メニューの透過スクリム (alpha 0.55) がホーム以外の画面でも透けて二重表示に見えた ② 幅基準の CanvasScaler で iPad 縦 (4:3) の実効キャンバス高が約 1547 (設計 1920) に縮み全画面が過密 | ① スクリムをホーム上のみ透過・他画面上はほぼ不透明 (0.97) に (`GlobalNav.cs`) ② CanvasScaler を Expand に変更し 4:3 では高さ 1920 基準で拡張 (`AppRoot.cs`) |
| 6 | **5.1.1(ii)** — カメラの purpose string が汎用文 | NativeCamera プラグインのビルド後処理が `NSCameraUsageDescription` を英語デフォルト文で上書き (Unity 側設定は正しかった) | `IosBuildPostProcess.cs` (`PostProcessBuild(10000)`) で最終的に具体文言 (日英併記) へ上書き。未使用のマイク・フォトライブラリ文言も除去 |

## 再提出前チェックリスト

- [x] `ykr.moe:11004` の部屋サーバー稼働確認 (審査期間中は常時稼働) — 2026-07-22 実測 OK (server_info が JSON 応答)
- [ ] `ykr.moe:11004` の外形監視 (uptime monitor) を設定し、ダウン時に通知が来ることを確認
- [x] IPv6 で疎通: `curl -6 http://ykr.moe:11004/api/server_info.php` が JSON を返す — 2026-07-22 実測 OK (AAAA レコードあり)
- [x] IPv4 で疎通: `curl -4 http://ykr.moe:11004/api/server_info.php` が JSON を返す — 2026-07-22 実測 OK
- [ ] NAT64 疎通 (Apple 審査網の再現): macOS の「インターネット共有 > NAT64 ネットワークを作成」で IPv6-only の Wi-Fi を作り、実機 iPhone からアプリで `http://ykr.moe:11004` に接続・検索できる
  ※ 未実施のため返信文から NAT64 検証の一文は削除済み (2026-07-22)。実施した場合は「We have verified connectivity from an IPv6-only (NAT64) network.」を 2.1(a) の段落末尾に戻してよい
- [ ] デモ部屋がかんたん認証なしで入れる設定になっている (審査メモの手順どおり素通りできる)
- [ ] TestFlight ビルドで確認: カメラ許可ダイアログが日英併記の具体文言になっている
- [ ] TestFlight ビルドで確認: Google ログインがアプリ内シートで開き、完了で自動的に閉じる。シートのキャンセルで数秒後に「ログインを中止しました」
- [ ] TestFlight ビルドで確認: マイページ > 連携 > 連携データを削除 が動作する
- [ ] iPad (実機または Simulator) で確認: 設定画面の上でメニューを開いても下の文字が透けない。ホーム・検索・設定が過密にならない
- [ ] App Store Connect の返信欄に下記の返信文を投稿してから Resubmit
- [x] プライバシーポリシー (ykr.moe/apps/yukanavi/privacy.html) にアプリ内削除手段の記述を反映 (`site-updates/` 参照) — 2026-07-22 反映済み

## App Review への返信文案 (App Store Connect のメッセージ欄に投稿)

> Thank you for the detailed review. We have addressed every issue in the new build and would like to add context for two of them.
>
> **Guideline 4.8 (Login Services)**
> The "Sign in with Google" option in our app is not a general login service and does not create or authenticate an app account. It exists solely so that users can back up their own data (song history, favorites, and other My Page data such as the "sing later" list and saved searches) to their own Google Drive (the hidden appDataFolder of their Google account). It is entirely optional: every feature of the app works without signing in, and no functionality other than the Google Drive backup itself is gated behind it. We believe this falls under the Guideline 4.8 exception for apps that use a third-party service login specifically to access that service's content ("your app is a client for a specific third-party service and users are required to sign in to their mail, social media, or other third-party account directly to access their content"): a Sign in with Apple account cannot access the user's Google Drive, so an equivalent-login option cannot provide this feature. The app has no account system of its own — users are identified toward their own karaoke server only by an anonymous, locally generated UUID.
>
> In addition, in this build we have:
> - moved the Google authentication into an in-app SFSafariViewController (no more hand-off to the default browser), per Guideline 4;
> - added an in-app "Delete linked data" action (My Page > Link) that deletes the backup file from the user's Google Drive, revokes the OAuth grant, and signs the user out.
>
> **Guideline 2.1(a) (server connection)**
> The demo server (http://ykr.moe:11004) suffered an unrelated operational outage during the review window — we sincerely apologize. It has been restored, we have verified it is reachable over both IPv4 and IPv6, and it will be kept online and monitored for the duration of the review.
>
> **Guideline 4 (iPad layout)**
> We fixed the menu overlay issue shown in your screenshot: the full-screen menu background is now nearly opaque over every content screen, so the underlying screen no longer shows through. (Only over the home screen — which displays the user's chosen wallpaper and mascot — does the menu retain a deliberate, decorative translucency.) The UI now also scales with the 4:3 iPad aspect ratio so that screens are no longer crowded.
>
> **Guideline 5.1.1(ii) (camera purpose string)**
> The camera purpose string now explains the specific use with an example (scanning the room's QR code to configure the connection to the user's Yukari server), in Japanese and English.
>
> Thank you again for your time. Please let us know if any further information would help.

## 補足メモ

- 4.8 の除外主張が認められなかった場合の代替案: Sign in with Apple を追加し、マイページの保存先をサーバー側 (KaraokeRequestorWeb / ykr.moe) に新設する (Unity + PHP 両方の開発が必要)。
- 「連携データを削除」の revoke は同一 OAuth クライアントの grant 全体を失効させるため、同じ Google アカウントで Web 版の Google 連携を使っている場合は Web 版で再ログインが必要になることがある (データ削除操作としては意図どおり)。
- NativeCamera の purpose string はプラグイン更新で挙動が変わる可能性があるため、`IosBuildPostProcess.cs` は最後 (callbackOrder 10000) に実行して常に勝つ構成にしてある。
