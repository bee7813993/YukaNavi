using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>押している間だけ少し縮む、ボタンの押下フィードバック。</summary>
    public class PressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            transform.localScale = new Vector3(0.96f, 0.96f, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            transform.localScale = Vector3.one;
        }
    }

    /// <summary>コードから UGUI 部品を組み立てる共通ヘルパー。</summary>
    public static class UiFactory
    {
        // テーマ色 (既定はゆかりちゃんの紫系)。スキンのテーマ色で差し替えられる。
        // UI は生成時に色を焼き込むため、変更後は画面の再構築 (ScreenManager.RebuildAll) が必要。
        public static Color Primary { get; private set; } = new Color(0.48f, 0.36f, 0.84f);
        public static Color PrimaryDark { get; private set; } = new Color(0.30f, 0.24f, 0.45f);
        public static Color PrimaryPale { get; private set; } = new Color(0.91f, 0.87f, 0.99f);
        public static Color PanelBg { get; private set; } = new Color(0.93f, 0.91f, 0.97f);
        /// <summary>ホームが透ける画面背景 (メニューと同系の半透明)。</summary>
        public static Color ScreenOverlayBg { get; private set; } = new Color(0.95f, 0.93f, 0.99f, 0.6f);
        public static readonly Color CardBg = Color.white;
        public static readonly Color TextDark = new Color(0.16f, 0.13f, 0.25f);
        public static readonly Color TextMuted = new Color(0.45f, 0.42f, 0.55f);
        public static readonly Color Danger = new Color(0.80f, 0.25f, 0.30f);

        /// <summary>テーマ色を既定 (紫) に戻す。</summary>
        public static void ResetThemeColors()
        {
            Primary = new Color(0.48f, 0.36f, 0.84f);
            PrimaryDark = new Color(0.30f, 0.24f, 0.45f);
            PrimaryPale = new Color(0.91f, 0.87f, 0.99f);
            PanelBg = new Color(0.93f, 0.91f, 0.97f);
            ScreenOverlayBg = new Color(0.95f, 0.93f, 0.99f, 0.6f);
        }

        /// <summary>基準色1色からテーマ一式 (濃色・淡色・背景) を導出して適用する。</summary>
        public static void SetThemeColors(Color primary)
        {
            Primary = primary;
            Color.RGBToHSV(primary, out float h, out float s, out float v);
            PrimaryDark = Color.HSVToRGB(h, Mathf.Min(1f, s * 0.95f), v * 0.55f);
            PrimaryPale = Color.HSVToRGB(h, s * 0.22f, 0.99f);
            PanelBg = Color.HSVToRGB(h, s * 0.12f, 0.96f);
            var overlay = Color.HSVToRGB(h, s * 0.08f, 0.975f);
            ScreenOverlayBg = new Color(overlay.r, overlay.g, overlay.b, 0.6f);
        }

        static Font _font;
        static Sprite _roundedSprite;
        static Sprite _gradientSprite;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // 同梱の丸ゴシック (M PLUS Rounded 1c) を優先し、無ければ組み込みフォント
                    _font = Resources.Load<Font>("Fonts/MPLUSRounded1c-Regular");
                    if (_font == null)
                    {
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                }
                return _font;
            }
        }

        /// <summary>角丸矩形の 9-slice スプライト (実行時生成、白。色は Image.color で乗せる)。</summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite == null)
                {
                    _roundedSprite = BuildRoundedSprite(64, 20);
                }
                return _roundedSprite;
            }
        }

        /// <summary>上が明るく下が濃い縦グラデーション (ヘッダーバー用)。</summary>
        public static Sprite GradientSprite
        {
            get
            {
                if (_gradientSprite == null)
                {
                    var tex = new Texture2D(2, 16, TextureFormat.RGBA32, false);
                    var pixels = new Color32[2 * 16];
                    for (int y = 0; y < 16; y++)
                    {
                        // 下 (y=0) を 78%、上 (y=15) を 112% の明るさに
                        byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(198f + (y / 15f) * 88f), 0, 255);
                        pixels[y * 2] = new Color32(v, v, v, 255);
                        pixels[y * 2 + 1] = new Color32(v, v, v, 255);
                    }
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _gradientSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 16f), new Vector2(0.5f, 0.5f), 100f);
                }
                return _gradientSprite;
            }
        }

        static Sprite BuildRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 最寄りの角円の中心からの距離でアルファを決める (縁はアンチエイリアス)
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, r, size - r);
                    float cy = Mathf.Clamp(py, r, size - r);
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            float border = radius + 4f;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>Image を角丸にする (カード・行アイテム用)。</summary>
        public static void Roundify(Image image)
        {
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
        }

        /// <summary>
        /// セグメント切替タブ。渡されたパネルを白角丸コンテナにし、中にタブボタンを並べる。
        /// 選択状態は SetSegmentSelected で切り替える。
        /// </summary>
        public static Button[] CreateSegmentTabs(RectTransform container, string[] labels, int fontSize = 28)
        {
            var img = container.gameObject.GetComponent<Image>();
            if (img == null)
            {
                img = container.gameObject.AddComponent<Image>();
            }
            img.color = Color.white;
            Roundify(img);
            AddShadow(container.gameObject, 3f);
            var layout = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 6f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            var buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var go = new GameObject(labels[i]);
                go.transform.SetParent(container, false);
                var tabImg = go.AddComponent<Image>();
                Roundify(tabImg);
                var button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None; // 色は SetSegmentSelected で制御
                go.AddComponent<PressEffect>();
                var text = CreateText(go.transform, "Label", labels[i], fontSize, Primary);
                StretchFull(text.rectTransform);
                buttons[i] = button;
            }
            return buttons;
        }

        /// <summary>セグメントタブの選択状態を反映する。</summary>
        public static void SetSegmentSelected(Button[] tabs, int selectedIndex)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                bool selected = i == selectedIndex;
                tabs[i].image.color = selected ? Primary : new Color(1f, 1f, 1f, 0f);
                tabs[i].GetComponentInChildren<Text>().color = selected ? Color.white : Primary;
            }
        }

        /// <summary>
        /// アウトラインボタン (白背景 + Primary 文字 + 細枠)。
        /// キャンセルや副操作に使い、主アクション (塗りボタン) と区別する。
        /// </summary>
        public static Button CreateOutlineButton(Transform parent, string name, string label, int fontSize = 36)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var frame = go.AddComponent<Image>();
            frame.color = new Color(0.72f, 0.64f, 0.90f);
            Roundify(frame);
            AddShadow(go, 3f);

            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(go.transform, false);
            var inner = innerGo.AddComponent<Image>();
            inner.color = Color.white;
            Roundify(inner);
            inner.raycastTarget = false;
            var innerRect = inner.rectTransform;
            StretchFull(innerRect);
            innerRect.offsetMin = new Vector2(3f, 3f);
            innerRect.offsetMax = new Vector2(-3f, -3f);

            var button = go.AddComponent<Button>();
            go.AddComponent<PressEffect>();
            var text = CreateText(go.transform, "Label", label, fontSize, Primary);
            StretchFull(text.rectTransform);
            return button;
        }

        /// <summary>
        /// インデックス系リストの行 (タイトル + サブテキスト + › 印)。
        /// 期別リストやシリーズの作品一覧で使う。onTap は効果音込みで渡す。
        /// </summary>
        public static GameObject CreateIndexRow(Transform listContent, string title, string sub,
                                                System.Action onTap)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(listContent, false);
            var img = rowGo.AddComponent<Image>();
            img.color = CardBg;
            Roundify(img);
            AddShadow(rowGo, 3f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 124f;
            var button = rowGo.AddComponent<Button>();
            rowGo.AddComponent<PressEffect>();
            button.onClick.AddListener(() => onTap());

            var titleText = CreateText(rowGo.transform, "Title", title, 30, TextDark, TextAnchor.UpperLeft);
            StretchFull(titleText.rectTransform);
            titleText.rectTransform.offsetMin = new Vector2(24f, 48f);
            titleText.rectTransform.offsetMax = new Vector2(-70f, -10f);
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

            var subText = CreateText(rowGo.transform, "Sub", sub, 22, TextMuted, TextAnchor.LowerLeft);
            StretchFull(subText.rectTransform);
            subText.rectTransform.offsetMin = new Vector2(24f, 12f);
            subText.rectTransform.offsetMax = new Vector2(-70f, -78f);
            subText.verticalOverflow = VerticalWrapMode.Truncate;

            var arrow = CreateText(rowGo.transform, "Arrow", "›", 44, PrimaryPale);
            var arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-20f, 0f);
            arrowRect.sizeDelta = new Vector2(44f, 60f);

            return rowGo;
        }

        /// <summary>角丸の小さなバッジ (状態表示用)。</summary>
        public static Text CreateBadge(Transform parent, string name, string label, Color bg, Color fg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            Roundify(img);
            img.raycastTarget = false;
            var text = CreateText(go.transform, "Label", label, 22, fg);
            StretchFull(text.rectTransform);
            return text;
        }

        /// <summary>ソフトシャドウを付ける。</summary>
        public static void AddShadow(GameObject go, float distance = 4f)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.22f, 0.12f, 0.38f, 0.25f);
            shadow.effectDistance = new Vector2(0f, -distance);
        }

        /// <summary>画面上部の共通ヘッダーバー (グラデーション + 影 + タイトル)。</summary>
        public static RectTransform CreateTopBar(Transform parent, string title)
        {
            var bar = CreatePanel(parent, "TopBar", Primary);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, 110f);
            var img = bar.GetComponent<Image>();
            img.sprite = GradientSprite;
            img.type = Image.Type.Simple;
            AddShadow(bar.gameObject, 5f);
            var text = CreateText(bar, "Title", title, 42, Color.white);
            StretchFull(text.rectTransform);
            return bar;
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

        /// <summary>9-slice 用の Sprite を生成する。border は (左, 下, 右, 上)。</summary>
        public static Sprite LoadSprite9Slice(string path, Vector4 border)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogError("[YukaNavi] テクスチャが見つかりません: Resources/" + path);
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
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
            if (bg.a > 0.05f)
            {
                // 透明ボタン (ナビバー等) 以外は角丸 + 影で立体感を出す
                Roundify(img);
                AddShadow(go);
            }
            var button = go.AddComponent<Button>();
            go.AddComponent<PressEffect>();
            var text = CreateText(go.transform, "Label", label, fontSize, fg);
            StretchFull(text.rectTransform);
            return button;
        }

        /// <summary>
        /// 縦スクロールのリストを作る。位置は戻り値の scroll (ScrollRect の RectTransform) で調整し、
        /// 行は content に追加する (VerticalLayoutGroup + ContentSizeFitter 設定済み)。
        /// </summary>
        public static RectTransform CreateScrollList(Transform parent, string name, out RectTransform content)
        {
            var scrollGo = new GameObject(name);
            scrollGo.transform.SetParent(parent, false);
            var scrollRectT = scrollGo.AddComponent<RectTransform>();
            // 外枠は付けず、カードを背景に直接並べる (Image はマスクとタッチ判定用)
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = new Color(1f, 1f, 1f, 0f);
            var scrollRect = scrollGo.AddComponent<ScrollRect>();

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            StretchFull(viewportRect);
            viewportGo.AddComponent<Image>().color = Color.white;
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // 既定の sizeDelta (100,100) が残ると左右に 50px ずつはみ出して文字が切れる
            content.sizeDelta = Vector2.zero;
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 14f;
            layout.padding = new RectOffset(4, 4, 8, 8);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = content;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            return scrollRectT;
        }

        /// <summary>1行入力欄 (legacy InputField)。</summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholder, int fontSize = 34)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            Roundify(img);
            AddShadow(go, 3f);
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
