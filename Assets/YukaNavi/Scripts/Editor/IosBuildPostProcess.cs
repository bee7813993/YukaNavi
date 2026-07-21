#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// iOS ビルド後の Info.plist 調整。
    /// NativeCamera パッケージのビルド後処理が NSCameraUsageDescription を英語の汎用文
    /// ("The app requires access to the camera to take pictures or record videos with it.")
    /// で上書きするため、その後 (callbackOrder 10000) に具体的な利用目的へ差し戻す。
    /// 汎用文のままだと App Store 審査ガイドライン 5.1.1(ii) で却下される
    /// (2026-07-21 の却下で指摘)。あわせて、使っていないマイク・フォトライブラリの
    /// 利用目的キーが注入されていれば取り除く (未使用の権限文言も審査の指摘対象)。
    /// </summary>
    public static class IosBuildPostProcess
    {
        const string CameraUsage =
            "QRコードを読み取ってゆかりサーバーの接続先を設定するためにカメラを使用します。"
            + " The camera is used to scan a QR code to set up the connection to your Yukari server.";

        [PostProcessBuild(10000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetString("NSCameraUsageDescription", CameraUsage);
            // アプリはマイク・フォトライブラリを使わない (QR 読み取りのカメラのみ。
            // NativeCamera.TakePicture もカメラ権限だけで動く)
            root.values.Remove("NSMicrophoneUsageDescription");
            root.values.Remove("NSPhotoLibraryUsageDescription");
            plist.WriteToFile(plistPath);
            Debug.Log("[YukaNavi] Info.plist の利用目的文言を設定しました: " + plistPath);
        }
    }
}
#endif
