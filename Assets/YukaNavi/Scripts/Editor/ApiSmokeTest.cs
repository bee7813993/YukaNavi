using UnityEditor;
using UnityEngine;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.EditorTools
{
    /// <summary>
    /// M0 疎通確認用のエディタメニュー。シーン不要で API を呼んで Console に結果を出す。
    /// 接続先は AppConfig.ServerUrl (既定 http://localhost/)。
    /// </summary>
    public static class ApiSmokeTest
    {
        [MenuItem("YukaNavi/API疎通テスト: capabilities")]
        public static async void TestCapabilities()
        {
            var client = new ApiClient(AppConfig.ServerUrl);
            Debug.Log($"[YukaNavi] capabilities 取得中... ({AppConfig.ServerUrl})");
            try
            {
                var caps = await client.GetCapabilitiesAsync();
                Debug.Log(
                    $"[YukaNavi] capabilities OK: player.mode={caps.Player.Mode} autoplay={caps.Player.Autoplay} " +
                    $"mypage={caps.Features.Mypage} secret={caps.Features.Secret} easyauth={caps.Features.Easyauth} " +
                    $"noname_username={caps.Request.NonameUsername}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[YukaNavi] capabilities 失敗: {e.Message}");
            }
        }

        [MenuItem("YukaNavi/API疎通テスト: search (キーワード「テスト」)")]
        public static async void TestSearch()
        {
            var client = new ApiClient(AppConfig.ServerUrl);
            Debug.Log($"[YukaNavi] search 実行中... ({AppConfig.ServerUrl})");
            try
            {
                var result = await client.SearchAsync("テスト");
                string head = (result.Items != null && result.Items.Count > 0)
                    ? $" 先頭: {result.Items[0].Name}"
                    : "";
                Debug.Log($"[YukaNavi] search OK: total={result.Total} count={result.Count}{head}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[YukaNavi] search 失敗: {e.Message}");
            }
        }
    }
}
