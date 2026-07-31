using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// ゆかりちゃん立ち絵の表示コンポーネント (静止画版)。
    /// 浮遊・まばたき・タップ時の表情切替+スクイーズを持つ。
    /// デフォルト (ゆかりちゃん) もカスタムスキンも MascotCharacter のリストとして同じに扱い、
    /// タップで 立ち絵 → 表情 → … と巡回して一巡すると次のキャラへ切り替わる。
    /// 将来 Live2D 版と差し替えられるよう、外部には Create() と OnTapped だけを見せる。
    /// </summary>
    public class MascotView : MonoBehaviour
    {
        /// <summary>マスコット1キャラ分の表示素材。デフォルトのゆかりちゃんもこの形に載せる。</summary>
        public class MascotCharacter
        {
            /// <summary>立ち絵 (必須)</summary>
            public Sprite Base;
            /// <summary>目閉じ差分 (null = まばたきなし)</summary>
            public Sprite EyesClosed;
            /// <summary>タップで巡回する表情差分 (null/空 = 表情なし)</summary>
            public Sprite[] Expressions;
            /// <summary>キャラ専用セリフ (null = スキン全体のセリフへフォールバック)</summary>
            public string[] Talk;
            /// <summary>表情ごとのセリフ (Expressions と同じ並び。要素は null 可)</summary>
            public string[][] ExpressionTalk;
        }

        /// <summary>
        /// Live2D で表示するときのモデルの供給元。
        /// Resources に組み込んだプレハブか、デフォルトテーマ拡張パックから実行時ロードした
        /// モデルのどちらかを持つ (両方あれば RuntimeModel を優先)。
        /// きせかえスキンからは指定できない (ライセンス上の判断。
        /// docs/default-theme-pack.md の「Live2D モデルの配信」を参照)。
        /// </summary>
        public class Live2DSource
        {
            /// <summary>Resources から読んだプレハブ (実行時ロードのときは null)</summary>
            public GameObject Prefab;
            /// <summary>拡張パックから実行時ロードしたモデル (Resources を使うときは null)</summary>
            public Live2DRuntimeLoader.Model RuntimeModel;
            /// <summary>待機モーション (Prefab を使うときに指定。RuntimeModel は自前で持つ)</summary>
            public AnimationClip IdleClip;
            /// <summary>タップ時モーション (同上)</summary>
            public AnimationClip TapClip;
            /// <summary>
            /// 呼吸をアプリ側で作るか。モーションに ParamBreath を入れない約束なので通常は true
            /// (art/mascot/live2d_parts/MODEL_REQUEST.md §6)。
            /// </summary>
            public bool AutoBreath = true;

            /// <summary>表示できるモデルを持っているか。</summary>
            public bool HasModel
            {
                get { return (RuntimeModel != null && RuntimeModel.GameObject != null) || Prefab != null; }
            }
        }

        // デフォルト (ゆかりちゃん) の素材 (Resources 配下のパス)
        const string DefaultBasePath = "Art/Mascot/yukari_mascot_transparent";
        const string DefaultEyesClosedPath = "Art/Mascot/yukari_expr_eyes_closed";
        static readonly string[] DefaultExpressionPaths =
        {
            "Art/Mascot/yukari_expr_smile",
            "Art/Mascot/yukari_expr_wink",
            "Art/Mascot/yukari_expr_surprised",
        };

        // タップ時にランダムで出すセリフ (デフォルトテーマ拡張パックの talk.json で差し替え可能)
        static readonly string[] SpeechLines =
        {
            "うたっていこ〜♪",
            "今日はなにうたう？",
            "タップありがと♪",
            "いっぱい予約してね！",
            "じゅんびばっちりだよ〜",
        };
        // 時間帯セリフ (朝 5〜11時 / 夕方 17〜22時 / 夜 22〜翌5時)。通常セリフと合算して抽選
        static readonly string[] SpeechLinesMorning =
        {
            "おはよ〜！今日もうたおうね♪",
            "あさのうた、きもちいいよ〜",
        };
        static readonly string[] SpeechLinesEvening =
        {
            "こんばんは〜♪",
            "夜はこれからだよ〜！",
        };
        static readonly string[] SpeechLinesNight =
        {
            "よふかしカラオケだ〜♪",
            "そろそろおやすみ？もう1曲だけ？",
        };

        public System.Action OnTapped;

        /// <summary>true を返す間はタップ演出 (表情切替・セリフ) を止める (ホームの移動モード用)。</summary>
        public System.Func<bool> SuppressTap;

        /// <summary>
        /// スキンで設定されたセリフ (1要素=1つ)。設定されていればデフォルトの代わりに使う。
        /// カスタムキャラはこれが無い場合セリフを出さない。
        /// </summary>
        public string[] CustomLines;

        /// <summary>スキンの時間帯セリフ (朝)。CustomLines と合算して抽選される。</summary>
        public string[] CustomLinesMorning;
        /// <summary>スキンの時間帯セリフ (夕方)。</summary>
        public string[] CustomLinesEvening;
        /// <summary>スキンの時間帯セリフ (夜)。</summary>
        public string[] CustomLinesNight;

        Image _image;
        RectTransform _rect;
        Live2DMascotRenderer _live2d; // null なら静止画版
        List<MascotCharacter> _characters;
        int _charIndex;
        int _exprIndex; // 0 = 立ち絵、1〜 = Expressions[_exprIndex - 1]
        float _baseY;
        float _nextBlinkTime;
        bool _blinking;
        Coroutine _squash;
        GameObject _bubble;
        Text _bubbleText;
        Coroutine _bubbleRoutine;

        bool _isCustom;

        MascotCharacter CurrentCharacter
        {
            get { return _characters[_charIndex]; }
        }

        /// <summary>
        /// 下端中央アンカーで立ち絵を生成する。
        /// characters を渡すとスキンのカスタムキャラ (null ならデフォルトのゆかりちゃん) になる。
        /// live2d を渡すと静止画の代わりに Live2D モデルを表示する
        /// (呼吸・まばたき・表情はモデル側が持つため、こちらの静止画向け演出は止める)。
        /// </summary>
        public static MascotView Create(Transform parent, Vector2 size, float baseY,
                                        List<MascotCharacter> characters = null,
                                        Live2DSource live2d = null)
        {
            var go = new GameObject("Mascot");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MascotView>();
            view.Build(size, baseY, characters, live2d);
            return view;
        }

        void Build(Vector2 size, float baseY, List<MascotCharacter> characters, Live2DSource live2d)
        {
            bool useLive2D = live2d != null && live2d.HasModel;
            if (useLive2D)
            {
                // 静止画のスプライトは使わない (表情差分もモデル側のパラメータで表現する)。
                // ただし _characters は下のデフォルト構築で必ず 1 件は入るようにしておく
                // (CurrentCharacter を参照する共通処理があるため)
                characters = null;
            }
            if (characters != null && characters.Count > 0)
            {
                _isCustom = true;
                _characters = characters;
            }
            else
            {
                // デフォルト (ゆかりちゃん): 立ち絵 + 表情3種 + 目閉じ。カスタムと同じ巡回に載せる
                var expressions = new Sprite[DefaultExpressionPaths.Length];
                for (int i = 0; i < DefaultExpressionPaths.Length; i++)
                {
                    expressions[i] = UiFactory.LoadSprite(DefaultExpressionPaths[i]);
                }
                _characters = new List<MascotCharacter>
                {
                    new MascotCharacter
                    {
                        Base = UiFactory.LoadSprite(DefaultBasePath),
                        EyesClosed = UiFactory.LoadSprite(DefaultEyesClosedPath),
                        Expressions = expressions,
                    },
                };
            }
            _nextBlinkTime = Time.time + Random.Range(2f, 4f);

            // Live2D 版でもこの GameObject には透明な Image を置いたままにする。
            // ホームの長押し移動 (HomeDraggable) とタップ (Button) がこの Graphic で
            // 判定されるため、消すと動かせなくなる。表示は子の Live2DMascotRenderer が担う
            _image = gameObject.AddComponent<Image>();
            if (useLive2D)
            {
                _image.color = new Color(1f, 1f, 1f, 0f); // 透明 (タップ判定のみ)
            }
            else
            {
                _image.sprite = _characters[0].Base;
                _image.preserveAspect = true;
            }

            _rect = _image.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _baseY = baseY;
            _rect.anchoredPosition = new Vector2(0f, _baseY);
            _rect.sizeDelta = size;

            if (useLive2D)
            {
                // 表示は子に置く (この GameObject の Graphic はタップ判定用の透明 Image のため)。
                // 枠いっぱいに広げてあるので、呼吸やスクイーズで親を拡縮すると一緒に動く
                var viewGo = new GameObject("Live2DView");
                viewGo.transform.SetParent(transform, false);
                var viewRect = viewGo.AddComponent<RectTransform>();
                UiFactory.StretchFull(viewRect);
                _live2d = viewGo.AddComponent<Live2DMascotRenderer>();
                _live2d.UseAutoBreath = live2d.AutoBreath; // Load より前に決めておく必要がある
                if (live2d.RuntimeModel != null)
                {
                    _live2d.Load(live2d.RuntimeModel);
                }
                else
                {
                    _live2d.Load(live2d.Prefab, live2d.IdleClip, live2d.TapClip);
                }
            }

            var button = gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HandleTap);

            BuildBubble();
        }

        /// <summary>セリフ吹き出し (しっぽが左下向き → キャラの右上に置く)。</summary>
        void BuildBubble()
        {
            _bubble = new GameObject("Bubble");
            _bubble.transform.SetParent(transform, false);
            var img = _bubble.AddComponent<Image>();
            // ASSET_MANIFEST の推奨 9-slice border: L=170, R=80, T=80, B=135
            img.sprite = UiFactory.LoadSprite9Slice("Art/UI/yukanavi_speech_bubble_9slice_tail_left",
                new Vector4(170f, 135f, 80f, 80f));
            img.type = Image.Type.Sliced;
            // border 合計 (縦215px) が表示サイズを超えると枠が潰れるため、実効スケールを縮める
            img.pixelsPerUnitMultiplier = 1.6f;
            img.raycastTarget = false;
            var rect = img.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.1f, 0.05f);
            rect.anchoredPosition = new Vector2(60f, -140f);
            rect.sizeDelta = new Vector2(470f, 190f);

            _bubbleText = UiFactory.CreateText(_bubble.transform, "Text", "", 30, UiFactory.TextDark,
                TextAnchor.MiddleLeft);
            UiFactory.StretchFull(_bubbleText.rectTransform);
            // 枠の内側に収める (左の border はしっぽ込みの値のため、見た目に合わせて詰める)
            _bubbleText.rectTransform.offsetMin = new Vector2(68f, 85f);
            _bubbleText.rectTransform.offsetMax = new Vector2(-45f, -50f);
            _bubbleText.verticalOverflow = VerticalWrapMode.Overflow;

            _bubble.SetActive(false);
        }

        /// <summary>吹き出しにセリフを表示する (数秒で自動的に消える)。</summary>
        public void Say(string line)
        {
            if (_bubbleRoutine != null)
            {
                StopCoroutine(_bubbleRoutine);
            }
            _bubbleRoutine = StartCoroutine(BubbleRoutine(line));
        }

        IEnumerator BubbleRoutine(string line)
        {
            _bubbleText.text = line;
            _bubble.SetActive(true);
            var rect = _bubble.GetComponent<RectTransform>();
            // セリフの長さに合わせて吹き出しの横幅を調整する (左右の余白 + テキスト幅)。
            // 最大幅を超える長さや大きい文字サイズでは折り返し、行数分だけ縦に伸ばす
            float width = Mathf.Clamp(_bubbleText.preferredWidth + 68f + 45f + 14f, 250f, 640f);
            int lines = UiFactory.EstimateWrapLines(line, 30, width - 68f - 45f);
            float height = Mathf.Max(190f, lines * UiFactory.LineHeight(30) + 85f + 50f + 8f);
            rect.sizeDelta = new Vector2(width, height);
            const float popDuration = 0.18f;
            for (float e = 0f; e < popDuration; e += Time.deltaTime)
            {
                float k = e / popDuration;
                float scale = Mathf.Lerp(0.6f, 1f, 1f - (1f - k) * (1f - k)); // easeOut
                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            rect.localScale = Vector3.one;
            yield return new WaitForSeconds(2.2f);
            _bubble.SetActive(false);
            _bubbleRoutine = null;
        }

        void Update()
        {
            // Live2D 版は呼吸もまばたきもモデル (Idle モーションと自動まばたき) が持つので、
            // 静止画向けのこれらの演出は動かさない
            if (_live2d != null)
            {
                return;
            }

            // 足を着けたまま呼吸で上体がゆっくり伸び縮みする (pivot が下端なので足元は動かない)。
            // ごく僅かな傾きで体重移動も乗せる。タップのスクイーズ中はそちらを優先
            if (_squash == null)
            {
                float breath = Mathf.Sin(Time.time * 1.6f);
                _rect.localScale = new Vector3(1f - 0.005f * breath, 1f + 0.009f * breath, 1f);
                _rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.45f) * 0.5f);
            }

            // 立ち絵のときだけランダム間隔でまばたき (目閉じ差分があるキャラのみ)
            if (_exprIndex == 0 && !_blinking && CurrentCharacter.EyesClosed != null
                && Time.time >= _nextBlinkTime)
            {
                StartCoroutine(BlinkRoutine());
            }
        }

        void HandleTap()
        {
            if (SuppressTap != null && SuppressTap())
            {
                return;
            }
            if (_live2d != null)
            {
                // Live2D 版はモデルの TapBody モーションで反応する (表情の巡回はしない)
                _live2d.PlayTapMotion();
            }
            else
            {
                // 現在キャラの表情を一巡してから次のキャラへ (デフォルトは 1キャラ + 表情3種の巡回)
                var ch = CurrentCharacter;
                int total = 1 + (ch.Expressions != null ? ch.Expressions.Length : 0);
                _exprIndex++;
                if (_exprIndex >= total)
                {
                    _exprIndex = 0;
                    _charIndex = (_charIndex + 1) % _characters.Count;
                }
                var sprite = CurrentSprite();
                if (sprite != null)
                {
                    _image.sprite = sprite;
                }
            }
            Se.Play(Se.Tap);
            // スクイーズ (画像全体をぷにっと潰す) は静止画版の演出。Live2D では
            // モデル自身が動くので重ねると不自然になるため行わない
            if (_live2d == null)
            {
                if (_squash != null)
                {
                    StopCoroutine(_squash);
                }
                _squash = StartCoroutine(SquashRoutine());
            }
            string line = PickLine();
            if (line != null)
            {
                Say(line);
            }
            OnTapped?.Invoke();
        }

        /// <summary>いま表示すべきスプライト (表情が読めていないときは立ち絵のまま)。</summary>
        Sprite CurrentSprite()
        {
            var ch = CurrentCharacter;
            if (_exprIndex == 0)
            {
                return ch.Base;
            }
            return (ch.Expressions != null && _exprIndex - 1 < ch.Expressions.Length)
                ? ch.Expressions[_exprIndex - 1] : ch.Base;
        }

        /// <summary>
        /// セリフの優先順: 表示中の表情専用 → キャラ専用 → スキン全体 (時間帯 + 通常の合算)
        /// → デフォルト (時間帯 + 通常の合算。カスタムキャラはなし)。
        /// </summary>
        string PickLine()
        {
            var ch = CurrentCharacter;
            string[] lines = null;
            if (_exprIndex > 0 && ch.ExpressionTalk != null && _exprIndex - 1 < ch.ExpressionTalk.Length)
            {
                lines = ch.ExpressionTalk[_exprIndex - 1];
            }
            if (lines == null || lines.Length == 0)
            {
                lines = ch.Talk;
            }
            if (lines == null || lines.Length == 0)
            {
                lines = MergeLines(TimeOfDayCustomLines(), CustomLines);
            }
            if ((lines == null || lines.Length == 0) && !_isCustom)
            {
                lines = MergeLines(TimeOfDayDefaultLines(), DefaultLines());
            }
            if (lines == null || lines.Length == 0)
            {
                return null;
            }
            return lines[Random.Range(0, lines.Length)];
        }

        string[] TimeOfDayCustomLines()
        {
            switch (SkinManager.GetTimeOfDaySuffix(System.DateTime.Now.Hour))
            {
                case "morning": return CustomLinesMorning;
                case "evening": return CustomLinesEvening;
                case "night": return CustomLinesNight;
                default: return null;
            }
        }

        /// <summary>デフォルトの通常セリフ (拡張パックの talk.json があればそちらを使う)。</summary>
        static string[] DefaultLines()
        {
            var pack = DefaultThemePack.GetTalk();
            return PackOrBuiltin(pack != null ? pack.Talk : null, SpeechLines);
        }

        /// <summary>デフォルトの時間帯セリフ (拡張パックの talk.json があればそちらを使う)。</summary>
        static string[] TimeOfDayDefaultLines()
        {
            var pack = DefaultThemePack.GetTalk();
            switch (SkinManager.GetTimeOfDaySuffix(System.DateTime.Now.Hour))
            {
                case "morning":
                    return PackOrBuiltin(pack != null ? pack.TalkMorning : null, SpeechLinesMorning);
                case "evening":
                    return PackOrBuiltin(pack != null ? pack.TalkEvening : null, SpeechLinesEvening);
                case "night":
                    return PackOrBuiltin(pack != null ? pack.TalkNight : null, SpeechLinesNight);
                default:
                    return null;
            }
        }

        static string[] PackOrBuiltin(List<string> packLines, string[] builtin)
        {
            return (packLines != null && packLines.Count > 0) ? packLines.ToArray() : builtin;
        }

        /// <summary>2つのセリフ配列を合算する (どちらも空なら null)。</summary>
        static string[] MergeLines(string[] a, string[] b)
        {
            bool hasA = a != null && a.Length > 0;
            bool hasB = b != null && b.Length > 0;
            if (hasA && hasB)
            {
                var merged = new string[a.Length + b.Length];
                a.CopyTo(merged, 0);
                b.CopyTo(merged, a.Length);
                return merged;
            }
            return hasA ? a : (hasB ? b : null);
        }

        /// <summary>タップ時のスクイーズ (ぷにっと潰れて戻る)。</summary>
        IEnumerator SquashRoutine()
        {
            const float duration = 0.14f;
            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                float k = Mathf.Sin(e / duration * Mathf.PI); // 0→1→0
                _rect.localScale = new Vector3(1f + 0.06f * k, 1f - 0.08f * k, 1f);
                yield return null;
            }
            _rect.localScale = Vector3.one;
            _squash = null; // 呼吸アニメ (Update) に戻す
        }

        /// <summary>一瞬目を閉じて戻す。</summary>
        IEnumerator BlinkRoutine()
        {
            _blinking = true;
            var eyesClosed = CurrentCharacter.EyesClosed;
            if (eyesClosed != null)
            {
                _image.sprite = eyesClosed;
            }
            yield return new WaitForSeconds(0.12f);
            if (_exprIndex == 0)
            {
                // タップでキャラが替わっていても、いま表示中のキャラの立ち絵へ戻す
                _image.sprite = CurrentCharacter.Base;
            }
            _blinking = false;
            _nextBlinkTime = Time.time + Random.Range(2.5f, 6f);
        }
    }
}
