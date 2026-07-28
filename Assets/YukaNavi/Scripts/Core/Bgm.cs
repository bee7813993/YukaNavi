using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace YukaNavi.Core
{
    /// <summary>
    /// アプリ音のミュート管理と BGM 再生 (AppRoot が Init する)。Se も Muted を見て操作音を止める。
    /// カラオケ用アプリなので既定はミュート。設定は端末ローカル (PlayerPrefs) に保存する。
    /// スキンに BGM (bgm.mp3 等) が登録されていればデフォルト BGM の代わりにそれを流す。
    /// スキンに BGM が無い時間帯も、組み込みの季節×昼夜曲があれば自動で切り替わる。
    /// </summary>
    public static class Bgm
    {
        const string MutedKey = "bgm.muted";
        // 組み込み BGM の識別キー (スキン BGM はファイルの絶対パスがそのままキーになる)
        const string BuiltinBaseKey = "builtin:base";

        static AudioSource _source;
        static AudioClip _defaultClip;
        static string _loadedKey;  // 再生中 BGM の識別キー (null = 未再生)
        static int _loadSerial;    // 連続切替時に古いロード結果を捨てるための世代番号
        static bool _suppressed;   // 動画プレビュー等の間だけの一時ミュート (設定は変えない)

        public static void Init(AudioSource source, AudioClip defaultClip)
        {
            _source = source;
            _defaultClip = defaultClip;
            _source.loop = true;
            PlayBuiltin(BuiltinBaseKey, _defaultClip);
            Apply();
            RefreshForCurrentSkin();
        }

        /// <summary>ミュート状態 (既定 true)。変更は即反映され保存される。</summary>
        public static bool Muted
        {
            get { return PlayerPrefs.GetInt(MutedKey, 1) == 1; }
            set
            {
                PlayerPrefs.SetInt(MutedKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        const string VolumeKey = "bgm.volume";

        /// <summary>BGM の音量 (0〜1、既定 0.35)。変更は即反映され保存される。</summary>
        public static float Volume
        {
            get { return PlayerPrefs.GetFloat(VolumeKey, 0.35f); }
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>
        /// 動画プレビュー等の再生中だけ BGM を止める (Muted の設定値は変更しない)。
        /// </summary>
        public static void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            Apply();
        }

        static void Apply()
        {
            if (_source != null)
            {
                _source.mute = Muted || _suppressed;
                _source.volume = Volume;
            }
        }

        /// <summary>組み込み BGM を再生し、キーを記録する。</summary>
        static void PlayBuiltin(string key, AudioClip clip)
        {
            SetClip(key, clip);
            if (clip != null)
            {
                _source.Play(); // ミュート中も再生しておき、解除した瞬間から流れるようにする
            }
        }

        /// <summary>
        /// 再生クリップを差し替える。ファイルからロードした古いクリップ (スキン・拡張パック) は
        /// アセットではないため破棄する ("builtin:" キーの Resources クリップは破棄しない)。
        /// </summary>
        static void SetClip(string key, AudioClip clip)
        {
            var old = _source.clip;
            bool oldIsFileLoaded = _loadedKey != null && !_loadedKey.StartsWith("builtin:");
            _loadedKey = key;
            _source.clip = clip;
            if (oldIsFileLoaded && old != null && old != clip)
            {
                Object.Destroy(old);
            }
        }

        /// <summary>
        /// 現在のスキンの BGM を反映する。スキンの適用・作成・編集・取り込み・削除の後に呼ぶ。
        /// 昼夜・季節 BGM があれば時間帯で選ぶ。時間帯またぎの自動切替は
        /// ホーム画面の分針更新がこのメソッドを呼ぶことで行われる (キー不変なら何もしない)。
        /// force=true でキーが同じでも読み直す (同名ファイルの中身が変わる編集・更新後に使う)。
        /// スキンに BGM が無ければ組み込み BGM (季節×昼夜 → 基本ループ曲) に戻す。
        /// </summary>
        public static async void RefreshForCurrentSkin(bool force = false)
        {
            if (_source == null)
            {
                return;
            }
            if (force)
            {
                _loadedKey = null;
            }
            var skin = SkinManager.Current();
            var now = System.DateTime.Now;
            string path = null;
            var bgm = SkinManager.GetBgmForNow(skin);
            if (skin.Folder != null && bgm != null && !string.IsNullOrEmpty(bgm.File))
            {
                path = SkinManager.GetFilePath(skin, bgm.File);
            }
            if (path == null)
            {
                // スキン BGM なし → 組み込み (季節×昼夜があればそれ、無ければ基本曲)
                await PlayBuiltinForNowAsync(now);
                return;
            }
            if (path == _loadedKey)
            {
                return; // 変化なし
            }
            int serial = ++_loadSerial;
            var clip = await LoadClipAsync(path);
            if (serial != _loadSerial)
            {
                return; // ロード中に別のスキンへ切り替わった
            }
            if (clip == null)
            {
                // 読めなければ組み込みへ (キーはスキンのパスにならないため毎分リトライされる)
                await PlayBuiltinForNowAsync(now);
                return;
            }
            SetClip(path, clip);
            _source.Play();
        }

        /// <summary>
        /// 現在時刻に合った組み込み BGM を再生する。季節×昼夜の曲を
        /// デフォルトテーマ拡張パック (default_theme/bgm/) → Resources の順で探し、
        /// 無ければ基本ループ曲。素材を配信 (または Resources に配置) するだけで
        /// コード変更なしに有効になる。
        /// </summary>
        static async Task PlayBuiltinForNowAsync(System.DateTime now)
        {
            string suffix = SkinManager.SeasonSuffix(SkinManager.GetSeason(now.Month))
                + "_" + (SkinManager.IsDaytime(now.Hour) ? "day" : "night");

            // 拡張パック (アプリ更新なしで配信された音源) を最優先。キーはファイルパスそのもの
            string packPath = DefaultThemePack.FindFile("bgm/yukanavi_home_loop_" + suffix,
                ".ogg", ".mp3", ".wav");
            if (packPath != null)
            {
                if (packPath == _loadedKey)
                {
                    return;
                }
                int serial = ++_loadSerial;
                var packClip = await LoadClipAsync(packPath);
                if (serial != _loadSerial)
                {
                    return;
                }
                if (packClip != null)
                {
                    PlayBuiltin(packPath, packClip);
                    return;
                }
                // 読めなければ Resources / 基本曲へ
            }

            string key = "builtin:" + suffix;
            if (key == _loadedKey)
            {
                return;
            }
            var clip = Resources.Load<AudioClip>("Audio/BGM/yukanavi_home_loop_" + suffix);
            if (clip != null)
            {
                PlayBuiltin(key, clip);
                return;
            }
            if (_loadedKey == BuiltinBaseKey)
            {
                return;
            }
            PlayBuiltin(BuiltinBaseKey, _defaultClip);
        }

        /// <summary>音声ファイルを AudioClip としてロードする (失敗時 null)。Se からも使う。</summary>
        public static async Task<AudioClip> LoadClipAsync(string path)
        {
            try
            {
                string url = new System.Uri(path).AbsoluteUri;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(url, GuessAudioType(path)))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("[YukaNavi] スキン音声の読み込みに失敗: " + req.error);
                        return null;
                    }
                    return DownloadHandlerAudioClip.GetContent(req);
                }
            }
            catch
            {
                return null;
            }
        }

        static AudioType GuessAudioType(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                    return AudioType.WAV;
                default:
                    return AudioType.MPEG; // mp3 ほか (m4a/aac は非対応、読めなければデフォルトに戻る)
            }
        }
    }
}
