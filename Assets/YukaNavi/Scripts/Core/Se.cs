using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// 効果音の再生 (AppRoot が Init する)。
    /// スキンに効果音 (se_tap 等) が登録されていればアプリ標準の音の代わりに鳴らす。
    /// </summary>
    public static class Se
    {
        public const string Tap = "yukanavi_tap";
        public const string Confirm = "yukanavi_confirm";
        public const string Error = "yukanavi_error";
        public const string Transition = "yukanavi_transition";
        public const string ReservationComplete = "yukanavi_reservation_complete";

        static AudioSource _source;
        // スキンの効果音 (キー = 上の定数)。再生時の遅延をなくすためスキン適用時に事前ロードする
        static readonly Dictionary<string, AudioClip> _skinClips = new Dictionary<string, AudioClip>();
        static int _loadSerial; // 連続切替時に古いロード結果を捨てるための世代番号

        public static void Init(AudioSource source)
        {
            _source = source;
            _source.volume = Volume;
            RefreshForCurrentSkin();
        }

        const string VolumeKey = "se.volume";

        /// <summary>効果音の音量 (0〜1、既定 1)。変更は即反映され保存される。</summary>
        public static float Volume
        {
            get { return PlayerPrefs.GetFloat(VolumeKey, 1f); }
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
                if (_source != null)
                {
                    _source.volume = Volume;
                }
            }
        }

        public static void Play(string name)
        {
            if (_source == null || Bgm.Muted)
            {
                return; // アプリ全体ミュート中は操作音も鳴らさない
            }
            AudioClip clip;
            if (!_skinClips.TryGetValue(name, out clip) || clip == null)
            {
                clip = Resources.Load<AudioClip>("Audio/SE/" + name);
            }
            if (clip != null)
            {
                _source.PlayOneShot(clip);
            }
        }

        /// <summary>
        /// 現在のスキンの効果音を事前ロードする。スキンの適用・作成・編集・取り込み・削除の
        /// 後に呼ぶ (Bgm.RefreshForCurrentSkin と同じタイミング)。
        /// スキンに無い音はアプリ標準 (Resources) のまま。
        /// </summary>
        public static async void RefreshForCurrentSkin()
        {
            int serial = ++_loadSerial;
            var skin = SkinManager.Current();
            var loaded = new Dictionary<string, AudioClip>();
            if (skin.Folder != null)
            {
                await LoadInto(loaded, Tap, skin, skin.SeTap);
                await LoadInto(loaded, Confirm, skin, skin.SeConfirm);
                await LoadInto(loaded, Error, skin, skin.SeError);
                await LoadInto(loaded, Transition, skin, skin.SeTransition);
                await LoadInto(loaded, ReservationComplete, skin, skin.SeComplete);
            }
            if (serial != _loadSerial)
            {
                // ロード中に別のスキンへ切り替わった。読んだ分は破棄する
                foreach (var pair in loaded)
                {
                    if (pair.Value != null)
                    {
                        Object.Destroy(pair.Value);
                    }
                }
                return;
            }
            // 古いスキン SE を破棄して入れ替え (ファイルからロードしたクリップはアセットではない)
            foreach (var pair in _skinClips)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value);
                }
            }
            _skinClips.Clear();
            foreach (var pair in loaded)
            {
                _skinClips[pair.Key] = pair.Value;
            }
        }

        static async Task LoadInto(Dictionary<string, AudioClip> dest, string name,
                                   SkinDef skin, SkinLayer layer)
        {
            if (layer == null || string.IsNullOrEmpty(layer.File))
            {
                return;
            }
            string path = SkinManager.GetFilePath(skin, layer.File);
            if (path == null)
            {
                return;
            }
            var clip = await Bgm.LoadClipAsync(path);
            if (clip != null)
            {
                dest[name] = clip;
            }
        }
    }
}
