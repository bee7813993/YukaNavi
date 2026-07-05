using UnityEngine;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>コードから UGUI 部品を組み立てる共通ヘルパー。</summary>
    public static class UiFactory
    {
        // テーマ色 (ゆかりちゃんの紫系)
        public static readonly Color Primary = new Color(0.48f, 0.36f, 0.84f);
        public static readonly Color PrimaryDark = new Color(0.30f, 0.24f, 0.45f);
        public static readonly Color PanelBg = new Color(0.96f, 0.94f, 1.00f);
        public static readonly Color CardBg = new Color(1f, 1f, 1f, 0.92f);
        public static readonly Color TextDark = new Color(0.16f, 0.13f, 0.25f);
        public static readonly Color Danger = new Color(0.80f, 0.25f, 0.30f);

        static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        /// <summary>インポート設定 (spriteMode) に依存しないよう Texture2D から Sprite を生成する。</summary>
        public static Sprite LoadSprite(string path)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogError("[YukaNavi] テクスチャが見つかりません: Resources/" + path);
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>RectTransform を親いっぱいに広げる。</summary>
        public static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color? bg = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            if (bg.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = bg.Value;
            }
            return rect;
        }

        public static Image CreateImage(Transform parent, string name, string texturePath)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (texturePath != null)
            {
                img.sprite = LoadSprite(texturePath);
            }
            return img;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize,
                                      Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // 装飾用テキストが他のボタンへのタップを横取りしないようにする
            // (ボタンや入力欄のタップ判定は各自の Image が受ける)
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label,
                                          Color bg, Color fg, int fontSize = 36)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var button = go.AddComponent<Button>();
            var text = CreateText(go.transform, "Label", label, fontSize, fg);
            StretchFull(text.rectTransform);
            return button;
        }

        /// <summary>1行入力欄 (legacy InputField)。</summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholder, int fontSize = 34)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var input = go.AddComponent<InputField>();

            var text = CreateText(go.transform, "Text", "", fontSize, TextDark, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(16f, 4f);
            text.rectTransform.offsetMax = new Vector2(-16f, -4f);

            var ph = CreateText(go.transform, "Placeholder", placeholder, fontSize,
                                new Color(0.55f, 0.53f, 0.62f), TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            StretchFull(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(16f, 4f);
            ph.rectTransform.offsetMax = new Vector2(-16f, -4f);

            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }
    }
}
