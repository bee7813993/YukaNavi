using UnityEngine;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>
    /// 背景表示の共通部品。テクスチャのアスペクト比を保ったまま領域を覆い (cover)、
    /// はみ出しはクリップする。ズーム・オフセット・90度単位の回転に対応し、
    /// スキンの背景調整 (skin.json の rotation/zoom/offset) を再現する。
    /// </summary>
    public class BackgroundView : MonoBehaviour
    {
        RawImage _image;
        RectTransform _imageRect;
        float _texAspect = 9f / 16f;

        /// <summary>回転 (度、90単位を想定)</summary>
        public float Rotation { get; private set; }
        /// <summary>ズーム (1 = cover ちょうど)</summary>
        public float Zoom { get; private set; } = 1f;
        /// <summary>オフセット (領域の幅・高さに対する比率)</summary>
        public Vector2 OffsetRatio { get; private set; }

        public static BackgroundView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            go.AddComponent<RectMask2D>();
            var view = go.AddComponent<BackgroundView>();

            var imageGo = new GameObject("Image");
            imageGo.transform.SetParent(go.transform, false);
            view._image = imageGo.AddComponent<RawImage>();
            view._image.raycastTarget = false;
            view._imageRect = view._image.rectTransform;
            view._imageRect.anchorMin = view._imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            view._imageRect.pivot = new Vector2(0.5f, 0.5f);
            return view;
        }

        public void SetTexture(Texture texture, float aspect)
        {
            _image.texture = texture;
            _texAspect = aspect > 0f ? aspect : 9f / 16f;
            Relayout();
        }

        /// <summary>テクスチャ差し替えなしでアスペクト比だけ更新する (動画の実サイズ確定時)。</summary>
        public void SetAspect(float aspect)
        {
            if (aspect > 0f)
            {
                _texAspect = aspect;
                Relayout();
            }
        }

        public void SetAdjust(float rotation, float zoom, Vector2 offsetRatio)
        {
            Rotation = Mathf.Repeat(rotation, 360f);
            Zoom = Mathf.Clamp(zoom <= 0f ? 1f : zoom, 1f, 4f);
            OffsetRatio = offsetRatio;
            Relayout();
        }

        void OnRectTransformDimensionsChange()
        {
            if (_imageRect != null)
            {
                Relayout();
            }
        }

        /// <summary>領域サイズとテクスチャのアスペクト比から cover サイズを計算して適用する。</summary>
        public void Relayout()
        {
            var area = ((RectTransform)transform).rect;
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }
            // 90/270度回転時は見た目のアスペクト比が入れ替わる
            bool quarterTurn = Mathf.Repeat(Rotation, 180f) >= 45f && Mathf.Repeat(Rotation, 180f) < 135f;
            float effAspect = quarterTurn ? 1f / _texAspect : _texAspect;

            float dispW;
            float dispH;
            if (effAspect > area.width / area.height)
            {
                dispH = area.height;
                dispW = dispH * effAspect;
            }
            else
            {
                dispW = area.width;
                dispH = dispW / effAspect;
            }
            dispW *= Zoom;
            dispH *= Zoom;

            // sizeDelta は回転前の軸で指定する
            _imageRect.sizeDelta = quarterTurn ? new Vector2(dispH, dispW) : new Vector2(dispW, dispH);
            _imageRect.localEulerAngles = new Vector3(0f, 0f, -Rotation);
            _imageRect.anchoredPosition = new Vector2(OffsetRatio.x * area.width, OffsetRatio.y * area.height);
        }
    }
}
