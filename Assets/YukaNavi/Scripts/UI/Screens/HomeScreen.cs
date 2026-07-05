using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YukaNavi.Api;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// ホーム画面。背景 + ロゴ + ゆかりちゃん + 下部メニュー。
    /// 表示時に capabilities を取得して接続状態を出す。
    /// </summary>
    public class HomeScreen : ScreenBase
    {
        Text _statusText;

        public override void BuildUi()
        {
            // 背景 (ゆかりなし版に立ち絵を重ねる)
            var bg = UiFactory.CreateImage(transform, "Background", "Art/Backgrounds/yukanavi_home_background_no_character_1080x1920");
            UiFactory.StretchFull(bg.rectTransform);

            // マスコット (グローバルナビバーの上に立つ)
            MascotView.Create(transform, new Vector2(740f, 1110f), GlobalNav.BarHeight + 20f);

            // ロゴ (上端中央、1800x520 と同比率)
            var logo = UiFactory.CreateImage(transform, "Logo", "Art/UI/yukanavi_logo");
            logo.preserveAspect = true;
            var logoRect = logo.rectTransform;
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 1f);
            logoRect.pivot = new Vector2(0.5f, 1f);
            logoRect.anchoredPosition = new Vector2(0f, -40f);
            logoRect.sizeDelta = new Vector2(560f, 162f);

            // 接続状態テキスト (ロゴの下)
            _statusText = UiFactory.CreateText(transform, "Status", "", 30, UiFactory.PrimaryDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -215f);
            statusRect.sizeDelta = new Vector2(-40f, 44f);

            // 画面下部の操作はグローバルナビバー (GlobalNav) が担当する
        }

        public override void OnShow()
        {
            _ = RefreshStatusAsync();
        }

        void ShowStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        async Task RefreshStatusAsync()
        {
            ShowStatus("接続確認中... (" + AppConfig.ServerUrl + ")");
            try
            {
                var caps = await AppConfig.CreateClient().GetCapabilitiesAsync();
                string playerName = caps.Player.Mode switch
                {
                    1 => "MPC-BE",
                    2 => "foobar2000",
                    3 => "自動",
                    _ => "その他",
                };
                ShowStatus("接続OK: " + AppConfig.ServerUrl + " (プレイヤー: " + playerName + ")");
            }
            catch (System.Exception e)
            {
                ShowStatus("未接続: " + e.Message);
            }
        }
    }
}
