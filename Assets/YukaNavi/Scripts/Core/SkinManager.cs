using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>skin.json の定義。設計書 §6.1 のマニフェスト形式。</summary>
    public class SkinDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("background")] public SkinLayer Background;
        [JsonProperty("character")] public SkinLayer Character;

        /// <summary>スキンフォルダの絶対パス。null = 組み込みデフォルト</summary>
        [JsonIgnore] public string Folder;
        /// <summary>フォルダ名 (保存キー)。"" = 組み込みデフォルト</summary>
        [JsonIgnore] public string Id = "";
    }

    public class SkinLayer
    {
        /// <summary>image | video | none (live2d は将来対応)</summary>
        [JsonProperty("type")] public string Type;
        [JsonProperty("file")] public string File;
        [JsonProperty("scale")] public float Scale = 1f;
    }

    /// <summary>
    /// スキン (きせかえ) の管理。persistentDataPath/skins/ 配下のフォルダをスキャンする。
    /// 壊れた skin.json や欠けたファイルは呼び出し側でデフォルトへフォールバックする。
    /// </summary>
    public static class SkinManager
    {
        public static string SkinsRoot
        {
            get { return Path.Combine(Application.persistentDataPath, "skins"); }
        }

        /// <summary>組み込みデフォルト (ゆかりちゃん + rich 動画背景)。</summary>
        public static SkinDef Default
        {
            get { return new SkinDef { Id = "", Name = "デフォルト (ゆかりちゃん)" }; }
        }

        /// <summary>スキンフォルダを用意し、初回は書き方の readme を生成する。</summary>
        public static void EnsureRoot()
        {
            try
            {
                if (!Directory.Exists(SkinsRoot))
                {
                    Directory.CreateDirectory(SkinsRoot);
                }
                string readme = Path.Combine(SkinsRoot, "readme.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme, BuildReadme(), new UTF8Encoding(true));
                }
            }
            catch
            {
                // フォルダが作れなくてもアプリは動かす (デフォルトスキンのみ)
            }
        }

        static string BuildReadme()
        {
            return
                "ゆかナビ きせかえフォルダ\r\n" +
                "\r\n" +
                "このフォルダにスキン (きせかえ) を置けます。\r\n" +
                "\r\n" +
                "skins/\r\n" +
                "└─ myskin/          ← 好きなフォルダ名\r\n" +
                "    ├─ skin.json    ← 設定ファイル (下記)\r\n" +
                "    ├─ bg.png       ← 背景 (画像 または mp4 動画)\r\n" +
                "    └─ chara.png    ← キャラ画像 (透過PNG推奨)\r\n" +
                "\r\n" +
                "skin.json の例:\r\n" +
                "{\r\n" +
                "  \"name\": \"マイスキン\",\r\n" +
                "  \"background\": { \"type\": \"image\", \"file\": \"bg.png\" },\r\n" +
                "  \"character\": { \"type\": \"image\", \"file\": \"chara.png\", \"scale\": 1.0 }\r\n" +
                "}\r\n" +
                "\r\n" +
                "- background の type: \"image\" または \"video\" (mp4)\r\n" +
                "- character の type: \"image\" または \"none\" (キャラなし)\r\n" +
                "- ファイルが見つからない場合はデフォルトに戻ります\r\n";
        }

        /// <summary>デフォルト + skins/ 配下の有効なスキンを列挙する。</summary>
        public static List<SkinDef> ListSkins()
        {
            var list = new List<SkinDef> { Default };
            try
            {
                EnsureRoot();
                foreach (var dir in Directory.GetDirectories(SkinsRoot))
                {
                    string jsonPath = Path.Combine(dir, "skin.json");
                    if (!File.Exists(jsonPath))
                    {
                        continue;
                    }
                    try
                    {
                        var def = JsonConvert.DeserializeObject<SkinDef>(File.ReadAllText(jsonPath));
                        if (def == null)
                        {
                            continue;
                        }
                        def.Folder = dir;
                        def.Id = new DirectoryInfo(dir).Name;
                        if (string.IsNullOrEmpty(def.Name))
                        {
                            def.Name = def.Id;
                        }
                        list.Add(def);
                    }
                    catch
                    {
                        // 壊れた skin.json はスキップ
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        /// <summary>保存されている選択スキンを解決する (見つからなければデフォルト)。</summary>
        public static SkinDef Current()
        {
            string id = AppConfig.SkinId;
            if (string.IsNullOrEmpty(id))
            {
                return Default;
            }
            foreach (var skin in ListSkins())
            {
                if (skin.Id == id)
                {
                    return skin;
                }
            }
            return Default;
        }

        /// <summary>
        /// 選択されたファイルから新しいスキンを作成する (ファイルをコピーして skin.json を生成)。
        /// 成功時はスキン ID (フォルダ名)、失敗時は null を返す。
        /// </summary>
        public static string CreateSkin(string name, string bgSourcePath, string charSourcePath)
        {
            try
            {
                EnsureRoot();
                string id = SanitizeFolderName(name);
                if (string.IsNullOrEmpty(id))
                {
                    id = "skin";
                }
                string folder = Path.Combine(SkinsRoot, id);
                int serial = 1;
                while (Directory.Exists(folder))
                {
                    serial++;
                    folder = Path.Combine(SkinsRoot, id + "_" + serial);
                }
                id = new DirectoryInfo(folder).Name;
                Directory.CreateDirectory(folder);

                SkinLayer background = null;
                if (!string.IsNullOrEmpty(bgSourcePath) && File.Exists(bgSourcePath))
                {
                    string ext = Path.GetExtension(bgSourcePath).ToLowerInvariant();
                    bool isVideo = ext == ".mp4" || ext == ".webm" || ext == ".mov";
                    string destName = "bg" + ext;
                    File.Copy(bgSourcePath, Path.Combine(folder, destName), true);
                    background = new SkinLayer { Type = isVideo ? "video" : "image", File = destName };
                }

                SkinLayer character = null;
                if (!string.IsNullOrEmpty(charSourcePath) && File.Exists(charSourcePath))
                {
                    string ext = Path.GetExtension(charSourcePath).ToLowerInvariant();
                    string destName = "chara" + ext;
                    File.Copy(charSourcePath, Path.Combine(folder, destName), true);
                    character = new SkinLayer { Type = "image", File = destName, Scale = 1f };
                }

                var def = new SkinDef { Name = name, Background = background, Character = character };
                string json = JsonConvert.SerializeObject(def, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                File.WriteAllText(Path.Combine(folder, "skin.json"), json, new UTF8Encoding(false));
                return id;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>フォルダ名に使えない文字を除去する。</summary>
        static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (System.Array.IndexOf(invalid, c) < 0)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// スキンを zip に書き出す (一時フォルダに作成)。zip のパスを返す (失敗時 null)。
        /// 呼び出し側が共有シート (Android) やフォルダ表示 (Windows) に渡す。
        /// </summary>
        public static string ExportSkin(SkinDef skin)
        {
            if (skin.Folder == null)
            {
                return null;
            }
            try
            {
                string exportDir = Path.Combine(Application.temporaryCachePath, "skin_export");
                Directory.CreateDirectory(exportDir);
                string zipPath = Path.Combine(exportDir, skin.Id + ".zip");
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                System.IO.Compression.ZipFile.CreateFromDirectory(skin.Folder, zipPath);
                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// zip からスキンを取り込む。成功時はスキン ID を返す (skin.json が無い zip は null)。
        /// </summary>
        public static string ImportSkin(string zipPath)
        {
            try
            {
                EnsureRoot();
                string baseName = SanitizeFolderName(Path.GetFileNameWithoutExtension(zipPath));
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = "imported";
                }
                string folder = Path.Combine(SkinsRoot, baseName);
                int serial = 1;
                while (Directory.Exists(folder))
                {
                    serial++;
                    folder = Path.Combine(SkinsRoot, baseName + "_" + serial);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, folder);

                // skin.json がルートに無ければ1階層下から探して引き上げる
                // (フォルダごと zip 圧縮された場合への対応)
                if (!File.Exists(Path.Combine(folder, "skin.json")))
                {
                    foreach (var sub in Directory.GetDirectories(folder))
                    {
                        if (!File.Exists(Path.Combine(sub, "skin.json")))
                        {
                            continue;
                        }
                        foreach (var f in Directory.GetFiles(sub))
                        {
                            File.Move(f, Path.Combine(folder, Path.GetFileName(f)));
                        }
                        foreach (var d in Directory.GetDirectories(sub))
                        {
                            Directory.Move(d, Path.Combine(folder, Path.GetFileName(d)));
                        }
                        Directory.Delete(sub, true);
                        break;
                    }
                }
                if (!File.Exists(Path.Combine(folder, "skin.json")))
                {
                    Directory.Delete(folder, true);
                    return null;
                }
                return new DirectoryInfo(folder).Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>スキンを削除する。</summary>
        public static bool DeleteSkin(SkinDef skin)
        {
            if (skin.Folder == null)
            {
                return false;
            }
            try
            {
                Directory.Delete(skin.Folder, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>スキンフォルダ内ファイルの絶対パスを返す (無ければ null)。</summary>
        public static string GetFilePath(SkinDef skin, string file)
        {
            if (skin.Folder == null || string.IsNullOrEmpty(file) || file.Contains(".."))
            {
                return null;
            }
            string path = Path.Combine(skin.Folder, file);
            return File.Exists(path) ? path : null;
        }

        /// <summary>スキンフォルダ内の画像を読み込む (失敗時 null)。</summary>
        public static Texture2D LoadTexture(SkinDef skin, string file)
        {
            string path = GetFilePath(skin, file);
            if (path == null)
            {
                return null;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes))
                {
                    Object.Destroy(tex);
                    return null;
                }
                return tex;
            }
            catch
            {
                return null;
            }
        }
    }
}
