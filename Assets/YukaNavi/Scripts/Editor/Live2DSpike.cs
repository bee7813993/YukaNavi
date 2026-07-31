using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.UI;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// Live2D 表示を SDK 同梱のサンプルモデルで試すための検証メニュー。
    /// ゆかりちゃんのモデルが未制作の間、見た目・動き・負荷を確かめるのに使う。
    ///
    /// 本番の経路はこれとは別で、HomeScreen が Resources の "Live2D/yukari" を見つけたら
    /// MascotView が Live2D 版になる (MascotView.Create の live2dPrefab)。
    /// このメニューは既にできあがったマスコットの上にサンプルモデルを重ねるだけなので、
    /// 静止画版の呼吸アニメが裏で動いたままになる点だけ本番と異なる
    /// (本番は Live2D 版だと静止画向けの演出を止める)。
    ///
    /// 描画の仕組みは Live2DMascotRenderer を参照。Cubism の API を直接参照していないので、
    /// SDK 未導入の環境でもコンパイルは通る (実行時に「見つからない」で失敗するだけ)。
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
            // 隠すのは「色を透明にする」方法で行うこと。ホームの長押し移動 (HomeDraggable) と
            // タップ (Button) はこの Image で判定されるため、SetActive(false) や
            // enabled = false にすると Graphic が raycast 対象から外れて操作できなくなる
            var still = group.transform.Find("Mascot");
            if (still != null)
            {
                var stillImage = still.GetComponent<Image>();
                if (stillImage != null)
                {
                    stillImage.color = new Color(1f, 1f, 1f, 0f);
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
            // 待機モーションのクリップも渡す (SDK が motion3.json から生成した .anim)
            var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                System.IO.Path.GetDirectoryName(ModelPath).Replace("\\", "/") + "/motions/Idle.anim");
            var tapClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                System.IO.Path.GetDirectoryName(ModelPath).Replace("\\", "/") + "/motions/TapBody.anim");
            renderer.Load(prefab, idleClip, tapClip);

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
        /// モデルの描画範囲の内訳を出す (診断用)。
        /// カメラの自動フィットが全パーツのメッシュから範囲を合成しているため、
        /// 見えていないパーツ (不透明度 0 の表情差分など) が含まれていると
        /// 範囲が過大になり、モデルが小さく表示される。その切り分け用。
        /// </summary>
        [MenuItem("YukaNavi/Live2D/スパイク: モデルの描画範囲を診断")]
        public static void DiagnoseBounds()
        {
            var stage = GameObject.Find("Live2DStage");
            var model = stage != null ? stage.transform.Find("Model") : null;
            if (model == null)
            {
                Debug.LogError("[YukaNavi] スパイクが表示されていません");
                return;
            }

            // マスクとして使われているメッシュを集める。
            // マスク専用のメッシュ (他のパーツの切り抜きに使うもの) は画面に描画されないため、
            // 描画範囲の計算に含めるとモデルが実際より大きく見積もられる
            var maskSet = new HashSet<Object>();
            foreach (var meshRenderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                var drawable = meshRenderer.GetComponent("CubismDrawable");
                if (drawable == null)
                {
                    continue;
                }
                var masksProperty = drawable.GetType().GetProperty("Masks");
                var masks = masksProperty != null ? masksProperty.GetValue(drawable) as System.Array : null;
                if (masks == null)
                {
                    continue;
                }
                foreach (var mask in masks)
                {
                    if (mask is Object maskObject)
                    {
                        maskSet.Add(maskObject);
                    }
                }
            }

            var all = new Bounds();
            var opaqueOnly = new Bounds();   // 不透明度 > 0 のみ
            var nonMaskOnly = new Bounds();  // マスク用を除く
            var both = new Bounds();         // 両方の条件を満たすもの
            bool hasAll = false, hasOpaque = false, hasNonMask = false, hasBoth = false;
            int total = 0, opaqueCount = 0, nonMaskCount = 0, bothCount = 0;
            var parts = new List<(string name, Bounds b, float opacity, bool isMask)>();

            foreach (var meshRenderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                var cubismRenderer = meshRenderer.GetComponent("CubismRenderer");
                if (cubismRenderer == null)
                {
                    continue;
                }
                var type = cubismRenderer.GetType();
                var meshProperty = type.GetProperty("Mesh", BindingFlags.Public | BindingFlags.Instance);
                var mesh = meshProperty != null ? meshProperty.GetValue(cubismRenderer) as Mesh : null;
                if (mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }
                total++;
                // Opacity は internal なので NonPublic も対象にする
                float opacity = -1f;
                var opacityProperty = type.GetProperty("Opacity",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (opacityProperty != null && opacityProperty.PropertyType == typeof(float))
                {
                    opacity = (float)opacityProperty.GetValue(cubismRenderer);
                }
                // マスク判定は CubismDrawable ではなく描画側の参照先と突き合わせる
                bool isMask = maskSet.Contains(meshRenderer.GetComponent("CubismDrawable"));
                var b = mesh.bounds;
                parts.Add((meshRenderer.name, b, opacity, isMask));

                if (!hasAll) { all = b; hasAll = true; } else { all.Encapsulate(b); }
                bool isOpaque = opacity > 0.01f;
                if (isOpaque)
                {
                    opaqueCount++;
                    if (!hasOpaque) { opaqueOnly = b; hasOpaque = true; } else { opaqueOnly.Encapsulate(b); }
                }
                if (!isMask)
                {
                    nonMaskCount++;
                    if (!hasNonMask) { nonMaskOnly = b; hasNonMask = true; } else { nonMaskOnly.Encapsulate(b); }
                }
                if (isOpaque && !isMask)
                {
                    bothCount++;
                    if (!hasBoth) { both = b; hasBoth = true; } else { both.Encapsulate(b); }
                }
            }

            // 範囲を押し広げている犯人を見つけるため、面積の大きい順に並べる
            parts.Sort((x, y) => (y.b.size.x * y.b.size.y).CompareTo(x.b.size.x * x.b.size.y));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[YukaNavi] 描画範囲の診断 (パーツ {total} 個 / マスク参照 {maskSet.Count} 個)");
            sb.AppendLine($"  (A) 全パーツ          : size={all.size} center={all.center}");
            sb.AppendLine($"  (B) 不透明度>0 のみ    : size={opaqueOnly.size} center={opaqueOnly.center} ({opaqueCount} 個)");
            sb.AppendLine($"  (C) マスク用を除く     : size={nonMaskOnly.size} center={nonMaskOnly.center} ({nonMaskCount} 個)");
            sb.AppendLine($"  (D) B と C の両方      : size={both.size} center={both.center} ({bothCount} 個)");
            sb.AppendLine("  大きいパーツ 上位 12 件 (名前 / サイズ / 不透明度 / マスク用か):");
            for (int i = 0; i < Mathf.Min(12, parts.Count); i++)
            {
                var p = parts[i];
                sb.AppendLine($"    {p.name} / {p.b.size} / opacity={p.opacity:F2} / mask={p.isMask}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Live2D の動き (モーション・まばたき・物理) が実際に動いているかを調べる (診断用)。
        /// 見た目では判断しづらいので、Animator の再生状況とパラメータの変化量を数値で出す。
        /// </summary>
        [MenuItem("YukaNavi/Live2D/動作診断: モーション・まばたき・物理を確認 (再生中)")]
        public static void DiagnoseMotion()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[YukaNavi] 再生 (▶) 中に実行してください");
                return;
            }
            var stage = GameObject.Find("Live2DStage");
            var model = stage != null ? stage.transform.Find("Model") : null;
            if (model == null)
            {
                Debug.LogError("[YukaNavi] Live2D モデルが見つかりません (Live2D 表示中に実行してください)");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[YukaNavi] Live2D 動作診断");

            // モーション再生は CubismMotionController で行う (Animator の controller は
            // SDK が空のものしか作らないため使っていない。未設定で正常)
            var motionController = model.GetComponent("CubismMotionController");
            sb.AppendLine("  CubismMotionController: "
                + (motionController != null ? "あり (この経由で待機モーションを再生)" : "なし → 待機モーションは動きません"));

            // 各コントローラの有無 (Cubism の型は名前で照合)
            foreach (var name in new[] { "CubismUpdateController", "CubismEyeBlinkController",
                "CubismAutoEyeBlinkInput", "CubismPhysicsController", "CubismFadeController",
                "CubismExpressionController", "CubismRenderController" })
            {
                sb.AppendLine($"  {name}: " + (model.GetComponent(name) != null ? "あり" : "なし"));
            }

            // パラメータの実値 (動いていれば時間で変わる)。
            // CubismParameter.Value はプロパティではなくフィールドなので GetField で読む
            sb.AppendLine("  パラメータの現在値 (2回実行して変化していれば動いている):");
            int shown = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                // 待機モーションが動かす対象と、まばたきの確認用
                if (!(t.name.Contains("Angle") || t.name.Contains("Breath") || t.name.Contains("EyeLOpen")))
                {
                    continue;
                }
                var param = t.GetComponent("CubismParameter");
                if (param == null)
                {
                    continue;
                }
                var valueField = param.GetType().GetField("Value",
                    BindingFlags.Public | BindingFlags.Instance);
                if (valueField == null)
                {
                    continue;
                }
                sb.AppendLine($"    {t.name} = {valueField.GetValue(param)}");
                if (++shown >= 8)
                {
                    break;
                }
            }
            if (shown == 0)
            {
                sb.AppendLine("    (パラメータが見つかりませんでした)");
            }
            Debug.Log(sb.ToString());
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
                    stillImage.color = Color.white; // 静止画マスコットを戻す
                }
            }
        }
    }
}
