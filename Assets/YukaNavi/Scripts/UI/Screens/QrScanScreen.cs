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

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            var topBar = UiFactory.CreatePanel(transform, "TopBar", UiFactory.Primary);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 110f);
            var title = UiFactory.CreateText(topBar, "Title", "QRコードを読み取る", 42, Color.white);
            UiFactory.StretchFull(title.rectTransform);

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

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
            }
#endif
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
            if (Time.time < _nextScanTime)
            {
                return;
            }
            _nextScanTime = Time.time + 0.5f;
            TryDecode();
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
