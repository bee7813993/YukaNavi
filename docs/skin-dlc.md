# ゆかナビ 配布スキン (DLC) の作り方

配布用のきせかえスキンを作って、シリアルコードでユーザーに配る手順。
**skin.json の全キー・挙動・フォールバック順の詳細は [skin-spec.md](skin-spec.md)** を参照
(この文書は配布の運用にフォーカスする)。

> **スキンメーカー (Web ツール)**: skin.json を手書きしなくても、`tools/skin-maker/`
> のブラウザツールでファイルを選ぶだけで zip を作れる (プレビュー・既存 zip の
> 編集にも対応)。ykr.moe に置けばブラウザだけでスキン制作が完結する。

## ユーザー側の操作

きせかえ画面 > **コードで追加** > シリアルコードを入力 > 追加する。
ダウンロード・取り込み・適用まで自動で行われる。

## 配布の仕組み

- アプリは `https://ykr.moe/yukanavi_dlc/<コード大文字>.zip` をダウンロードする
  (入力コードは大文字化される。例: コード `LIELLA2026XK` → `LIELLA2026XK.zip`)
- 配布側は ykr.moe のその場所に zip を置くだけ (静的ファイル。サーバープログラム不要)
- **コード = ファイル名**なので、推測されにくい長さ (10文字以上目安) にする
- 現状は認証なし (コードを知っていれば誰でも何度でもダウンロード可能)。
  課金・一回性が必要になったら、コード検証 API を ykr.moe に置いて
  アプリのダウンロード先 URL を差し替える (アプリ側フローはそのまま使える)

## zip の構成

zip 直下 (またはフォルダ1階層) に skin.json と素材ファイルを入れる。
アプリの「共有」ボタンで書き出した zip がそのままベースにできる。
サンプルスキンのソースが `art/sample_skins/yukari_four_seasons/` にある。

```
LIELLA2026XK.zip
├─ skin.json
├─ thumbnail.png    ← きせかえ一覧のサムネイル (任意、skin.json への記載不要)
├─ bg1.mp4          ← 背景 (複数可)
├─ bg2.png
├─ bg_night.png     ← 夜背景 (任意)
├─ bg_winter.png    ← 季節背景 (任意)
├─ chara1.png       ← キャラ (複数可、透過PNG推奨)
├─ chara1_blink.png ← キャラ1の目閉じ差分 (任意)
├─ chara1_smile.png ← キャラ1の表情差分 (任意)
├─ chara2.png
├─ pose.png         ← 予約完了ポーズ (任意)
├─ bgm_day.ogg      ← 昼BGM (6:00〜18:00)
├─ bgm_night.ogg    ← 夜BGM
├─ spring_day.ogg   ← 季節×昼夜BGM (任意)
├─ se_tap.ogg       ← タップ効果音 (任意)
├─ record.png       ← リモコンのレコード盤 (任意)
└─ splash.png       ← 起動画面 (任意、skin.json への記載不要)
```

## skin.json の書式 (配布スキン向けフル構成)

```json
{
  "name": "○○コラボスキン",
  "author": "作者名",
  "description": "スキンの説明",
  "backgrounds": [
    { "type": "video", "file": "bg1.mp4" },
    { "type": "image", "file": "bg2.png" }
  ],
  "background_night":     { "type": "image", "file": "bg_night.png" },
  "background_winter_day": { "type": "image", "file": "bg_winter.png" },
  "characters": [
    { "type": "image", "file": "chara1.png", "scale": 1.0,
      "talk": ["キャラ1専用のセリフ", "2つ目のセリフ"],
      "eyes_closed": "chara1_blink.png",
      "expressions": [
        { "file": "chara1_smile.png", "talk": ["この表情のときだけのセリフ"] }
      ] },
    { "type": "image", "file": "chara2.png" }
  ],
  "pose_complete": { "type": "image", "file": "pose.png" },
  "bgm_day":   { "type": "audio", "file": "bgm_day.ogg" },
  "bgm_night": { "type": "audio", "file": "bgm_night.ogg" },
  "bgm_spring_day": { "type": "audio", "file": "spring_day.ogg" },
  "se_tap": { "type": "audio", "file": "se_tap.ogg" },
  "record": { "type": "image", "file": "record.png" },
  "talk": ["うたっていこ〜♪", "つぎはどの曲にする？"],
  "talk_morning": ["おはよ〜！"],
  "talk_evening": ["こんばんは〜♪"],
  "talk_night": ["よふかしさんだ〜"],
  "theme": { "primary": "#E06BA8" }
}
```

### 挙動・書式の詳細

各キーの意味・フォールバック順・時刻/季節の区分・推奨素材サイズは
**[skin-spec.md](skin-spec.md)** にまとまっている。配布時に特に効くポイントだけ抜粋:

- BGM・SE は mp3 / ogg / wav (**m4a・aac は再生不可**)
- 目閉じ・表情差分は立ち絵と同じポーズ・同じ解像度で顔だけ違うのが理想
  (同じ枠に表示されるだけで、位置合わせは自動ではない)
- `thumbnail.png` をフォルダに置くと一覧にサムネイルが出る (skin.json への記載不要)
- アプリのきせかえ編集モーダルで配布スキンを編集されても、拡張フィールド
  (複数指定・昼夜/季節・SE・時間帯セリフ等) は skin.json に維持される
- 書式の誤りはアプリのきせかえ一覧に ⚠ で出る。Unity Editor では
  メニュー「YukaNavi > スキン検証」でも確認できる
