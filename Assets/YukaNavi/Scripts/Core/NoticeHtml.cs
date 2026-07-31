using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YukaNavi.Core
{
    /// <summary>
    /// 機材係お知らせ用の簡易 HTML パーサー。
    /// ゆかりの「リクエスト一覧(トップ)画面表示メッセージ」(HTML 記述可) から、
    /// アプリ向けマーカー class の付いた要素を抜き出し、WebView を使わずに UGUI で
    /// 描けるブロック列 (テキスト / リンク / 画像 / 罫線) へ変換する。
    ///
    /// 対応タグ: h1〜h6 / p / div / br / hr / ul / ol / li / img / a /
    ///           b / strong / i / em / span / font (color 属性・style="color:")
    /// それ以外のタグは無視して中身のテキストだけを残す。
    /// インライン装飾は UGUI (legacy Text) のリッチテキストタグ (b / i / color) に変換する。
    /// リンクはインライン表示ができないため、文中の位置で独立した行 (LinkBlock) になる。
    /// </summary>
    public static class NoticeHtml
    {
        // ---- 出力ブロック ----

        public abstract class Block { }

        /// <summary>本文テキスト (UGUI リッチテキスト変換済み)。</summary>
        public class TextBlock : Block
        {
            public string Text;
            /// <summary>公称フォントサイズ (UiFactory.CreateText に渡す値。FontScale 適用前)。</summary>
            public int FontSize = 26;
            public bool Bold;
        }

        /// <summary>リンク (タップ可能な行として描く)。</summary>
        public class LinkBlock : Block
        {
            public string Label;
            public string Href;
        }

        public class ImageBlock : Block
        {
            public string Src;
        }

        /// <summary>区切り線 (hr)。</summary>
        public class RuleBlock : Block { }

        // ---- 公開 API ----

        /// <summary>
        /// ページ HTML からマーカー class 付き要素を抜き出し、ブロック列にする。
        /// マーカーが無ければ空リスト (= アプリ向けお知らせなし)。
        /// </summary>
        public static List<Block> ExtractAppBlocks(string pageHtml, string markerClass)
        {
            var blocks = new List<Block>();
            if (string.IsNullOrEmpty(pageHtml))
            {
                return blocks;
            }
            foreach (var fragment in ExtractMarkedElements(pageHtml, markerClass))
            {
                ParseInto(blocks, fragment);
            }
            return blocks;
        }

        /// <summary>ブロック列の内容シグネチャ (変化検知用)。</summary>
        public static string Signature(List<Block> blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks)
            {
                if (block is TextBlock t)
                {
                    sb.Append("T:").Append(t.FontSize).Append(':').Append(t.Text);
                }
                else if (block is LinkBlock l)
                {
                    sb.Append("L:").Append(l.Label).Append(':').Append(l.Href);
                }
                else if (block is ImageBlock i)
                {
                    sb.Append("I:").Append(i.Src);
                }
                else
                {
                    sb.Append("R");
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // ---- タグ走査 ----

        static readonly HashSet<string> VoidTags = new HashSet<string>
        {
            "br", "hr", "img", "input", "meta", "link", "source", "wbr",
            "area", "base", "col", "embed", "track", "param",
        };

        class TagInfo
        {
            public string Name;     // 小文字
            public bool Closing;    // </xxx>
            public bool SelfClosed; // <xxx ... />
            public string Attrs;    // 属性部の生文字列
            public int Start;       // '<' の位置
            public int End;         // '>' の位置
        }

        /// <summary>
        /// タグ名の開始になりうる文字か。HTML の仕様どおり ASCII 英字だけに限定する
        /// (char.IsLetter は日本語にも true を返すため、「参加費&lt;無料&gt;です」のような
        /// 半角山括弧入りの本文をタグとして飲み込んでしまう)。
        /// </summary>
        static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        static bool IsAsciiLetterOrDigit(char c)
        {
            return IsAsciiLetter(c) || (c >= '0' && c <= '9');
        }

        /// <summary>タグの '>' を探す (属性値の引用符内は無視)。無ければ -1。</summary>
        static int FindTagEnd(string html, int start)
        {
            char quote = '\0';
            for (int i = start + 1; i < html.Length; i++)
            {
                char c = html[i];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '>')
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>'<' 位置 start のタグを読む。タグとして解釈できなければ null。</summary>
        static TagInfo ReadTag(string html, int start)
        {
            int end = FindTagEnd(html, start);
            if (end < 0)
            {
                return null;
            }
            int i = start + 1;
            bool closing = i < end && html[i] == '/';
            if (closing)
            {
                i++;
            }
            int nameStart = i;
            while (i < end && (IsAsciiLetterOrDigit(html[i]) || html[i] == '-'))
            {
                i++;
            }
            if (i == nameStart)
            {
                return null;
            }
            string name = html.Substring(nameStart, i - nameStart).ToLowerInvariant();
            bool selfClosed = html[end - 1] == '/';
            int attrsEnd = selfClosed ? end - 1 : end;
            string attrs = attrsEnd > i ? html.Substring(i, attrsEnd - i) : "";
            return new TagInfo
            {
                Name = name,
                Closing = closing,
                SelfClosed = selfClosed,
                Attrs = attrs,
                Start = start,
                End = end,
            };
        }

        /// <summary>pos 以降の次のタグ (コメント・DOCTYPE はスキップ)。無ければ null。</summary>
        static TagInfo NextTag(string html, int pos)
        {
            while (pos < html.Length)
            {
                int lt = html.IndexOf('<', pos);
                if (lt < 0 || lt + 1 >= html.Length)
                {
                    return null;
                }
                char c = html[lt + 1];
                if (c == '!' || c == '?')
                {
                    if (lt + 3 < html.Length && html[lt + 1] == '!'
                        && html[lt + 2] == '-' && html[lt + 3] == '-')
                    {
                        int close = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                        pos = close < 0 ? html.Length : close + 3;
                        continue;
                    }
                    int gt = html.IndexOf('>', lt + 1);
                    pos = gt < 0 ? html.Length : gt + 1;
                    continue;
                }
                if (c != '/' && !IsAsciiLetter(c))
                {
                    pos = lt + 1;
                    continue;
                }
                var tag = ReadTag(html, lt);
                if (tag == null)
                {
                    pos = lt + 1;
                    continue;
                }
                return tag;
            }
            return null;
        }

        /// <summary>属性文字列から属性値を取り出す (大文字小文字を区別しない)。無ければ null。</summary>
        static string GetAttr(string attrs, string name)
        {
            if (string.IsNullOrEmpty(attrs))
            {
                return null;
            }
            int i = 0;
            while (i < attrs.Length)
            {
                while (i < attrs.Length && !char.IsLetter(attrs[i]))
                {
                    i++;
                }
                if (i >= attrs.Length)
                {
                    break;
                }
                int nameStart = i;
                while (i < attrs.Length && (char.IsLetterOrDigit(attrs[i])
                    || attrs[i] == '-' || attrs[i] == ':'))
                {
                    i++;
                }
                string attrName = attrs.Substring(nameStart, i - nameStart);
                while (i < attrs.Length && char.IsWhiteSpace(attrs[i]))
                {
                    i++;
                }
                string value = "";
                if (i < attrs.Length && attrs[i] == '=')
                {
                    i++;
                    while (i < attrs.Length && char.IsWhiteSpace(attrs[i]))
                    {
                        i++;
                    }
                    if (i < attrs.Length && (attrs[i] == '"' || attrs[i] == '\''))
                    {
                        char q = attrs[i];
                        i++;
                        int valueStart = i;
                        while (i < attrs.Length && attrs[i] != q)
                        {
                            i++;
                        }
                        value = attrs.Substring(valueStart, i - valueStart);
                        if (i < attrs.Length)
                        {
                            i++;
                        }
                    }
                    else
                    {
                        int valueStart = i;
                        while (i < attrs.Length && !char.IsWhiteSpace(attrs[i]))
                        {
                            i++;
                        }
                        value = attrs.Substring(valueStart, i - valueStart);
                    }
                }
                if (string.Equals(attrName, name, StringComparison.OrdinalIgnoreCase))
                {
                    // 属性値もエンティティを復号する (href の &amp;amp; がそのまま残ると
                    // クエリの分割を壊し、検索リンクのアプリ内接続が効かなくなる)
                    return DecodeEntities(value);
                }
            }
            return null;
        }

        /// <summary>class 属性にクラス名が含まれるか (空白区切りの完全一致)。</summary>
        static bool HasClass(string attrs, string className)
        {
            string classes = GetAttr(attrs, "class");
            if (string.IsNullOrEmpty(classes))
            {
                return false;
            }
            foreach (var token in classes.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(token, className, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>マーカー class 付き要素の外側 HTML を文書順に集める (入れ子の再検出はしない)。</summary>
        static List<string> ExtractMarkedElements(string html, string markerClass)
        {
            var found = new List<string>();
            int pos = 0;
            while (true)
            {
                var tag = NextTag(html, pos);
                if (tag == null)
                {
                    break;
                }
                if (!tag.Closing && HasClass(tag.Attrs, markerClass))
                {
                    int end = tag.End;
                    if (!tag.SelfClosed && !VoidTags.Contains(tag.Name))
                    {
                        end = FindElementEnd(html, tag);
                    }
                    found.Add(html.Substring(tag.Start, end - tag.Start + 1));
                    pos = end + 1;
                    continue;
                }
                pos = tag.End + 1;
            }
            return found;
        }

        /// <summary>開始タグに対応する閉じタグの '>' 位置 (閉じられていなければ文書末尾)。</summary>
        static int FindElementEnd(string html, TagInfo open)
        {
            int depth = 1;
            int pos = open.End + 1;
            while (true)
            {
                var tag = NextTag(html, pos);
                if (tag == null)
                {
                    return html.Length - 1;
                }
                if (tag.Name == open.Name)
                {
                    if (tag.Closing)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return tag.End;
                        }
                    }
                    else if (!tag.SelfClosed && !VoidTags.Contains(tag.Name))
                    {
                        depth++;
                    }
                }
                pos = tag.End + 1;
            }
        }

        // ---- ブロック化 ----

        class InlineStyle
        {
            public string Element;  // 由来のタグ名 (閉じタグとの対応付け)
            public string OpenTag;  // 出力するリッチテキストタグ (無ければ null)
            public string CloseTag;
        }

        static void ParseInto(List<Block> blocks, string html)
        {
            var text = new StringBuilder();
            bool hasContent = false;
            int headingSize = 0; // 0 = 本文
            var styles = new List<InlineStyle>();
            bool inLink = false;
            string linkHref = null;
            var linkLabel = new StringBuilder();
            var listStack = new List<int>(); // -1 = ul / 1〜 = ol の次の番号

            // 段落の区切り。溜まったテキストをブロックとして確定し、
            // 開いているインライン装飾は閉じて次のブロックで開き直す
            void Flush()
            {
                if (inLink)
                {
                    return; // リンク内では段落を切らない
                }
                if (hasContent)
                {
                    string body = text.ToString().Trim();
                    for (int i = styles.Count - 1; i >= 0; i--)
                    {
                        if (styles[i].CloseTag != null)
                        {
                            body += styles[i].CloseTag;
                        }
                    }
                    blocks.Add(new TextBlock
                    {
                        Text = body,
                        FontSize = headingSize > 0 ? headingSize : 26,
                        Bold = headingSize > 0,
                    });
                }
                text.Length = 0;
                hasContent = false;
                foreach (var style in styles)
                {
                    if (style.OpenTag != null)
                    {
                        text.Append(style.OpenTag);
                    }
                }
            }

            void AppendText(string raw)
            {
                string decoded = DecodeEntities(raw);
                // ソース中の改行・タブ・連続空白は空白1つに畳む (改行は <br> で入れる)。
                // タグをまたぐテキストにも効くよう、出力先の末尾文字を見て判定する
                var target = inLink ? linkLabel : text;
                foreach (char c in decoded)
                {
                    if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                    {
                        if (target.Length > 0 && target[target.Length - 1] != ' '
                            && target[target.Length - 1] != '\n')
                        {
                            target.Append(' ');
                        }
                        continue;
                    }
                    target.Append(c);
                    if (!inLink)
                    {
                        hasContent = true;
                    }
                }
            }

            void OpenStyle(string element, string openTag, string closeTag)
            {
                if (inLink)
                {
                    // リンクのラベルは装飾なしの素のテキストにする
                    styles.Add(new InlineStyle { Element = element });
                    return;
                }
                if (openTag != null)
                {
                    text.Append(openTag);
                }
                styles.Add(new InlineStyle
                {
                    Element = element,
                    OpenTag = openTag,
                    CloseTag = closeTag,
                });
            }

            void CloseStyle(string element)
            {
                for (int i = styles.Count - 1; i >= 0; i--)
                {
                    if (styles[i].Element != element)
                    {
                        continue;
                    }
                    // i 以降を閉じ、i より上だけ開き直す (交差したタグの補正)
                    for (int j = styles.Count - 1; j >= i; j--)
                    {
                        if (!inLink && styles[j].CloseTag != null)
                        {
                            text.Append(styles[j].CloseTag);
                        }
                    }
                    var reopen = styles.GetRange(i + 1, styles.Count - i - 1);
                    styles.RemoveRange(i, styles.Count - i);
                    foreach (var style in reopen)
                    {
                        if (!inLink && style.OpenTag != null)
                        {
                            text.Append(style.OpenTag);
                        }
                        styles.Add(style);
                    }
                    return;
                }
            }

            void EndLink()
            {
                if (!inLink)
                {
                    return;
                }
                inLink = false;
                string label = linkLabel.ToString().Trim();
                if (!string.IsNullOrEmpty(linkHref))
                {
                    blocks.Add(new LinkBlock
                    {
                        Label = label != "" ? label : linkHref,
                        Href = linkHref,
                    });
                }
                else if (label != "")
                {
                    // href の無い <a> はただのテキストとして残す
                    text.Append(label);
                    hasContent = true;
                }
                linkHref = null;
                linkLabel.Length = 0;
            }

            int pos = 0;
            while (pos < html.Length)
            {
                if (html[pos] != '<')
                {
                    int next = html.IndexOf('<', pos);
                    if (next < 0)
                    {
                        next = html.Length;
                    }
                    AppendText(html.Substring(pos, next - pos));
                    pos = next;
                    continue;
                }
                char c1 = pos + 1 < html.Length ? html[pos + 1] : '\0';
                if (c1 == '!' || c1 == '?')
                {
                    if (pos + 3 < html.Length && c1 == '!'
                        && html[pos + 2] == '-' && html[pos + 3] == '-')
                    {
                        int close = html.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                        pos = close < 0 ? html.Length : close + 3;
                        continue;
                    }
                    int gt = html.IndexOf('>', pos + 1);
                    pos = gt < 0 ? html.Length : gt + 1;
                    continue;
                }
                if (c1 != '/' && !IsAsciiLetter(c1))
                {
                    AppendText("<");
                    pos++;
                    continue;
                }
                var tag = ReadTag(html, pos);
                if (tag == null)
                {
                    if (FindTagEnd(html, pos) < 0)
                    {
                        break; // '>' が無い壊れたタグ: 以降は捨てる
                    }
                    AppendText("<"); // タグ名が読めない ("</3" 等) → ただの文字として残す
                    pos++;
                    continue;
                }
                pos = tag.End + 1;

                switch (tag.Name)
                {
                    case "b":
                    case "strong":
                        if (tag.Closing) CloseStyle(tag.Name);
                        else OpenStyle(tag.Name, "<b>", "</b>");
                        break;
                    case "i":
                    case "em":
                        if (tag.Closing) CloseStyle(tag.Name);
                        else OpenStyle(tag.Name, "<i>", "</i>");
                        break;
                    case "span":
                    case "font":
                        if (tag.Closing)
                        {
                            CloseStyle(tag.Name);
                        }
                        else
                        {
                            string hex = CssColorToHex(GetAttr(tag.Attrs, "color"))
                                ?? StyleColor(GetAttr(tag.Attrs, "style"));
                            OpenStyle(tag.Name,
                                hex != null ? "<color=" + hex + ">" : null,
                                hex != null ? "</color>" : null);
                        }
                        break;
                    case "u":
                    case "small":
                    case "big":
                    case "sub":
                    case "sup":
                        // UGUI のリッチテキストに対応タグが無いため装飾は落とす
                        if (tag.Closing) CloseStyle(tag.Name);
                        else OpenStyle(tag.Name, null, null);
                        break;
                    case "a":
                        if (tag.Closing)
                        {
                            EndLink();
                        }
                        else if (!tag.SelfClosed)
                        {
                            EndLink(); // 閉じ忘れの <a> は前のリンクを確定させる
                            Flush();
                            inLink = true;
                            linkHref = GetAttr(tag.Attrs, "href");
                            linkLabel.Length = 0;
                        }
                        break;
                    case "br":
                        if (inLink) linkLabel.Append(' ');
                        else text.Append('\n');
                        break;
                    case "hr":
                        Flush();
                        blocks.Add(new RuleBlock());
                        break;
                    case "img":
                        if (!tag.Closing)
                        {
                            string src = GetAttr(tag.Attrs, "src");
                            if (!string.IsNullOrEmpty(src))
                            {
                                if (!inLink)
                                {
                                    Flush();
                                }
                                blocks.Add(new ImageBlock { Src = src });
                            }
                        }
                        break;
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                        Flush();
                        headingSize = tag.Closing ? 0 : HeadingSize(tag.Name);
                        break;
                    case "ul":
                    case "ol":
                        Flush();
                        if (tag.Closing)
                        {
                            if (listStack.Count > 0)
                            {
                                listStack.RemoveAt(listStack.Count - 1);
                            }
                        }
                        else
                        {
                            listStack.Add(tag.Name == "ol" ? 1 : -1);
                        }
                        break;
                    case "li":
                        Flush();
                        if (!tag.Closing && !inLink)
                        {
                            int depth = listStack.Count > 0 ? listStack.Count - 1 : 0;
                            string mark = "・";
                            if (listStack.Count > 0 && listStack[listStack.Count - 1] >= 1)
                            {
                                mark = listStack[listStack.Count - 1] + ". ";
                                listStack[listStack.Count - 1]++;
                            }
                            text.Append(new string('　', depth)).Append(mark);
                        }
                        break;
                    case "script":
                    case "style":
                        if (!tag.Closing && !tag.SelfClosed)
                        {
                            // 中身を丸ごと読み飛ばす
                            int close = html.IndexOf("</" + tag.Name, pos,
                                StringComparison.OrdinalIgnoreCase);
                            if (close < 0)
                            {
                                pos = html.Length;
                            }
                            else
                            {
                                int gt = html.IndexOf('>', close);
                                pos = gt < 0 ? html.Length : gt + 1;
                            }
                        }
                        break;
                    case "p":
                    case "div":
                    case "section":
                    case "article":
                    case "header":
                    case "footer":
                    case "center":
                    case "blockquote":
                    case "table":
                    case "tbody":
                    case "thead":
                    case "tr":
                    case "td":
                    case "th":
                    case "dl":
                    case "dt":
                    case "dd":
                        Flush();
                        break;
                    default:
                        break; // 未知のタグは無視して中身だけ残す
                }
            }
            EndLink();
            Flush();
        }

        static int HeadingSize(string name)
        {
            switch (name)
            {
                case "h1": return 38;
                case "h2": return 34;
                case "h3": return 31;
                case "h4": return 29;
                case "h5": return 27;
                default: return 26;
            }
        }

        /// <summary>style="..." から color 指定を取り出す。</summary>
        static string StyleColor(string style)
        {
            if (string.IsNullOrEmpty(style))
            {
                return null;
            }
            foreach (var decl in style.Split(';'))
            {
                int colon = decl.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                if (decl.Substring(0, colon).Trim().ToLowerInvariant() != "color")
                {
                    continue;
                }
                return CssColorToHex(decl.Substring(colon + 1));
            }
            return null;
        }

        /// <summary>
        /// CSS の色を UGUI の color タグ値 (#rrggbb) にする。白背景カードに描くため、
        /// 色名は読めるトーンに丸める。解釈できない色は null (装飾なし)。
        /// </summary>
        static string CssColorToHex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            value = value.Trim().ToLowerInvariant();
            if (value.StartsWith("#"))
            {
                if (value.Length == 7)
                {
                    return value;
                }
                if (value.Length == 4)
                {
                    return "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];
                }
                return null;
            }
            switch (value)
            {
                case "red": return "#d32f2f";
                case "crimson": return "#c2185b";
                case "orange": return "#e07820";
                case "yellow": return "#b8a000";
                case "green": return "#2e7d32";
                case "teal": return "#00796b";
                case "blue": return "#1e64d3";
                case "navy": return "#283593";
                case "purple": return "#7b5cd6";
                case "pink": return "#d35c8e";
                case "brown": return "#6d4c41";
                case "gray":
                case "grey": return "#777777";
                case "black": return "#333333";
                default: return null;
            }
        }

        /// <summary>主要な HTML エンティティを復号する。</summary>
        static string DecodeEntities(string s)
        {
            if (s.IndexOf('&') < 0)
            {
                return s;
            }
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '&')
                {
                    sb.Append(c);
                    continue;
                }
                int semi = s.IndexOf(';', i + 1);
                if (semi < 0 || semi - i > 10)
                {
                    sb.Append(c);
                    continue;
                }
                string entity = s.Substring(i + 1, semi - i - 1);
                string replaced = null;
                if (entity.StartsWith("#"))
                {
                    try
                    {
                        int code;
                        if (entity.Length > 2 && (entity[1] == 'x' || entity[1] == 'X'))
                        {
                            if (int.TryParse(entity.Substring(2), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            {
                                replaced = char.ConvertFromUtf32(code);
                            }
                        }
                        else if (int.TryParse(entity.Substring(1), out code))
                        {
                            replaced = char.ConvertFromUtf32(code);
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // 不正なコードポイントはそのまま残す
                    }
                }
                else
                {
                    switch (entity.ToLowerInvariant())
                    {
                        case "amp": replaced = "&"; break;
                        case "lt": replaced = "<"; break;
                        case "gt": replaced = ">"; break;
                        case "quot": replaced = "\""; break;
                        case "apos": replaced = "'"; break;
                        case "nbsp": replaced = " "; break;
                    }
                }
                if (replaced == null)
                {
                    sb.Append(c);
                    continue;
                }
                sb.Append(replaced);
                i = semi;
            }
            return sb.ToString();
        }
    }
}
