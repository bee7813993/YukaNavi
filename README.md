# ゆかナビ (YukaNavi)

カラオケリクエストツール「ゆかり」([KaraokeRequestorWeb](https://github.com/bee7813993/KaraokeRequestorWeb)) の公式ネイティブアプリ。
Unity 6 製、Android / Windows 先行(iOS は後続)。名前はカラオケ機の端末(キョクナビ)に由来し、「曲を探してナビゲートする端末」を表す。

- **設計書**: [docs/design.md](docs/design.md)
- **API 仕様書**(サーバーとの契約): [KaraokeRequestorWeb の api/README.md](https://github.com/bee7813993/KaraokeRequestorWeb/blob/master/api/README.md)
- **マスコット素材の原本**: [art/mascot/](art/mascot/)(Unity プロジェクト作成後に `Assets/` へ取り込む)

## セットアップ

### 1. Unity のインストール

- Unity Hub で **Unity 6 LTS (6000.x)** をインストール
- 追加モジュール:
  - **Android Build Support**(OpenJDK / Android SDK & NDK Tools 込み)
  - **Windows Build Support (IL2CPP)**

### 2. Unity プロジェクトの作成(初回のみ・未実施)

リポジトリのルートを Unity プロジェクトルートにする。Unity Hub は既存フォルダに直接プロジェクトを作れないため:

1. Unity Hub で別の場所に新規プロジェクトを作成(テンプレート: **Universal 2D (URP)**、プロジェクト名 `YukaNavi`)
   ※ Built-in Render Pipeline は Unity 6.5 で非推奨になり、最新の Cubism SDK も URP のみサポートのため使わない
2. 生成された `Assets/` `Packages/` `ProjectSettings/` をこのリポジトリのルートへ移動
3. Unity Hub の「Add」→ このリポジトリのルートを指定して開く
4. `art/mascot/` の素材を `Assets/YukaNavi/Art/Mascot/` へコピー
5. ブランチを切ってコミット → PR

### 3. Live2D Cubism SDK の導入(キャラの Live2D 対応時)

SDK は**再配布不可**のためリポジトリに含まれない(`Assets/Live2D/` は .gitignore 済み)。

1. [Live2D 公式サイト](https://www.live2d.com/sdk/download/unity/)から「Cubism SDK for Unity」をダウンロード
   (**Cubism 5 SDK R5 以降** — URP 対応版。R5 以降は Built-in RP / HDRP がサポート外)
2. unitypackage をプロジェクトにインポート
3. URP アセットの Renderer List に `CubismURPRenderer.asset` を設定([公式手順](https://docs.live2d.com/en/cubism-sdk-tutorials/urp-import/))
4. Cubism Editor と同等の見た目にするため、HDR を無効化し Color Space を Gamma に設定
   (HDR を使う場合は HDR Precision を 64-bit にしないと背景が黒くなることがある)
5. 導入した SDK バージョンをこの README に記録すること

導入済み SDK バージョン: (未導入)

## 開発時の接続先

- ローカル XAMPP の「ゆかり」(`http://localhost/`)。localhost からのアクセスは easyauth 素通りなので開発が楽
- Android 実機からは同一 LAN のサーバー IP(例: `http://192.168.x.x/`)を指定

## Git 運用

- master へ直接コミットしない(ブランチ → PR → マージ)
- 画像・音声・動画などのバイナリは **Git LFS** で管理([.gitattributes](.gitattributes) 参照)。
  GitHub 無料枠(ストレージ 1GB / 帯域 月 1GB)に注意し、大きな動画は極力リポジトリに入れない
- コミットメッセージは `<type>: <日本語要約>` 形式(`feat` / `fix` / `docs` / `refactor` / `chore`)。
  末尾に `Co-Authored-By` を付ける運用はサーバーリポジトリと同じ
