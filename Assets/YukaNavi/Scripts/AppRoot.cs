using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using YukaNavi.Core;
using YukaNavi.UI;

namespace YukaNavi
{
    /// <summary>
    /// アプリの起動点。再生開始時に Canvas / EventSystem / BGM / 画面群をコードで構築する。
    /// 初回起動 (未設定) は接続設定画面、設定済みならホーム画面から始まる。
    /// </summary>
    public class AppRoot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<AppRoot>() != null)
            {
                return;
            }
            var go = new GameObject("YukaNaviApp");
            go.AddComponent<AppRoot>();
            DontDestroyOnLoad(go);
        }

        ScreenManager _screens;
        AudioSource _bgmSource;

        void Start()
        {
            // EventSystem (プロジェクトは新 Input System のみのため InputSystemUIInputModule を使う)
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // Canvas (基準解像度 1080x1920 の縦持ち想定)
            var canvasGo = new GameObject("AppCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // SE / BGM
            var seSource = gameObject.AddComponent<AudioSource>();
            Se.Init(seSource);
            _bgmSource = gameObject.AddComponent<AudioSource>();
            var bgmClip = Resources.Load<AudioClip>("Audio/BGM/yukanavi_home_loop");
            if (bgmClip != null)
            {
                _bgmSource.clip = bgmClip;
                _bgmSource.loop = true;
                _bgmSource.volume = 0.35f;
                _bgmSource.Play();
            }

            // 画面登録と初期画面
            _screens = new ScreenManager(canvasGo.transform);
            _screens.Register<HomeScreen>();
            _screens.Register<ConnectScreen>();

            if (AppConfig.IsConfigured)
            {
                _screens.Show<HomeScreen>();
            }
            else
            {
                _screens.Show<ConnectScreen>();
            }
        }
    }
}
