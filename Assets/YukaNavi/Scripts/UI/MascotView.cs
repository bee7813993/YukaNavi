using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// ゆかりちゃん立ち絵の表示コンポーネント (静止画版)。
    /// 浮遊・まばたき・タップ時の表情切替+スクイーズを持つ。
    /// 将来 Live2D 版と差し替えられるよう、外部には Create() と OnTapped だけを見せる。
    /// </summary>
    public class MascotView : MonoBehaviour
    {
        // 表情の切替順 (Resources 配下のパス)
        static readonly string[] ExpressionPaths =
        {
            "Art/Mascot/yukari_mascot_transparent",
            "Art/Mascot/yukari_expr_smile",
            "Art/Mascot/yukari_expr_wink",
            "Art/Mascot/yukari_expr_surprised",
        };

        // タップ時にランダムで出すセリフ
        static readonly string[] SpeechLines =
        {
            "うたっていこ〜♪",
            "今日はなにうたう？",
            "タップありがと♪",
            "いっぱい予約してね！",
            "じゅんびばっちりだよ〜",
        };

        public System.Action OnTapped;

        /// <summary>true を返す間はタップ演出 (表情切替・セリフ) を止める (ホームの移動モード用)。</summary>
        public System.Func<bool> SuppressTap;

        /// <summary>
        /// スキンで設定されたセリフ (1要素=1つ)。設定されていればデフォルトの代わりに使う。
        /// カスタムキャラはこれが無い場合セリフを出さない。
        /// </summary>
        public string[] CustomLines;

        Image _image;
        RectTransform _rect;
        Sprite[] _expressions;
        Sprite _eyesClosed;
        int _expressionIndex;
        float _baseY;
        float _nextBlinkTime;
        bool _blinking;
        Coroutine _squash;
        GameObject _bubble;
        Text _bubbleText;
        Coroutine _bubbleRoutine;

        bool _isCustom;

        /// <summary>
        /// 下端中央アンカーで立ち絵を生成する。
        /// customSprite を渡すとスキンのカスタムキャラ (表情切替・まばたき・セリフなし) になる。
        /// </summary>
        public static MascotView Create(Transform parent, Vector2 size, float baseY, Sprite customSprite = null)
        {
            var go = new GameObject("Mascot");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MascotView>();
            view.Build(size, baseY, customSprite);
            return view;
        }

        void Build(Vector2 size, float baseY, Sprite customSprite)
        {
            if (customSprite != null)
            {
                _isCustom = true;
                _expressions = new[] { customSprite };
                _eyesClosed = null;
            }
            else
            {
                _expressions = new Sprite[ExpressionPaths.Length];
                for (int i = 0; i < ExpressionPaths.Length; i++)
                {
                    _expressions[i] = UiFactory.LoadSprite(ExpressionPaths[i]);
                }
                _eyesClosed = UiFactory.LoadSprite("Art/Mascot/yukari_expr_eyes_closed");
            }
            _nextBlinkTime = Time.time + Random.Range(2f, 4f);

            _image = gameObject.AddComponent<Image>();
            _image.sprite = _expressions[0];
            _image.preserveAspect = true;

            _rect = _image.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _baseY = baseY;
            _rect.anchoredPosition = new Vector2(0f, _baseY);
            _rect.sizeDelta = size;

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
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;

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
            // セリフの長さに合わせて吹き出しの横幅を調整する (左右の余白 + テキスト幅)
            float width = Mathf.Clamp(_bubbleText.preferredWidth + 68f + 45f + 14f, 250f, 640f);
            rect.sizeDelta = new Vector2(width, 190f);
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
            // ふわふわ浮遊 (静止画でも生きてる感を出す)
            float y = _baseY + Mathf.Sin(Time.time * 1.6f) * 12f;
            var p = _rect.anchoredPosition;
            _rect.anchoredPosition = new Vector2(p.x, y);

            // 通常表情のときだけランダム間隔でまばたき
            if (_expressionIndex == 0 && !_blinking && _eyesClosed != null && Time.time >= _nextBlinkTime)
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
            _expressionIndex = (_expressionIndex + 1) % _expressions.Length;
            if (_expressions[_expressionIndex] != null)
            {
                _image.sprite = _expressions[_expressionIndex];
            }
            Se.Play(Se.Tap);
            if (_squash != null)
            {
                StopCoroutine(_squash);
            }
            _squash = StartCoroutine(SquashRoutine());
            // スキンにセリフがあればそれを、無ければデフォルト (カスタムキャラはセリフなし)
            string[] lines = (CustomLines != null && CustomLines.Length > 0)
                ? CustomLines
                : (_isCustom ? null : SpeechLines);
            if (lines != null)
            {
                Say(lines[Random.Range(0, lines.Length)]);
            }
            OnTapped?.Invoke();
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
        }

        /// <summary>一瞬目を閉じて戻す。</summary>
        IEnumerator BlinkRoutine()
        {
            _blinking = true;
            _image.sprite = _eyesClosed;
            yield return new WaitForSeconds(0.12f);
            if (_expressionIndex == 0)
            {
                _image.sprite = _expressions[0];
            }
            _blinking = false;
            _nextBlinkTime = Time.time + Random.Range(2.5f, 6f);
        }
    }
}
