using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// M0 ビルド確認用のメニュー。出力先の Build/ は .gitignore 済み。
    /// </summary>
    public static class BuildCommands
    {
        static readonly string[] Scenes = { "Assets/Scenes/SampleScene.unity" };

        const string AndroidIdentifier = "com.bee7813993.yukanavi";
        const string IconPath = "Assets/YukaNavi/Art/Icon/yukanavi_app_icon_1024.png";
        const string AdaptiveFgPath = "Assets/YukaNavi/Art/Icon/yukanavi_icon_adaptive_fg.png";
        const string AdaptiveBgPath = "Assets/YukaNavi/Art/Icon/yukanavi_icon_adaptive_bg.png";

        /// <summary>再生モード中のビルドは Unity に拒否されるため、分かりやすく案内して中断する。</summary>
        static bool CanBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[YukaNavi] 再生モード中はビルドできません。停止 (■) してから実行してください。");
                return false;
            }
            return true;
        }

        [MenuItem("YukaNavi/ビルド/Windows (Build~Windows)")]
        public static void BuildWindows()
        {
            if (!CanBuild())
            {
                return;
            }
            EnsureSettings();
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = "Build/Windows/YukaNavi.exe",
                target = BuildTarget.StandaloneWindows64,
            };
            Report(BuildPipeline.BuildPlayer(options), "Build/Windows");
        }

        [MenuItem("YukaNavi/ビルド/Android (APK)")]
        public static void BuildAndroid()
        {
            if (!CanBuild())
            {
                return;
            }
            EnsureSettings();
            // Play ストア提出時は AAB だが、M0 は実機に直接入れる APK で確認する
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = "Build/Android/YukaNavi.apk",
                target = BuildTarget.Android,
            };
            Report(BuildPipeline.BuildPlayer(options), "Build/Android");
        }

        /// <summary>アプリ名・識別子・アイコンを設定する (何度実行しても同じ結果になる)。</summary>
        static void EnsureSettings()
        {
            PlayerSettings.productName = "ゆかナビ";
            PlayerSettings.companyName = "bee7813993";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidIdentifier);

            // Windows: デモは縦持ちレイアウトのため、スマホ縦型のウィンドウで起動する
            // (横画面レイアウト対応は M1 以降)
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;

            // ゆかりは LAN 内の http 運用が基本のため、Android の非 HTTPS 通信を許可する
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // 現状の UI は縦持ち専用のため Android は Portrait 固定 (横レイアウトは将来対応)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            }

            // Android のアダプティブアイコン (Android 8+)。未設定だとランチャーが
            // デフォルトアイコンを白枠の中に縮小表示してしまう。
            // 前景はマスクで切られない中央 66.7% (表示保証領域) にキャラを収めた素材
            var fg = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveFgPath);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBgPath);
            if (icon != null && fg != null && bg != null)
            {
                SetAndroidIcons(AndroidPlatformIconKind.Adaptive, bg, fg); // [背景, 前景] の順
                SetAndroidIcons(AndroidPlatformIconKind.Round, icon);
                SetAndroidIcons(AndroidPlatformIconKind.Legacy, icon);
            }
            AssetDatabase.SaveAssets();
        }

        static void SetAndroidIcons(PlatformIconKind kind, params Texture2D[] layers)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var platformIcon in icons)
            {
                for (int i = 0; i < layers.Length && i < platformIcon.maxLayerCount; i++)
                {
                    platformIcon.SetTexture(layers[i], i);
                }
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }

        static void Report(BuildReport report, string revealPath)
        {
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[YukaNavi] ビルド成功: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} 秒)");
                EditorUtility.RevealInFinder(summary.outputPath);
            }
            else
            {
                Debug.LogError($"[YukaNavi] ビルド失敗: {summary.result} " +
                               $"(エラー {summary.totalErrors} 件。Console と Editor.log を確認)");
            }
        }
    }
}
