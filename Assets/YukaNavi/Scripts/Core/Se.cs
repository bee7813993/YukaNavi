using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>効果音の再生 (AppRoot が Init する)。</summary>
    public static class Se
    {
        public const string Tap = "yukanavi_tap";
        public const string Confirm = "yukanavi_confirm";
        public const string Error = "yukanavi_error";
        public const string Transition = "yukanavi_transition";
        public const string ReservationComplete = "yukanavi_reservation_complete";

        static AudioSource _source;

        public static void Init(AudioSource source)
        {
            _source = source;
            _source.volume = Volume;
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
            var clip = Resources.Load<AudioClip>("Audio/SE/" + name);
            if (clip != null)
            {
                _source.PlayOneShot(clip);
            }
        }
    }
}
