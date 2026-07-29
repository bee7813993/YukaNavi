using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace YukaNavi.UI
{
    /// <summary>
    /// Live2D Cubism モデルをホーム画面の uGUI (ScreenSpaceOverlay) に統合して表示するコンポーネント。
    ///
    /// ゆかナビの UI はすべて ScreenSpaceOverlay の Canvas で、MeshRenderer である Cubism モデルを
    /// 既存の描画順 (背景 → パーティクル → マスコット → 吹き出し) にそのまま挟むことができない。
    /// そのため、本体シーンから離した位置に専用カメラでモデルを描画し、RenderTexture 経由で
    /// RawImage に映す方式を取る (背景動画で RenderTexture を使っているのと同じ考え方)。
    /// 技術検証の経緯は Live2DSpike.cs のコメントを参照。
    ///
    /// Cubism SDK は再配布不可のためリポジトリに含まれない (.gitignore 済み) が、このクラスは
    /// Cubism の型を一切直接参照せず、コンポーネント名の文字列照合とリフレクションだけで
    /// 必要な情報 (描画メッシュ) を読む。そのため SDK 未導入の環境でも問題なくコンパイルできる
    /// (未導入時は GetComponent が常に null を返し、単に「準備できない」状態になるだけ)。
    ///
    /// 重要な Cubism 5-r.5 の仕様 (このクラスの実装が前提にしていること):
    /// - モデルの描画位置は「CubismRenderController の localPosition + 各 Drawable の
    ///   localPosition」だけで決まり、親 (祖先) の Transform は完全に無視される。
    ///   そのため隔離オフセットは modelPrefab の Instantiate 結果自身の localPosition に載せる
    /// - URP の描画パスはカメラの cullingMask/レイヤーを見ずに全 Game カメラで描画するため、
    ///   「隔離オフセットで視錐台の外に置く」ことでしか他カメラへの写り込みを防げない
    /// - 実行時は MeshRenderer に bounds が乗らない (MeshFilter はエディタ非再生時のみ付与される)
    ///   ため、自動フィットは各 CubismRenderer が持つ Mesh (リフレクションで取得) から範囲を合成する。
    ///   メッシュは実行時に構築されるため、生成直後は範囲が取れないことがあり、数フレーム待って
    ///   リトライする
    /// </summary>
    public class Live2DMascotRenderer : MonoBehaviour
    {
        // 本体シーンから離した位置に描画し、他カメラの視錐台に入らないようにする
        static readonly Vector3 StageOffset = new Vector3(1000f, 0f, 0f);
        const int MaxFitAttempts = 30; // 30 フレーム待っても範囲が取れなければ諦める

        RawImage _view;
        RenderTexture _texture;
        GameObject _stage; // シーンルート直下の隔離用コンテナ (Camera/Model はここに置く)
        GameObject _modelGo;
        Camera _camera;
        Coroutine _fitRoutine;

        /// <summary>モデルの描画範囲が確定し、カメラを合わせ終えたら true。</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// モデルを読み込んで表示を開始する。表示先はこのコンポーネントと同じ GameObject
        /// (RawImage を追加する) なので、呼び出し側は RectTransform で表示枠を用意しておくこと。
        /// textureWidth/Height は描画解像度 (既定はデフォルト立ち絵と同じ 1024x1536)。
        /// </summary>
        public void Load(GameObject modelPrefab, int textureWidth = 1024, int textureHeight = 1536)
        {
            Unload();
            if (modelPrefab == null)
            {
                return;
            }

            // Unity 6 の Render Graph API はカメラ出力先の RenderTexture に depth buffer を要求する
            _texture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "Live2DMascotRT",
            };

            // Camera と Model は UI (Canvas/RectTransform) の階層から意図的に切り離し、
            // 親を持たないシーンルート直下の GameObject に置く。UI 階層の祖先に非等倍スケールが
            // 入っていた場合、カメラの orthographicSize (ワールド単位) の計算とずれてしまうため、
            // 常にワールド原点基準で確実に計算できるここに隔離する
            // (RawImage だけは呼び出し側が用意した UI の RectTransform=this.transform に乗せる)
            _stage = new GameObject("Live2DStage");

            _modelGo = Instantiate(modelPrefab, _stage.transform);
            _modelGo.name = "Model";
            _modelGo.transform.localPosition = StageOffset; // 親ではなくモデル自身に載せる (仕様参照)

            var cameraGo = new GameObject("Live2DCamera");
            cameraGo.transform.SetParent(_stage.transform, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1f; // フィット前の仮値 (SDK サンプルシーン相当)
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透過
            _camera.cullingMask = ~0;
            _camera.targetTexture = _texture;
            _camera.transform.localPosition = StageOffset + new Vector3(0f, 0f, -10f);
            var camData = _camera.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.renderType = CameraRenderType.Base;
                camData.renderPostProcessing = false;
            }

            _view = gameObject.AddComponent<RawImage>();
            _view.texture = _texture;
            _view.raycastTarget = false;

            IsReady = false;
            _fitRoutine = StartCoroutine(FitCameraRoutine());
        }

        /// <summary>表示を止めてリソースを解放する。</summary>
        public void Unload()
        {
            if (_fitRoutine != null)
            {
                StopCoroutine(_fitRoutine);
                _fitRoutine = null;
            }
            if (_view != null)
            {
                Destroy(_view);
                _view = null;
            }
            if (_camera != null)
            {
                _camera.targetTexture = null;
                _camera = null; // GameObject 自体は _stage ごと破棄する
            }
            _modelGo = null; // 同上
            if (_stage != null)
            {
                Destroy(_stage);
                _stage = null;
            }
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
            }
            IsReady = false;
        }

        void OnDestroy()
        {
            Unload();
        }

        IEnumerator FitCameraRoutine()
        {
            for (int i = 0; i < MaxFitAttempts; i++)
            {
                yield return null;
                if (TryFitCamera())
                {
                    IsReady = true;
                    yield break;
                }
            }
            Debug.LogWarning("[YukaNavi] Live2D モデルの描画範囲が取得できませんでした ("
                + MaxFitAttempts + " フレーム待機後)");
        }

        /// <summary>
        /// モデルの実際の描画範囲にカメラを合わせる。範囲が取れなければ false
        /// (メッシュがまだ構築されていない。数フレーム後に再試行すれば取れることが多い)。
        /// </summary>
        bool TryFitCamera()
        {
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var meshRenderer in _modelGo.GetComponentsInChildren<MeshRenderer>())
            {
                // Cubism の型を直接参照せず、コンポーネント名の文字列照合で見つける
                var cubismRenderer = meshRenderer.GetComponent("CubismRenderer");
                if (cubismRenderer == null)
                {
                    continue;
                }
                var meshProperty = cubismRenderer.GetType()
                    .GetProperty("Mesh", BindingFlags.Public | BindingFlags.Instance);
                var mesh = meshProperty != null ? meshProperty.GetValue(cubismRenderer) as Mesh : null;
                if (mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }
                var b = mesh.bounds;
                b.center += _modelGo.transform.localPosition + meshRenderer.transform.localPosition;
                if (!hasBounds)
                {
                    bounds = b;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(b);
                }
            }
            if (!hasBounds || bounds.extents.y <= 0.0001f)
            {
                return false;
            }
            float aspect = (float)_texture.width / _texture.height;
            float halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * 1.05f;
            _camera.orthographicSize = halfHeight;
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            return true;
        }
    }
}
