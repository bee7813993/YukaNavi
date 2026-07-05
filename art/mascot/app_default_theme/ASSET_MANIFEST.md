# ゆかナビ デフォルトテーマ素材

KaraokeRequestorWeb 操作アプリ向けの「ゆかり」ちゃんデフォルトテーマ素材一式です。

## 高優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| 表情差分: 笑顔 | `expressions/yukari_expr_smile.png` | 1024x1536, RGBA 透過 |
| 表情差分: ウィンク | `expressions/yukari_expr_wink.png` | 1024x1536, RGBA 透過 |
| 表情差分: 驚き | `expressions/yukari_expr_surprised.png` | 1024x1536, RGBA 透過 |
| 表情差分: 目閉じ | `expressions/yukari_expr_eyes_closed.png` | 1024x1536, RGBA 透過。まばたき用 |
| 表情差分: 困り顔 | `expressions/yukari_expr_worried_error.png` | 1024x1536, RGBA 透過。通信エラー時用 |
| 予約完了ポーズ | `poses/yukari_pose_request_complete.png` | 1024x1536, RGBA 透過 |
| 案内ポーズ | `poses/yukari_pose_guidance.png` | 1024x1536, RGBA 透過。手を差し出すガイド用 |
| ホーム背景 | `backgrounds/yukanavi_home_background_1080x1920.png` | 1080x1920, RGB |
| 横持ちホーム背景 | `backgrounds/yukanavi_home_background_landscape_1920x1080.png` | 1920x1080, RGB |
| ホーム背景（ゆかりなし） | `backgrounds/yukanavi_home_background_no_character_1080x1920.png` | 1080x1920, RGB。透過立ち絵重ね用 |
| 横持ちホーム背景（ゆかりなし） | `backgrounds/yukanavi_home_background_no_character_landscape_1920x1080.png` | 1920x1080, RGB。透過立ち絵重ね用 |
| スプラッシュ縦 | `splash/yukanavi_splash_portrait_1080x1920.png` | 1080x1920, RGB。ロゴ+ゆかりちゃん |
| スプラッシュ横 | `splash/yukanavi_splash_landscape_1920x1080.png` | 1920x1080, RGB。ロゴ+ゆかりちゃん |

## 中優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| 「ゆかナビ」タイトルロゴ | `logo/yukanavi_logo.png` | 1800x520, RGBA 透過 |
| アプリアイコン | `icon/yukanavi_app_icon_1024.png` | 1024x1024, RGB, 角丸なし |
| タップ音 | `se/yukanavi_tap.wav` / `se/yukanavi_tap.ogg` | 0.12 秒 |
| 決定音 | `se/yukanavi_confirm.wav` / `se/yukanavi_confirm.ogg` | 0.25 秒 |
| エラー音 | `se/yukanavi_error.wav` / `se/yukanavi_error.ogg` | 0.42 秒 |
| 画面遷移音 | `se/yukanavi_transition.wav` / `se/yukanavi_transition.ogg` | 0.36 秒 |
| 予約完了ジングル | `se/yukanavi_reservation_complete.wav` / `se/yukanavi_reservation_complete.ogg` | 1.45 秒 |
| セリフ吹き出し | `ui/yukanavi_speech_bubble_9slice_tail_left.png` | 1024x448, RGBA 透過。推奨9-slice border: L=170, R=80, T=80, B=135 |
| セリフ吹き出し本体のみ | `ui/yukanavi_speech_bubble_9slice_body.png` | 1024x360, RGBA 透過。推奨9-slice border: L=80, R=80, T=80, B=80 |
| 音符パーティクル | `particles/yukanavi_particle_note_single_256.png` | 256x256, RGBA 透過, 白単色 |
| 音符パーティクル | `particles/yukanavi_particle_note_double_256.png` | 256x256, RGBA 透過, 白単色 |
| きらめきパーティクル | `particles/yukanavi_particle_sparkle_256.png` | 256x256, RGBA 透過, 白単色 |

## 低優先

| 用途 | ファイル | 仕様 |
|---|---|---|
| ホームBGM | `bgm/yukanavi_home_loop.wav` / `bgm/yukanavi_home_loop.ogg` | 16.0 秒ループ想定 |

## 補足

- `icon/yukanavi_app_icon_1024.png` は既存 `yukari_icon.png` を M0 流用候補として 1024x1024 に整えたものです。
- `_raw_` で始まるファイルは制作途中の中間ファイルです。アプリ実装では上記表のファイルだけを参照してください。
- 画像確認用プレビュー: `yukanavi_default_theme_preview.png`
- 追加素材確認用プレビュー: `yukanavi_extra_assets_preview.png`
- ゆかりなし背景確認用プレビュー: `yukanavi_backgrounds_no_character_preview.png`
- 表情・ポーズ追加素材確認用プレビュー: `yukanavi_interaction_assets_preview.png`
- スプラッシュ確認用プレビュー: `yukanavi_splash_preview.png`
