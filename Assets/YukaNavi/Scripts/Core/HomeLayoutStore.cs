using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// ホーム画面パーツ (時計/メッセージ/マスコット) の配置の読み書き。
    /// カスタムスキンでは skin.json の layout に保存し、スキン切替・共有 (zip) にも配置が付いて回る。
    /// 組み込みデフォルトスキンでは端末 (PlayerPrefs) に保存する。
    /// </summary>
    public static class HomeLayoutStore
    {
        public const string Clock = "clock";
        public const string Ticker = "ticker";
        public const string Mascot = "mascot";

        public const float MinScale = 0.5f;
        public const float MaxScale = 2f;

        /// <summary>パーツの初期位置 (HomeScreen の座標系)。</summary>
        public static Vector2 DefaultPos(string key)
        {
            // 上部の白帯 (110) + 設定/音ボタン (〜-218) と被らないように配置する
            switch (key)
            {
                case Clock:
                    return new Vector2(-44f, -430f);
                case Mascot:
                    return new Vector2(0f, 160f); // GlobalNav.BarHeight + 20
                default:
                    return new Vector2(0f, -240f); // ticker
            }
        }

        /// <summary>保存済みの配置を取得する (未設定なら null = 初期配置)。</summary>
        public static SkinLayoutItem Load(SkinDef skin, string key)
        {
            if (skin.Folder != null)
            {
                return SkinManager.GetLayoutItem(skin, key);
            }
            if (!PlayerPrefs.HasKey(PrefKey(key, "x")) && !PlayerPrefs.HasKey(PrefKey(key, "visible")))
            {
                return null;
            }
            var def = DefaultPos(key);
            return new SkinLayoutItem
            {
                Visible = PlayerPrefs.GetInt(PrefKey(key, "visible"), 1) == 1,
                X = PlayerPrefs.GetFloat(PrefKey(key, "x"), def.x),
                Y = PlayerPrefs.GetFloat(PrefKey(key, "y"), def.y),
                Scale = PlayerPrefs.GetFloat(PrefKey(key, "scale"), 1f),
            };
        }

        public static void Save(SkinDef skin, string key, SkinLayoutItem item)
        {
            if (skin.Folder != null)
            {
                SkinManager.SaveLayoutItem(skin, key, item);
                return;
            }
            PlayerPrefs.SetInt(PrefKey(key, "visible"), item.Visible ? 1 : 0);
            PlayerPrefs.SetFloat(PrefKey(key, "x"), item.X);
            PlayerPrefs.SetFloat(PrefKey(key, "y"), item.Y);
            PlayerPrefs.SetFloat(PrefKey(key, "scale"), item.Scale);
            PlayerPrefs.Save();
        }

        public static bool GetVisible(SkinDef skin, string key)
        {
            var item = Load(skin, key);
            return item == null || item.Visible;
        }

        /// <summary>表示/非表示のみ変える (位置未保存なら初期配置で保存する)。</summary>
        public static void SetVisible(SkinDef skin, string key, bool visible)
        {
            var item = Load(skin, key) ?? NewDefaultItem(key);
            item.Visible = visible;
            Save(skin, key, item);
        }

        /// <summary>全パーツを初期配置 (表示・等倍) に戻す。</summary>
        public static void ResetAll(SkinDef skin)
        {
            foreach (var key in new[] { Clock, Ticker, Mascot })
            {
                Save(skin, key, NewDefaultItem(key));
            }
        }

        static SkinLayoutItem NewDefaultItem(string key)
        {
            var def = DefaultPos(key);
            return new SkinLayoutItem { Visible = true, X = def.x, Y = def.y, Scale = 1f };
        }

        static string PrefKey(string key, string field)
        {
            return "home." + key + "." + field;
        }
    }
}
