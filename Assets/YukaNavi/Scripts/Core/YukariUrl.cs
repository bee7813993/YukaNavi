namespace YukaNavi.Core
{
    /// <summary>
    /// ゆかりサーバー URL の整形。Web 版の共有 URL や別部屋 URL 設定に付く
    /// ?easypass=XXXX (かんたん認証キーワード) にも対応する。
    /// </summary>
    public static class YukariUrl
    {
        /// <summary>
        /// URL の体裁を整える (http:// 前置、末尾 / 補完)。
        /// クエリ・フラグメントは除去し、easypass が付いていれば取り出して返す (無ければ null)。
        /// </summary>
        public static string Normalize(string raw, out string easypass)
        {
            easypass = null;
            string url = (raw ?? "").Trim();
            if (url == "")
            {
                return "";
            }
            // 数字だけならポート番号とみなし、ゆかりのポート公開サービス (ykr.moe) の URL を生成する
            if (url.Length >= 2 && url.Length <= 5 && IsAllDigits(url))
            {
                return "http://ykr.moe:" + url + "/";
            }
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                url = url.Substring(0, hash);
            }
            int q = url.IndexOf('?');
            if (q >= 0)
            {
                string query = url.Substring(q + 1);
                url = url.Substring(0, q);
                foreach (var pair in query.Split('&'))
                {
                    var kv = pair.Split(new[] { '=' }, 2);
                    if (kv.Length == 2 && kv[0] == "easypass" && kv[1] != "")
                    {
                        easypass = System.Uri.UnescapeDataString(kv[1]);
                    }
                }
            }
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "http://" + url;
            }
            if (!url.EndsWith("/"))
            {
                url += "/";
            }
            return url;
        }

        static bool IsAllDigits(string s)
        {
            foreach (char c in s)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
