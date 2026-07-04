# ゆかナビ デフォルトテーマ素材

KaraokeRequestorWeb 操作アプリ向けの「ゆかり」ちゃんデフォルトテーマ素材一式です。

## 高優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| 表情差分: 笑顔 | `expressions/yukari_expr_smile.png` | 1024x1536, RGBA 透過 |
| 表情差分: ウィンク | `expressions/yukari_expr_wink.png` | 1024x1536, RGBA 透過 |
| 表情差分: 驚き | `expressions/yukari_expr_surprised.png` | 1024x1536, RGBA 透過 |
| 予約完了ポーズ | `poses/yukari_pose_request_complete.png` | 1024x1536, RGBA 透過 |
| ホーム背景 | `backgrounds/yukanavi_home_background_1080x1920.png` | 1080x1920, RGB |

## 中優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| 「ゆかナビ」タイトルロゴ | `logo/yukanavi_logo.png` | 1800x520, RGBA 透過 |
| アプリアイコン | `icon/yukanavi_app_icon_1024.png` | 1024x1024, RGB, 角丸なし |
| タップ音 | `se/yukanavi_tap.wav` / `se/yukanavi_tap.ogg` | 0.12 秒 |
| 予約完了ジングル | `se/yukanavi_reservation_complete.wav` / `se/yukanavi_reservation_complete.ogg` | 1.45 秒 |

## 低優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| ホームBGM | `bgm/yukanavi_home_loop.wav` / `bgm/yukanavi_home_loop.ogg` | 16.0 秒ループ想定 |

## 補足

- `icon/yukanavi_app_icon_1024.png` は既存 `yukari_icon.png` を M0 流用候補として 1024x1024 に整えたものです。
- `_raw_` で始まるファイルは制作途中の中間ファイルです。アプリ実装では上記表のファイルだけを参照してください。
- 画像確認用プレビュー: `yukanavi_default_theme_preview.png`
