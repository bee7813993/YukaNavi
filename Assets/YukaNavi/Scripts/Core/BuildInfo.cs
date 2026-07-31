using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// ビルド時点の情報 (日時・git コミット)。設定画面のバージョン表示に添えて、
    /// 開発中のビルドが「どの時点のものか」を判別できるようにする。
    /// データはビルド直前に Editor/BuildInfoGenerator が
    /// Resources/build_info.json へ書き出す (リポジトリにはコミットしない)。
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
        /// 設定画面に出す1行 (例: "build 2026-07-31 21:04 / ec0d0cb+ (feat/xxx)")。
        /// エディタではビルド前のコードが動いているため固定文言、
        /// ビルド情報の無い実行ファイルでは空を返す。
        /// </summary>
        public static string Describe()
        {
#if UNITY_EDITOR
            return "エディタ実行 (ビルド前)";
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
            if (!string.IsNullOrEmpty(data.branch) && data.branch != "master")
            {
                // Claude の作業ブランチの接頭辞は情報にならないので省く
                string branch = data.branch;
                if (branch.StartsWith("claude/"))
                {
                    branch = branch.Substring("claude/".Length);
                }
                line += " (" + branch + ")";
            }
            return line;
#endif
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
    }
}
