using System.Collections.Generic;
using UnityEngine;

namespace YukaNavi.UI
{
    /// <summary>
    /// 単一シーン + パネル切替の画面管理。
    /// 各画面は ScreenBase を継承し、BuildUi() で自分の UI をコードで組み立てる。
    /// </summary>
    public class ScreenManager
    {
        readonly Transform _screenLayer;
        readonly Dictionary<System.Type, ScreenBase> _screens = new Dictionary<System.Type, ScreenBase>();
        ScreenBase _current;

        public ScreenManager(Transform screenLayer)
        {
            _screenLayer = screenLayer;
        }

        /// <summary>画面を生成して登録する (非表示状態で待機)。</summary>
        public T Register<T>() where T : ScreenBase
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(_screenLayer, false);
            var rect = go.AddComponent<RectTransform>();
            UiFactory.StretchFull(rect);
            var screen = go.AddComponent<T>();
            screen.Manager = this;
            screen.BuildUi();
            go.SetActive(false);
            _screens[typeof(T)] = screen;
            return screen;
        }

        /// <summary>画面を切り替える。</summary>
        public void Show<T>() where T : ScreenBase
        {
            var next = _screens[typeof(T)];
            if (_current == next)
            {
                return;
            }
            if (_current != null)
            {
                _current.OnHide();
                _current.gameObject.SetActive(false);
            }
            _current = next;
            next.gameObject.SetActive(true);
            next.OnShow();
        }
    }

    /// <summary>1画面 = 1パネル。ScreenManager.Register で生成される。</summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        public ScreenManager Manager { get; set; }

        /// <summary>UI を組み立てる (Register 時に1回だけ呼ばれる)。</summary>
        public abstract void BuildUi();

        /// <summary>表示された直後に呼ばれる。</summary>
        public virtual void OnShow() { }

        /// <summary>非表示になる直前に呼ばれる。</summary>
        public virtual void OnHide() { }
    }
}
