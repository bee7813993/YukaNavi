using UnityEngine;
using YukaNavi.UI;

namespace YukaNavi.Core
{
    /// <summary>
    /// スキンのテーマ色を UiFactory へ適用する。UI は生成時に色を焼き込むため、
    /// 色が変わったときは呼び出し側が画面の再構築 (ScreenManager.RebuildAll 等) を行う。
    /// </summary>
    public static class YukariTheme
    {
        static string _appliedHex; // null = 既定 (紫)

        /// <summary>スキンのテーマ色を適用する。色が変わったら true を返す。</summary>
        public static bool ApplyFromSkin(SkinDef skin)
        {
            string hex = (skin != null && skin.Theme != null) ? skin.Theme.Primary : null;
            if (string.IsNullOrEmpty(hex))
            {
                hex = null;
            }
            if (hex == _appliedHex)
            {
                return false;
            }
            _appliedHex = hex;
            if (hex == null || !ColorUtility.TryParseHtmlString(hex, out var primary))
            {
                UiFactory.ResetThemeColors();
            }
            else
            {
                UiFactory.SetThemeColors(primary);
            }
            return true;
        }
    }
}
