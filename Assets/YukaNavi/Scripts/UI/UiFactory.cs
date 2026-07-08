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

        static Sprite _softGlowSprite;

        /// <summary>
        /// 外側に向かってふわっと透明にぼける角丸矩形の 9-slice スプライト
        /// (実行時生成、白。色は Image.color で乗せる)。枠のグロー表現用。
        /// </summary>
        public static Sprite SoftGlowSprite
        {
            get
            {
                if (_softGlowSprite == null)
                {
                    const int size = 96;
                    const float blur = 20f;   // 外側のぼかし幅
                    const float radius = 12f; // 中心矩形の角丸
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            // 中心の角丸矩形からの距離で alpha を落とす (内側は不透明)
                            float cx = Mathf.Clamp(x + 0.5f, blur + radius, size - blur - radius);
                            float cy = Mathf.Clamp(y + 0.5f, blur + radius, size - blur - radius);
                            float dx = x + 0.5f - cx;
                            float dy = y + 0.5f - cy;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                            float a = Mathf.Clamp01(1f - dist / blur);
                            a *= a; // 外側ほど早く薄く
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    }
                    tex.Apply();
                    tex.wrapMode = TextureWrapMode.Clamp;
                    _softGlowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size),
                        new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                        new Vector4(40f, 40f, 40f, 40f));
                }
                return _softGlowSprite;
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

        static Sprite _circleSprite;
        /// <summary>アンチエイリアス付きの白い円 (丸ボタン等)。色は Image.color で付ける。</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite == null)
                {
                    _circleSprite = CreateCircleSprite(128, 62f, 0f);
                }
                return _circleSprite;
            }
        }

        static Sprite _ringSprite;
        /// <summary>白い円環 (プログレスリング用)。Image.Type.Filled + Radial360 で弧にできる。</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (_ringSprite == null)
                {
                    _ringSprite = CreateCircleSprite(256, 124f, 106f);
                }
                return _ringSprite;
            }
        }

        static Sprite _circleGlossSprite;
        /// <summary>円形の上部ツヤ (白、上ほど濃い)。丸ボタンに重ねて立体感を出す。</summary>
        public static Sprite CircleGlossSprite
        {
            get
            {
                if (_circleGlossSprite == null)
                {
                    const int size = 128;
                    float c = (size - 1) / 2f;
                    const float outer = 62f;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var pixels = new Color[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                            float a = Mathf.Clamp01(outer - d);
                            // 上半分だけ白くフェード (テクスチャは y=0 が下)
                            float t = Mathf.Clamp01((y - c) / outer);
                            pixels[y * size + x] = new Color(1f, 1f, 1f, a * t * 0.34f);
                        }
                    }
                    tex.SetPixels(pixels);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _circleGlossSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                }
                return _circleGlossSprite;
            }
        }

        /// <summary>
        /// やわらかい質感のピルボタン (ごく細い縁 + 白ベース + 下側の薄いシェード + ソフト影)。
        /// 縁があるので白いカードの上に置いても全周の輪郭が分かる。
        /// 全画面の副操作ボタン共通の見た目 (旧 CreateOutlineButton はこれに一本化)。
        /// </summary>
        public static Button CreateSoftButton(Transform parent, string name, string label, int fontSize = 36)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var frame = go.AddComponent<Image>();
            frame.color = new Color(0.70f, 0.63f, 0.86f, 0.45f);
            Roundify(frame);
            AddShadow(go, 4f);

            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(go.transform, false);
            var inner = innerGo.AddComponent<Image>();
            inner.color = new Color(0.995f, 0.99f, 1f);
            Roundify(inner);
            inner.raycastTarget = false;
            StretchFull(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(2f, 2f);
            inner.rectTransform.offsetMax = new Vector2(-2f, -2f);

            // 下側の薄いシェードでぷっくり感を出す
            var shadeGo = new GameObject("Shade");
            shadeGo.transform.SetParent(go.transform, false);
            var shade = shadeGo.AddComponent<Image>();
            shade.color = new Color(0.40f, 0.32f, 0.58f, 0.08f);
            Roundify(shade);
            shade.raycastTarget = false;
            var shadeRect = shade.rectTransform;
            shadeRect.anchorMin = new Vector2(0f, 0f);
            shadeRect.anchorMax = new Vector2(1f, 0.5f);
            shadeRect.offsetMin = new Vector2(2f, 2f);
            shadeRect.offsetMax = new Vector2(-2f, 0f);

            var button = go.AddComponent<Button>();
            go.AddComponent<PressEffect>();
            var text = CreateText(go.transform, "Label", label, fontSize, Primary);
            StretchFull(text.rectTransform);
            return button;
        }

        static Sprite CreateCircleSprite(int size, float outerR, float innerR)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) / 2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(outerR - d);
                    if (innerR > 0f)
                    {
                        a *= Mathf.Clamp01(d - innerR);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite _vinylSprite;
        /// <summary>
        /// レコード盤 (実行時生成のプレースホルダ)。
        /// Resources/Art/UI に専用素材 (yukanavi_record_disc) があればそちらを優先すること。
        /// </summary>
        public static Sprite VinylSprite
        {
            get
            {
                if (_vinylSprite == null)
                {
                    const int size = 512;
                    float c = (size - 1) / 2f;
                    const float outer = 253f;
                    var dark = new Color(0.14f, 0.12f, 0.18f);
                    var groove = new Color(0.23f, 0.20f, 0.29f);
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var pixels = new Color[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                            float a = Mathf.Clamp01(outer - d);
                            // 同心円の溝でレコードらしさを出す
                            bool isGroove = d > 110f && d < 242f && (d % 16f) < 2.4f;
                            var col = isGroove ? groove : dark;
                            pixels[y * size + x] = new Color(col.r, col.g, col.b, a);
                        }
                    }
                    tex.SetPixels(pixels);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _vinylSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                }
                return _vinylSprite;
            }
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
                FitLabel(text); // 大きい文字サイズ設定でもタブ内に収める
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

        /// <summary>全角基準の概算テキスト幅 (Text.preferredWidth はレイアウト前に信用できないため)。</summary>
        public static float EstimateTextWidth(string text, int fontSize)
        {
            float units = 0f;
            foreach (char c in text ?? "")
            {
                units += c <= 0x7F ? 0.56f : 1.04f;
            }
            return units * ScaledFontSize(fontSize);
        }

        /// <summary>概算の折り返し行数 (足りないと最終行が欠けるため少し多めに見積もる)。改行にも対応。</summary>
        public static int EstimateWrapLines(string text, int fontSize, float width)
        {
            int total = 0;
            foreach (var line in (text ?? "").Split('\n'))
            {
                float estimated = EstimateTextWidth(line, fontSize) * 1.08f;
                total += Mathf.Max(1, Mathf.CeilToInt(estimated / width));
            }
            return Mathf.Max(1, total);
        }

        /// <summary>
        /// 半角スペースを改行しないスペース (NBSP) に置き換える。
        /// ファイル名や曲名は文章ではないため、単語折り返しではなく文字単位で折り返させる
        /// (単語折り返しだと行数の見積もりとずれて最終行がはみ出す)。
        /// </summary>
        public static string NoWordWrap(string text)
        {
            return (text ?? "").Replace(' ', ' ');
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

        /// <summary>
        /// 画面上部の共通ヘッダーバー (白帯 + タイトル)。
        /// リンクラ風に、壁紙 (ホーム透過) の対象外となる不透明の白にしている。
        /// </summary>
        /// <summary>
        /// セーフエリア (ノッチ・パンチホールカメラ・下部ホームバー) の上下インセット
        /// (Canvas 単位)。AppRoot が起動時に Screen.safeArea から設定する。エディタ等では 0。
        /// 画面 (Screens レイヤー) は上インセット分下がり、ノッチ裏はバーの背景が受け持つ。
        /// </summary>
        public static float SafeTop;
        public static float SafeBottom;

        /// <summary>
        /// バーの白背景をセーフエリア外 (ノッチの裏) まで上に伸ばす。
        /// 中身はバー本体 (セーフエリア内) のままなので座標系は変わらない。
        /// </summary>
        public static void ExtendBarIntoSafeArea(RectTransform bar, Color color)
        {
            if (SafeTop <= 0f)
            {
                return;
            }
            var ext = CreatePanel(bar, "SafeAreaExtension", color);
            ext.anchorMin = new Vector2(0f, 1f);
            ext.anchorMax = new Vector2(1f, 1f);
            ext.pivot = new Vector2(0.5f, 0f);
            ext.anchoredPosition = Vector2.zero;
            ext.sizeDelta = new Vector2(0f, SafeTop);
        }

        public static RectTransform CreateTopBar(Transform parent, string title)
        {
            var bar = CreatePanel(parent, "TopBar", Color.white);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, 110f);
            AddShadow(bar.gameObject, 4f);
            ExtendBarIntoSafeArea(bar, Color.white);
            var text = CreateText(bar, "Title", title, 40, PrimaryDark);
            text.fontStyle = FontStyle.Bold;
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

        const string FontScaleKey = "ui.font_scale";
        static float _fontScale;
        static bool _fontScaleLoaded;

        /// <summary>
        /// 全テキスト共通の拡大率 (スマホでの視認性向上。既定 1.3)。
        /// 各所の指定サイズは公称値のまま、実描画・幅見積もり・行高をこの係数で揃える。
        /// 設定画面から変更でき端末に保存される。変更後は ScreenManager.RebuildAll() で反映。
        /// </summary>
        public static float FontScale
        {
            get
            {
                if (!_fontScaleLoaded)
                {
                    _fontScale = PlayerPrefs.GetFloat(FontScaleKey, 1.3f);
                    _fontScaleLoaded = true;
                }
                return _fontScale;
            }
            set
            {
                _fontScale = Mathf.Clamp(value, 1f, 1.9f);
                _fontScaleLoaded = true;
                PlayerPrefs.SetFloat(FontScaleKey, _fontScale);
                PlayerPrefs.Save();
            }
        }

        /// <summary>FontScale 適用後の実フォントサイズ。</summary>
        public static int ScaledFontSize(int fontSize)
        {
            return Mathf.RoundToInt(fontSize * FontScale);
        }

        /// <summary>
        /// 折り返しテキスト1行分の高さ (FontScale 込み)。
        /// 丸ゴシックの実行高は fontSize より大きいため、大きいサイズでは比例分を確保する
        /// (固定 +12px だけだと 145% 以上で縦 Truncate の行が消える)。
        /// </summary>
        public static float LineHeight(int fontSize)
        {
            float scaled = ScaledFontSize(fontSize);
            return Mathf.Max(scaled + 12f, scaled * 1.45f);
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize,
                                      Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = ScaledFontSize(fontSize);
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
            scrollRect.movementType = ScrollRect.MovementType.Elastic; // 端で少しバウンス
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

        /// <summary>
        /// 実キーボードの Enter で入力を確定したときに action を実行する (検索欄用)。
        /// Windows / エディタ向けで、モバイルのソフトキーボードでは何もしない。
        /// 「Enter を押した状態でフォーカスが外れた」ことを監視するため、
        /// 日本語入力の変換確定 (フォーカスが残る) では発火しない。
        /// </summary>
        public static void OnSubmit(InputField input, System.Action action)
        {
            var watcher = input.gameObject.AddComponent<SubmitOnEnter>();
            watcher.Input = input;
            watcher.Action = action;
        }

        class SubmitOnEnter : MonoBehaviour
        {
            public InputField Input;
            public System.Action Action;
            bool _wasFocused;

            void Update()
            {
                bool focused = Input.isFocused;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (_wasFocused && !focused && keyboard != null
                    && (keyboard.enterKey.isPressed || keyboard.enterKey.wasPressedThisFrame
                        || keyboard.numpadEnterKey.isPressed || keyboard.numpadEnterKey.wasPressedThisFrame))
                {
                    Action?.Invoke();
                }
                _wasFocused = focused;
            }
        }

        /// <summary>ラベルが枠に入りきらないときに自動で縮小させる (タブ・小さいボタン用)。</summary>
        public static void FitLabel(Text text, int minSize = 16)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = minSize;
        }

        /// <summary>トーストの親 (Canvas)。AppRoot が起動時に設定する。</summary>
        public static Transform ToastRoot;
        static GameObject _activeToast;

        /// <summary>
        /// 画面下部にトースト (操作結果の短い通知) を出す。数秒表示して自動で消える。
        /// sticky=true は自動で消えず、タップされるまで表示し続ける (書式エラーなど読ませたいもの用)。
        /// 連続して呼ばれたら前のトーストは置き換わる。
        /// </summary>
        public static void ShowToast(string message, bool isError = false, bool sticky = false)
        {
            if (ToastRoot == null || string.IsNullOrEmpty(message))
            {
                return;
            }
            if (_activeToast != null)
            {
                Object.Destroy(_activeToast);
            }
            if (sticky)
            {
                message += "\n(タップで閉じる)";
            }
            var go = new GameObject("Toast");
            _activeToast = go;
            go.transform.SetParent(ToastRoot, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            const float maxWidth = 1000f;
            float width = Mathf.Min(EstimateTextWidth(message, 26) + 72f, maxWidth);
            int lines = 0;
            foreach (string line in message.Split('\n'))
            {
                lines += EstimateWrapLines(line, 26, maxWidth - 72f);
            }
            rect.sizeDelta = new Vector2(width, lines * LineHeight(26) + 28f);
            rect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 28f);
            var img = go.AddComponent<Image>();
            img.color = isError
                ? new Color(0.72f, 0.25f, 0.32f, 0.95f)
                : new Color(0.27f, 0.22f, 0.38f, 0.92f);
            Roundify(img);
            img.raycastTarget = sticky;
            AddShadow(go, 4f);
            var text = CreateText(go.transform, "Message", message, 26, Color.white);
            StretchFull(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(28f, 4f);
            text.rectTransform.offsetMax = new Vector2(-28f, -4f);
            text.raycastTarget = false;
            var view = go.AddComponent<ToastView>();
            view.Sticky = sticky;
            if (sticky)
            {
                var button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(view.Dismiss);
            }
        }

        /// <summary>
        /// 音符が順に跳ねるローディング表示 (♪♪♪ + メッセージ)。不要になったら Destroy する。
        /// </summary>
        public static GameObject CreateLoadingNotes(Transform parent, string message)
        {
            var go = new GameObject("Loading");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 60f);
            rect.sizeDelta = new Vector2(600f, 200f);
            go.AddComponent<LoadingNotesView>().Init(message);
            return go;
        }

        /// <summary>ローディング表示のアニメ駆動 (3つの音符が波打つように跳ねる)。</summary>
        class LoadingNotesView : MonoBehaviour
        {
            readonly Text[] _notes = new Text[3];
            float _elapsed;

            public void Init(string message)
            {
                for (int i = 0; i < _notes.Length; i++)
                {
                    var note = CreateText(transform, "Note" + i, "♪", 56, Primary);
                    var rect = note.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = new Vector2((i - 1) * 72f, 30f);
                    rect.sizeDelta = new Vector2(90f, 110f);
                    note.raycastTarget = false;
                    AddShadow(note.gameObject, 2f);
                    _notes[i] = note;
                }
                if (!string.IsNullOrEmpty(message))
                {
                    var text = CreateText(transform, "Message", message, 26, TextMuted);
                    var rect = text.rectTransform;
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = new Vector2(0f, -56f);
                    rect.sizeDelta = new Vector2(0f, LineHeight(26));
                    text.raycastTarget = false;
                }
            }

            void Update()
            {
                _elapsed += Time.deltaTime;
                for (int i = 0; i < _notes.Length; i++)
                {
                    if (_notes[i] == null)
                    {
                        continue;
                    }
                    float phase = _elapsed * 5f - i * 0.9f;
                    float wave = Mathf.Clamp01(Mathf.Sin(phase));
                    _notes[i].rectTransform.anchoredPosition =
                        new Vector2((i - 1) * 72f, 30f + wave * 26f);
                    var c = _notes[i].color;
                    c.a = 0.4f + 0.6f * wave;
                    _notes[i].color = c;
                }
            }
        }

        /// <summary>トーストの出現・待機・退場を自前で駆動する (どの画面からでも出せる)。</summary>
        class ToastView : MonoBehaviour
        {
            const float InDuration = 0.18f;
            const float HoldDuration = 2.2f;
            const float OutDuration = 0.35f;

            /// <summary>true なら自動で消えず、Dismiss (タップ) されるまで表示し続ける。</summary>
            public bool Sticky;

            CanvasGroup _group;
            RectTransform _rect;
            float _baseY;
            float _elapsed;
            bool _dismissed;

            void Awake()
            {
                _group = gameObject.AddComponent<CanvasGroup>();
                _rect = (RectTransform)transform;
                _baseY = _rect.anchoredPosition.y;
                _group.alpha = 0f;
            }

            /// <summary>退場を開始する (sticky トーストのタップ)。</summary>
            public void Dismiss()
            {
                if (_dismissed)
                {
                    return;
                }
                _dismissed = true;
                _elapsed = InDuration + HoldDuration; // 退場フェーズへ
            }

            void Update()
            {
                _elapsed += Time.deltaTime;
                if (_elapsed < InDuration)
                {
                    float k = _elapsed / InDuration;
                    k = 1f - (1f - k) * (1f - k); // easeOut
                    _group.alpha = k;
                    _rect.anchoredPosition = new Vector2(0f, _baseY - 24f * (1f - k));
                }
                else if (Sticky && !_dismissed)
                {
                    _group.alpha = 1f;
                    _rect.anchoredPosition = new Vector2(0f, _baseY);
                    _elapsed = InDuration; // タップされるまでここで待つ
                }
                else if (_elapsed < InDuration + HoldDuration)
                {
                    _group.alpha = 1f;
                    _rect.anchoredPosition = new Vector2(0f, _baseY);
                }
                else if (_elapsed < InDuration + HoldDuration + OutDuration)
                {
                    _group.alpha = 1f - (_elapsed - InDuration - HoldDuration) / OutDuration;
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// ラベルを1行に固定し、幅に収まらないときは縮小する。FitLabel だけだと
        /// 2行に折り返して収まった時点で縮小が止まるため、枠の高さを1行分に制限する。
        /// </summary>
        public static void FitLabelOneLine(Text text, int minSize = 16)
        {
            FitLabel(text, minSize);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(rect.anchorMin.x, 0.5f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 0.5f);
            rect.pivot = new Vector2(rect.pivot.x, 0.5f);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, text.fontSize * 1.45f);
        }

        /// <summary>
        /// 横スクロールの右にまだ続きがあるときだけ「›」のしるしを出す (チップ行用)。
        /// </summary>
        public static void AddHorizontalMoreIndicator(ScrollRect scroll)
        {
            var go = new GameObject("MoreIndicator");
            go.transform.SetParent(scroll.transform, false);
            go.transform.SetAsLastSibling(); // マスク付き viewport より前面に出す
            var bg = go.AddComponent<Image>();
            bg.sprite = CircleSprite;
            bg.color = Primary;
            bg.raycastTarget = false;
            AddShadow(go, 3f);
            var rect = bg.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-4f, 0f);
            rect.sizeDelta = new Vector2(52f, 52f);
            var arrow = CreateText(go.transform, "Arrow", "›", 36, Color.white);
            arrow.fontStyle = FontStyle.Bold;
            StretchFull(arrow.rectTransform);
            go.SetActive(false);

            var watcher = scroll.gameObject.AddComponent<ScrollMoreIndicator>();
            watcher.Scroll = scroll;
            watcher.Indicator = go;
        }

        class ScrollMoreIndicator : MonoBehaviour
        {
            public ScrollRect Scroll;
            public GameObject Indicator;

            void Update()
            {
                var viewport = Scroll.viewport != null ? Scroll.viewport : (RectTransform)Scroll.transform;
                bool more = false;
                if (Scroll.content != null)
                {
                    // content の x はスクロールにつれて 0 → -(はみ出し幅) へ動く
                    float overflow = Scroll.content.rect.width - viewport.rect.width;
                    more = overflow > 4f && Scroll.content.anchoredPosition.x > -(overflow - 4f);
                }
                if (Indicator.activeSelf != more)
                {
                    Indicator.SetActive(more);
                }
            }
        }

        /// <summary>横スライダー (音量調整等)。位置とサイズは戻り値の RectTransform で調整する。</summary>
        public static Slider CreateSlider(Transform parent, string name, float min, float max, bool wholeNumbers = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            // トラック (背景)
            var trackGo = new GameObject("Background");
            trackGo.transform.SetParent(go.transform, false);
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.color = new Color(0.85f, 0.83f, 0.90f);
            Roundify(trackImg);
            trackImg.raycastTarget = false;
            var trackRect = trackGo.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, 14f);

            // 塗り (現在値まで)
            var fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(go.transform, false);
            var fillArea = fillAreaGo.AddComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-20f, 14f);
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = Primary;
            Roundify(fillImg);
            fillImg.raycastTarget = false;
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(10f, 0f);

            // つまみ
            var handleAreaGo = new GameObject("Handle Slide Area");
            handleAreaGo.transform.SetParent(go.transform, false);
            var handleArea = handleAreaGo.AddComponent<RectTransform>();
            StretchFull(handleArea);
            handleArea.offsetMin = new Vector2(22f, 0f);
            handleArea.offsetMax = new Vector2(-22f, 0f);
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = Color.white;
            Roundify(handleImg);
            AddShadow(handleGo, 3f);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(44f, 44f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            return slider;
        }
    }
}
