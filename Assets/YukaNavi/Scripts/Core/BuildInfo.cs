using UnityEngine;
#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
#endif

namespace YukaNavi.Core
{
    /// <summary>
    /// 実行中のコードが「どの時点のものか」を設定画面のバージョン表示に添える。
    /// - ビルド版: ビルド直前に Editor/BuildInfoGenerator が書き出した
    ///   Resources/build_info.json (日時・git コミット) を表示する
    /// - エディタ: 常に作業ツリーのコードが動くため、git の現在位置をその場で取得して表示する
    /// </summary>
    public static class BuildInfo
    {
        [System.Serializable]
        public class Data
        {
            public string builtAt; // ビルドした日時 "2026-07-31 21:04"
            public string commit;  // git の短縮ハッシュ (取得できなければ空)
            public string branch;  // ビルド時のブランチ (master なら表示しない)
            public bool dirty;     // コミットしていない変更を含むビルド (表示は「+」)

            // Cubism SDK (submodule) がビルドに含まれていない。マスコットが静止画に
            // フォールバックしただけのビルドはエラーにならず気づけないので、実機で
            // 見えるようにする。「無い」ときだけ true にするのは、SDK の情報を持たない
            // UCB マニフェストからの復元で誤って「なし」と出さないため
            public bool live2dMissing;
        }

        /// <summary>
        /// Unity Build Automation (Cloud Build) がビルドへ自動で埋め込むマニフェスト。
        /// git 情報が取れなかったクラウドビルドのフォールバックに使う。
        /// </summary>
        [System.Serializable]
        class CloudBuildManifest
        {
            public string scmCommitId;
            public string buildStartTime;
        }

        static Data _data;
        static bool _loaded;

        static Data Load()
        {
            if (_loaded)
            {
                return _data;
            }
            _loaded = true;
            var text = Resources.Load<TextAsset>("build_info");
            if (text != null)
            {
                try
                {
                    _data = JsonUtility.FromJson<Data>(text.text);
                }
                catch (System.Exception)
                {
                    // 壊れた build_info はビルド情報なしとして扱う
                }
            }
            return _data;
        }

        /// <summary>
        /// 設定画面に出す1行。
        /// ビルド版: "build 2026-07-31 21:04 / ec0d0cb+ (feat/xxx)"
        /// エディタ: "エディタ実行 / ec0d0cb+ (feat/xxx)"
        /// ビルド情報の無い実行ファイルでは空を返す (表示しない)。
        /// </summary>
        public static string Describe()
        {
#if UNITY_EDITOR
            return DescribeEditor();
#else
            var data = Load();
            if (data == null || string.IsNullOrEmpty(data.builtAt))
            {
                data = FromCloudBuildManifest();
            }
            if (data == null || string.IsNullOrEmpty(data.builtAt))
            {
                return "";
            }
            string line = "build " + data.builtAt;
            if (!string.IsNullOrEmpty(data.commit))
            {
                line += " / " + data.commit + (data.dirty ? "+" : "");
            }
            return line + BranchSuffix(data.branch) + (data.live2dMissing ? " / Live2D なし" : "");
#endif
        }

        /// <summary>ブランチの表示 (" (name)")。master と Claude の作業接頭辞は情報にならないので省く。</summary>
        static string BranchSuffix(string branch)
        {
            if (string.IsNullOrEmpty(branch) || branch == "master")
            {
                return "";
            }
            if (branch.StartsWith("claude/"))
            {
                branch = branch.Substring("claude/".Length);
            }
            return " (" + branch + ")";
        }

        /// <summary>UCB のマニフェストからビルド情報を復元する (git が使えなかったとき用)。</summary>
        static Data FromCloudBuildManifest()
        {
            var text = Resources.Load<TextAsset>("UnityCloudBuildManifest.json");
            if (text == null)
            {
                return null;
            }
            try
            {
                var manifest = JsonUtility.FromJson<CloudBuildManifest>(text.text);
                if (manifest == null || string.IsNullOrEmpty(manifest.buildStartTime))
                {
                    return null;
                }
                string commit = manifest.scmCommitId ?? "";
                if (commit.Length > 7)
                {
                    commit = commit.Substring(0, 7);
                }
                return new Data
                {
                    builtAt = manifest.buildStartTime,
                    commit = commit,
                };
            }
            catch (System.Exception)
            {
                return null;
            }
        }

#if UNITY_EDITOR
        static string _editorDescribe;

        static string DescribeEditor()
        {
            if (_editorDescribe != null)
            {
                return _editorDescribe;
            }
            string commit = RunGit("rev-parse --short HEAD");
            if (string.IsNullOrEmpty(commit))
            {
                _editorDescribe = "エディタ実行 (ビルド前)"; // git が無い環境
            }
            else
            {
                bool dirty = !string.IsNullOrEmpty(RunGit("status --porcelain -uno"));
                _editorDescribe = "エディタ実行 / " + commit + (dirty ? "+" : "")
                    + BranchSuffix(RunGit("rev-parse --abbrev-ref HEAD"));
            }
            return _editorDescribe;
        }

        /// <summary>
        /// プロジェクトルートで git コマンドを実行して標準出力を返す。失敗したら null。
        /// エディタ専用 (BuildInfoGenerator のビルド時書き出しもこれを使う)。
        /// </summary>
        public static string RunGit(string arguments)
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
                return null; // git 不在などは「情報なし」扱い (動作は止めない)
            }
        }
#endif
    }
}
