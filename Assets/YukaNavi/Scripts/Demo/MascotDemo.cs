using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.Demo
{
    /// <summary>
    /// M0 検証用のマスコット表示デモ。シーン編集不要で、再生開始時にコードで UI を組み立てる。
    /// - ホーム背景 + ロゴ + ゆかりちゃん立ち絵 (ふわふわ浮遊)
    /// - マスコットをタップで表情切替 (通常→笑顔→ウィンク→驚き) + タップ音 + スクイーズ
    /// - 起動時に capabilities を取得して接続状態を表示
    /// - ホーム BGM をループ再生
    /// M1 で正式なホーム画面に置き換える予定。
    /// </summary>
    public class MascotDemo : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("YukaNaviMascotDemo");
            go.AddComponent<MascotDemo>();
            DontDestroyOnLoad(go);
        }

        // 表情の切替順 (Resources 配下のパス)
        static readonly string[] ExpressionPaths =
        {
            "Art/Mascot/yukari_mascot_transparent",
            "Art/Mascot/yukari_expr_smile",
            "Art/Mascot/yukari_expr_wink",
            "Art/Mascot/yukari_expr_surprised",
        };

        Image _mascotImage;
        RectTransform _mascotRect;
        Text _statusText;
        AudioSource _seSource;
        AudioSource _bgmSource;
        AudioClip _tapSe;
        Sprite[] _expressions;
        int _expressionIndex;
        float _baseY;
        Coroutine _squash;

        void Start()
        {
            BuildUi();
            StartBgm();
            _ = UpdateConnectionStatusAsync();
        }

        void Update()
        {
            // ふわふわ浮遊 (静止画でも生きてる感を出す)
            if (_mascotRect != null)
            {
                float y = _baseY + Mathf.Sin(Time.time * 1.6f) * 12f;
                var p = _mascotRect.anchoredPosition;
                _mascotRect.anchoredPosition = new Vector2(p.x, y);
            }
        }

        void BuildUi()
        {
            // EventSystem (プロジェクトは新 Input System のみのため InputSystemUIInputModule を使う)
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // Canvas (基準解像度 1080x1920 の縦持ち想定)
            var canvasGo = new GameObject("DemoCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 背景 (画面全体)
            var bg = CreateImage(canvasGo.transform, "Background", "Art/Backgrounds/yukanavi_home_background_1080x1920");
            var bgRect = bg.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // マスコット (下端中央、1024x1536 と同比率)
            _expressions = new Sprite[ExpressionPaths.Length];
            for (int i = 0; i < ExpressionPaths.Length; i++)
            {
                _expressions[i] = LoadSprite(ExpressionPaths[i]);
            }
            _mascotImage = CreateImage(canvasGo.transform, "Mascot", null);
            _mascotImage.sprite = _expressions[0];
            _mascotImage.preserveAspect = true;
            _mascotRect = _mascotImage.rectTransform;
            _mascotRect.anchorMin = _mascotRect.anchorMax = new Vector2(0.5f, 0f);
            _mascotRect.pivot = new Vector2(0.5f, 0f);
            _baseY = 30f;
            _mascotRect.anchoredPosition = new Vector2(0f, _baseY);
            _mascotRect.sizeDelta = new Vector2(760f, 1140f);
            var button = _mascotImage.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnMascotTapped);

            // ロゴ (上端中央、1800x520 と同比率)
            var logo = CreateImage(canvasGo.transform, "Logo", "Art/UI/yukanavi_logo");
            logo.preserveAspect = true;
            var logoRect = logo.rectTransform;
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 1f);
            logoRect.pivot = new Vector2(0.5f, 1f);
            logoRect.anchoredPosition = new Vector2(0f, -50f);
            logoRect.sizeDelta = new Vector2(620f, 179f);

            // 接続状態テキスト (ロゴの下)
            var textGo = new GameObject("Status");
            textGo.transform.SetParent(canvasGo.transform, false);
            _statusText = textGo.AddComponent<Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.fontSize = 32;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = new Color(0.30f, 0.24f, 0.45f);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -240f);
            statusRect.sizeDelta = new Vector2(0f, 48f);

            // オーディオ
            _seSource = gameObject.AddComponent<AudioSource>();
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _tapSe = Resources.Load<AudioClip>("Audio/SE/yukanavi_tap");
        }

        void StartBgm()
        {
            var clip = Resources.Load<AudioClip>("Audio/BGM/yukanavi_home_loop");
            if (clip == null)
            {
                return;
            }
            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            _bgmSource.volume = 0.4f;
            _bgmSource.Play();
        }

        void OnMascotTapped()
        {
            _expressionIndex = (_expressionIndex + 1) % _expressions.Length;
            if (_expressions[_expressionIndex] != null)
            {
                _mascotImage.sprite = _expressions[_expressionIndex];
            }
            if (_tapSe != null)
            {
                _seSource.PlayOneShot(_tapSe);
            }
            if (_squash != null)
            {
                StopCoroutine(_squash);
            }
            _squash = StartCoroutine(SquashRoutine());
        }

        /// <summary>タップ時のスクイーズ (ぷにっと潰れて戻る)。</summary>
        IEnumerator SquashRoutine()
        {
            const float duration = 0.14f;
            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                float k = Mathf.Sin(e / duration * Mathf.PI); // 0→1→0
                _mascotRect.localScale = new Vector3(1f + 0.06f * k, 1f - 0.08f * k, 1f);
                yield return null;
            }
            _mascotRect.localScale = Vector3.one;
        }

        async Task UpdateConnectionStatusAsync()
        {
            SetStatus("接続確認中... (" + AppConfig.ServerUrl + ")");
            try
            {
                var caps = await new ApiClient(AppConfig.ServerUrl).GetCapabilitiesAsync();
                string playerName = caps.Player.Mode switch
                {
                    1 => "MPC-BE",
                    2 => "foobar2000",
                    3 => "自動",
                    _ => "その他",
                };
                SetStatus("接続OK: " + AppConfig.ServerUrl + " (プレイヤー: " + playerName + ")");
            }
            catch (System.Exception e)
            {
                SetStatus("未接続: " + e.Message);
            }
        }

        void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        Image CreateImage(Transform parent, string name, string texturePath)
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

        /// <summary>インポート設定 (spriteMode) に依存しないよう Texture2D から Sprite を生成する。</summary>
        static Sprite LoadSprite(string path)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogError("[YukaNavi] テクスチャが見つかりません: Resources/" + path);
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
