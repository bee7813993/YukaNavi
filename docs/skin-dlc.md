# ゆかナビ 配布スキン (DLC) の作り方

配布用のきせかえスキンを作って、シリアルコードでユーザーに配る手順。

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

```
LIELLA2026XK.zip
├─ skin.json
├─ bg1.mp4          ← 背景 (複数可)
├─ bg2.png
├─ chara1.png       ← キャラ (複数可、透過PNG推奨)
├─ chara2.png
├─ bgm_day.ogg      ← 昼BGM (6:00〜18:00)
├─ bgm_night.ogg    ← 夜BGM
├─ record.png       ← リモコンのレコード盤 (任意)
└─ splash.png       ← 起動画面 (任意、skin.json への記載不要)
```

## skin.json の書式 (配布スキン向けフル構成)

```json
{
  "name": "○○コラボスキン",
  "backgrounds": [
    { "type": "video", "file": "bg1.mp4" },
    { "type": "image", "file": "bg2.png" }
  ],
  "characters": [
    { "type": "image", "file": "chara1.png", "scale": 1.0,
      "talk": ["キャラ1専用のセリフ", "2つ目のセリフ"] },
    { "type": "image", "file": "chara2.png" }
  ],
  "bgm_day":   { "type": "audio", "file": "bgm_day.ogg" },
  "bgm_night": { "type": "audio", "file": "bgm_night.ogg" },
  "record": { "type": "image", "file": "record.png" },
  "talk": ["うたっていこ〜♪", "つぎはどの曲にする？"],
  "theme": { "primary": "#E06BA8" }
}
```

### 挙動

| 項目 | 挙動 |
|---|---|
| `backgrounds` (複数) | ホームの背景 (前面に UI が無いところ) をタップで次の背景へ。選んだ背景はスキンごとに端末保存 |
| `characters` (複数) | マスコットをタップで次のキャラへ。予約完了画面は1枚目を使用 |
| キャラごとの `talk` | 表示中のキャラのセリフが優先される。無いキャラはスキン全体の `talk` にフォールバック |
| `bgm_day` / `bgm_night` | 6:00〜18:00 が昼。再生中に時間帯をまたぐと自動で切替。無い時間帯は `bgm` → アプリ標準の順にフォールバック |
| 従来の単数指定 | `background` / `character` / `bgm` も引き続き有効。複数指定と併用すると単数が1枚目扱い |

### 注意

- BGM は mp3 / ogg / wav (m4a・aac は Unity のランタイム読み込み非対応)
- 背景動画は mp4 推奨。縦画面 (1080x1920 目安) に cover 表示される
- キャラ画像の推奨は縦長の透過 PNG (表示枠は 740x1110 × scale)
- アプリのきせかえ編集モーダルで配布スキンを編集すると、単数フィールドのみ
  書き換わる (複数指定はそのまま残る)
