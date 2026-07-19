using UnityEngine;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>
    /// モバイル実機用の自前カーソル描画。UiFactory.CreateInputField が取り付ける。
    ///
    /// Android/iOS の既定 (OS キーボード側で編集) では Unity はカーソルを描画しない
    /// (InputField.UpdateGeometry がモバイルでは早期 return する仕様) が、
    /// caretPosition 自体は TouchScreenKeyboard.selection と毎フレーム同期されている。
    /// そこで caretPosition に追従する点滅カーソルを自前で重ねることで、
    /// 「タップでのカーソル移動 (OS 編集)」と「位置の見える化」を両立する。
    /// PC・エディタでは Unity 標準のカーソルが描画されるため何もしない。
    /// </summary>
    public class MobileCaret : MonoBehaviour
    {
        const float BlinkInterval = 0.55f;

        InputField _input;
        RectTransform _caret;
        Image _caretImage;
        int _lastCaret = -1;
        float _blinkStart;

        void Awake()
        {
            _input = GetComponent<InputField>();
        }

        void LateUpdate()
        {
            if (!Application.isMobilePlatform)
            {
                return;
            }
            if (_input == null || !_input.isFocused)
            {
                if (_caret != null)
                {
                    _caret.gameObject.SetActive(false);
                }
                _lastCaret = -1;
                return;
            }
            EnsureCaret();
            PositionCaret();
            // 動かした直後は必ず見せて、そこから点滅する
            bool visible = Mathf.Repeat(Time.unscaledTime - _blinkStart, BlinkInterval * 2f)
                < BlinkInterval;
            _caret.gameObject.SetActive(true);
            _caretImage.enabled = visible;
        }

        void EnsureCaret()
        {
            if (_caret != null)
            {
                return;
            }
            var go = new GameObject("MobileCaret");
            go.transform.SetParent(_input.textComponent.rectTransform, false);
            _caretImage = go.AddComponent<Image>();
            _caretImage.color = _input.customCaretColor ? _input.caretColor : Color.black;
            _caretImage.raycastTarget = false;
            _caret = (RectTransform)go.transform;
            _caret.anchorMin = _caret.anchorMax = new Vector2(0.5f, 0.5f);
            _caret.pivot = new Vector2(0f, 1f); // カーソル上端を文字の上端に合わせる
        }

        void PositionCaret()
        {
            var text = _input.textComponent;
            var gen = text.cachedTextGenerator;
            float scale = text.pixelsPerUnit > 0f ? text.pixelsPerUnit : 1f;
            float height = text.fontSize * 1.2f;
            Vector2 pos;
            if (gen.characterCount == 0)
            {
                // 空のとき: アラインメントに合わせた行頭位置
                var rect = text.rectTransform.rect;
                bool upper = text.alignment == TextAnchor.UpperLeft
                    || text.alignment == TextAnchor.UpperCenter
                    || text.alignment == TextAnchor.UpperRight;
                pos = new Vector2(rect.xMin, upper ? rect.yMax : rect.center.y + height / 2f);
            }
            else
            {
                // caretPosition は全文のインデックス。長文で表示が切れている場合は
                // 表示中の文字列範囲にクランプする (欄に収まる長さなら正確)
                int index = Mathf.Clamp(_input.caretPosition, 0, gen.characterCount - 1);
                if (index != _lastCaret)
                {
                    _lastCaret = index;
                    _blinkStart = Time.unscaledTime;
                }
                pos = gen.characters[index].cursorPos / scale;
                int line = FindLine(gen, index);
                if (line >= 0 && line < gen.lineCount)
                {
                    height = gen.lines[line].height / scale;
                }
            }
            _caret.localPosition = new Vector3(pos.x, pos.y, 0f);
            _caret.sizeDelta = new Vector2(Mathf.Max(3f, _input.caretWidth), height);
        }

        static int FindLine(TextGenerator gen, int charIndex)
        {
            for (int i = gen.lineCount - 1; i >= 0; i--)
            {
                if (gen.lines[i].startCharIdx <= charIndex)
                {
                    return i;
                }
            }
            return 0;
        }
    }
}
