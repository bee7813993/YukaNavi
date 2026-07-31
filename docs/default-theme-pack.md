# デフォルトテーマ拡張パック

デフォルトテーマ (ゆかりちゃん) のデータ追加を、**アプリのバージョンアップなしで**
配信するための仕組み。対象はロジックに関係ないメディアデータのみ (コードは配信しない)。

現在の対象:

- 組み込み季節×昼夜 BGM の音源 (未制作。できたらここで配信する)
- ゆかりちゃんのタップセリフの追加・差し替え (talk.json)

実装は `Assets/YukaNavi/Scripts/Core/DefaultThemePack.cs`。

## 動作の仕組み

1. アプリ起動時に `https://ykr.moe/yukanavi_assets/default_theme_pack.json` (マニフェスト、
   数百バイト) を毎回確認する
2. マニフェストの `version` が端末に展開済みのパック (`pack.json` の `version`) より
   新しければ、zip をダウンロードして `persistentDataPath/default_theme/` に置き換える
3. アセットの解決は **パック → Resources (アプリ内蔵) → 基本素材** の順。
   パックが無くても全機能が従来どおり動く
4. 失敗 (圏外・サーバー不達・壊れた zip) はすべて黙って諦め、次回起動時に再試行する

## サーバーに置くファイル (ykr.moe)

```
https://ykr.moe/yukanavi_assets/
├─ default_theme_pack.json      ← マニフェスト
└─ default_theme_pack_v2.zip    ← パック本体 (ファイル名は自由、マニフェストで指す)
```

`default_theme_pack.json`:

```json
{ "version": 2, "file": "default_theme_pack_v2.zip" }
```

- `version`: 1 から始まる整数。**上げないと端末は取り直さない**
- `file`: 同じフォルダに置いた zip のファイル名 (パス区切りは不可)
- zip を差し替えるときはファイル名も変える (`_v3.zip` 等) と CDN キャッシュに悩まされない

## zip (パック本体) の構成

zip 直下 (またはフォルダ1階層下) に `pack.json` が必要。無い zip はパックとして
認識されず捨てられる。

```
default_theme_pack_v2.zip
├─ pack.json                              ← {"version": 2} (マニフェストと同じ値にする)
├─ bgm/
│   ├─ yukanavi_home_loop_spring_day.ogg  ← 季節×昼夜 BGM (あるものだけでよい)
│   ├─ yukanavi_home_loop_spring_night.ogg
│   ├─ ... (summer / autumn / winter × day / night、計8種まで)
├─ live2d/                                ← Live2D モデル (任意)
│   └─ yukari/
│       ├─ yukari.model3.json
│       ├─ yukari.moc3
│       ├─ yukari.2048/texture_00.png
│       ├─ yukari.physics3.json
│       ├─ Idle.motion3.json
│       └─ TapBody.motion3.json
└─ talk.json                              ← セリフの差し替え (任意)
```

### 季節×昼夜 BGM

- ファイル名は `yukanavi_home_loop_<季節>_<昼夜>` + `.ogg` / `.mp3` / `.wav`
  (季節 = spring / summer / autumn / winter、昼夜 = day / night)
- 昼は 6:00〜18:00。季節は 3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬
- 8種すべて揃える必要はない。無い組み合わせは基本ループ曲 (アプリ内蔵) になる
- 仕様は既存 BGM と同じ 16 秒程度の自然ループ推奨 (ogg 推奨)
- スキンが BGM を持つ場合はスキンが優先 (パックはデフォルトテーマの音)

### Live2D モデルの配信

ホーム画面のマスコットを Live2D モデルに差し替える。**アプリを更新せずにモデルを
追加・差し替えできる**のがこの仕組みの主目的。

- 置き場所は `live2d/<任意のフォルダ名>/`。その直下に `*.model3.json` があれば拾う
  (フォルダ名・モデル名は決め打ちしない。複数フォルダがある場合は最初に見つかったもの)
- 中身は **Cubism Editor の書き出し一式そのまま**。アプリ側が
  `CubismModel3Json` の実行時ロード API で組み立てる ([Live2DRuntimeLoader.cs](../Assets/YukaNavi/Scripts/UI/Live2DRuntimeLoader.cs))
- **`model3.json` の `Motions` に `Idle` / `TapBody` を必ず記載する**。
  Cubism Editor の書き出しには `Motions` セクションが入らないので手で足す。
  記載が無いモーションはフェード情報が作られず再生できない
- `Idle` に `ParamBreath` のキーを打たないこと (呼吸はアプリ側が作る)。
  モデル制作の詳細仕様は [MODEL_REQUEST.md](../art/mascot/live2d_parts/MODEL_REQUEST.md)
- 解決順は **パック → Resources (アプリ組み込み) → 静止画**。パックのモデルが
  読めなかった場合は組み込みモデルに自動で戻るので、配信事故でマスコットが消えることはない
- スキンがキャラ画像を持つ場合はスキンが優先 (Live2D は表示されない)

#### 制約

- **`moc3` のバージョンが Cubism Core の対応を超えていると読めない**。
  新しい Cubism Editor で書き出したモデルは、アプリ側の SDK 更新 (= アプリ更新) が
  必要になることがある。書き出し時の moc3 バージョンは据え置きにするのが安全
- 表情 (`exp3.json`) は現状アプリ側で未使用。入れておいても読み込まれない
- 実行時に生成したテクスチャ・クリップは `Live2DRuntimeLoader.Model.Dispose()` で破棄する
  (スキン切替時に `Live2DMascotRenderer.Unload()` から呼ばれる)

#### きせかえスキンでは Live2D を許可しない

`skin.json` に Live2D のキーは**意図的に用意していない**。ユーザーが持ち込む zip から
任意の Live2D モデルを読み込めるようにすると、Live2D の
[拡張性アプリケーション](https://www.live2d.com/sdk/license/expandable/) に該当するため。

> ファイルやデータの追加や組み合わせ等によって不特定多数のモデルを利用および生成する派生作品

同ライセンスは審査・契約が必須で、**「有効な収益モデルを有していること (原則として
完全無料は許諾対象外)」**が条件に含まれる。ゆかナビは無料・アプリ内課金なし・広告なし
([store-listing.md](store/store-listing.md)) なので、そもそも許諾を受けられない可能性が高い。
専用ロゴの表示義務や四半期ごとの売上報告義務も付く。

一方、**このパックで配るのは自分たちの単一作品の更新**であり、「不特定多数のモデル」でも
「複数作品のコレクション/ポータル」でもないため通常の出版許諾の範囲に収まる。
この線引きを保つため、Live2D モデルの供給元はパックと Resources だけにしている
(実装上も `Live2DRuntimeLoader` を `SkinManager` 系から呼ばない)。

将来きせかえ側にも広げるなら、事前に Live2D へ「無料アプリでの可否」「公式配布限定なら
該当しないと言えるか」の 2 点を確認すること。

### talk.json

書いたキーだけが組み込みのセリフを**置き換える** (無いキーは組み込みのまま)。

```json
{
  "talk": ["うたっていこ〜♪", "新しいセリフも増えたよ！"],
  "talk_morning": ["おはよ〜！"],
  "talk_evening": ["こんばんは〜♪"],
  "talk_night": ["よふかしさんだ〜"]
}
```

- `talk` = 通常セリフ、`talk_morning` (5:00〜11:00) / `talk_evening` (17:00〜22:00) /
  `talk_night` (22:00〜翌5:00) = 時間帯セリフ (通常セリフと合算して抽選される)

## 配信作業の手順 (季節 BGM が完成したときの例)

1. 音源 8 ファイルを `bgm/` 構成で zip に固める。`pack.json` の `version` を
   現行 +1 にする
2. zip を `default_theme_pack_v3.zip` のような新しい名前で
   `ykr.moe/yukanavi_assets/` にアップロード
3. `default_theme_pack.json` の `version` と `file` を更新
4. 端末は次回起動時に自動で取得する。即時確認は Unity Editor の
   メニュー「YukaNavi > スキン検証: 拡張パックを強制再取得」

## 検証

- Unity Editor メニュー「YukaNavi > スキン検証: 拡張パックの状態表示」で
  展開済み version とファイル一覧を確認できる
- サーバーに置く前のローカル検証は、`persistentDataPath/default_theme/` に
  zip の中身を手で置けばよい (次の BGM 切り替わりタイミングから反映される)
- Windows Editor の実体パス: `%USERPROFILE%\AppData\LocalLow\bee7813993\ゆかナビ\default_theme\`

## 将来の拡張

同じ仕組みで、コード変更なしに配れるものを増やせる (`DefaultThemePack.GetFilePath()`
を解決点に追加するだけ)。候補: 季節背景 (デフォルトテーマ用)、追加の表情差分、
記念日スプラッシュなど。実行コード・アセットバンドルは配信しない
(iOS 審査ガイドラインの実行コード配信禁止に抵触しないための線引き)。
