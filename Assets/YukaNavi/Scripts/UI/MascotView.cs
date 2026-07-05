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

        public System.Action OnTapped;

        Image _image;
        RectTransform _rect;
        Sprite[] _expressions;
        Sprite _eyesClosed;
        int _expressionIndex;
        float _baseY;
        float _nextBlinkTime;
        bool _blinking;
        Coroutine _squash;

        /// <summary>下端中央アンカーで立ち絵を生成する。</summary>
        public static MascotView Create(Transform parent, Vector2 size, float baseY)
        {
            var go = new GameObject("Mascot");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MascotView>();
            view.Build(size, baseY);
            return view;
        }

        void Build(Vector2 size, float baseY)
        {
            _expressions = new Sprite[ExpressionPaths.Length];
            for (int i = 0; i < ExpressionPaths.Length; i++)
            {
                _expressions[i] = UiFactory.LoadSprite(ExpressionPaths[i]);
            }
            _eyesClosed = UiFactory.LoadSprite("Art/Mascot/yukari_expr_eyes_closed");
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
