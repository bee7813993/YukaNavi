using UnityEngine;
using ZXing;
using ZXing.QrCode;

namespace YukaNavi.Core
{
    /// <summary>
    /// QR コードの Texture2D 生成 (読み取りと同じ zxing.dll の生成 API を使用)。
    /// ダッシュボードの「部屋に接続」「アプリを入手」QR で使う。
    /// </summary>
    public static class QrCodeTexture
    {
        /// <summary>
        /// 文字列を QR コード化した正方形テクスチャを返す。呼び出し側が Destroy すること。
        /// </summary>
        public static Texture2D Create(string content, int size = 512)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = size,
                    Height = size,
                    Margin = 2, // クワイエットゾーン。読み取りに必要な白枠
                    ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                    CharacterSet = "UTF-8",
                },
            };
            var pixels = writer.Write(content);

            // PixelData は上から下、Texture2D は下から上に行が並ぶ。
            // 上下反転した QR は鏡像になり読み取れないため、行順を入れ替えてコピーする
            int stride = pixels.Width * 4;
            var flipped = new byte[pixels.Pixels.Length];
            for (int y = 0; y < pixels.Height; y++)
            {
                System.Array.Copy(pixels.Pixels, y * stride,
                    flipped, (pixels.Height - 1 - y) * stride, stride);
            }

            var tex = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(flipped);
            tex.filterMode = FilterMode.Point; // モジュールの縁をにじませない
            tex.Apply(false, true);
            return tex;
        }
    }
}
