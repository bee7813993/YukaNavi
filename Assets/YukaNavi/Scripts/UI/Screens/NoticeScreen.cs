using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using YukaNavi.Core;

namespace YukaNavi.UI
{
    /// <summary>
    /// 機材係 (ゆかりの運用者) 提供コンテンツの表示画面。
    /// NoticeService が抽出したブロック列 (テキスト / リンク / 画像 / 罫線) を
    /// WebView を使わず UGUI で描く。ゆかりの検索ページへのリンクは外部ブラウザに
    /// 出さずアプリ内の検索へつなぐ。それ以外のリンクはアプリ内シート
    /// (iOS) / 既定ブラウザで開く (InAppBrowser)。
    /// </summary>
    public class NoticeScreen : ScreenBase
    {
        Text _statusText;
        RectTransform _listContent;
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly List<Object> _imageAssets = new List<Object>(); // 画像の Texture / Sprite (破棄用)
        List<NoticeHtml.Block> _renderedBlocks;
        int _loadSerial; // 画像の非同期ロードの世代 (再描画で古いロードを無効化)

        public override void BuildUi()
        {
            var bg = UiFactory.CreatePanel(transform, "Background", UiFactory.ScreenOverlayBg);
            UiFactory.StretchFull(bg);

            UiFactory.CreateTopBar(transform, "おしらせ");

            var caption = UiFactory.CreateText(transform, "Caption", "機材係からのお知らせ", 22,
                UiFactory.TextMuted);
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -122f);
            captionRect.sizeDelta = new Vector2(-40f, UiFactory.LineHeight(22));

            _statusText = UiFactory.CreateText(transform, "Status", "", 28, UiFactory.TextDark);
            var statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -240f);
            statusRect.sizeDelta = new Vector2(-80f, 200f);

            var scrollRect = UiFactory.CreateScrollList(transform, "NoticeList", out _listContent);
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(20f, GlobalNav.BarHeight + 16f);
            scrollRect.offsetMax = new Vector2(-20f, -(122f + UiFactory.LineHeight(22) + 12f));
        }

        public override async void OnShow()
        {
            Render();
            // 開いたときは新しい内容を取りにいき、変わっていたら描き直す
            await NoticeService.RefreshAsync(true);
            if (this == null || !gameObject.activeSelf)
            {
                return;
            }
            if (!ReferenceEquals(_renderedBlocks, NoticeService.Blocks))
            {
                Render();
            }
        }

        public override void OnHide()
        {
            Clear();
            _renderedBlocks = null;
        }

        public override void OnRebuild()
        {
            // 子 (行) は RebuildAll が破棄する。自前で持っている画像リソースだけ片付ける
            _loadSerial++;
            _rows.Clear();
            DestroyImageAssets();
            _renderedBlocks = null;
        }

        void Clear()
        {
            _loadSerial++;
            foreach (var row in _rows)
            {
                Destroy(row);
            }
            _rows.Clear();
            DestroyImageAssets();
        }

        void DestroyImageAssets()
        {
            foreach (var asset in _imageAssets)
            {
                if (asset != null)
                {
                    Destroy(asset);
                }
            }
            _imageAssets.Clear();
        }

        void Render()
        {
            Clear();
            _renderedBlocks = NoticeService.Blocks;
            var blocks = _renderedBlocks;
            if (blocks == null || blocks.Count == 0)
            {
                _statusText.text = "機材係からのお知らせは、いまはありません";
                return;
            }
            _statusText.text = "";

            // お知らせ全体をひとつの白カードにまとめる。行の高さは
            // VerticalLayoutGroup が各要素の preferredHeight から決める
            var cardGo = new GameObject("Card");
            cardGo.transform.SetParent(_listContent, false);
            var cardImg = cardGo.AddComponent<Image>();
            cardImg.color = UiFactory.CardBg;
            UiFactory.Roundify(cardImg);
            UiFactory.AddShadow(cardGo, 3f);
            var layout = cardGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 12f;
            layout.padding = new RectOffset(28, 28, 26, 26);
            _rows.Add(cardGo);

            foreach (var block in blocks)
            {
                AddBlock(cardGo.transform, block);
            }
        }

        void AddBlock(Transform parent, NoticeHtml.Block block)
        {
            if (block is NoticeHtml.TextBlock textBlock)
            {
                var text = UiFactory.CreateText(parent, "Text", textBlock.Text,
                    textBlock.FontSize, UiFactory.TextDark, TextAnchor.UpperLeft);
                if (textBlock.Bold)
                {
                    text.fontStyle = FontStyle.Bold;
                    text.color = UiFactory.PrimaryDark;
                }
                return;
            }
            if (block is NoticeHtml.LinkBlock linkBlock)
            {
                var text = UiFactory.CreateText(parent, "Link",
                    "▶ " + UiFactory.NoWordWrap(linkBlock.Label), 26,
                    UiFactory.Primary, TextAnchor.UpperLeft);
                text.raycastTarget = true;
                var button = text.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                string href = linkBlock.Href;
                button.onClick.AddListener(() => OpenLink(href));
                return;
            }
            if (block is NoticeHtml.ImageBlock imageBlock)
            {
                var imgGo = new GameObject("Image");
                imgGo.transform.SetParent(parent, false);
                var img = imgGo.AddComponent<Image>();
                img.color = UiFactory.PrimaryPale; // 読み込み中のプレースホルダ
                img.preserveAspect = true;
                img.raycastTarget = false;
                var le = imgGo.AddComponent<LayoutElement>();
                le.preferredHeight = 220f;
                LoadImageAsync(img, le, imageBlock.Src, _loadSerial);
                return;
            }
            if (block is NoticeHtml.RuleBlock)
            {
                var lineGo = new GameObject("Rule");
                lineGo.transform.SetParent(parent, false);
                var line = lineGo.AddComponent<Image>();
                line.color = new Color(0.85f, 0.83f, 0.90f);
                line.raycastTarget = false;
                var le = lineGo.AddComponent<LayoutElement>();
                le.preferredHeight = 3f;
            }
        }

        /// <summary>画像ブロックの非同期ロード。失敗したら行ごと畳む。</summary>
        async void LoadImageAsync(Image img, LayoutElement le, string src, int serial)
        {
            string url = NoticeService.ResolveUrl(src);
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                if (serial == _loadSerial && img != null)
                {
                    img.gameObject.SetActive(false);
                }
                return;
            }
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = 15;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
                if (serial != _loadSerial || img == null)
                {
                    return; // 画面が描き直された後に届いた
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    img.gameObject.SetActive(false);
                    return;
                }
                var texture = DownloadHandlerTexture.GetContent(req);
                var sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _imageAssets.Add(texture);
                _imageAssets.Add(sprite);
                img.sprite = sprite;
                img.color = Color.white;
                // カード内の幅に合わせて縦横比を保った高さにする
                float width = ((RectTransform)img.transform).rect.width;
                if (width <= 0f)
                {
                    width = 960f;
                }
                le.preferredHeight = width * texture.height / texture.width;
            }
        }

        /// <summary>
        /// リンクを開く。接続中サーバーの検索ページ (search*) はアプリ内の検索へつなぎ、
        /// それ以外の http(s) リンクは InAppBrowser (iOS はアプリ内シート) で開く。
        /// </summary>
        void OpenLink(string href)
        {
            string url = NoticeService.ResolveUrl(href);
            if (NoticeService.TryParseSearchLink(url, out string keyword, out bool listerSearch))
            {
                Se.Play(Se.Transition);
                if (string.IsNullOrEmpty(keyword))
                {
                    Manager.Show<SearchScreen>();
                    return;
                }
                SearchResultScreen.Open(Manager, new SearchResultScreen.SearchQuery
                {
                    Kind = listerSearch
                        ? SearchResultScreen.QueryKind.ListerAnyword
                        : SearchResultScreen.QueryKind.Everything,
                    Keyword = keyword,
                    Label = "検索: " + keyword,
                });
                return;
            }
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                Se.Play(Se.Tap);
                InAppBrowser.Open(url);
                return;
            }
            Se.Play(Se.Error);
            UiFactory.ShowToast("このリンクはアプリでは開けません", true);
        }
    }
}
