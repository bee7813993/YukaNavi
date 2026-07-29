# ykr.moe 公式サイトへの修正提案

ykr.moe/apps/yukanavi/ で公開中のページの修正版と、新規追加ページ。
スナップショットコミット (公開中ページの取り込み) との git 差分で変更点を確認できる。

**適用方法**: 各ファイルを ykr.moe の `/apps/yukanavi/` に配置する。

## manual.html — 使い方ガイド (新規)

エンドユーザー向けの操作マニュアル。`docs/user-manual.md` から生成した HTML 版で、
サイト共通のヘッダー / フッター / `assets/style.css` に合わせてある。

**配置するもの**:

- `manual.html` → `/apps/yukanavi/manual.html`
- `images/` (19枚) → `/apps/yukanavi/images/` (HTML から `images/xxx.png` で参照)

**あわせて配置するもの** (ナビに「使い方」リンクを追加済み):

- `index.html` / `privacy.html` / `support.html` → 同じく `/apps/yukanavi/` に上書き配置
  (ヘッダー・フッター両方のナビに `manual.html` へのリンクを追加してある)

**補足**: アプリの設定 `helpurl` にこのページの URL
(`https://ykr.moe/apps/yukanavi/manual.html`) を入れると、アプリ内から参照できる。

**更新方法**: 本文の原本は `docs/user-manual.md`。直したら同じ手順で `manual.html` を作り直す。

## index.html — ナビに「使い方」を追加 + iOS公開 / Androidテスト参加の案内

公開中の内容をそのまま取り込んだうえで、次の 2 点を追加した。

1. ヘッダー・フッターのナビに `manual.html` へのリンク
2. ヒーロー部に **iOS(App Store)/ Android(オープンテスト) を左右対称の2カード** で案内

**2026-07-28 iOS 1.0.0 (10) が App Store で公開されたため、iOS側はTestFlight案内から公式バッジ+QRに差し替え済み。Androidはまだオープンテストのため、案内カードはそのまま残している。**
**2026-07-29 追記**: 当初iOS側をバッジのみ・Android側をカード付きで実装したところ見た目のバランスが悪かった(Androidの方が目立つ)ため、両OSとも同じ `.app-card` の2カードグリッドに統一。あわせて、PCで見て手元のスマホでApp Storeを開けるよう iOS側にもQRコードを追加した。

| 項目 | 内容 |
|---|---|
| App Store (iOS) | バッジ画像 `images/Download_on_the_App_Store_Badge_JP_RGB_blk_100317.svg` + QR `images/ios_appstore_qr.png` → `https://apps.apple.com/jp/app/ゆかナビ/id6792447073` |
| Android (オープンテスト) | ボタン + QR `images/android_test_qr.png` → `https://play.google.com/apps/testing/com.yfrteam.yukanavi` |

- QRは2つともこのリポジトリで生成し (`qrcode` + `Pillow`)、OpenCVの `QRCodeDetector` でデコードしてURL一致を確認済み
- 白背景用に黒バッジ (`_blk_`) を採用。白バッジ (`_wht_`) も同梱してあるので、暗色背景に変更する場合はそちらに差し替える
- スタイルは `<head>` 内の `<style>` に閉じ込めてある (共通 `assets/style.css` は無変更)
- 画面が狭いときは2カードとも自動で縦積みになる (flex-wrap)

> **一時的な掲載 (Android)**: Android正式公開時には、`release-status` の文言を「iOS版・Android版 配信中」等に更新し、
> Androidの `.app-card` (テスト参加ボタン・QR・注記) を、iOS側と同様の公式バッジ + Google Playの QR に差し替える。

## privacy.html — ストア申告との整合と実装反映

アプリの実データフロー (コード全体を調査) と突合して修正。

- 改定日 (2026年7月19日) を追加
- **第2節「アプリの利用に伴う情報」**: リクエスト内容の具体化 (うたう人の名前・コメント・選曲)、曲情報の修正内容を追加、利用者識別子の送信と保存を明示、接続先サーバーに IP アドレス / User-Agent が記録されうる旨を追記
  — Play データセーフティ / App Store プライバシーで「名前」「ユーザー ID」を申告するため、ポリシー側の裏付けとして必要
- **第2節「カメラ」**: 「写真で読み取る」機能 (OS カメラで撮影した写真からの読み取り) を反映。
  旧文の「画像として保存することはありません」は写真の一時保存と食い違うため、
  「アプリ専用の一時領域にのみ置き、フォトライブラリ保存や外部送信はしない」という正確な記述に変更
- **第4節「外部サービスへの送信」**: 中継サーバーの保持期間を具体化 (受け渡し完了時に削除、最長10分)。
  きせかえ追加コンテンツ (DLC) の配信サーバーを送信先一覧に追加 (入力コードが URL で送信されるため — 従来の一覧に載っていない通信先)
- **第8節「保存期間と削除」** (2026-07-22 追記): アプリ内の「マイページ > 連携 > 連携データを削除」
  (Drive 上のバックアップ削除 + アクセス権取り消し) を追記 — App Store 審査 (2026-07-21 却下) の
  データ削除要件対応で追加した機能の反映。**アプリ v1.0.0 (6) 以降の再提出前に ykr.moe へ適用が必要**
- ナビ (ヘッダー・フッター) に「使い方」リンクを追加

## support.html

- QR の FAQ にズームスライダー / ピンチ操作 / 「写真で読み取る」の案内を追加 (実装済み機能の反映)
- ナビ (ヘッダー・フッター) に「使い方」リンクを追加

## 備考

- `manual.html` にも同じ hero (App Storeバッジ + Androidテスト参加案内) ブロックがあるため、index.html と同時に修正済み
