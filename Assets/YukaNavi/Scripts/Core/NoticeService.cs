using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// 機材係 (ゆかりの運用者) 提供コンテンツの取得と保持。
    ///
    /// ゆかりの「リクエスト一覧(トップ)画面表示メッセージ」(HTML 記述可) の中に
    /// class="yukanavi-notice" を付けた要素があるときだけ、それをアプリ向けの
    /// お知らせとして表示する (メニュー・ホームに導線が出る)。
    /// 表示メッセージは API に公開されていないため、Web 版のリクエスト一覧ページ
    /// (requestlist_only.php。新旧どちらのサーバーにもある) の HTML から抽出する。
    /// #yukarihost# の置換はサーバー側でページ描画時に済んでいる。
    /// </summary>
    public static class NoticeService
    {
        /// <summary>アプリに表示する要素へ機材係が付けるマーカー class。</summary>
        public const string MarkerClass = "yukanavi-notice";

        /// <summary>これより新しい取得結果があれば再取得しない (画面を開いたときは force で無視)。</summary>
        const float RefreshIntervalSeconds = 300f;

        static List<NoticeHtml.Block> _blocks = new List<NoticeHtml.Block>();
        static string _signature = "";
        static string _server;        // 取得したサーバー (これが変わったら取り直す)
        static float _fetchedAt = -1f; // Time.realtimeSinceStartup。-1 = 未取得
        static Task _inflight;

        /// <summary>お知らせの有無・内容が変わったときに発火する (メニュー・ホームが購読)。</summary>
        public static event System.Action Changed;

        /// <summary>機材係のお知らせがあるか (= マーカー付き要素が見つかったか)。</summary>
        public static bool HasNotice
        {
            get { return _blocks.Count > 0; }
        }

        /// <summary>
        /// 表示するブロック列。内容が変わったときはリストごと差し替わるため、
        /// 参照比較で再描画の要否を判定できる。
        /// </summary>
        public static List<NoticeHtml.Block> Blocks
        {
            get { return _blocks; }
        }

        /// <summary>
        /// お知らせを取得する (取得済みで新しければ何もしない)。失敗しても例外は投げず、
        /// 前回の内容を保つ (次回の再取得で追い付く)。多重呼び出しは同じ取得を待つ。
        /// </summary>
        public static Task RefreshAsync(bool force = false)
        {
            if (_inflight != null && !_inflight.IsCompleted)
            {
                return _inflight;
            }
            if (!AppConfig.IsConfigured)
            {
                return Task.CompletedTask;
            }
            string server = AppConfig.ServerUrl.Trim().TrimEnd('/');
            bool serverChanged = server != _server;
            if (!force && !serverChanged && _fetchedAt >= 0
                && Time.realtimeSinceStartup - _fetchedAt < RefreshIntervalSeconds)
            {
                return Task.CompletedTask;
            }
            _inflight = DoRefreshAsync(server, serverChanged);
            return _inflight;
        }

        static async Task DoRefreshAsync(string server, bool serverChanged)
        {
            if (serverChanged && _blocks.Count > 0)
            {
                // 前のサーバーのお知らせを出しっぱなしにしない
                Apply(new List<NoticeHtml.Block>());
            }
            _server = server;
            try
            {
                string html = await AppConfig.CreateClient().GetRequestListPageAsync();
                var blocks = NoticeHtml.ExtractAppBlocks(html, MarkerClass);
                _fetchedAt = Time.realtimeSinceStartup;
                Apply(blocks);
            }
            catch (System.Exception)
            {
                // 通信失敗・旧サーバーの応答異常などは現状維持
            }
        }

        static void Apply(List<NoticeHtml.Block> blocks)
        {
            string signature = NoticeHtml.Signature(blocks);
            if (signature == _signature)
            {
                return;
            }
            _blocks = blocks;
            _signature = signature;
            Changed?.Invoke();
        }

        /// <summary>お知らせ内の相対 URL を接続先サーバー基準の絶対 URL にする。</summary>
        public static string ResolveUrl(string href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return "";
            }
            if (System.Uri.TryCreate(href, System.UriKind.Absolute, out var absolute)
                && !string.IsNullOrEmpty(absolute.Host))
            {
                return href;
            }
            try
            {
                var baseUri = new System.Uri(AppConfig.ServerUrl);
                return new System.Uri(baseUri, href).ToString();
            }
            catch (System.UriFormatException)
            {
                return href;
            }
        }

        /// <summary>
        /// 接続中のゆかりサーバーの検索ページへのリンクか判定する
        /// (ファイル名が search で始まるページ)。検索ワードは searchword / anyword /
        /// keyword / word のいずれかのクエリから拾う (無ければ null)。
        /// listerSearch はアニソンDB (ListerDB) 系ページへのリンクのとき true。
        /// </summary>
        public static bool TryParseSearchLink(string url, out string keyword, out bool listerSearch)
        {
            keyword = null;
            listerSearch = false;
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
            {
                return false;
            }
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return false;
            }
            if (!System.Uri.TryCreate(AppConfig.ServerUrl, System.UriKind.Absolute, out var server))
            {
                return false;
            }
            if (!string.Equals(uri.Host, server.Host, System.StringComparison.OrdinalIgnoreCase)
                || uri.Port != server.Port)
            {
                return false;
            }
            string path = uri.AbsolutePath;
            int slash = path.LastIndexOf('/');
            string file = (slash >= 0 ? path.Substring(slash + 1) : path).ToLowerInvariant();
            if (!file.StartsWith("search"))
            {
                return false;
            }
            listerSearch = file.StartsWith("search_listerdb");
            string query = uri.Query;
            if (query.StartsWith("?"))
            {
                query = query.Substring(1);
            }
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = pair.Substring(0, eq).ToLowerInvariant();
                if (key != "searchword" && key != "anyword" && key != "keyword" && key != "word")
                {
                    continue;
                }
                string value = UnityEngine.Networking.UnityWebRequest
                    .UnEscapeURL(pair.Substring(eq + 1));
                if (!string.IsNullOrEmpty(value))
                {
                    keyword = value;
                    break;
                }
            }
            return true;
        }
    }
}
