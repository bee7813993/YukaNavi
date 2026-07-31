using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace YukaNavi.UI
{
    /// <summary>
    /// Cubism の書き出し一式 (model3.json / moc3 / テクスチャ / physics3.json / motion3.json) が
    /// 置かれたフォルダから、実行時に Live2D モデルを組み立てる。
    ///
    /// 通常の Live2D モデルはエディタの Cubism インポータがプレハブに変換したものを Resources から
    /// 読むが、それはビルドに焼き込まれるためアプリを更新しないと差し替えられない。
    /// デフォルトテーマ拡張パック (<see cref="Core.DefaultThemePack"/>) で配信したモデルを
    /// 使えるようにするのがこのクラスの役割で、アプリのバージョンを上げずにモデルを追加・更新できる。
    ///
    /// **きせかえスキン (ユーザーが持ち込む zip) からは呼ばないこと。**
    /// 不特定多数のモデルを読み込めるアプリは Live2D の「拡張性アプリケーション」に該当し、
    /// 審査・契約と収益モデル (完全無料は原則対象外) が必要になる。
    /// 判断の経緯は docs/default-theme-pack.md の「Live2D モデルの配信」を参照。
    ///
    /// Cubism SDK は再配布不可でリポジトリに含まれないため、<see cref="Live2DMascotRenderer"/> と
    /// 同じく SDK の型を一切直接参照せず、型名の文字列とリフレクションだけで組み立てる
    /// (SDK 未導入の環境ではコンパイルは通り、Load() が null を返すだけになる)。
    ///
    /// SDK 側の守備範囲 (CubismModel3Json.ToModel が自前でやってくれること):
    /// - moc3 からのモデル生成、Drawable ごとのマテリアル・テクスチャ割り当て
    /// - physics3.json からの物理演算 (CubismPhysicsController)
    /// - EyeBlink / LipSync グループからの CubismEyeBlinkController などの付与
    /// - Animator の付与
    /// このクラスが補うのはモーション (motion3.json → AnimationClip) と、その再生に必須の
    /// フェード情報 (CubismFadeMotionList) の構築だけ。
    /// </summary>
    public static class Live2DRuntimeLoader
    {
        const string CubismAssembly = ", Live2D.Cubism";

        /// <summary>実行時に組み立てたモデル一式。使い終わったら <see cref="Dispose"/> すること。</summary>
        public class Model
        {
            /// <summary>モデルの GameObject。非アクティブな状態で返るので、配置後に有効化する。</summary>
            public GameObject GameObject;

            /// <summary>待機モーション (model3.json の Idle グループ)。無ければ null。</summary>
            public AnimationClip IdleClip;

            /// <summary>タップ時のモーション (model3.json の TapBody グループ)。無ければ null。</summary>
            public AnimationClip TapClip;

            /// <summary>
            /// 実行時に生成した非 GameObject のアセット (テクスチャ・AnimationClip・
            /// ScriptableObject)。GameObject を Destroy しても消えないので、ここで持って明示的に破棄する。
            /// </summary>
            internal readonly List<UnityEngine.Object> Assets = new List<UnityEngine.Object>();

            /// <summary>モデルと、実行時に生成したアセットをまとめて破棄する。</summary>
            public void Dispose()
            {
                if (GameObject != null)
                {
                    UnityEngine.Object.Destroy(GameObject);
                    GameObject = null;
                }
                foreach (var asset in Assets)
                {
                    if (asset != null)
                    {
                        UnityEngine.Object.Destroy(asset);
                    }
                }
                Assets.Clear();
                IdleClip = null;
                TapClip = null;
            }
        }

        /// <summary>
        /// model3.json のパスからモデルを組み立てる。読めなければ null
        /// (SDK 未導入、ファイルが無い、moc3 のバージョンが SDK より新しい、など)。
        /// 返る GameObject は非アクティブなので、配置してから有効化すること
        /// (アクティブなまま組み立てると、フェード情報を差し込む前に SDK のコンポーネントが
        ///  初期化されてしまい「CubismFadeMotionList doesn't set」になる)。
        /// </summary>
        public static Model Load(string model3JsonPath)
        {
            if (string.IsNullOrEmpty(model3JsonPath) || !File.Exists(model3JsonPath))
            {
                return null;
            }
            var model3JsonType = FindCubismType("Live2D.Cubism.Framework.Json.CubismModel3Json");
            if (model3JsonType == null)
            {
                Debug.LogWarning("[YukaNavi] Live2D: Cubism SDK が見つからないため実行時ロードできません");
                return null;
            }

            var model3Json = ParseModel3Json(model3JsonType, model3JsonPath);
            if (model3Json == null)
            {
                Debug.LogWarning("[YukaNavi] Live2D: model3.json を読めませんでした: " + model3JsonPath);
                return null;
            }

            // ToModel(bool shouldImportAsOriginalWorkflow)。もう一方のオーバーロードは
            // マテリアル/テクスチャの picker を取る 4 引数版なので、引数の型で区別する
            var toModel = model3JsonType.GetMethod("ToModel", new[] { typeof(bool) });
            var modelComponent = toModel != null
                ? toModel.Invoke(model3Json, new object[] { false }) as Component
                : null;
            if (modelComponent == null)
            {
                // moc3 のバージョンが Cubism Core の対応を超えている場合もここに来る
                // (SDK が「Please check Model's Moc version」を別途 Console に出す)
                Debug.LogWarning("[YukaNavi] Live2D: モデルを生成できませんでした: " + model3JsonPath);
                return null;
            }

            var result = new Model { GameObject = modelComponent.gameObject };
            result.GameObject.name = Path.GetFileNameWithoutExtension(model3JsonPath);
            result.GameObject.SetActive(false);
            CollectTextures(model3JsonType, model3Json, result);
            SetUpMotions(model3JsonType, model3Json, model3JsonPath, result);
            return result;
        }

        /// <summary>
        /// model3.json をパースする。SDK の LoadAtPath は「アセットの読み方」を差し替えられる
        /// ようになっており、既定では Resources から読もうとするので、ファイルから読む
        /// ハンドラを渡す (これが実行時ロードの入口)。
        /// </summary>
        static object ParseModel3Json(Type model3JsonType, string model3JsonPath)
        {
            var handlerType = model3JsonType.GetNestedType("LoadAssetAtPathHandler", BindingFlags.Public);
            if (handlerType == null)
            {
                return null;
            }
            var handlerMethod = typeof(Live2DRuntimeLoader).GetMethod(nameof(LoadAssetAtPath),
                BindingFlags.NonPublic | BindingFlags.Static);
            Delegate handler;
            try
            {
                handler = Delegate.CreateDelegate(handlerType, handlerMethod);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YukaNavi] Live2D: SDK のロードハンドラを作れませんでした: " + e.Message);
                return null;
            }
            var loadAtPath = model3JsonType.GetMethod("LoadAtPath",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), handlerType }, null);
            return loadAtPath != null ? loadAtPath.Invoke(null, new object[] { model3JsonPath, handler }) : null;
        }

        /// <summary>
        /// SDK から「このパスのアセットを読んで」と呼ばれるハンドラ。
        /// パスは model3.json のフォルダからの解決済み絶対パスで渡ってくる。
        /// </summary>
        static object LoadAssetAtPath(Type assetType, string assetPath)
        {
            try
            {
                if (assetType == typeof(byte[]))
                {
                    return File.ReadAllBytes(assetPath);
                }
                if (assetType == typeof(string))
                {
                    return File.ReadAllText(assetPath);
                }
                if (assetType == typeof(Texture2D))
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                    if (!texture.LoadImage(File.ReadAllBytes(assetPath)))
                    {
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }
                    texture.name = Path.GetFileNameWithoutExtension(assetPath);
                    // アトラスの端がにじまないようにする (Cubism のテクスチャは常にアトラス)
                    texture.wrapMode = TextureWrapMode.Clamp;
                    return texture;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YukaNavi] Live2D: " + assetPath + " を読めませんでした: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 実行時に生成されたテクスチャを破棄対象として控える。
        /// Textures プロパティは初回アクセスで読み込んでキャッシュする作りなので、
        /// ToModel の後に読めば同じインスタンスが返る。
        /// </summary>
        static void CollectTextures(Type model3JsonType, object model3Json, Model result)
        {
            var texturesProperty = model3JsonType.GetProperty("Textures",
                BindingFlags.Public | BindingFlags.Instance);
            var textures = texturesProperty != null
                ? texturesProperty.GetValue(model3Json) as Texture2D[]
                : null;
            if (textures == null)
            {
                return;
            }
            foreach (var texture in textures)
            {
                if (texture != null)
                {
                    result.Assets.Add(texture);
                }
            }
        }

        /// <summary>
        /// model3.json に登録されたモーションを AnimationClip に変換し、再生に必要な
        /// フェード情報 (CubismFadeMotionList) を組み立ててモデルに載せる。
        ///
        /// CubismMotionController は再生時に CubismFadeController が持つリストからクリップを
        /// 逆引きする作りで、リストに載っていないクリップを渡すと
        /// 「Not found motion from CubismFadeMotionList」になる。エディタのインポータが
        /// やっている登録を、ここで実行時にやり直している。
        /// </summary>
        static void SetUpMotions(Type model3JsonType, object model3Json, string model3JsonPath, Model result)
        {
            var motion3JsonType = FindCubismType("Live2D.Cubism.Framework.Json.CubismMotion3Json");
            var fadeDataType = FindCubismType("Live2D.Cubism.Framework.MotionFade.CubismFadeMotionData");
            var fadeListType = FindCubismType("Live2D.Cubism.Framework.MotionFade.CubismFadeMotionList");
            var fadeControllerType = FindCubismType("Live2D.Cubism.Framework.MotionFade.CubismFadeController");
            if (motion3JsonType == null || fadeDataType == null
                || fadeListType == null || fadeControllerType == null)
            {
                return;
            }
            var entries = ReadMotionEntries(model3JsonType, model3Json);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[YukaNavi] Live2D: model3.json にモーションの記載がありません: "
                    + model3JsonPath + " (Motions セクションが必要)");
                return;
            }

            var loadFrom = motion3JsonType.GetMethod("LoadFrom",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(bool) }, null);
            var toAnimationClip = FindToAnimationClip(motion3JsonType);
            var createFadeData = FindCreateFadeData(fadeDataType, motion3JsonType, model3JsonType);
            if (loadFrom == null || toAnimationClip == null || createFadeData == null)
            {
                Debug.LogWarning("[YukaNavi] Live2D: SDK のモーション変換 API が見つかりませんでした");
                return;
            }

            var folder = Path.GetDirectoryName(model3JsonPath);
            var instanceIds = new List<int>();
            var fadeDataList = new List<object>();
            foreach (var entry in entries)
            {
                var motionPath = Path.Combine(folder, entry.Value);
                if (!File.Exists(motionPath))
                {
                    Debug.LogWarning("[YukaNavi] Live2D: モーションが見つかりません: " + motionPath);
                    continue;
                }
                object motion3Json;
                AnimationClip clip;
                try
                {
                    motion3Json = loadFrom.Invoke(null, new object[] { File.ReadAllText(motionPath), true });
                    if (motion3Json == null)
                    {
                        continue;
                    }
                    clip = toAnimationClip.Invoke(motion3Json, new object[] { false, false, false, null })
                        as AnimationClip;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[YukaNavi] Live2D: " + motionPath + " を変換できませんでした: " + e.Message);
                    continue;
                }
                if (clip == null)
                {
                    continue;
                }
                // 実行時に生成したクリップは legacy になる (SDK が SetCurve のためにそうしている)。
                // CubismMotionController は Animator/Playables 経由で再生するため legacy では扱えない
                clip.legacy = false;
                clip.name = MotionAssetName(entry.Value);

                var fadeData = createFadeData.Invoke(null,
                    new object[] { motion3Json, clip.name, clip.length, false, true, model3Json });
                if (fadeData == null)
                {
                    continue;
                }
                // リストの逆引きキーは AnimationClip のインスタンス ID
                instanceIds.Add(clip.GetInstanceID());
                fadeDataList.Add(fadeData);
                result.Assets.Add(clip);
                result.Assets.Add(fadeData as UnityEngine.Object);

                if (result.IdleClip == null && entry.Key == "Idle")
                {
                    result.IdleClip = clip;
                }
                else if (result.TapClip == null && entry.Key == "TapBody")
                {
                    result.TapClip = clip;
                }
            }
            if (fadeDataList.Count == 0)
            {
                return;
            }

            var fadeObjects = Array.CreateInstance(fadeDataType, fadeDataList.Count);
            for (int i = 0; i < fadeDataList.Count; i++)
            {
                fadeObjects.SetValue(fadeDataList[i], i);
            }
            var fadeList = ScriptableObject.CreateInstance(fadeListType);
            SetField(fadeList, fadeListType, "MotionInstanceIds", instanceIds.ToArray());
            SetField(fadeList, fadeListType, "CubismFadeMotionObjects", fadeObjects);
            result.Assets.Add(fadeList);

            var go = result.GameObject;
            var fadeController = go.GetComponent(fadeControllerType) ?? go.AddComponent(fadeControllerType);
            SetField(fadeController, fadeControllerType, "CubismFadeMotionList", fadeList);
        }

        /// <summary>
        /// model3.json の FileReferences.Motions を「グループ名 → モーションファイル」の
        /// 並びとして読み出す。グループ名は Idle / TapBody を想定
        /// (art/mascot/live2d_parts/MODEL_REQUEST.md §6 で固定している)。
        /// </summary>
        static List<KeyValuePair<string, string>> ReadMotionEntries(Type model3JsonType, object model3Json)
        {
            var entries = new List<KeyValuePair<string, string>>();
            var fileReferences = GetFieldValue(model3Json, model3JsonType, "FileReferences");
            var motions = fileReferences != null
                ? GetFieldValue(fileReferences, fileReferences.GetType(), "Motions")
                : null;
            if (motions == null)
            {
                return entries;
            }
            var motionsType = motions.GetType();
            var groupNames = GetFieldValue(motions, motionsType, "GroupNames") as string[];
            var groups = GetFieldValue(motions, motionsType, "Motions") as Array;
            if (groupNames == null || groups == null)
            {
                return entries;
            }
            for (int i = 0; i < groupNames.Length && i < groups.Length; i++)
            {
                if (!(groups.GetValue(i) is Array group))
                {
                    continue;
                }
                for (int j = 0; j < group.Length; j++)
                {
                    var motion = group.GetValue(j);
                    var file = motion != null
                        ? GetFieldValue(motion, motion.GetType(), "File") as string
                        : null;
                    if (!string.IsNullOrEmpty(file))
                    {
                        entries.Add(new KeyValuePair<string, string>(groupNames[i], file));
                    }
                }
            }
            return entries;
        }

        /// <summary>
        /// ToAnimationClip(bool, bool, bool, CubismPose3Json) を探す。
        /// 既存のクリップに上書きする 5 引数版と区別するため、第 1 引数が bool のものを選ぶ。
        /// </summary>
        static MethodInfo FindToAnimationClip(Type motion3JsonType)
        {
            foreach (var method in motion3JsonType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "ToAnimationClip")
                {
                    continue;
                }
                var parameters = method.GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType == typeof(bool))
                {
                    return method;
                }
            }
            return null;
        }

        /// <summary>
        /// CubismFadeMotionData.CreateInstance(motion3Json, name, length, ..., model3Json) を探す。
        /// 既存インスタンスに書き戻す 7 引数版と区別するため、第 1 引数が motion3Json のものを選ぶ。
        /// </summary>
        static MethodInfo FindCreateFadeData(Type fadeDataType, Type motion3JsonType, Type model3JsonType)
        {
            return fadeDataType.GetMethod("CreateInstance",
                BindingFlags.Public | BindingFlags.Static, null,
                new[]
                {
                    motion3JsonType, typeof(string), typeof(float),
                    typeof(bool), typeof(bool), model3JsonType,
                }, null);
        }

        /// <summary>"Idle.motion3.json" → "Idle" (エディタが生成する .anim と同じ名前にする)。</summary>
        static string MotionAssetName(string motionFile)
        {
            var name = Path.GetFileName(motionFile);
            const string suffix = ".motion3.json";
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - suffix.Length)
                : Path.GetFileNameWithoutExtension(name);
        }

        static Type FindCubismType(string fullTypeName)
        {
            return Type.GetType(fullTypeName + CubismAssembly);
        }

        static object GetFieldValue(object target, Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(target) : null;
        }

        static void SetField(object target, Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
