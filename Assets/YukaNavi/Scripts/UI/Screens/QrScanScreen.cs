using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
        /// <summary>デジタルズーム倍率 (映像の中央 1/_zoom を表示・デコードする)</summary>
        float _zoom = 1f;
        Slider _zoomSlider;
        Text _zoomLabel;
        /// <summary>ピンチ操作の前フレームの2本指間距離 (0 = ピンチ中でない)</summary>
        float _pinchLastDist;
        string _deviceName;
        /// <summary>高解像度要求が拒否されて既定解像度で再試行したか</summary>
        bool _triedDefaultRes;

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.PanelBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "QRコードを読み取る");

            // カメラプレビュー (できるだけ大きく = トップバー下からズーム行の上まで)
            var areaGo = new GameObject("PreviewArea");
            areaGo.transform.SetParent(transform, false);
            var area = areaGo.AddComponent<RectTransform>();
            UiFactory.StretchFull(area);
            area.offsetMax = new Vector2(0f, -120f); // トップバーの下
            area.offsetMin = new Vector2(0f, GlobalNav.BarHeight + 210f); // ズーム行 + 案内の上

            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(areaGo.transform, false);
            _preview = previewGo.AddComponent<RawImage>();
            _preview.color = Color.white;
            var previewRect = _preview.rectTransform;
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 0.5f);
            previewRect.pivot = new Vector2(0.5f, 0.5f);
            previewRect.anchoredPosition = Vector2.zero;
            previewRect.sizeDelta = new Vector2(1040f, 1040f);

            // デジタルズーム (席から遠い TV に映した QR 用。中央を切り出して拡大する)
            var zoomRow = UiFactory.CreatePanel(transform, "ZoomRow");
            zoomRow.anchorMin = new Vector2(0f, 0f);
            zoomRow.anchorMax = new Vector2(1f, 0f);
            zoomRow.pivot = new Vector2(0.5f, 0f);
            zoomRow.anchoredPosition = new Vector2(0f, GlobalNav.BarHeight + 120f);
            zoomRow.offsetMin = new Vector2(40f, zoomRow.offsetMin.y);
            zoomRow.offsetMax = new Vector2(-40f, zoomRow.offsetMax.y);
            zoomRow.sizeDelta = new Vector2(zoomRow.sizeDelta.x, 70f);

            _zoomLabel = UiFactory.CreateText(zoomRow, "Label", "拡大 1.0x", 26,
                UiFactory.TextDark, TextAnchor.MiddleLeft);
            var zoomLabelRect = _zoomLabel.rectTransform;
            zoomLabelRect.anchorMin = new Vector2(0f, 0f);
            zoomLabelRect.anchorMax = new Vector2(0f, 1f);
            zoomLabelRect.pivot = new Vector2(0f, 0.5f);
            zoomLabelRect.anchoredPosition = Vector2.zero;
            zoomLabelRect.sizeDelta = new Vector2(220f, 0f);

            _zoomSlider = UiFactory.CreateSlider(zoomRow, "Zoom", 10f, 50f); // 1.0x〜5.0x
            var sliderRect = _zoomSlider.GetComponent<RectTransform>();
            UiFactory.StretchFull(sliderRect);
            sliderRect.offsetMin = new Vector2(240f, 10f);
            sliderRect.offsetMax = new Vector2(-10f, -10f);
            _zoomSlider.value = 10f;
            _zoomSlider.onValueChanged.AddListener(value =>
            {
                _zoom = value / 10f;
                _zoomLabel.text = "拡大 " + _zoom.ToString("0.0") + "x";
                ApplyPreviewUv();
            });

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
            if (_zoomSlider != null)
            {
                _zoomSlider.value = 10f; // ズームを 1.0x に戻す
            }
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

            _deviceName = devices[0].name;
            _triedDefaultRes = false;
            StartCameraTexture(true);

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
            UpdatePinchZoom();
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
                    if (!_triedDefaultRes)
                    {
                        // 高解像度の要求を拒否する環境 (OBS 仮想カメラ等) は既定解像度で再試行
                        _triedDefaultRes = true;
                        _camTexture.Stop();
                        Destroy(_camTexture);
                        StartCameraTexture(false);
                        return;
                    }
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

        /// <summary>カメラテクスチャを起動する (高解像度優先、拒否環境は既定で再試行)。</summary>
        void StartCameraTexture(bool highRes)
        {
            // 遠くの TV に映した QR も読めるよう高解像度を要求する
            // (デジタルズームの切り出しに十分な画素が要る)
            _camTexture = highRes
                ? new WebCamTexture(_deviceName, 1920, 1080, 30)
                : new WebCamTexture(_deviceName);
            _camTexture.Play();
            _cameraStartedAt = Time.time;
            _orientationApplied = false;
            _preview.texture = _camTexture;
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
            ApplyPreviewUv();

            // 映像のアスペクト比を保ちつつ、回転後の見た目がプレビュー領域に収まるようにする
            var areaRect = ((RectTransform)_preview.rectTransform.parent).rect;
            float texAspect = (float)_camTexture.width / _camTexture.height;
            bool quarterTurn = (angle % 180) != 0;
            float dispAspect = quarterTurn ? 1f / texAspect : texAspect; // 画面上の縦横比
            float fitW = areaRect.width;
            float fitH = fitW / dispAspect;
            if (fitH > areaRect.height)
            {
                fitH = areaRect.height;
                fitW = fitH * dispAspect;
            }
            // sizeDelta は回転前の軸で指定する (90/270度回転時は幅と高さが入れ替わって見える)
            _preview.rectTransform.sizeDelta = quarterTurn
                ? new Vector2(fitH, fitW)
                : new Vector2(fitW, fitH);
        }

        /// <summary>2本指のピンチイン/アウトでズームを変える (スライダーと連動)。</summary>
        void UpdatePinchZoom()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null || _zoomSlider == null)
            {
                return;
            }
            int active = 0;
            Vector2 p0 = default, p1 = default;
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.isPressed)
                {
                    continue;
                }
                if (active == 0)
                {
                    p0 = touch.position.ReadValue();
                }
                else if (active == 1)
                {
                    p1 = touch.position.ReadValue();
                }
                active++;
            }
            if (active < 2)
            {
                _pinchLastDist = 0f;
                return;
            }
            float dist = Vector2.Distance(p0, p1);
            if (_pinchLastDist > 0f)
            {
                // 画面幅ぶん指を広げたときスライダー全域 (1.0x→3.0x) 動く感度
                float delta = (dist - _pinchLastDist) / Screen.width
                    * (_zoomSlider.maxValue - _zoomSlider.minValue);
                _zoomSlider.value += delta; // 表示・uvRect の更新は onValueChanged が行う
            }
            _pinchLastDist = dist;
        }

        /// <summary>
        /// 映像とデコード対象を中央 1/_zoom に切り出す (デジタルズーム)。
        /// 遠くの QR がフレーム内で相対的に大きくなり、読み取りやすくなる。
        /// </summary>
        void ApplyPreviewUv()
        {
            float size = 1f / _zoom;
            float inset = (1f - size) * 0.5f;
            bool mirrored = _camTexture != null && _camTexture.videoVerticallyMirrored;
            _preview.uvRect = mirrored
                ? new Rect(inset, 1f - inset, size, -size)
                : new Rect(inset, inset, size, size);
        }

        void TryDecode()
        {
            var pixels = _camTexture.GetPixels32();
            int fullW = _camTexture.width;
            int fullH = _camTexture.height;
            // ズーム中は中央だけを切り出してデコードする (表示と同じ範囲)
            int cropW = Mathf.Clamp(Mathf.RoundToInt(fullW / _zoom), 1, fullW);
            int cropH = Mathf.Clamp(Mathf.RoundToInt(fullH / _zoom), 1, fullH);
            int x0 = (fullW - cropW) / 2;
            int y0 = (fullH - cropH) / 2;
            var bytes = new byte[cropW * cropH * 4];
            for (int y = 0; y < cropH; y++)
            {
                int src = (y0 + y) * fullW + x0;
                int dst = y * cropW * 4;
                for (int x = 0; x < cropW; x++)
                {
                    var p = pixels[src + x];
                    int o = dst + x * 4;
                    bytes[o] = p.r;
                    bytes[o + 1] = p.g;
                    bytes[o + 2] = p.b;
                    bytes[o + 3] = p.a;
                }
            }
            var source = new RGBLuminanceSource(bytes, cropW, cropH,
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
