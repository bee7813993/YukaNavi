using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// ビルド直前にビルド時点の情報 (日時・git コミット) を Resources へ書き出す。
    /// 設定画面のバージョン表示 (Core/BuildInfo) が「どの時点のビルドか」を示すのに使う。
    /// 生成物はビルドごとに変わるためリポジトリにはコミットしない (.gitignore 済み)。
    /// git が使えない環境 (UCB 等) では日時だけになり、UCB では実行時に
    /// UnityCloudBuildManifest がフォールバックとして参照される。
    /// </summary>
    public class BuildInfoGenerator : IPreprocessBuildWithReport
    {
        const string OutputPath = "Assets/YukaNavi/Resources/build_info.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var data = new Core.BuildInfo.Data
            {
                builtAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                commit = Git("rev-parse --short HEAD"),
                branch = Git("rev-parse --abbrev-ref HEAD"),
                // 未追跡ファイルは無視 (作業メモ等で常に + が付くのを避ける)
                dirty = !string.IsNullOrEmpty(Git("status --porcelain -uno")),
            };
            File.WriteAllText(OutputPath, JsonUtility.ToJson(data), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputPath); // 書いた直後のビルドに確実に含める
        }

        /// <summary>git コマンドの標準出力 (1行)。失敗したら null。</summary>
        static string Git(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return null;
                    }
                    if (process.ExitCode != 0)
                    {
                        return null;
                    }
                    return output.Trim();
                }
            }
            catch (System.Exception)
            {
                return null; // git 不在などはビルド情報なしで続行 (ビルドは止めない)
            }
        }
    }
}
