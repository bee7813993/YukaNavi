using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.UI;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// Live2D 統合の検証・実験メニュー。
    /// 実体は Live2DMascotRenderer (Assets/YukaNavi/Scripts/UI/) で、ここはその薄いテストハーネス。
    ///
    /// ゆかナビの UI はすべて ScreenSpaceOverlay の Canvas で、Overlay は常に最前面に描かれる。
    /// そのため MeshRenderer である Live2D モデルを既存の描画順
    /// (背景 → パーティクル → マスコット → 吹き出し) にそのまま挟むことができない。
    /// Live2DMascotRenderer は専用カメラ + RenderTexture 経由でこれを解決する
    /// (背景動画で RenderTexture を使っているのと同じ考え方。動作は実機で検証済み)。
    ///
    /// Live2DMascotRenderer は Cubism の API を一切直接参照していないので、
    /// このファイルも含めて SDK 未導入の環境でコンパイルは通る (実行時に「見つからない」で
    /// 失敗するだけ)。
    /// </summary>
    public static class Live2DSpike
    {
        // SDK 同梱のサンプルモデル (Samples は Resources 配下ではないためパス指定で読む)
        const string ModelPath = "Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab";
        const string RootName = "__Live2DSpike";

        [MenuItem("YukaNavi/Live2D/スパイク: サンプルモデルをマスコット位置に表示 (再生中)")]
        public static void Show()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) を開始し、ホーム画面を表示してから実行してください");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
            {
                Debug.LogError("[YukaNavi] サンプルモデルが見つかりません: " + ModelPath
                    + "\nLive2D Cubism SDK を導入してください (README の「3. Live2D Cubism SDK の導入」)");
                return;
            }
            var group = GameObject.Find("MascotGroup");
            if (group == null)
            {
                Debug.LogError("[YukaNavi] MascotGroup が見つかりません。ホーム画面を表示した状態で実行してください"
                    + " (きせかえでマスコットを非表示にしている場合は表示に戻してください)");
                return;
            }
            Cleanup();

            // 既存の静止画マスコットを隠し、同じ枠に Live2D 表示を重ねる
            // (MascotGroup の子なので、ホームの移動・拡縮の設定がそのまま効く)。
            // GameObject 自体は有効のままにすること: ホームの長押し移動を検出する
            // HomeDraggable がこの GameObject に付いている (HomeScreen.SetupMovable の
            // eventTarget) ため、SetActive(false) にすると移動できなくなる
            var still = group.transform.Find("Mascot");
            if (still != null)
            {
                var stillImage = still.GetComponent<Image>();
                if (stillImage != null)
                {
                    stillImage.enabled = false;
                }
            }

            var viewGo = new GameObject(RootName);
            viewGo.transform.SetParent(group.transform, false);
            var rect = viewGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var renderer = viewGo.AddComponent<Live2DMascotRenderer>();
            renderer.Load(prefab);

            Debug.Log("[YukaNavi] Live2D スパイクを表示しました (カメラは自動でフィットします)。\n"
                + "確認する点: (1) 背景の上にモデルが重なって見えるか (2) 周囲が透過して抜けているか "
                + "(3) まばたき等の自動モーションが動くか (4) 描画が重くないか");
        }

        /// <summary>
        /// RenderTexture の中身を PNG に書き出して、実際に何が描かれているかを確かめる (診断用)。
        /// Inspector の Camera Preview は cameraType が Preview のため Cubism が描画されず
        /// (CubismRenderPassFeature が Game / SceneView 以外を弾く)、プレビューでは判断できない。
        /// </summary>
        [MenuItem("YukaNavi/Live2D/スパイク: RenderTexture を PNG に保存 (診断用)")]
        public static void DumpRenderTexture()
        {
            var camera = FindCamera(out var error);
            if (camera == null)
            {
                Debug.LogError(error);
                return;
            }
            var rt = camera.targetTexture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // 「描かれているのに透明」なのか「そもそも描かれていない」のかを数値で切り分ける
            var pixels = tex.GetPixels32();
            int opaque = 0;
            int colored = 0;
            foreach (var p in pixels)
            {
                if (p.a > 10) { opaque++; }
                if (p.r > 10 || p.g > 10 || p.b > 10) { colored++; }
            }

            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "live2d_spike_rt.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            float total = pixels.Length;
            Debug.Log($"[YukaNavi] RenderTexture を保存しました: {path}\n"
                + $"アルファのあるピクセル: {opaque}/{total} ({100f * opaque / total:F1}%)  "
                + $"色のあるピクセル: {colored}/{total} ({100f * colored / total:F1}%)\n"
                + "→ 色があるのにアルファが 0 なら「描画はできているがアルファが抜けている」、"
                + "どちらも 0 なら「カメラが描画していない」");
        }

        /// <summary>
        /// カメラの背景を不透明マゼンタにする (診断用)。
        /// RawImage にマゼンタの矩形が出れば、カメラ → RenderTexture → RawImage の経路は生きている。
        /// </summary>
        [MenuItem("YukaNavi/Live2D/スパイク: 背景を不透明マゼンタに切替 (診断用)")]
        public static void ToggleOpaqueBackground()
        {
            var camera = FindCamera(out var error);
            if (camera == null)
            {
                Debug.LogError(error);
                return;
            }
            bool toMagenta = camera.backgroundColor.a < 0.5f;
            camera.backgroundColor = toMagenta ? new Color(1f, 0f, 1f, 1f) : new Color(0f, 0f, 0f, 0f);
            Debug.Log("[YukaNavi] カメラ背景を " + (toMagenta ? "不透明マゼンタ" : "透明") + " にしました");
        }

        static Camera FindCamera(out string error)
        {
            var group = GameObject.Find("MascotGroup");
            var view = group != null ? group.transform.Find(RootName) : null;
            var camera = view != null ? view.GetComponentInChildren<Camera>() : null;
            if (camera == null || camera.targetTexture == null)
            {
                error = "[YukaNavi] スパイクが表示されていません (先に「サンプルモデルを表示」を実行してください)";
                return null;
            }
            error = null;
            return camera;
        }

        [MenuItem("YukaNavi/Live2D/スパイク: 片付ける")]
        public static void Cleanup()
        {
            var group = GameObject.Find("MascotGroup");
            if (group == null)
            {
                return;
            }
            var view = group.transform.Find(RootName);
            if (view != null)
            {
                Object.Destroy(view.gameObject); // Live2DMascotRenderer.OnDestroy がリソースを解放する
            }
            var still = group.transform.Find("Mascot");
            if (still != null)
            {
                var stillImage = still.GetComponent<Image>();
                if (stillImage != null)
                {
                    stillImage.enabled = true; // 静止画マスコットを戻す
                }
            }
        }
    }
}
