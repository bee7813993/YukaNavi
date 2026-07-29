# サンプルスキン「ゆかりの四季」の配布 zip を組み立てる。
# 画像はリポジトリ内の季節背景 (縦版) をリネームコピーして使う。
$ErrorActionPreference = "Stop"

$seasonalDir = Join-Path $PSScriptRoot "..\..\mascot\app_default_theme\backgrounds\seasonal"
$work = Join-Path $env:TEMP "yukari_four_seasons_zip"
$zipPath = Join-Path $PSScriptRoot "yukari_four_seasons.zip"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null

Copy-Item (Join-Path $PSScriptRoot "skin.json") (Join-Path $work "skin.json")
foreach ($season in @("spring", "summer", "autumn", "winter")) {
    $src = Join-Path $seasonalDir "yukanavi_home_background_${season}_outdoor_1080x1920.png"
    Copy-Item $src (Join-Path $work "bg_${season}.png")
}
# 一覧サムネイル (春の絵の流用。軽くするなら縮小版に差し替える)
Copy-Item (Join-Path $seasonalDir "yukanavi_home_background_spring_outdoor_1080x1920.png") (Join-Path $work "thumbnail.png")

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $work "*") -DestinationPath $zipPath
Remove-Item $work -Recurse -Force

Write-Host "作成しました: $zipPath"
