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
        Coroutine _tapRoutine;
        Component _motionController; // CubismMotionController (型を直接参照しないため Component で持つ)
        AnimationClip _idleClip;
        AnimationClip _tapClip;

        /// <summary>モデルの描画範囲が確定し、カメラを合わせ終えたら true。</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 呼吸をアプリ側で自動生成するか (既定 false)。Load() より前に設定する。
        ///
        /// false: 待機モーションに含まれる ParamBreath のカーブで呼吸する (制作側が意図した
        ///        タイミングになり、体の動きと同期する)。
        /// true : SDK の HarmonicMotion で ParamBreath をサイン波で揺らす。モーションに
        ///        呼吸が入っていないモデルでも呼吸させられる。
        /// **両方を有効にすると ParamBreath の取り合いになるので、モーション側に
        /// 呼吸が入っている場合は false のままにすること。**
        /// </summary>
        public bool UseAutoBreath { get; set; }

        /// <summary>自動呼吸の 1 周期の長さ (秒)。ゆっくりした呼吸で 3〜4 秒程度。</summary>
        public float AutoBreathDuration { get; set; } = 3.5f;

        /// <summary>
        /// モデルを読み込んで表示を開始する。表示先はこのコンポーネントと同じ GameObject
        /// (RawImage を追加する) なので、呼び出し側は RectTransform で表示枠を用意しておくこと。
        /// idleClip に待機モーション (Cubism が motion3.json から生成した .anim) を渡すと
        /// ループ再生する。tapClip はタップ時に PlayTapMotion() で再生する。
        /// textureWidth/Height は描画解像度 (既定はデフォルト立ち絵と同じ 1024x1536)。
        /// </summary>
        public void Load(GameObject modelPrefab, AnimationClip idleClip = null,
                         AnimationClip tapClip = null,
                         int textureWidth = 1024, int textureHeight = 1536)
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
            _idleClip = idleClip;
            _tapClip = tapClip;
            SetUpMotionAndBlink(_modelGo, idleClip);

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
            if (_tapRoutine != null)
            {
                StopCoroutine(_tapRoutine);
                _tapRoutine = null;
            }
            _motionController = null;
            _idleClip = null;
            _tapClip = null;
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
                if (TryFitCamera(opaqueOnly: true))
                {
                    IsReady = true;
                    yield break;
                }
            }
            // 不透明度が読めない SDK バージョンなどに備えて、最後は全パーツで合わせる
            // (見えないパーツも含むぶんモデルが小さめに表示されるが、表示はされる)
            if (TryFitCamera(opaqueOnly: false))
            {
                IsReady = true;
                Debug.LogWarning("[YukaNavi] Live2D: 表示中のパーツを判別できなかったため、"
                    + "全パーツで画角を合わせました (モデルが小さめに表示されます)");
                yield break;
            }
            Debug.LogWarning("[YukaNavi] Live2D モデルの描画範囲が取得できませんでした ("
                + MaxFitAttempts + " フレーム待機後)");
        }

        /// <summary>
        /// モデルの実際の描画範囲にカメラを合わせる。範囲が取れなければ false
        /// (メッシュがまだ構築されていない。数フレーム後に再試行すれば取れることが多い)。
        /// opaqueOnly=true のときは不透明度が 0 のパーツを除外する。
        /// Cubism のモデルは未使用の表情差分なども全てメッシュとして持っており、それらを
        /// 含めると範囲が実際の見た目の 2 倍以上になることがある (SDK サンプルの Mao で確認)。
        /// </summary>
        bool TryFitCamera(bool opaqueOnly)
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
                var type = cubismRenderer.GetType();
                if (opaqueOnly)
                {
                    float opacity = GetOpacity(cubismRenderer, type);
                    if (opacity < 0f)
                    {
                        return false; // 判別できないので、このモードは諦めてフォールバックに委ねる
                    }
                    if (opacity <= 0.01f)
                    {
                        continue; // 未使用の表情差分など、画面に出ていないパーツ
                    }
                }
                var meshProperty = type.GetProperty("Mesh", BindingFlags.Public | BindingFlags.Instance);
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

        /// <summary>
        /// モーション再生と自動まばたきを有効にする。
        ///
        /// Cubism SDK が model3.json のインポート時に付けてくれるのは「適用する側」だけで、
        /// 動かすための入力は付かない。具体的には:
        /// - CubismEyeBlinkController (まばたきを適用) は付くが、値を作る
        ///   CubismAutoEyeBlinkInput は付かない (= まばたきしない)
        /// - Animator は付くが、生成される AnimatorController は空
        ///   (DefaultState も Motions も無い) ため、そのままでは何も再生されない
        /// そのため、ここで両方を補う。モーションは AnimatorController を組み立てるのではなく、
        /// SDK の CubismMotionController でクリップを直接再生する (公式サンプルと同じ方式)。
        ///
        /// Cubism の型を直接書くと SDK 未導入の環境でコンパイルが通らなくなるため、
        /// 型名の文字列から解決して扱う (SDK が無ければ何もしない)。
        /// </summary>
        void SetUpMotionAndBlink(GameObject model, AnimationClip idleClip)
        {
            // 自動まばたき。CubismEyeBlinkController が付いているモデルにだけ意味がある
            AddCubismComponent(model, "Live2D.Cubism.Framework.CubismAutoEyeBlinkInput");

            if (UseAutoBreath)
            {
                SetUpAutoBreath(model);
            }

            if (idleClip == null)
            {
                return;
            }
            _motionController = AddCubismComponent(model,
                "Live2D.Cubism.Framework.Motion.CubismMotionController");
            PlayMotion(idleClip, loop: true, force: false);
        }

        /// <summary>
        /// タップ時のモーションを再生する (設定されていなければ何もしない)。
        /// 待機モーションより優先して割り込み、終わると待機に戻る。
        /// </summary>
        public void PlayTapMotion()
        {
            if (_tapClip == null || _motionController == null)
            {
                return;
            }
            PlayMotion(_tapClip, loop: false, force: true);
            if (_idleClip != null)
            {
                // 単発再生のあとは何も流れなくなるので、長さぶん待って待機に戻す
                if (_tapRoutine != null)
                {
                    StopCoroutine(_tapRoutine);
                }
                _tapRoutine = StartCoroutine(BackToIdleRoutine(_tapClip.length));
            }
        }

        IEnumerator BackToIdleRoutine(float tapLength)
        {
            yield return new WaitForSeconds(tapLength);
            PlayMotion(_idleClip, loop: true, force: true);
            _tapRoutine = null;
        }

        /// <summary>
        /// CubismMotionController でクリップを再生する。
        /// force=true は CubismMotionPriority.PriorityForce 相当で、再生中のモーションに割り込む。
        /// 優先度は SDK の定数を直接参照しないため数値で渡す
        /// (None=0 / Idle=1 / Normal=2 / Force=3)。
        /// </summary>
        void PlayMotion(AnimationClip clip, bool loop, bool force)
        {
            if (_motionController == null || clip == null)
            {
                return;
            }
            // PlayAnimation(clip, layerIndex, priority, isLoop, speed)
            var play = _motionController.GetType().GetMethod("PlayAnimation",
                BindingFlags.Public | BindingFlags.Instance);
            if (play == null)
            {
                return;
            }
            play.Invoke(_motionController, new object[] { clip, 0, force ? 3 : 2, loop, 1f });
        }

        /// <summary>
        /// SDK の HarmonicMotion で ParamBreath を自動的に揺らす (呼吸)。
        /// SDK にはまばたきのような専用の自動呼吸コンポーネントが無く、
        /// 「指定パラメータをサイン波で動かす」HarmonicMotion がその役割にあたる。
        /// パラメータを後から足すので、収集し直すため Refresh() を呼ぶ
        /// (SDK のコメントにも「追加・削除の後に呼ぶこと」と書かれている)。
        /// </summary>
        void SetUpAutoBreath(GameObject model)
        {
            const string ns = "Live2D.Cubism.Framework.HarmonicMotion.";
            var controller = AddCubismComponent(model, ns + "CubismHarmonicMotionController");
            if (controller == null)
            {
                return;
            }
            // ChannelTimescales は SDK の Reset() で初期化されるが、Reset は
            // エディタで手動追加したときにしか呼ばれない。実行時に AddComponent すると
            // null のままで、LateUpdate の Play(ChannelTimescales) が
            // NullReferenceException を投げ続けるので自分で用意する
            var controllerType = controller.GetType();
            var timescalesField = controllerType.GetField("ChannelTimescales",
                BindingFlags.Public | BindingFlags.Instance);
            if (timescalesField != null && timescalesField.GetValue(controller) == null)
            {
                // 本数は SDK の DefaultChannelCount に合わせる (private const なので読めなければ 1)
                int channelCount = 1;
                var countField = controllerType.GetField("DefaultChannelCount",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (countField != null && countField.FieldType == typeof(int))
                {
                    channelCount = Mathf.Max(1, (int)countField.GetValue(null));
                }
                var timescales = new float[channelCount];
                for (int i = 0; i < timescales.Length; i++)
                {
                    timescales[i] = 1f; // 等速
                }
                timescalesField.SetValue(controller, timescales);
            }
            // ParamBreath のパラメータオブジェクトを探して揺れの設定を付ける
            Component breathParameter = null;
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                if (t.name != "ParamBreath")
                {
                    continue;
                }
                breathParameter = AddCubismComponent(t.gameObject, ns + "CubismHarmonicMotionParameter");
                break;
            }
            if (breathParameter == null)
            {
                Debug.LogWarning("[YukaNavi] Live2D: ParamBreath が無いため自動呼吸を設定できませんでした");
                return;
            }

            var paramType = breathParameter.GetType();
            SetField(breathParameter, paramType, "Channel", 0);
            SetField(breathParameter, paramType, "Duration", AutoBreathDuration);
            // 原点を中心に往復させる (Centric = 2)。呼吸は吸って吐いての往復なのでこれが合う
            var directionField = paramType.GetField("Direction", BindingFlags.Public | BindingFlags.Instance);
            if (directionField != null && directionField.FieldType.IsEnum)
            {
                directionField.SetValue(breathParameter, System.Enum.ToObject(directionField.FieldType, 2));
            }
            // ParamBreath は 0〜1。中心 0.5 で振幅いっぱいに動かす
            SetField(breathParameter, paramType, "NormalizedOrigin", 0.5f);
            SetField(breathParameter, paramType, "NormalizedRange", 0.5f);

            var refresh = controller.GetType().GetMethod("Refresh",
                BindingFlags.Public | BindingFlags.Instance);
            if (refresh != null)
            {
                refresh.Invoke(controller, null);
            }
        }

        static void SetField(object target, System.Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        /// <summary>
        /// Cubism のコンポーネントを型名から取得する (無ければ追加する)。
        /// SDK 未導入や名前変更で型が見つからないときは null を返す。
        /// </summary>
        static Component AddCubismComponent(GameObject target, string fullTypeName)
        {
            var type = System.Type.GetType(fullTypeName + ", Live2D.Cubism");
            if (type == null)
            {
                return null;
            }
            var existing = target.GetComponent(type);
            return existing != null ? existing : target.AddComponent(type);
        }

        /// <summary>
        /// パーツの不透明度。読めなかったときは -1 (SDK の変更などで判別不能)。
        /// CubismRenderer.Opacity は internal のためリフレクションで読む。
        /// </summary>
        static float GetOpacity(Component cubismRenderer, System.Type type)
        {
            var opacityProperty = type.GetProperty("Opacity",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (opacityProperty == null || opacityProperty.PropertyType != typeof(float))
            {
                return -1f;
            }
            return (float)opacityProperty.GetValue(cubismRenderer);
        }
    }
}
