using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YukaNavi.Core;

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
            var data = new BuildInfo.Data
            {
                builtAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                commit = BuildInfo.RunGit("rev-parse --short HEAD"),
                branch = BuildInfo.RunGit("rev-parse --abbrev-ref HEAD"),
                // 未追跡ファイルは無視 (作業メモ等で常に + が付くのを避ける)
                dirty = !string.IsNullOrEmpty(BuildInfo.RunGit("status --porcelain -uno")),
            };
            File.WriteAllText(OutputPath, JsonUtility.ToJson(data), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputPath); // 書いた直後のビルドに確実に含める
        }
    }
}
