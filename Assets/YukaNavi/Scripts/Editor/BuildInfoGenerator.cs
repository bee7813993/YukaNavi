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
    ///
    /// あわせて Cubism SDK がビルドに含まれているかも記録する。SDK が無くても
    /// ビルドは通ってマスコットが静止画に落ちるだけなので (実際にクラウドビルドの
    /// iOS で起きた)、ログの警告と設定画面の表示で気づけるようにしている。
    /// </summary>
    public class BuildInfoGenerator : IPreprocessBuildWithReport
    {
        const string OutputPath = "Assets/YukaNavi/Resources/build_info.json";

        /// <summary>
        /// Cubism SDK (submodule) が取得済みかの判定に使うディレクトリ。
        /// SDK の型は実行時リフレクションでしか触っていない (Live2DRuntimeLoader 参照) ので
        /// SDK が無くてもコンパイルは通ってしまい、存在確認はファイルで見るしかない。
        /// </summary>
        const string CubismPath = "Assets/Live2D/Cubism";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool live2dMissing = !Directory.Exists(CubismPath);
            if (live2dMissing)
            {
                // クラウドビルドのログは警告が数百行出るので、埋もれないよう検索できる印を付ける
                Debug.LogWarning(
                    "[YukaNavi] LIVE2D-SDK-MISSING: " + CubismPath + " が無い状態でビルドしています。"
                    + "マスコットは静止画にフォールバックします。"
                    + "submodule の取得 (git submodule update --init) を確認してください。");
            }

            var data = new BuildInfo.Data
            {
                builtAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                commit = BuildInfo.RunGit("rev-parse --short HEAD"),
                branch = BuildInfo.RunGit("rev-parse --abbrev-ref HEAD"),
                // 未追跡ファイルは無視 (作業メモ等で常に + が付くのを避ける)
                dirty = !string.IsNullOrEmpty(BuildInfo.RunGit("status --porcelain -uno")),
                live2dMissing = live2dMissing,
            };
            File.WriteAllText(OutputPath, JsonUtility.ToJson(data), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputPath); // 書いた直後のビルドに確実に含める
        }
    }
}
