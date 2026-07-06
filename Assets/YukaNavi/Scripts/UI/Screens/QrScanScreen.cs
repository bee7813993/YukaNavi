using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Core;
using ZXing;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace YukaNavi.UI
{
    /// <summary>
    /// QR コード読み取り画面。カメラ映像を ZXing.Net でデコードし、
    /// ゆかりの部屋 URL (http/https) を読み取ったら接続設定画面へ戻す。
    /// </summary>
    public class QrScanScreen : ScreenBase
    {
        /// <summary>直近で読み取った URL。ConnectScreen が OnShow で回収してクリアする。</summary>
        public static string LastScannedText;

        /// <summary>映像が来ない場合に諦めるまでの秒数 (仮想カメラ等が列挙される環境対策)</summary>
        const float CameraStartTimeoutSeconds = 6f;

        RawImage _preview;
        Text _statusText;
        WebCamTexture _camTexture;
        BarcodeReaderGeneric _reader;
        float _nextScanTime;
        float _cameraStartedAt;
        bool _orientationApplied;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "QRコードを読み取る");

            var caption = UiFactory.CreateText(transform, "Caption",
                "ゆかりの部屋の QR コードを写してください", 30, UiFactory.TextDark);
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -130f);
            captionRect.sizeDelta = new Vector2(-40f, 44f);

            // カメラプレビュー
            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(transform, false);
            _preview = previewGo.AddComponent<RawImage>();
            _preview.color = Color.white;
            var previewRect = _preview.rectTransform;
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 0.5f);
            previewRect.pivot = new Vector2(0.5f, 0.5f);
            previewRect.anchoredPosition = new Vector2(0f, 60f);
            previewRect.sizeDelta = new Vector2(920f, 920f);

            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 30f);
            statusRect.sizeDelta = new Vector2(-40f, 80f);
        }

        public override void OnShow()
        {
            LastScannedText = null;
            SetStatus("カメラを起動中...", false);
            StartCoroutine(StartCameraRoutine());
        }

        /// <summary>カメラ権限の応答を待ってからカメラを起動する。</summary>
        IEnumerator StartCameraRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                SetStatus("カメラの使用許可を待っています...", false);
                int state = 0; // 0=応答待ち 1=許可 2=拒否
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => state = 1;
                callbacks.PermissionDenied += _ => state = 2;
                Permission.RequestUserPermission(Permission.Camera, callbacks);
                while (state == 0)
                {
                    yield return null;
                }
                if (state == 2)
                {
                    SetStatus("カメラの使用が許可されませんでした。URL を手入力してください", true);
                    yield break;
                }
                SetStatus("カメラを起動中...", false);
            }
#endif
            StartCamera();
            yield break;
        }

        void StartCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                SetStatus("カメラが見つかりません。URL を手入力してください", true);
                return;
            }
            var names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
            {
                names[i] = devices[i].name;
            }
            Debug.Log("[YukaNavi] カメラデバイス: " + string.Join(", ", names));

            // 解像度は指定せずデバイスの既定に任せる (指定すると OBS 仮想カメラ等が拒否することがある)
            _camTexture = new WebCamTexture(devices[0].name);
            _camTexture.Play();
            _cameraStartedAt = Time.time;
            _orientationApplied = false;
            _preview.texture = _camTexture;

            _reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                },
            };
            _nextScanTime = Time.time + 1f;
        }

        public override void OnHide()
        {
            if (_camTexture != null)
            {
                _camTexture.Stop();
                Destroy(_camTexture);
                _camTexture = null;
            }
            _preview.texture = null;
        }

        void Update()
        {
            if (_camTexture == null || !_camTexture.isPlaying)
            {
                return;
            }
            // WebCamTexture は起動直後 16x16 のダミーサイズを返すため、実サイズになるまで待つ。
            // 仮想カメラ等で映像が一向に来ない場合はタイムアウトして案内を出す
            if (_camTexture.width < 100)
            {
                if (Time.time - _cameraStartedAt > CameraStartTimeoutSeconds)
                {
                    SetStatus("カメラの映像を取得できません。URL を手入力してください", true);
                    OnHide();
                }
                return;
            }
            if (!_orientationApplied)
            {
                ApplyPreviewOrientation();
            }
            if (Time.time < _nextScanTime)
            {
                return;
            }
            _nextScanTime = Time.time + 0.5f;
            TryDecode();
        }

        /// <summary>
        /// Android のカメラ映像はセンサー基準で回転して届くため、プレビューの回転・ミラー・
        /// アスペクト比を補正する (映像サイズ確定後に1回だけ)。
        /// </summary>
        void ApplyPreviewOrientation()
        {
            _orientationApplied = true;
            int angle = _camTexture.videoRotationAngle;
            _preview.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
            _preview.uvRect = _camTexture.videoVerticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);

            // 映像のアスペクト比を保ちつつ、回転後の見た目が 920x920 に収まるようにする
            const float box = 920f;
            float aspect = (float)_camTexture.width / _camTexture.height;
            bool quarterTurn = (angle % 180) != 0;
            // 画面上の見た目サイズ (90/270度回転時は縦横が入れ替わる)
            float dispW = quarterTurn ? box / aspect : box;
            float dispH = quarterTurn ? box : box / aspect;
            float scale = Mathf.Min(box / dispW, box / dispH, 1f);
            dispW *= scale;
            dispH *= scale;
            // sizeDelta は回転前の軸で指定する (90/270度回転時は幅と高さが入れ替わって見える)
            _preview.rectTransform.sizeDelta = quarterTurn
                ? new Vector2(dispH, dispW)
                : new Vector2(dispW, dispH);
        }

        void TryDecode()
        {
            var pixels = _camTexture.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                int o = i * 4;
                bytes[o] = pixels[i].r;
                bytes[o + 1] = pixels[i].g;
                bytes[o + 2] = pixels[i].b;
                bytes[o + 3] = pixels[i].a;
            }
            var source = new RGBLuminanceSource(bytes, _camTexture.width, _camTexture.height,
                RGBLuminanceSource.BitmapFormat.RGBA32);
            var result = _reader.Decode(source);
            if (result == null)
            {
                SetStatus("QRコードを探しています...", false);
                return;
            }
            string text = (result.Text ?? "").Trim();
            if (!text.StartsWith("http://") && !text.StartsWith("https://"))
            {
                SetStatus("ゆかりの QR ではないようです: " + text, true);
                return;
            }
            LastScannedText = text;
            Se.Play(Se.Confirm);
            Manager.Back();
        }

        void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UiFactory.Danger : UiFactory.TextDark;
        }
    }
}
