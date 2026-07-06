using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>
    /// UGUI 上の疑似パーティクル。音符ときらめきが下からゆっくり立ち上ってフェードする。
    /// (Screen Space Overlay の Canvas では ParticleSystem が描画できないため Image で自作)
    /// </summary>
    public class NoteParticles : MonoBehaviour
    {
        static readonly string[] TexturePaths =
        {
            "Art/Particles/yukanavi_particle_note_single_256",
            "Art/Particles/yukanavi_particle_note_double_256",
            "Art/Particles/yukanavi_particle_sparkle_256",
        };

        const int PoolSize = 12;

        class Note
        {
            public RectTransform Rect;
            public Image Image;
            public float BornAt;
            public float Life;
            public float Speed;
            public float SwayFreq;
            public float SwayPhase;
            public float BaseX;
            public float StartY;
        }

        readonly List<Note> _notes = new List<Note>(PoolSize);
        Sprite[] _sprites;
        RectTransform _rect;

        public static NoteParticles Create(Transform parent)
        {
            var go = new GameObject("NoteParticles");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            var particles = go.AddComponent<NoteParticles>();
            particles.Build(rect);
            return particles;
        }

        void Build(RectTransform rect)
        {
            _rect = rect;
            _sprites = new Sprite[TexturePaths.Length];
            for (int i = 0; i < TexturePaths.Length; i++)
            {
                _sprites[i] = UiFactory.LoadSprite(TexturePaths[i]);
            }
            for (int i = 0; i < PoolSize; i++)
            {
                var noteGo = new GameObject("Note" + i);
                noteGo.transform.SetParent(transform, false);
                var img = noteGo.AddComponent<Image>();
                img.raycastTarget = false;
                var note = new Note { Rect = img.rectTransform, Image = img };
                note.Rect.anchorMin = note.Rect.anchorMax = new Vector2(0.5f, 0f);
                Respawn(note, initial: true);
                _notes.Add(note);
            }
        }

        void Respawn(Note note, bool initial)
        {
            note.Image.sprite = _sprites[Random.Range(0, _sprites.Length)];
            float size = Random.Range(48f, 110f);
            note.Rect.sizeDelta = new Vector2(size, size);
            note.Life = Random.Range(6f, 11f);
            // 初期配置だけは進行途中から始めて、出だしに一斉に湧かないようにする
            note.BornAt = Time.time - (initial ? Random.Range(0f, note.Life) : 0f);
            note.Speed = Random.Range(90f, 170f);
            note.SwayFreq = Random.Range(0.6f, 1.4f);
            note.SwayPhase = Random.Range(0f, Mathf.PI * 2f);
            note.BaseX = Random.Range(-500f, 500f);
            note.StartY = Random.Range(-80f, 120f);
        }

        void Update()
        {
            // テーマの淡い紫 (白単色テクスチャに乗せる)
            var baseColor = new Color(0.62f, 0.52f, 0.88f);
            foreach (var note in _notes)
            {
                float age = Time.time - note.BornAt;
                float t = age / note.Life;
                if (t >= 1f)
                {
                    Respawn(note, initial: false);
                    age = 0f;
                    t = 0f;
                }
                float x = note.BaseX + Mathf.Sin(age * note.SwayFreq + note.SwayPhase) * 46f;
                float y = note.StartY + note.Speed * age;
                note.Rect.anchoredPosition = new Vector2(x, y);
                // ふわっと現れてゆっくり消える
                float alpha = Mathf.Min(t / 0.15f, (1f - t) / 0.35f);
                alpha = Mathf.Clamp01(alpha) * 0.55f;
                note.Image.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            }
        }
    }
}
