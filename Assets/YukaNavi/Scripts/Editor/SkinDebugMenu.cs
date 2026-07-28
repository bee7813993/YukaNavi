using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using YukaNavi.Core;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// きせかえスキンの動作検証用エディタメニュー。シーン不要で Console に結果を出す。
    /// 季節×昼夜 BGM・時間帯背景・時間帯セリフのフォールバック解決を時刻を注入して確認できる。
    /// </summary>
    public static class SkinDebugMenu
    {
        [MenuItem("YukaNavi/スキン検証: 時刻別の解決を表示")]
        public static void ShowResolution()
        {
            var skin = SkinManager.Current();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[YukaNavi] スキン「{skin.Name}」(id={(skin.Id == "" ? "デフォルト" : skin.Id)}) の時刻別解決:");

            // 4季節 × 昼夜の代表時刻 (BGM と時間帯背景)
            var times = new[]
            {
                new System.DateTime(2026, 4, 1, 10, 0, 0),
                new System.DateTime(2026, 4, 1, 22, 0, 0),
                new System.DateTime(2026, 7, 1, 10, 0, 0),
                new System.DateTime(2026, 7, 1, 22, 0, 0),
                new System.DateTime(2026, 10, 1, 10, 0, 0),
                new System.DateTime(2026, 10, 1, 22, 0, 0),
                new System.DateTime(2026, 1, 1, 10, 0, 0),
                new System.DateTime(2026, 1, 1, 22, 0, 0),
            };
            foreach (var t in times)
            {
                string season = SkinManager.SeasonSuffix(SkinManager.GetSeason(t.Month));
                string tod = SkinManager.IsDaytime(t.Hour) ? "day" : "night";
                var bgm = SkinManager.GetBgmFor(skin, t);
                var bg = SkinManager.GetTimedBackground(skin, t);
                var bgList = SkinManager.GetBackgroundsFor(skin, t);
                sb.AppendLine($"  {t:MM/dd HH:mm} ({season}_{tod}): BGM={DescribeLayer(bgm, "アプリ標準")} " +
                              $"時間帯背景={DescribeLayer(bg, "なし")} 背景リスト={bgList.Count}枚");
            }

            // 時間帯セリフ (朝 / 昼 / 夕方 / 夜)
            foreach (int hour in new[] { 7, 13, 19, 23 })
            {
                var t = new System.DateTime(2026, 4, 1, hour, 0, 0);
                var talk = SkinManager.GetTalkFor(skin, t);
                string suffix = SkinManager.GetTimeOfDaySuffix(hour) ?? "通常";
                sb.AppendLine($"  {hour}時 ({suffix}): セリフ=" +
                              (talk == null ? "(デフォルト)" : string.Join(" / ", talk)));
            }
            Debug.Log(sb.ToString());
        }

        static string DescribeLayer(SkinLayer layer, string fallback)
        {
            return (layer != null && !string.IsNullOrEmpty(layer.File)) ? layer.File : "(" + fallback + ")";
        }

        [MenuItem("YukaNavi/スキン検証: 全スキンの ValidateSkin")]
        public static void ValidateAll()
        {
            int count = 0;
            foreach (var skin in SkinManager.ListSkins())
            {
                if (skin.Folder == null)
                {
                    continue; // 組み込みデフォルトは常に正常
                }
                count++;
                if (!string.IsNullOrEmpty(skin.Error))
                {
                    Debug.LogError($"[YukaNavi] {skin.Id}: skin.json が壊れています: {skin.Error}");
                    continue;
                }
                var problems = SkinManager.ValidateSkin(skin);
                if (problems.Count > 0)
                {
                    Debug.LogWarning($"[YukaNavi] {skin.Id}: ⚠ {problems.Count} 件\n  "
                        + string.Join("\n  ", problems));
                }
                else
                {
                    Debug.Log($"[YukaNavi] {skin.Id}: OK");
                }
            }
            Debug.Log($"[YukaNavi] ValidateSkin 完了 ({count} スキン)");
        }

        [MenuItem("YukaNavi/スキン検証: 拡張パックの状態表示")]
        public static void ShowPackStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[YukaNavi] デフォルトテーマ拡張パック: {DefaultThemePack.PackRoot}");
            if (!Directory.Exists(DefaultThemePack.PackRoot))
            {
                sb.AppendLine("  未取得 (フォルダなし)。組み込みデータで動作中");
            }
            else
            {
                sb.AppendLine($"  version: {DefaultThemePack.LocalVersion()}");
                var talk = DefaultThemePack.GetTalk();
                sb.AppendLine("  talk.json: " + (talk == null ? "なし" : "あり"));
                foreach (var file in Directory.GetFiles(DefaultThemePack.PackRoot, "*",
                             SearchOption.AllDirectories))
                {
                    sb.AppendLine("  - " + file.Substring(DefaultThemePack.PackRoot.Length + 1));
                }
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("YukaNavi/スキン検証: 拡張パックを強制再取得")]
        public static void ForceUpdatePack()
        {
            Debug.Log("[YukaNavi] 拡張パックを強制再取得します...");
            DefaultThemePack.CheckForUpdateAsync(force: true);
        }

        [MenuItem("YukaNavi/スキン検証: 全キー入りテストスキンを生成")]
        public static void CreateTestSkin()
        {
            // Resources の既存素材を流用して、新スキーマ全キーの動作を確認できるスキンを作る。
            // BGM は容量節約のため代表2キーのみ (フォールバック鎖の確認も兼ねる)
            string res = Path.Combine(Application.dataPath, "YukaNavi/Resources");
            string folder = Path.Combine(SkinManager.SkinsRoot, "_test_allkeys");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);

            void Copy(string relSource, string destName)
            {
                File.Copy(Path.Combine(res, relSource), Path.Combine(folder, destName), true);
            }
            Copy("Art/Backgrounds/yukanavi_home_background_no_character_1080x1920.png", "bg.png");
            Copy("Art/ScreenArt/yukanavi_connect_illustration.png", "bg_night.png");
            Copy("Art/UI/Banners/yukanavi_banner_queue_1000x220.png", "bg_spring_day.png");
            Copy("Art/Mascot/yukari_mascot_transparent.png", "chara1.png");
            Copy("Art/Mascot/yukari_expr_eyes_closed.png", "chara1_blink.png");
            Copy("Art/Mascot/yukari_expr_wink.png", "chara1_wink.png");
            Copy("Art/Mascot/yukari_expr_surprised.png", "chara1_surprised.png");
            Copy("Art/Mascot/yukari_pose_request_complete.png", "chara2.png");
            Copy("Art/Mascot/yukari_pose_request_complete.png", "pose.png");
            Copy("Art/Mascot/yukari_expr_surprised.png", "thumbnail.png");
            Copy("Art/UI/yukanavi_record_disc.png", "record.png");
            Copy("Audio/BGM/yukanavi_home_loop.wav", "bgm.wav");
            Copy("Audio/BGM/yukanavi_home_loop.wav", "bgm_spring_day.wav");
            // タップ音に決定音を入れる (鳴れば差し替わったと分かる)
            Copy("Audio/SE/yukanavi_confirm.wav", "se_tap.wav");

            var def = new SkinDef
            {
                Name = "テスト (全キー)",
                Author = "SkinDebugMenu",
                Description = "新スキーマ全キーの動作検証用 (自動生成)",
                Backgrounds = new List<SkinLayer>
                {
                    new SkinLayer { Type = "image", File = "bg.png" },
                },
                BackgroundNight = new SkinLayer { Type = "image", File = "bg_night.png" },
                BackgroundSpringDay = new SkinLayer { Type = "image", File = "bg_spring_day.png" },
                Characters = new List<SkinLayer>
                {
                    new SkinLayer
                    {
                        Type = "image",
                        File = "chara1.png",
                        Talk = new List<string> { "キャラ1だよ" },
                        EyesClosed = "chara1_blink.png",
                        Expressions = new List<SkinExpression>
                        {
                            new SkinExpression { File = "chara1_wink.png", Talk = new List<string> { "ウィンク♪" } },
                            new SkinExpression { File = "chara1_surprised.png" },
                        },
                    },
                    new SkinLayer { Type = "image", File = "chara2.png" },
                },
                PoseComplete = new SkinLayer { Type = "image", File = "pose.png" },
                Bgm = new SkinLayer { Type = "audio", File = "bgm.wav" },
                BgmSpringDay = new SkinLayer { Type = "audio", File = "bgm_spring_day.wav" },
                SeTap = new SkinLayer { Type = "audio", File = "se_tap.wav" },
                Talk = new List<string> { "テストスキンだよ" },
                TalkMorning = new List<string> { "おはよう (テスト)" },
                TalkEvening = new List<string> { "こんばんは (テスト)" },
                TalkNight = new List<string> { "おやすみ (テスト)" },
                Theme = new SkinTheme { Primary = "#3A8ED0" },
            };
            string json = JsonConvert.SerializeObject(def, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            File.WriteAllText(Path.Combine(folder, "skin.json"), json, new System.Text.UTF8Encoding(false));

            SkinManager.BumpRevision();
            Debug.Log($"[YukaNavi] テストスキンを生成しました: {folder}\n" +
                      "きせかえ画面で「テスト (全キー)」を適用して動作を確認してください。");
        }
    }
}
