using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace YukaNavi.Core
{
    /// <summary>
    /// デフォルトテーマの拡張パック。ロジックに関係ないデータ (季節 BGM の音源やセリフの追加) を
    /// アプリのバージョンアップなしで配信するための仕組み。
    /// ykr.moe に置いたバージョン付き zip を起動時に確認し、新しければ
    /// persistentDataPath/default_theme/ に展開する。アセットの解決は「パック → Resources」の
    /// 2段構えで、パックが無ければ従来どおり動く。配信するのはメディアデータのみ (コードは含まない)。
    /// 仕様と配信手順は docs/default-theme-pack.md を参照。
    /// </summary>
    public static class DefaultThemePack
    {
        // 配信元 (静的ファイル。サーバープログラム不要)
        const string ManifestUrl = "https://ykr.moe/yukanavi_assets/default_theme_pack.json";
        const string BaseUrl = "https://ykr.moe/yukanavi_assets/";

        public static string PackRoot
        {
            get { return Path.Combine(Application.persistentDataPath, "default_theme"); }
        }

        /// <summary>パック内ファイルの絶対パスを返す (無ければ null)。</summary>
        public static string GetFilePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || relativePath.Contains(".."))
            {
                return null;
            }
            try
            {
                string path = Path.Combine(PackRoot, relativePath);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>拡張子違いを順に探してパック内ファイルの絶対パスを返す (無ければ null)。</summary>
        public static string FindFile(string relativePathNoExt, params string[] extensions)
        {
            foreach (var ext in extensions)
            {
                string path = GetFilePath(relativePathNoExt + ext);
                if (path != null)
                {
                    return path;
                }
            }
            return null;
        }

        // ---- セリフ (talk.json) ----

        /// <summary>talk.json の定義。デフォルトのゆかりちゃんのセリフをキー単位で差し替える。</summary>
        public class TalkDef
        {
            [JsonProperty("talk")] public List<string> Talk;
            [JsonProperty("talk_morning")] public List<string> TalkMorning;
            [JsonProperty("talk_evening")] public List<string> TalkEvening;
            [JsonProperty("talk_night")] public List<string> TalkNight;
        }

        static TalkDef _talk;
        static bool _talkLoaded;

        /// <summary>
        /// パックの talk.json (無ければ null)。書かれているキーだけがデフォルトのセリフを
        /// 上書きし、無いキーは組み込みのセリフのまま。
        /// </summary>
        public static TalkDef GetTalk()
        {
            if (!_talkLoaded)
            {
                _talkLoaded = true;
                try
                {
                    string path = GetFilePath("talk.json");
                    if (path != null)
                    {
                        _talk = JsonConvert.DeserializeObject<TalkDef>(File.ReadAllText(path));
                    }
                }
                catch
                {
                    _talk = null; // 壊れた talk.json は無視して組み込みセリフで動く
                }
            }
            return _talk;
        }

        // ---- 更新チェック ----

        class PackInfo
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("file")] public string File;
        }

        /// <summary>ローカルに展開済みのパックのバージョン (未取得なら 0)。</summary>
        public static int LocalVersion()
        {
            try
            {
                string path = GetFilePath("pack.json");
                if (path == null)
                {
                    return 0;
                }
                var info = JsonConvert.DeserializeObject<PackInfo>(File.ReadAllText(path));
                return info != null ? info.Version : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 更新チェック (起動時に呼ぶ)。サーバーのマニフェスト (数百バイト) を毎回確認し、
        /// version がローカルより新しいときだけ zip を取得して置き換える。
        /// 失敗はすべて黙って諦める (現行データのまま動き、次回起動時に再試行)。
        /// force=true で version 比較を飛ばして取り直す (検証メニュー用)。
        /// </summary>
        public static async void CheckForUpdateAsync(bool force = false)
        {
            try
            {
                var manifest = await FetchManifestAsync();
                if (manifest == null || string.IsNullOrEmpty(manifest.File))
                {
                    return;
                }
                if (!force && manifest.Version <= LocalVersion())
                {
                    return; // 最新
                }
                string zipPath = await DownloadZipAsync(manifest.File);
                if (zipPath == null)
                {
                    return;
                }
                if (!InstallZip(zipPath))
                {
                    return;
                }
                // 反映: セリフのキャッシュを破棄し、BGM は同名ファイル差し替えに備えて強制再読込。
                // マスコットのセリフはタップのたびに GetTalk() を参照するため次のタップから反映される
                _talkLoaded = false;
                _talk = null;
                Bgm.RefreshForCurrentSkin(force: true);
                Debug.Log("[YukaNavi] デフォルトテーマ拡張パックを v" + manifest.Version + " に更新しました");
            }
            catch
            {
                // ネットワーク不達などは無視 (パック無しでも全機能が動く)
            }
        }

        static async Task<PackInfo> FetchManifestAsync()
        {
            using (var req = UnityWebRequest.Get(ManifestUrl))
            {
                req.timeout = 15;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    return null;
                }
                try
                {
                    return JsonConvert.DeserializeObject<PackInfo>(req.downloadHandler.text);
                }
                catch
                {
                    return null;
                }
            }
        }

        static async Task<string> DownloadZipAsync(string fileName)
        {
            // マニフェストの file はファイル名のみ許可 (配信フォルダの外を指せないように)
            if (fileName.Contains("/") || fileName.Contains("\\") || fileName.Contains(".."))
            {
                return null;
            }
            string dest = Path.Combine(Application.temporaryCachePath, "default_theme_pack.zip");
            using (var req = new UnityWebRequest(BaseUrl + UnityWebRequest.EscapeURL(fileName)))
            {
                req.method = UnityWebRequest.kHttpVerbGET;
                req.downloadHandler = new DownloadHandlerFile(dest);
                req.timeout = 120;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    return null;
                }
            }
            return dest;
        }

        /// <summary>
        /// 取得した zip を検証して default_theme/ に置き換える。
        /// zip 直下 (またはフォルダ1階層下) に pack.json が無い zip はパックとして扱わず捨てる。
        /// </summary>
        static bool InstallZip(string zipPath)
        {
            string temp = Path.Combine(Application.temporaryCachePath, "default_theme_new");
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, true);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);
                string root = temp;
                if (!File.Exists(Path.Combine(root, "pack.json")))
                {
                    foreach (var sub in Directory.GetDirectories(temp))
                    {
                        if (File.Exists(Path.Combine(sub, "pack.json")))
                        {
                            root = sub;
                            break;
                        }
                    }
                }
                if (!File.Exists(Path.Combine(root, "pack.json")))
                {
                    return false;
                }
                if (Directory.Exists(PackRoot))
                {
                    Directory.Delete(PackRoot, true);
                }
                Directory.Move(root, PackRoot);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                    if (Directory.Exists(temp))
                    {
                        Directory.Delete(temp, true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
