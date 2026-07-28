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
        /// <summary>作者名 (任意)。きせかえ一覧に表示される</summary>
        [JsonProperty("author")] public string Author;
        /// <summary>スキンの説明 (任意)</summary>
        [JsonProperty("description")] public string Description;
        [JsonProperty("background")] public SkinLayer Background;
        [JsonProperty("character")] public SkinLayer Character;
        /// <summary>
        /// 背景 (複数。ホームの背景タップで切替)。単数 background と併用可 (単数が先頭)。
        /// 配布スキン用の拡張で、アプリの編集モーダルでは単数のみ扱う。
        /// </summary>
        [JsonProperty("backgrounds")] public List<SkinLayer> Backgrounds;
        /// <summary>昼 (6:00〜18:00) の背景。あれば backgrounds / background より優先で先頭になる</summary>
        [JsonProperty("background_day")] public SkinLayer BackgroundDay;
        /// <summary>夜 (18:00〜翌6:00) の背景。あれば backgrounds / background より優先で先頭になる</summary>
        [JsonProperty("background_night")] public SkinLayer BackgroundNight;
        /// <summary>
        /// 季節×昼夜の背景。あれば background_day / background_night よりさらに優先。
        /// 季節は気象学的区分 (3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬)
        /// </summary>
        [JsonProperty("background_spring_day")] public SkinLayer BackgroundSpringDay;
        [JsonProperty("background_spring_night")] public SkinLayer BackgroundSpringNight;
        [JsonProperty("background_summer_day")] public SkinLayer BackgroundSummerDay;
        [JsonProperty("background_summer_night")] public SkinLayer BackgroundSummerNight;
        [JsonProperty("background_autumn_day")] public SkinLayer BackgroundAutumnDay;
        [JsonProperty("background_autumn_night")] public SkinLayer BackgroundAutumnNight;
        [JsonProperty("background_winter_day")] public SkinLayer BackgroundWinterDay;
        [JsonProperty("background_winter_night")] public SkinLayer BackgroundWinterNight;
        /// <summary>キャラ画像 (複数。マスコットのタップで切替)。単数 character と併用可</summary>
        [JsonProperty("characters")] public List<SkinLayer> Characters;
        /// <summary>予約完了画面のポーズ絵 (type="image")。null = キャラの1枚目 → デフォルト</summary>
        [JsonProperty("pose_complete")] public SkinLayer PoseComplete;
        /// <summary>スキン専用 BGM (type="audio")。null = アプリのデフォルト BGM</summary>
        [JsonProperty("bgm")] public SkinLayer Bgm;
        /// <summary>昼 (6:00〜18:00) の BGM。あれば bgm より優先</summary>
        [JsonProperty("bgm_day")] public SkinLayer BgmDay;
        /// <summary>夜 (18:00〜翌6:00) の BGM。あれば bgm より優先</summary>
        [JsonProperty("bgm_night")] public SkinLayer BgmNight;
        /// <summary>
        /// 季節×昼夜の BGM。あれば bgm_day / bgm_night よりさらに優先。
        /// 季節は気象学的区分 (3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬)
        /// </summary>
        [JsonProperty("bgm_spring_day")] public SkinLayer BgmSpringDay;
        [JsonProperty("bgm_spring_night")] public SkinLayer BgmSpringNight;
        [JsonProperty("bgm_summer_day")] public SkinLayer BgmSummerDay;
        [JsonProperty("bgm_summer_night")] public SkinLayer BgmSummerNight;
        [JsonProperty("bgm_autumn_day")] public SkinLayer BgmAutumnDay;
        [JsonProperty("bgm_autumn_night")] public SkinLayer BgmAutumnNight;
        [JsonProperty("bgm_winter_day")] public SkinLayer BgmWinterDay;
        [JsonProperty("bgm_winter_night")] public SkinLayer BgmWinterNight;
        /// <summary>効果音の差し替え (type="audio")。null = アプリ標準の音</summary>
        [JsonProperty("se_tap")] public SkinLayer SeTap;
        [JsonProperty("se_confirm")] public SkinLayer SeConfirm;
        [JsonProperty("se_error")] public SkinLayer SeError;
        [JsonProperty("se_transition")] public SkinLayer SeTransition;
        /// <summary>予約完了音の差し替え (type="audio")</summary>
        [JsonProperty("se_complete")] public SkinLayer SeComplete;
        /// <summary>リモコンのレコード盤画像 (type="image")。null = アプリ標準の盤</summary>
        [JsonProperty("record")] public SkinLayer Record;
        /// <summary>マスコットをタップしたときのセリフ (1要素=1つ、ランダム表示)。null = 標準</summary>
        [JsonProperty("talk")] public List<string> Talk;
        /// <summary>朝 (5:00〜11:00) のセリフ。talk と合わせて抽選される</summary>
        [JsonProperty("talk_morning")] public List<string> TalkMorning;
        /// <summary>夕方 (17:00〜22:00) のセリフ。talk と合わせて抽選される</summary>
        [JsonProperty("talk_evening")] public List<string> TalkEvening;
        /// <summary>夜 (22:00〜翌5:00) のセリフ。talk と合わせて抽選される</summary>
        [JsonProperty("talk_night")] public List<string> TalkNight;
        /// <summary>テーマ色。null = 既定 (紫)</summary>
        [JsonProperty("theme")] public SkinTheme Theme;
        /// <summary>ホーム画面パーツの配置 (時計など)。null = 未設定 (初期配置)</summary>
        [JsonProperty("layout")] public SkinHomeLayout Layout;

        /// <summary>スキンフォルダの絶対パス。null = 組み込みデフォルト</summary>
        [JsonIgnore] public string Folder;
        /// <summary>フォルダ名 (保存キー)。"" = 組み込みデフォルト</summary>
        [JsonIgnore] public string Id = "";
        /// <summary>skin.json の読み込みエラー (null = 正常)。一覧で「壊れている」と示す用</summary>
        [JsonIgnore] public string Error;
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
        /// <summary>
        /// このキャラ専用のセリフ (characters の要素で使う。1要素=1つ、タップでランダム表示)。
        /// null ならスキン全体の talk にフォールバック。
        /// </summary>
        [JsonProperty("talk")] public List<string> Talk;
        /// <summary>
        /// 目閉じ差分のファイル (characters の要素で使う)。指定するとそのキャラがまばたきする。
        /// 立ち絵と同じポーズ・同じ解像度の透過 PNG 推奨。
        /// </summary>
        [JsonProperty("eyes_closed")] public string EyesClosed;
        /// <summary>
        /// 表情差分 (characters の要素で使う)。タップで 立ち絵 → 表情1 → 表情2 → … と巡回し、
        /// 一巡すると次のキャラへ切り替わる。
        /// </summary>
        [JsonProperty("expressions")] public List<SkinExpression> Expressions;
        /// <summary>背景の回転 (度、90単位)</summary>
        [JsonProperty("rotation")] public float Rotation = 0f;
        /// <summary>背景のズーム (1 = 画面を覆うちょうどの大きさ)</summary>
        [JsonProperty("zoom")] public float Zoom = 1f;
        /// <summary>背景の位置調整 (画面幅に対する比率)</summary>
        [JsonProperty("offset_x")] public float OffsetX = 0f;
        /// <summary>背景の位置調整 (画面高さに対する比率)</summary>
        [JsonProperty("offset_y")] public float OffsetY = 0f;
    }

    /// <summary>キャラの表情差分1枚分。</summary>
    public class SkinExpression
    {
        /// <summary>表情画像 (立ち絵と同じポーズ・同じ解像度の透過 PNG 推奨)</summary>
        [JsonProperty("file")] public string File;
        /// <summary>この表情のときのセリフ (任意)。無ければキャラ → スキン全体の talk へ</summary>
        [JsonProperty("talk")] public List<string> Talk;
    }

    /// <summary>
    /// 季節 (気象学的区分: 3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬)。
    /// アニメ期のインデックス区分 (PeriodScreen: 1〜3月=冬 等) とは別物なので混同しないこと。
    /// </summary>
    public enum Season { Spring, Summer, Autumn, Winter }

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
                // readme は説明の更新 (splash.png の追記など) が届くよう、内容が変わったら書き直す
                string readme = Path.Combine(SkinsRoot, "readme.txt");
                string readmeContent = BuildReadme();
                if (!File.Exists(readme) || File.ReadAllText(readme) != readmeContent)
                {
                    File.WriteAllText(readme, readmeContent, new UTF8Encoding(true));
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
                "    ├─ record.png   ← リモコンのレコード盤 (円形の透過PNG)\r\n" +
                "    └─ splash.png   ← 起動画面 (縦 1080x1920 推奨)\r\n" +
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
                "- splash.png は任意 (skin.json への記載は不要)。フォルダに置くと\r\n" +
                "  スキン適用中の起動画面が差し替わります\r\n" +
                "- ファイルが見つからない場合はデフォルトに戻ります\r\n" +
                "\r\n" +
                "配布スキン向けの拡張 (skin.json に追記):\r\n" +
                "  \"author\": \"作者名\",\r\n" +
                "  \"description\": \"スキンの説明\",\r\n" +
                "  \"backgrounds\": [ {\"type\":\"video\",\"file\":\"bg1.mp4\"},\r\n" +
                "                    {\"type\":\"image\",\"file\":\"bg2.png\"} ],\r\n" +
                "  \"characters\":  [ {\"type\":\"image\",\"file\":\"chara1.png\",\r\n" +
                "                     \"talk\":[\"キャラ1のセリフ\"],\r\n" +
                "                     \"eyes_closed\":\"chara1_blink.png\",\r\n" +
                "                     \"expressions\":[ {\"file\":\"chara1_smile.png\",\r\n" +
                "                                       \"talk\":[\"えへへ♪\"]} ]},\r\n" +
                "                    {\"type\":\"image\",\"file\":\"chara2.png\"} ],\r\n" +
                "  \"pose_complete\": {\"type\":\"image\",\"file\":\"pose.png\"},\r\n" +
                "  \"bgm_day\":   {\"type\":\"audio\",\"file\":\"bgm_day.ogg\"},\r\n" +
                "  \"bgm_night\": {\"type\":\"audio\",\"file\":\"bgm_night.ogg\"},\r\n" +
                "  \"bgm_spring_day\": {\"type\":\"audio\",\"file\":\"spring_day.ogg\"},\r\n" +
                "  \"background_night\": {\"type\":\"image\",\"file\":\"bg_night.png\"},\r\n" +
                "  \"background_winter_day\": {\"type\":\"image\",\"file\":\"bg_wd.png\"},\r\n" +
                "  \"se_tap\": {\"type\":\"audio\",\"file\":\"tap.ogg\"},\r\n" +
                "  \"talk_morning\": [\"おはよ〜！\"]\r\n" +
                "- author / description: 作者名と説明。きせかえ一覧に表示されます\r\n" +
                "- backgrounds: 複数の背景。ホームの背景 (何もないところ) をタップで切替\r\n" +
                "- characters: 複数のキャラ。マスコットをタップで切替。各キャラに talk を\r\n" +
                "  書くとそのキャラ専用のセリフになる (無ければスキン全体の talk)\r\n" +
                "- eyes_closed: 目閉じ差分。書くとそのキャラがまばたきします\r\n" +
                "- expressions: 表情差分。タップで 立ち絵 → 表情 → … → 次のキャラ と巡回。\r\n" +
                "  表情に talk を書くとその表情のときだけのセリフになります\r\n" +
                "- pose_complete: 予約完了画面のポーズ絵 (無ければキャラの1枚目)\r\n" +
                "- bgm_day / bgm_night: 昼 (6時〜18時) と夜で BGM を自動で切替\r\n" +
                "- bgm_<季節>_<昼夜>: 季節×昼夜の BGM。spring / summer / autumn / winter ×\r\n" +
                "  day / night の8種。季節は 3〜5月=春 / 6〜8月=夏 / 9〜11月=秋 / 12〜2月=冬。\r\n" +
                "  優先順: 季節×昼夜 → bgm_day/bgm_night → bgm → アプリ標準\r\n" +
                "- background_day / background_night / background_<季節>_<昼夜>: 背景も同様に\r\n" +
                "  時間帯・季節で自動切替 (指定時はタップ巡回の1枚目になる)\r\n" +
                "- se_tap / se_confirm / se_error / se_transition / se_complete: 効果音の\r\n" +
                "  差し替え (タップ音 / 決定音 / エラー音 / 画面切替音 / 予約完了音)\r\n" +
                "- talk_morning / talk_evening / talk_night: 時間帯セリフ。朝 (5時〜11時) /\r\n" +
                "  夕方 (17時〜22時) / 夜 (22時〜翌5時) に talk と合わせて抽選されます\r\n" +
                "- thumbnail.png をフォルダに置くと一覧にサムネイルが出ます (記載不要)\r\n" +
                "- いずれも従来の単数指定 (background / character / bgm) と併用できます\r\n";
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
                    catch (System.Exception e)
                    {
                        // 壊れた skin.json も一覧に出し、理由を確認できるようにする (手書き編集の支援)
                        list.Add(new SkinDef
                        {
                            Name = new DirectoryInfo(dir).Name,
                            Folder = dir,
                            Id = new DirectoryInfo(dir).Name,
                            Error = e.Message,
                        });
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        /// <summary>
        /// スキン定義の書式チェック。問題点のリストを返す (空 = OK)。
        /// skin.json を手書きする配布スキン制作の支援用で、きせかえ一覧に ⚠ として出る。
        /// </summary>
        public static List<string> ValidateSkin(SkinDef skin)
        {
            var problems = new List<string>();
            if (skin.Folder == null)
            {
                return problems; // 組み込みデフォルトは常に正常
            }
            if (!string.IsNullOrEmpty(skin.Error))
            {
                problems.Add("skin.json が壊れています: " + skin.Error);
                return problems;
            }
            CheckLayer(skin, skin.Background, "background", "image/video", problems);
            CheckLayers(skin, skin.Backgrounds, "backgrounds", "image/video", problems);
            CheckLayer(skin, skin.BackgroundDay, "background_day", "image/video", problems);
            CheckLayer(skin, skin.BackgroundNight, "background_night", "image/video", problems);
            CheckLayer(skin, skin.BackgroundSpringDay, "background_spring_day", "image/video", problems);
            CheckLayer(skin, skin.BackgroundSpringNight, "background_spring_night", "image/video", problems);
            CheckLayer(skin, skin.BackgroundSummerDay, "background_summer_day", "image/video", problems);
            CheckLayer(skin, skin.BackgroundSummerNight, "background_summer_night", "image/video", problems);
            CheckLayer(skin, skin.BackgroundAutumnDay, "background_autumn_day", "image/video", problems);
            CheckLayer(skin, skin.BackgroundAutumnNight, "background_autumn_night", "image/video", problems);
            CheckLayer(skin, skin.BackgroundWinterDay, "background_winter_day", "image/video", problems);
            CheckLayer(skin, skin.BackgroundWinterNight, "background_winter_night", "image/video", problems);
            CheckLayer(skin, skin.Character, "character", "image/none", problems);
            CheckLayers(skin, skin.Characters, "characters", "image", problems);
            CheckCharacterExtras(skin, skin.Character, "character", problems);
            if (skin.Characters != null)
            {
                for (int i = 0; i < skin.Characters.Count; i++)
                {
                    CheckCharacterExtras(skin, skin.Characters[i], "characters[" + i + "]", problems);
                }
            }
            CheckLayer(skin, skin.PoseComplete, "pose_complete", "image", problems);
            CheckLayer(skin, skin.Bgm, "bgm", "audio", problems);
            CheckLayer(skin, skin.BgmDay, "bgm_day", "audio", problems);
            CheckLayer(skin, skin.BgmNight, "bgm_night", "audio", problems);
            CheckLayer(skin, skin.BgmSpringDay, "bgm_spring_day", "audio", problems);
            CheckLayer(skin, skin.BgmSpringNight, "bgm_spring_night", "audio", problems);
            CheckLayer(skin, skin.BgmSummerDay, "bgm_summer_day", "audio", problems);
            CheckLayer(skin, skin.BgmSummerNight, "bgm_summer_night", "audio", problems);
            CheckLayer(skin, skin.BgmAutumnDay, "bgm_autumn_day", "audio", problems);
            CheckLayer(skin, skin.BgmAutumnNight, "bgm_autumn_night", "audio", problems);
            CheckLayer(skin, skin.BgmWinterDay, "bgm_winter_day", "audio", problems);
            CheckLayer(skin, skin.BgmWinterNight, "bgm_winter_night", "audio", problems);
            CheckLayer(skin, skin.SeTap, "se_tap", "audio", problems);
            CheckLayer(skin, skin.SeConfirm, "se_confirm", "audio", problems);
            CheckLayer(skin, skin.SeError, "se_error", "audio", problems);
            CheckLayer(skin, skin.SeTransition, "se_transition", "audio", problems);
            CheckLayer(skin, skin.SeComplete, "se_complete", "audio", problems);
            CheckLayer(skin, skin.Record, "record", "image", problems);
            return problems;
        }

        /// <summary>
        /// アプリ内の編集モーダルが扱わない拡張フィールド (複数指定・季節/昼夜・SE・時間帯セリフ等)
        /// を持つか。配布スキンの編集時に「保持される」旨の案内を出す判定に使う。
        /// </summary>
        public static bool HasExtendedFields(SkinDef skin)
        {
            return (skin.Backgrounds != null && skin.Backgrounds.Count > 0)
                || (skin.Characters != null && skin.Characters.Count > 0)
                || skin.BgmDay != null || skin.BgmNight != null
                || skin.BgmSpringDay != null || skin.BgmSpringNight != null
                || skin.BgmSummerDay != null || skin.BgmSummerNight != null
                || skin.BgmAutumnDay != null || skin.BgmAutumnNight != null
                || skin.BgmWinterDay != null || skin.BgmWinterNight != null
                || skin.BackgroundDay != null || skin.BackgroundNight != null
                || skin.BackgroundSpringDay != null || skin.BackgroundSpringNight != null
                || skin.BackgroundSummerDay != null || skin.BackgroundSummerNight != null
                || skin.BackgroundAutumnDay != null || skin.BackgroundAutumnNight != null
                || skin.BackgroundWinterDay != null || skin.BackgroundWinterNight != null
                || skin.PoseComplete != null
                || skin.SeTap != null || skin.SeConfirm != null || skin.SeError != null
                || skin.SeTransition != null || skin.SeComplete != null
                || (skin.TalkMorning != null && skin.TalkMorning.Count > 0)
                || (skin.TalkEvening != null && skin.TalkEvening.Count > 0)
                || (skin.TalkNight != null && skin.TalkNight.Count > 0);
        }

        /// <summary>キャラ拡張 (eyes_closed / expressions) のファイル存在チェック。</summary>
        static void CheckCharacterExtras(SkinDef skin, SkinLayer layer, string label, List<string> problems)
        {
            if (layer == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(layer.EyesClosed) && GetFilePath(skin, layer.EyesClosed) == null)
            {
                problems.Add(label + ": eyes_closed のファイルが見つかりません (" + layer.EyesClosed + ")");
            }
            if (layer.Expressions != null)
            {
                for (int i = 0; i < layer.Expressions.Count; i++)
                {
                    var expr = layer.Expressions[i];
                    string exprLabel = label + ".expressions[" + i + "]";
                    if (expr == null || string.IsNullOrEmpty(expr.File))
                    {
                        problems.Add(exprLabel + ": file がありません");
                        continue;
                    }
                    if (GetFilePath(skin, expr.File) == null)
                    {
                        problems.Add(exprLabel + ": ファイルが見つかりません (" + expr.File + ")");
                    }
                }
            }
        }

        static void CheckLayers(SkinDef skin, List<SkinLayer> layers, string label,
                                string validTypes, List<string> problems)
        {
            if (layers == null)
            {
                return;
            }
            for (int i = 0; i < layers.Count; i++)
            {
                CheckLayer(skin, layers[i], label + "[" + i + "]", validTypes, problems);
            }
        }

        static void CheckLayer(SkinDef skin, SkinLayer layer, string label,
                               string validTypes, List<string> problems)
        {
            if (layer == null)
            {
                return;
            }
            if (layer.Type == "none")
            {
                return; // キャラなし指定 (file 不要)
            }
            if (string.IsNullOrEmpty(layer.Type) || !("/" + validTypes + "/").Contains("/" + layer.Type + "/"))
            {
                problems.Add(label + ": type「" + layer.Type + "」は使えません (" + validTypes + ")");
            }
            if (string.IsNullOrEmpty(layer.File))
            {
                problems.Add(label + ": file がありません");
                return;
            }
            if (GetFilePath(skin, layer.File) == null)
            {
                problems.Add(label + ": ファイルが見つかりません (" + layer.File + ")");
                return;
            }
            string ext = Path.GetExtension(layer.File).ToLowerInvariant();
            // Unity のランタイム読み込みが非対応の音声形式 (BGM・SE 共通)
            if (validTypes == "audio" && (ext == ".m4a" || ext == ".aac"))
            {
                problems.Add(label + ": m4a/aac は再生できません (mp3/ogg/wav に変換してください)");
            }
            // type と拡張子の食い違い (画像 type に動画ファイル等)
            bool looksVideo = ext == ".mp4" || ext == ".mov" || ext == ".webm";
            bool looksImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg"
                || ext == ".webp" || ext == ".gif";
            if (layer.Type == "image" && looksVideo)
            {
                problems.Add(label + ": type が image ですが動画ファイルです (video にしてください)");
            }
            else if (layer.Type == "video" && looksImage)
            {
                problems.Add(label + ": type が video ですが画像ファイルです (image にしてください)");
            }
        }

        /// <summary>背景リスト (単数 background と backgrounds の合成。ファイル指定のあるものだけ)。</summary>
        public static List<SkinLayer> GetBackgrounds(SkinDef skin)
        {
            return MergeLayers(skin.Background, skin.Backgrounds);
        }

        /// <summary>キャラ画像リスト (単数 character と characters の合成)。type="none" は含めない。</summary>
        public static List<SkinLayer> GetCharacters(SkinDef skin)
        {
            var list = MergeLayers(skin.Character, skin.Characters);
            list.RemoveAll(layer => layer.Type == "none");
            return list;
        }

        static List<SkinLayer> MergeLayers(SkinLayer single, List<SkinLayer> multiple)
        {
            var list = new List<SkinLayer>();
            if (single != null && (!string.IsNullOrEmpty(single.File) || single.Type == "none"))
            {
                list.Add(single);
            }
            if (multiple != null)
            {
                foreach (var layer in multiple)
                {
                    if (layer != null && !string.IsNullOrEmpty(layer.File))
                    {
                        list.Add(layer);
                    }
                }
            }
            return list;
        }

        // ---- 時刻・季節ヘルパー ----

        /// <summary>月から季節を求める (3〜5月=春, 6〜8月=夏, 9〜11月=秋, 12〜2月=冬)。</summary>
        public static Season GetSeason(int month)
        {
            if (month >= 3 && month <= 5) { return Season.Spring; }
            if (month >= 6 && month <= 8) { return Season.Summer; }
            if (month >= 9 && month <= 11) { return Season.Autumn; }
            return Season.Winter;
        }

        /// <summary>昼 (6:00〜18:00) かどうか。</summary>
        public static bool IsDaytime(int hour)
        {
            return hour >= 6 && hour < 18;
        }

        /// <summary>季節のファイル名サフィックス ("spring" 等)。</summary>
        public static string SeasonSuffix(Season season)
        {
            switch (season)
            {
                case Season.Spring: return "spring";
                case Season.Summer: return "summer";
                case Season.Autumn: return "autumn";
                default: return "winter";
            }
        }

        /// <summary>
        /// セリフの時間帯サフィックス。朝 (5:00〜11:00)="morning" / 夕方 (17:00〜22:00)="evening" /
        /// 夜 (22:00〜翌5:00)="night"。それ以外の昼間は null (通常セリフのみ)。
        /// BGM・背景の昼夜 (6:00〜18:00) とは別の区分 (挨拶の自然さを優先)。
        /// </summary>
        public static string GetTimeOfDaySuffix(int hour)
        {
            if (hour >= 5 && hour < 11) { return "morning"; }
            if (hour >= 17 && hour < 22) { return "evening"; }
            if (hour >= 22 || hour < 5) { return "night"; }
            return null;
        }

        /// <summary>レイヤーにファイル指定があるか。</summary>
        static bool HasFile(SkinLayer layer)
        {
            return layer != null && !string.IsNullOrEmpty(layer.File);
        }

        // ---- BGM の解決 ----

        /// <summary>現在時刻に合った BGM レイヤー (null = アプリのデフォルト BGM)。</summary>
        public static SkinLayer GetBgmForNow(SkinDef skin)
        {
            return GetBgmFor(skin, System.DateTime.Now);
        }

        /// <summary>
        /// 指定時刻の BGM レイヤー。季節×昼夜 (bgm_spring_day 等) → 昼夜 (bgm_day / bgm_night) →
        /// 従来の bgm の順で選ぶ (null = アプリのデフォルト BGM)。検証メニューから時刻を注入できる。
        /// </summary>
        public static SkinLayer GetBgmFor(SkinDef skin, System.DateTime now)
        {
            bool day = IsDaytime(now.Hour);
            var seasonal = GetSeasonalBgm(skin, GetSeason(now.Month), day);
            if (HasFile(seasonal))
            {
                return seasonal;
            }
            var timed = day ? skin.BgmDay : skin.BgmNight;
            if (HasFile(timed))
            {
                return timed;
            }
            return skin.Bgm;
        }

        static SkinLayer GetSeasonalBgm(SkinDef skin, Season season, bool day)
        {
            switch (season)
            {
                case Season.Spring: return day ? skin.BgmSpringDay : skin.BgmSpringNight;
                case Season.Summer: return day ? skin.BgmSummerDay : skin.BgmSummerNight;
                case Season.Autumn: return day ? skin.BgmAutumnDay : skin.BgmAutumnNight;
                default: return day ? skin.BgmWinterDay : skin.BgmWinterNight;
            }
        }

        // ---- 背景の解決 ----

        /// <summary>
        /// 指定時刻の時間帯背景 (季節×昼夜 → 昼夜の順、無ければ null)。
        /// あれば背景リストの先頭 (タップ巡回の起点) として扱われる。
        /// </summary>
        public static SkinLayer GetTimedBackground(SkinDef skin, System.DateTime now)
        {
            bool day = IsDaytime(now.Hour);
            var seasonal = GetSeasonalBackground(skin, GetSeason(now.Month), day);
            if (HasFile(seasonal))
            {
                return seasonal;
            }
            var timed = day ? skin.BackgroundDay : skin.BackgroundNight;
            return HasFile(timed) ? timed : null;
        }

        static SkinLayer GetSeasonalBackground(SkinDef skin, Season season, bool day)
        {
            switch (season)
            {
                case Season.Spring: return day ? skin.BackgroundSpringDay : skin.BackgroundSpringNight;
                case Season.Summer: return day ? skin.BackgroundSummerDay : skin.BackgroundSummerNight;
                case Season.Autumn: return day ? skin.BackgroundAutumnDay : skin.BackgroundAutumnNight;
                default: return day ? skin.BackgroundWinterDay : skin.BackgroundWinterNight;
            }
        }

        /// <summary>
        /// 指定時刻の背景リスト。時間帯背景があれば先頭に置き、続けて従来の
        /// background / backgrounds を並べる (タップ巡回で全部を回れる)。
        /// </summary>
        public static List<SkinLayer> GetBackgroundsFor(SkinDef skin, System.DateTime now)
        {
            var list = GetBackgrounds(skin);
            var timed = GetTimedBackground(skin, now);
            if (timed != null)
            {
                list.Insert(0, timed);
            }
            return list;
        }

        // ---- セリフの解決 ----

        /// <summary>
        /// 指定時刻のスキン全体セリフ (時間帯セリフ + 通常 talk の合算。1件も無ければ null)。
        /// </summary>
        public static List<string> GetTalkFor(SkinDef skin, System.DateTime now)
        {
            var list = new List<string>();
            var timed = GetTimeOfDayTalk(skin, now.Hour);
            if (timed != null)
            {
                list.AddRange(timed);
            }
            if (skin.Talk != null)
            {
                list.AddRange(skin.Talk);
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>指定時刻の時間帯セリフ (無ければ null)。</summary>
        public static List<string> GetTimeOfDayTalk(SkinDef skin, int hour)
        {
            switch (GetTimeOfDaySuffix(hour))
            {
                case "morning": return skin.TalkMorning;
                case "evening": return skin.TalkEvening;
                case "night": return skin.TalkNight;
                default: return null;
            }
        }

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
        /// 編集モーダルが扱わない拡張フィールド (backgrounds / characters / 昼夜・季節 BGM 等) は
        /// skin.json にそのまま維持される。
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

                // skin を直接更新して丸ごと書き戻す (SaveLayoutItem と同じ方式)。
                // 新規 SkinDef を組み立てると、編集モーダルが扱わない拡張フィールド
                // (backgrounds / characters / 昼夜・季節 BGM 等) が skin.json から消えてしまう
                skin.Name = string.IsNullOrEmpty(name) ? skin.Name : name;
                skin.Background = background;
                skin.Character = character;
                skin.Bgm = bgm;
                skin.Record = record;
                skin.Talk = (talkLines != null && talkLines.Count > 0) ? talkLines : null;
                skin.Theme = string.IsNullOrEmpty(themePrimary) ? null : new SkinTheme { Primary = themePrimary };
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
