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
        /// <summary>スキン専用 BGM (type="audio")。null = アプリのデフォルト BGM</summary>
        [JsonProperty("bgm")] public SkinLayer Bgm;
        /// <summary>リモコンのレコード盤画像 (type="image")。null = アプリ標準の盤</summary>
        [JsonProperty("record")] public SkinLayer Record;
        /// <summary>マスコットをタップしたときのセリフ (1要素=1つ、ランダム表示)。null = 標準</summary>
        [JsonProperty("talk")] public List<string> Talk;
        /// <summary>テーマ色。null = 既定 (紫)</summary>
        [JsonProperty("theme")] public SkinTheme Theme;
        /// <summary>ホーム画面パーツの配置 (時計など)。null = 未設定 (初期配置)</summary>
        [JsonProperty("layout")] public SkinHomeLayout Layout;

        /// <summary>スキンフォルダの絶対パス。null = 組み込みデフォルト</summary>
        [JsonIgnore] public string Folder;
        /// <summary>フォルダ名 (保存キー)。"" = 組み込みデフォルト</summary>
        [JsonIgnore] public string Id = "";
    }

    /// <summary>スキンのテーマ色。基準色1色から UI の派生色 (濃色・淡色・背景) を自動で作る。</summary>
    public class SkinTheme
    {
        /// <summary>基準色 ("#RRGGBB")</summary>
        [JsonProperty("primary")] public string Primary;
    }

    /// <summary>ホーム画面パーツ (時計/メッセージ/マスコット) の配置。スキンごとに保存される。</summary>
    public class SkinHomeLayout
    {
        [JsonProperty("clock")] public SkinLayoutItem Clock;
        [JsonProperty("ticker")] public SkinLayoutItem Ticker;
        [JsonProperty("mascot")] public SkinLayoutItem Mascot;
    }

    public class SkinLayoutItem
    {
        [JsonProperty("visible")] public bool Visible = true;
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("scale")] public float Scale = 1f;
    }

    public class SkinLayer
    {
        /// <summary>image | video | none (live2d は将来対応)</summary>
        [JsonProperty("type")] public string Type;
        [JsonProperty("file")] public string File;
        [JsonProperty("scale")] public float Scale = 1f;
        /// <summary>背景の回転 (度、90単位)</summary>
        [JsonProperty("rotation")] public float Rotation = 0f;
        /// <summary>背景のズーム (1 = 画面を覆うちょうどの大きさ)</summary>
        [JsonProperty("zoom")] public float Zoom = 1f;
        /// <summary>背景の位置調整 (画面幅に対する比率)</summary>
        [JsonProperty("offset_x")] public float OffsetX = 0f;
        /// <summary>背景の位置調整 (画面高さに対する比率)</summary>
        [JsonProperty("offset_y")] public float OffsetY = 0f;
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

        /// <summary>
        /// スキン内容の世代カウンタ。同じスキン ID のまま内容が変わったこと (編集) を
        /// 表示側が検知するために使う。
        /// </summary>
        public static int Revision { get; private set; }

        public static void BumpRevision()
        {
            Revision++;
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
                "    ├─ chara.png    ← キャラ画像 (透過PNG推奨)\r\n" +
                "    └─ record.png   ← リモコンのレコード盤 (円形の透過PNG)\r\n" +
                "\r\n" +
                "skin.json の例:\r\n" +
                "{\r\n" +
                "  \"name\": \"マイスキン\",\r\n" +
                "  \"background\": { \"type\": \"image\", \"file\": \"bg.png\" },\r\n" +
                "  \"character\": { \"type\": \"image\", \"file\": \"chara.png\", \"scale\": 1.0 },\r\n" +
                "  \"bgm\": { \"type\": \"audio\", \"file\": \"bgm.mp3\" },\r\n" +
                "  \"record\": { \"type\": \"image\", \"file\": \"record.png\" },\r\n" +
                "  \"talk\": [\"うたっていこ〜♪\", \"つぎはどの曲にする？\"]\r\n" +
                "}\r\n" +
                "\r\n" +
                "- background の type: \"image\" または \"video\" (mp4)\r\n" +
                "- character の type: \"image\" または \"none\" (キャラなし)\r\n" +
                "- bgm は任意 (mp3 / ogg / wav)。無ければアプリのデフォルト BGM\r\n" +
                "- record は任意。リモコン画面のレコード盤が差し替わります\r\n" +
                "- talk は任意。キャラをタップしたときのセリフ (ランダムで1つ表示)\r\n" +
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
            return CreateSkin(name, bgSourcePath, charSourcePath, 0f, 1f, Vector2.zero);
        }

        /// <summary>
        /// 背景の調整値 (回転/ズーム/オフセット) 付きでスキンを作成する。
        /// charNone=true でキャラを表示しないスキンになる (charSourcePath より優先)。
        /// </summary>
        public static string CreateSkin(string name, string bgSourcePath, string charSourcePath,
                                        float bgRotation, float bgZoom, Vector2 bgOffset,
                                        bool charNone = false, string bgmSourcePath = null,
                                        string themePrimary = null, string recordSourcePath = null,
                                        List<string> talkLines = null)
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
                    background = new SkinLayer
                    {
                        Type = isVideo ? "video" : "image",
                        File = destName,
                        Rotation = bgRotation,
                        Zoom = bgZoom,
                        OffsetX = bgOffset.x,
                        OffsetY = bgOffset.y,
                    };
                }

                SkinLayer character = null;
                if (charNone)
                {
                    character = new SkinLayer { Type = "none" };
                }
                else if (!string.IsNullOrEmpty(charSourcePath) && File.Exists(charSourcePath))
                {
                    string ext = Path.GetExtension(charSourcePath).ToLowerInvariant();
                    string destName = "chara" + ext;
                    File.Copy(charSourcePath, Path.Combine(folder, destName), true);
                    character = new SkinLayer { Type = "image", File = destName, Scale = 1f };
                }

                SkinLayer bgm = null;
                if (!string.IsNullOrEmpty(bgmSourcePath) && File.Exists(bgmSourcePath))
                {
                    string ext = Path.GetExtension(bgmSourcePath).ToLowerInvariant();
                    string destName = "bgm" + ext;
                    File.Copy(bgmSourcePath, Path.Combine(folder, destName), true);
                    bgm = new SkinLayer { Type = "audio", File = destName };
                }

                SkinLayer record = null;
                if (!string.IsNullOrEmpty(recordSourcePath) && File.Exists(recordSourcePath))
                {
                    string ext = Path.GetExtension(recordSourcePath).ToLowerInvariant();
                    string destName = "record" + ext;
                    File.Copy(recordSourcePath, Path.Combine(folder, destName), true);
                    record = new SkinLayer { Type = "image", File = destName };
                }

                var def = new SkinDef
                {
                    Name = name,
                    Background = background,
                    Character = character,
                    Bgm = bgm,
                    Record = record,
                    Talk = (talkLines != null && talkLines.Count > 0) ? talkLines : null,
                    Theme = string.IsNullOrEmpty(themePrimary) ? null : new SkinTheme { Primary = themePrimary },
                };
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

        /// <summary>
        /// 既存スキンを更新する。newBgSource / newCharSource / newBgmSource が null なら既存ファイルを維持する。
        /// charMode: 0=デフォルト (ゆかりちゃん) / 1=画像 / 2=キャラなし
        /// removeBgm=true でスキン BGM を外す (newBgmSource より優先)。
        /// </summary>
        public static bool UpdateSkin(SkinDef skin, string name, string newBgSource, string newCharSource,
                                      float bgRotation, float bgZoom, Vector2 bgOffset, int charMode,
                                      string newBgmSource = null, bool removeBgm = false,
                                      string themePrimary = null,
                                      string newRecordSource = null, bool removeRecord = false,
                                      List<string> talkLines = null)
        {
            if (skin.Folder == null)
            {
                return false;
            }
            try
            {
                var background = skin.Background;
                if (!string.IsNullOrEmpty(newBgSource) && File.Exists(newBgSource))
                {
                    if (background != null && !string.IsNullOrEmpty(background.File))
                    {
                        string old = Path.Combine(skin.Folder, background.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    string ext = Path.GetExtension(newBgSource).ToLowerInvariant();
                    bool isVideo = ext == ".mp4" || ext == ".webm" || ext == ".mov";
                    string destName = "bg" + ext;
                    File.Copy(newBgSource, Path.Combine(skin.Folder, destName), true);
                    background = new SkinLayer { Type = isVideo ? "video" : "image", File = destName };
                }
                if (background != null)
                {
                    background.Rotation = bgRotation;
                    background.Zoom = bgZoom;
                    background.OffsetX = bgOffset.x;
                    background.OffsetY = bgOffset.y;
                }

                var character = skin.Character;
                if (charMode == 2)
                {
                    character = new SkinLayer { Type = "none" };
                }
                else if (charMode == 0)
                {
                    character = null; // デフォルト (ゆかりちゃん)
                }
                else if (!string.IsNullOrEmpty(newCharSource) && File.Exists(newCharSource))
                {
                    if (character != null && character.Type == "image" && !string.IsNullOrEmpty(character.File))
                    {
                        string old = Path.Combine(skin.Folder, character.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    string ext = Path.GetExtension(newCharSource).ToLowerInvariant();
                    string destName = "chara" + ext;
                    File.Copy(newCharSource, Path.Combine(skin.Folder, destName), true);
                    character = new SkinLayer { Type = "image", File = destName, Scale = 1f };
                }
                // charMode==1 で newCharSource なし → 既存のキャラ画像を維持

                var bgm = skin.Bgm;
                if (removeBgm)
                {
                    if (bgm != null && !string.IsNullOrEmpty(bgm.File))
                    {
                        string old = Path.Combine(skin.Folder, bgm.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    bgm = null;
                }
                else if (!string.IsNullOrEmpty(newBgmSource) && File.Exists(newBgmSource))
                {
                    if (bgm != null && !string.IsNullOrEmpty(bgm.File))
                    {
                        string old = Path.Combine(skin.Folder, bgm.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    string ext = Path.GetExtension(newBgmSource).ToLowerInvariant();
                    string destName = "bgm" + ext;
                    File.Copy(newBgmSource, Path.Combine(skin.Folder, destName), true);
                    bgm = new SkinLayer { Type = "audio", File = destName };
                }

                var record = skin.Record;
                if (removeRecord)
                {
                    if (record != null && !string.IsNullOrEmpty(record.File))
                    {
                        string old = Path.Combine(skin.Folder, record.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    record = null;
                }
                else if (!string.IsNullOrEmpty(newRecordSource) && File.Exists(newRecordSource))
                {
                    if (record != null && !string.IsNullOrEmpty(record.File))
                    {
                        string old = Path.Combine(skin.Folder, record.File);
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                    }
                    string ext = Path.GetExtension(newRecordSource).ToLowerInvariant();
                    string destName = "record" + ext;
                    File.Copy(newRecordSource, Path.Combine(skin.Folder, destName), true);
                    record = new SkinLayer { Type = "image", File = destName };
                }

                var def = new SkinDef
                {
                    Name = string.IsNullOrEmpty(name) ? skin.Name : name,
                    Background = background,
                    Character = character,
                    Bgm = bgm,
                    Record = record,
                    Talk = (talkLines != null && talkLines.Count > 0) ? talkLines : null,
                    Theme = string.IsNullOrEmpty(themePrimary) ? null : new SkinTheme { Primary = themePrimary },
                    Layout = skin.Layout, // ホーム配置は編集操作では変えない (引き継ぐ)
                };
                string json = JsonConvert.SerializeObject(def, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                File.WriteAllText(Path.Combine(skin.Folder, "skin.json"), json, new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
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

        /// <summary>ホーム配置の1パーツ分を取得する (未設定なら null)。key: clock / ticker / mascot</summary>
        public static SkinLayoutItem GetLayoutItem(SkinDef skin, string key)
        {
            if (skin.Layout == null)
            {
                return null;
            }
            switch (key)
            {
                case "clock":
                    return skin.Layout.Clock;
                case "ticker":
                    return skin.Layout.Ticker;
                case "mascot":
                    return skin.Layout.Mascot;
                default:
                    return null;
            }
        }

        /// <summary>ホーム配置の1パーツ分をスキンに保存する (skin.json を書き戻す)。</summary>
        public static bool SaveLayoutItem(SkinDef skin, string key, SkinLayoutItem item)
        {
            if (skin.Folder == null)
            {
                return false;
            }
            try
            {
                if (skin.Layout == null)
                {
                    skin.Layout = new SkinHomeLayout();
                }
                switch (key)
                {
                    case "clock":
                        skin.Layout.Clock = item;
                        break;
                    case "ticker":
                        skin.Layout.Ticker = item;
                        break;
                    case "mascot":
                        skin.Layout.Mascot = item;
                        break;
                    default:
                        return false;
                }
                string json = JsonConvert.SerializeObject(skin, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                File.WriteAllText(Path.Combine(skin.Folder, "skin.json"), json, new UTF8Encoding(false));
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
