# ゆかナビ (YukaNavi)

カラオケリクエストツール「ゆかり」([KaraokeRequestorWeb](https://github.com/bee7813993/KaraokeRequestorWeb)) の公式ネイティブアプリ。
Unity 6 製、Android / Windows 先行(iOS は後続)。名前はカラオケ機の端末(キョクナビ)に由来し、「曲を探してナビゲートする端末」を表す。

- **設計書**: [docs/design.md](docs/design.md)
- **API 仕様書**(サーバーとの契約): [KaraokeRequestorWeb の api/README.md](https://github.com/bee7813993/KaraokeRequestorWeb/blob/master/api/README.md)
- **きせかえスキン仕様書**: [docs/skin-spec.md](docs/skin-spec.md)(skin.json 全キー・挙動・フォールバック順)。配布手順は [docs/skin-dlc.md](docs/skin-dlc.md)、Web 制作ツールは [tools/skin-maker/](tools/skin-maker/)
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
クローンした環境で Live2D を触るときは、各自で以下を実施する。

1. [Live2D 公式サイト](https://www.live2d.com/sdk/download/unity/)から
   **「Cubism SDK for Unity(URP版)」**をダウンロード
   (ページに URP 版と BiRP 版があるので注意。R5 以降は Built-in RP / HDRP がサポート外)
2. unitypackage をプロジェクトにインポート(`Assets/Live2D/` に入る)
3. `Assets/Settings/UniversalRP.asset` の Renderer List に
   `Assets/Live2D/Cubism/Rendering/URP/CubismURPRenderer.asset` を追加し、**Default に設定**
   ([公式手順](https://docs.live2d.com/en/cubism-sdk-tutorials/urp-import/))。この設定はコミット済みなので、
   SDK を入れると参照が解決して有効になる
4. 導入した SDK バージョンをこの README に記録すること

導入済み SDK バージョン: **Cubism 5 SDK for Unity R5** (`5-r.5` / 2026-04-02、URP 版)

#### このプロジェクトでの判断(公式手順との差分)

- **Color Space は Linear のまま**(公式は Gamma 推奨)。Gamma に変えると既存 UI 全体の色味が
  変わってしまうため。Cubism Editor と多少見た目が変わる場合はモデル側で調整する
- **HDR は無効にした**(公式推奨に合わせた)。ゆかナビは 2D の UI アプリで HDR の恩恵がなく、
  モバイルではメモリ帯域を無駄に使うだけのため。HDR を使う場合は
  HDR Precision を 64-bit にしないと背景が黒くなる
- **`Renderer2D.asset` は残したまま** Cubism を Default にしている。ゆかナビの UI は
  すべて ScreenSpaceOverlay で URP のレンダラーを経由しないため実害がない
  (Renderer Data が複数あってもゲーム実行時の描画には影響しないことを実機で確認済み)
- `Assets/csc.rsp` / `Assets/mcs.rsp` (`-unsafe`) は SDK 同梱のコンパイラ設定。
  SDK 本体と違いリポジトリに含めている(無いと Cubism Core を使うコードが通らない)

#### Cubism 5-r.5 (URP) の重要な仕様 — 実装前に必ず読むこと

SDK のソースを読んで確定させた、ハマりどころ。`Live2DMascotRenderer.cs` はこれらを前提にしている。

- **モデルの描画位置に親(祖先)の Transform が反映されない**。描画位置は
  「`CubismRenderController` の `localPosition` + 各 Drawable の `localPosition`」だけで決まる
  (`CubismRendererUsingBlendMode.ApplyTransform` が `localPosition` しか読まず、
  描画は `DrawMesh(mesh, Matrix4x4.identity, ...)` のため)。
  **モデルを動かしたいときは、親ではなくモデル自身の `localPosition` を変える**。
  回転・スケールも同様に local のみが効く
- **描画パスはカメラの `cullingMask`(レイヤー)を見ない**。登録されたモデルは全 Game カメラで
  描画されるので、特定カメラにだけ写らせたい場合は
  **レイヤー分離ではなく「視錐台の外に置く」**しかない
- **実行時は `MeshRenderer.bounds` が常に 0**。`MeshFilter` はエディタ非再生時にしか付与されない
  (`CubismRenderer.TryInitializeMeshFilter`)。実行時にモデルの大きさを知りたいときは
  各 `CubismRenderer` が持つ `Mesh` から求める
- **メッシュは実行時に構築される**ため、生成直後のフレームでは上記の `Mesh` もまだ空。
  数フレーム待ってリトライする必要がある
- Inspector の Camera Preview には**モデルが描画されない**(`CubismRenderPassFeature` が
  `cameraType` の Game / SceneView 以外を弾くため)。プレビューで表示確認をしないこと
- `RenderTexture` に描画する場合、Unity 6 の Render Graph API は**depth buffer 必須**
  (`new RenderTexture(w, h, 24, ...)`)。無いと何も描かれない
- **SDK が付けるのは「適用する側」だけで、動かす入力は付かない**。モデルを置いただけでは
  静止したままなので、アプリ側で次を補う (`Live2DMascotRenderer.SetUpMotionAndBlink`):
  - まばたき: `CubismEyeBlinkController` (適用) は付くが、値を作る
    **`CubismAutoEyeBlinkInput` は付かない**ので自分で追加する
  - 待機モーション: `Animator` は付くが、生成される **`AnimatorController` は空**
    (`DefaultState` も `Motions` も無い) で使えない。公式サンプルと同じく
    **`CubismMotionController.PlayAnimation(clip, ...)`** でクリップを直接再生する
- インポートで生成されるプレハブ名は**モデル名依存** (`<モデル名>.prefab`)。
  `Resources.Load` でパスを決め打ちせず、フォルダ内から `CubismModel` を持つものを探すと
  モデルを差し替えても動く
- **`CubismRenderer.Opacity` は internal、`CubismParameter.Value` はプロパティではなくフィールド**。
  リフレクションで読むときは `BindingFlags.NonPublic` / `GetField` が要る
- モデルを一度置いたフォルダの**親にも空の `*.fadeMotionList.asset` が作られることがある**。
  中身が null のまま残ると、次のインポートで `ArgumentNullException` を投げるので削除する
- 呼吸は**アプリ側 (`HarmonicMotion`) で `ParamBreath` を揺らしている**
  (`Live2DMascotRenderer.UseAutoBreath`、ホームでは常時 on)。SDK にまばたきの
  ような専用の自動呼吸コンポーネントは無く、これが相当機能。モーション側にも呼吸を
  入れると同じパラメータの取り合いになるので、**`Idle` には `ParamBreath` のキーを
  打たない**約束にしている (`art/mascot/live2d_parts/MODEL_REQUEST.md` §6)
- **`fadeMotionList` に載るのは `model3.json` に書かれたモーションだけ**。
  後から `.motion3.json` を Resources に置き足しても登録されず、再生時に
  `Not found motion from CubismFadeMotionList` になる。モーションを増やすときは
  必ず `model3.json` の `Motions` にも追加する
- **実行時に `AddComponent` すると `Reset()` は呼ばれない** (Unity 全般の仕様)。
  `CubismHarmonicMotionController.ChannelTimescales` は `Reset()` でしか初期化されないため、
  自前で配列を用意しないと毎フレーム `NullReferenceException` になる

#### モデルの供給元は 2 系統ある

- **Resources 組み込み** (`Assets/YukaNavi/Resources/Live2D/yukari/`)。エディタの Cubism
  インポータがプレハブに変換したものを読む。ビルドに焼き込まれるのでアプリ更新が必要
- **デフォルトテーマ拡張パック** (`persistentDataPath/default_theme/live2d/`)。
  Cubism の書き出し一式をそのまま置き、`Live2DRuntimeLoader` が実行時に組み立てる。
  **アプリを更新せずにモデルを追加・差し替えできる**。配信手順と制約は
  [docs/default-theme-pack.md](docs/default-theme-pack.md) を参照

解決順は「パック → Resources → 静止画」。**きせかえスキンからは Live2D を指定できない**
(Live2D の「拡張性アプリケーション」ライセンスに該当するのを避けるための線引き。
理由は上記ドキュメントに記載)。

#### モデルを差し替えるとき

Cubism Editor の書き出し (`art/mascot/live2d_parts/export/`) を、そのまま Resources へ
コピーしてはいけない。**ファイル名を `yukari.*` に統一する規約**で運用している
(`Assets/YukaNavi/Resources/Live2D/yukari/`)。手順:

1. `export/` と Resources のファイルをハッシュで突き合わせ、**変わったものだけ**コピーする
   (リグだけ直した場合は `.moc3` だけが変わる。テクスチャや physics まで毎回入れ替えない)
2. コピー先の名前は `yukari.moc3` / `yukari.physics3.json` / `yukari.cdi3.json` /
   `yukari.2048/texture_00.png`。**`.meta` は上書きせず既存を残す** (guid が変わると
   プレハブの参照が切れる)
3. **`yukari.model3.json` は Resources 側が正**。Cubism Editor の書き出しには
   `Motions` セクションが無く、上書きするとモーションが全部消える
4. Unity にフォーカスを戻して再インポートさせる (`yukari.asset` / `.prefab` /
   `*.anim` / `*.fade.asset` が再生成される)
5. 再生して呼吸・まばたき・タップを確認

書き出しに紛れ込む**全パラメータが値 0 の `.exp3.json`** (Cubism がモーション編集時に
作ることがある) は何もしないファイルなので取り込まない。

## 開発時の接続先

- ローカル XAMPP の「ゆかり」(`http://localhost/`)。localhost からのアクセスは easyauth 素通りなので開発が楽
- Android 実機からは同一 LAN のサーバー IP(例: `http://192.168.x.x/`)を指定

## Git 運用

- master へ直接コミットしない(ブランチ → PR → マージ)
- 画像・音声・動画などのバイナリは **Git LFS** で管理([.gitattributes](.gitattributes) 参照)。
  無料枠は **ストレージ 10GB / 帯域 月 10GB**(2026-07 時点。以前は各 1GB だった)。
  使用量は [GitHub の Billing](https://github.com/settings/billing) の「Git LFS」で確認する
  (API では取得できない)。帯域は clone / pull のたびに消費されるので、
  CI が頻繁に取得する大きなファイルには引き続き注意する
- コミットメッセージは `<type>: <日本語要約>` 形式(`feat` / `fix` / `docs` / `refactor` / `chore`)。
  末尾に `Co-Authored-By` を付ける運用はサーバーリポジトリと同じ
