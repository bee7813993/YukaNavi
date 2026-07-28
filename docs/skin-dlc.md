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

### 挙動

| 項目 | 挙動 |
|---|---|
| `backgrounds` (複数) | ホームの背景 (前面に UI が無いところ) をタップで次の背景へ。選んだ背景はスキンごとに端末保存 |
| `background_day` / `background_night` / `background_<季節>_<昼夜>` | 時間帯・季節で自動で切り替わる背景。指定があるとタップ巡回の1枚目 (自動枠) になり、他の `backgrounds` へもタップで巡回できる |
| `characters` (複数) | マスコットをタップで切替。表情 (`expressions`) があるキャラは 立ち絵 → 表情 → … と一巡してから次のキャラへ |
| `eyes_closed` | 目閉じ差分。書いたキャラは立ち絵表示中にまばたきする (立ち絵と同ポーズ・同解像度の透過 PNG 推奨) |
| `expressions` | 表情差分。`talk` を書くとその表情のときだけのセリフになる |
| `pose_complete` | 予約完了画面のポーズ絵。無ければキャラの1枚目 → デフォルトの順 |
| キャラごとの `talk` | 表示中のキャラのセリフが優先される。無いキャラはスキン全体の `talk` にフォールバック |
| `talk_morning` / `talk_evening` / `talk_night` | 朝 (5:00〜11:00) / 夕方 (17:00〜22:00) / 夜 (22:00〜翌5:00) のセリフ。通常の `talk` と合わせて抽選される |
| `bgm_day` / `bgm_night` | 6:00〜18:00 が昼。再生中に時間帯をまたぐと自動で切替 |
| `bgm_<季節>_<昼夜>` | 季節×昼夜の BGM (spring / summer / autumn / winter × day / night の8種)。季節は 3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬。優先順: 季節×昼夜 → `bgm_day`/`bgm_night` → `bgm` → アプリ標準 |
| `se_tap` / `se_confirm` / `se_error` / `se_transition` / `se_complete` | 効果音の差し替え (タップ / 決定 / エラー / 画面切替 / 予約完了)。無い音はアプリ標準のまま |
| `author` / `description` | きせかえ一覧に作者名を表示。`thumbnail.png` をフォルダに置くとサムネイルも出る |
| 従来の単数指定 | `background` / `character` / `bgm` も引き続き有効。複数指定と併用すると単数が1枚目扱い |

### 注意

- BGM・SE は mp3 / ogg / wav (m4a・aac は Unity のランタイム読み込み非対応)
- 背景動画は mp4 推奨。縦画面 (1080x1920 目安) に cover 表示される
- キャラ画像の推奨は縦長の透過 PNG (表示枠は 740x1110 × scale)
- 目閉じ・表情差分は立ち絵と同じポーズ・同じ解像度で顔だけ違うのが理想
  (画像は同じ枠に cover 表示されるだけで、位置合わせは自動ではない)
- アプリのきせかえ編集モーダルで配布スキンを編集すると、単数フィールド
  (name / background / character / bgm / record / talk / theme) のみ書き換わる。
  拡張フィールド (backgrounds / characters / 昼夜・季節 BGM など) は skin.json に
  そのまま維持される
- 書式の誤りはアプリのきせかえ一覧に ⚠ で出る。Unity Editor では
  メニュー「YukaNavi > スキン検証」でも確認できる
