using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace YukaNavi.Core
{
    /// <summary>
    /// アプリ音のミュート管理と BGM 再生 (AppRoot が Init する)。Se も Muted を見て操作音を止める。
    /// カラオケ用アプリなので既定はミュート。設定は端末ローカル (PlayerPrefs) に保存する。
    /// スキンに BGM (bgm.mp3 等) が登録されていればデフォルト BGM の代わりにそれを流す。
    /// </summary>
    public static class Bgm
    {
        const string MutedKey = "bgm.muted";

        static AudioSource _source;
        static AudioClip _defaultClip;
        static string _loadedPath; // 再生中のスキン BGM のパス (null = デフォルト BGM)
        static int _loadSerial;    // 連続切替時に古いロード結果を捨てるための世代番号

        public static void Init(AudioSource source, AudioClip defaultClip)
        {
            _source = source;
            _defaultClip = defaultClip;
            _source.loop = true;
            PlayDefault();
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

        static void Apply()
        {
            if (_source != null)
            {
                _source.mute = Muted;
            }
        }

        static void PlayDefault()
        {
            _loadedPath = null;
            _source.clip = _defaultClip;
            if (_defaultClip != null)
            {
                _source.Play(); // ミュート中も再生しておき、解除した瞬間から流れるようにする
            }
        }

        /// <summary>
        /// 現在のスキンの BGM を反映する。スキンの適用・作成・編集・取り込み・削除の後に呼ぶ。
        /// スキンに BGM が無ければデフォルト BGM に戻す。
        /// </summary>
        public static async void RefreshForCurrentSkin()
        {
            if (_source == null)
            {
                return;
            }
            var skin = SkinManager.Current();
            string path = null;
            if (skin.Folder != null && skin.Bgm != null && !string.IsNullOrEmpty(skin.Bgm.File))
            {
                path = SkinManager.GetFilePath(skin, skin.Bgm.File);
            }
            if (path == _loadedPath)
            {
                return; // 変化なし
            }
            int serial = ++_loadSerial;
            if (path == null)
            {
                PlayDefault();
                return;
            }
            var clip = await LoadClipAsync(path);
            if (serial != _loadSerial)
            {
                return; // ロード中に別のスキンへ切り替わった
            }
            if (clip == null)
            {
                PlayDefault();
                return;
            }
            _loadedPath = path;
            _source.clip = clip;
            _source.Play();
        }

        static async Task<AudioClip> LoadClipAsync(string path)
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
                        Debug.LogWarning("[YukaNavi] スキン BGM の読み込みに失敗: " + req.error);
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
