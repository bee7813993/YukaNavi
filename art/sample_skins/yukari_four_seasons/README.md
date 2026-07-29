# サンプルスキン「ゆかりの四季」

季節×昼夜スキーマのショーケース兼、動作検証用の公式サンプルスキン。
季節 (3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬) に合わせて
ホーム背景が屋外ステージの四季の絵に自動で切り替わる。
キャラは指定しない (デフォルトのゆかりちゃんのまま、まばたき・表情も従来どおり)。

画像の実体はリポジトリの容量節約のため置いていない。
`art/mascot/app_default_theme/backgrounds/seasonal/` の季節背景 (縦版) を
`make_zip.ps1` がコピーして配布 zip を組み立てる。

## 配布 zip の作り方

```powershell
powershell -ExecutionPolicy Bypass -File make_zip.ps1
```

`yukari_four_seasons.zip` がこのフォルダに出来る。中身:

```
yukari_four_seasons.zip
├─ skin.json
├─ bg_spring.png   ← spring_outdoor 1080x1920
├─ bg_summer.png
├─ bg_autumn.png
├─ bg_winter.png
└─ thumbnail.png   ← 一覧サムネイル (春の絵の流用)
```

## 配布方法

- **zip 取り込み**: そのままユーザーに渡し、きせかえ画面の「取り込む (zip)」から
- **DLC コード**: `docs/skin-dlc.md` の手順どおり、コード名にリネームして
  `https://ykr.moe/yukanavi_dlc/<コード>.zip` に置く

## 手を入れるなら

- 夜は同じ絵を使っている (`background_*_night` が `*_day` と同じファイル)。
  夜版の絵ができたら `bg_spring_night.png` 等を追加して skin.json を差し替える
- `thumbnail.png` は等倍コピーなので、配布前に 256x256 程度へ縮小すると zip が軽くなる
- 季節×昼夜の BGM (`bgm_spring_day` 等) も音源を足せば同じ zip に同梱できる
