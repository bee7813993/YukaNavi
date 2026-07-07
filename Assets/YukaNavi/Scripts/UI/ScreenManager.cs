using System.Collections.Generic;
using UnityEngine;

namespace YukaNavi.UI
{
    /// <summary>
    /// 単一シーン + パネル切替の画面管理。画面履歴を持ち「戻る」に対応する。
    /// 各画面は ScreenBase を継承し、BuildUi() で自分の UI をコードで組み立てる。
    /// </summary>
    public class ScreenManager
    {
        readonly Transform _screenLayer;
        readonly Dictionary<System.Type, ScreenBase> _screens = new Dictionary<System.Type, ScreenBase>();
        readonly Stack<ScreenBase> _history = new Stack<ScreenBase>();
        ScreenBase _current;

        /// <summary>表示中の画面 (ナビの「戻る」が画面内履歴を問い合わせるのに使う)。</summary>
        public ScreenBase Current
        {
            get { return _current; }
        }

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

        /// <summary>画面を切り替える (現在の画面を履歴に積む)。</summary>
        public void Show<T>() where T : ScreenBase
        {
            ShowInternal(_screens[typeof(T)], true);
        }

        /// <summary>履歴をクリアして画面を表示する (ホームへの移動用)。</summary>
        public void ShowAsRoot<T>() where T : ScreenBase
        {
            _history.Clear();
            ShowInternal(_screens[typeof(T)], false);
        }

        /// <summary>ひとつ前の画面へ戻る。履歴が無ければ false。</summary>
        public bool Back()
        {
            if (_history.Count == 0)
            {
                return false;
            }
            ShowInternal(_history.Pop(), false);
            return true;
        }

        /// <summary>
        /// 全画面の UI を作り直す (テーマ色の変更時)。表示中の画面は OnHide → 再構築 → OnShow される。
        /// 呼び出し後、呼び出し元が持っていた UI 参照は無効になるので何も触らないこと。
        /// </summary>
        public void RebuildAll()
        {
            foreach (var screen in _screens.Values)
            {
                bool wasActive = screen.gameObject.activeSelf;
                if (wasActive)
                {
                    screen.OnHide();
                }
                screen.OnRebuild();
                for (int i = screen.transform.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(screen.transform.GetChild(i).gameObject);
                }
                // Register 時と同じくアクティブ状態で BuildUi する
                // (非アクティブだと GetComponentInChildren が子を見つけられない)
                if (!wasActive)
                {
                    screen.gameObject.SetActive(true);
                }
                screen.BuildUi();
                if (wasActive)
                {
                    screen.OnShow();
                }
                else
                {
                    screen.gameObject.SetActive(false);
                }
            }
        }

        void ShowInternal(ScreenBase next, bool pushHistory)
        {
            if (_current == next)
            {
                return;
            }
            if (pushHistory && _current != null)
            {
                _history.Push(_current);
            }
            if (_current != null)
            {
                _current.OnHide();
                // 背景として残す画面 (ホーム) は非表示にせず、他画面が半透明背景で透かして見せる
                if (!_current.KeepVisibleInBackground)
                {
                    _current.gameObject.SetActive(false);
                }
            }
            _current = next;
            next.gameObject.SetActive(true);
            next.transform.SetAsLastSibling(); // 背景に残っている画面より前面に出す
            next.OnShow();
        }
    }

    /// <summary>1画面 = 1パネル。ScreenManager.Register で生成される。</summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        public ScreenManager Manager { get; set; }

        /// <summary>true なら他画面の背後に表示したまま残す (壁紙を透かして見せるホーム用)。</summary>
        public virtual bool KeepVisibleInBackground
        {
            get { return false; }
        }

        /// <summary>
        /// ナビの「戻る」が押されたとき、画面遷移より先に呼ばれる。
        /// 画面内の階層・検索履歴を1つ戻したら true を返す (true の間は画面遷移しない)。
        /// </summary>
        public virtual bool OnBackRequested()
        {
            return false;
        }

        /// <summary>UI を組み立てる (Register 時と RebuildAll 時に呼ばれる)。</summary>
        public abstract void BuildUi();

        /// <summary>RebuildAll で子が破棄される直前に呼ばれる。子と一緒に消えない自前リソースを片付ける。</summary>
        public virtual void OnRebuild() { }

        /// <summary>表示された直後に呼ばれる。</summary>
        public virtual void OnShow() { }

        /// <summary>非表示になる直前に呼ばれる。</summary>
        public virtual void OnHide() { }
    }
}
