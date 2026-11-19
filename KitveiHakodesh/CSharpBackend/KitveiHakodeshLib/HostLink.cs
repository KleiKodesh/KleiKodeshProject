using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// A parsed link into a library app (Otzaria, Zayit or this app's own) pointing at a book, and
    /// optionally a line or a TOC heading in it. The same grammar as the frontend's
    /// utils/appDeepLink.ts parseBookLink - change both together. Schemes, hosts and nouns ignore
    /// case; trailing slashes and URL-encoding are accepted; a book is named by id (digits) or title.
    ///
    ///   otzaria://open/book/&lt;id|title&gt;[?index=&lt;n&gt;][&amp;mark][&amp;m=..][&amp;q=..][&amp;source=user|db:&lt;slug&gt;]
    ///   otzaria://open/pdf/&lt;id&gt;[?index=&lt;page&gt;]          the book at its top (a PDF page is not a line)
    ///   otzaria://inline-link?path=&lt;file path&gt;&amp;index=&lt;1-based line&gt;[&amp;ref=..]
    ///   book://&lt;title&gt;[#&lt;heading&gt;[#&lt;sub-heading&gt;...]]  Otzaria's links inside books
    ///   kitveihakodeshapp://book/&lt;id|title&gt;[?index=&lt;n&gt;]   this app's own (LegacyAppScheme too)
    ///   zayit://book/&lt;id|title&gt;[/line/&lt;lineId&gt;]           also https://zayitapp.com/open.html#book/...
    ///   seforim://line/&lt;lineId&gt;                            Zayit's citation links
    ///
    /// Sources: Otzaria lib/core/external_uri_router.dart, lib/utils/book_link_builder.dart,
    /// lib/utils/text/html_link_handler.dart; Zayit core/deeplink/ZayitDeepLink.kt, RefLinkUriHandler.kt.
    /// Index is a 0-based POSITIONAL line; LineId a DB line row id (Zayit), which also names the
    /// book when the link has no book (seforim://line/). No Index, LineId or Headings = the top.
    /// </summary>
    public sealed class HostLink
    {
        public enum LinkScheme { Otzaria, Zayit, KitveiHakodesh }

        /// <summary>This app's own link scheme - the one place it is spelled.</summary>
        public const string AppScheme = "kitveihakodeshapp";

        /// <summary>The scheme this app's links used before AppScheme; still parsed.</summary>
        public const string LegacyAppScheme = "seforimapp";

        /// <summary>Which of the three link families this URL came from.</summary>
        public LinkScheme Scheme { get; private set; }

        /// <summary>The book's id in the library DB; null when the link names it by Title or by LineId only.</summary>
        public int? BookId { get; private set; }

        /// <summary>The book's title (Otzaria's book:// and inline-link links); null when named by id.</summary>
        public string Title { get; private set; }

        /// <summary>A TOC heading path to open at (book://title#heading#sub-heading); null when absent.</summary>
        public IReadOnlyList<string> Headings { get; private set; }

        /// <summary>The 0-based positional line index; null when the link has none.</summary>
        public int? Index { get; private set; }

        /// <summary>Zayit: the DB line row-id. Null otherwise.</summary>
        public int? LineId { get; private set; }

        /// <summary>Otzaria ?source=user / db:&lt;slug&gt;: a book of another library, whose id is not ours.</summary>
        public bool OtherLibrary { get; private set; }

        /// <summary>Otzaria &amp;mark - highlight the whole target line.</summary>
        public bool Mark { get; private set; }

        /// <summary>Otzaria &amp;m=... - the decoded text of the span to highlight. Null when absent.</summary>
        public string MarkText { get; private set; }

        private static readonly Regex SchemeRe = new Regex(@"^([a-z][a-z0-9+.-]*)://(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ZayitWebOpenRe = new Regex(@"^https?://(?:www\.)?zayitapp\.com/(?:[a-z]{2}/)?open(?:\.html)?/?#(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex DigitsRe = new Regex(@"^\s*[0-9]+\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse a single URL string. Returns null unless it is a book link in one of the
        /// shapes above.
        /// </summary>
        public static HostLink TryParse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            string text = url.Trim();
            Match web = ZayitWebOpenRe.Match(text);
            if (web.Success) text = "zayit://" + web.Groups[1].Value;
            Match head = SchemeRe.Match(text);
            if (!head.Success) return null;

            string scheme = head.Groups[1].Value.ToLowerInvariant();
            string rest = head.Groups[2].Value, fragment = "", queryText = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0) { fragment = rest.Substring(hash + 1); rest = rest.Substring(0, hash); }
            int question = rest.IndexOf('?');
            if (question >= 0) { queryText = rest.Substring(question + 1); rest = rest.Substring(0, question); }
            string[] segments = rest.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, string> query = ParseQuery(queryText);
            string Noun(int i) => i < segments.Length ? segments[i].ToLowerInvariant() : null;

            switch (scheme)
            {
                case "otzaria":
                {
                    if (Noun(0) == "inline-link" && segments.Length == 1)
                    {
                        query.TryGetValue("path", out string path);
                        string title = TitleFromPath(path ?? "");
                        if (title.Length == 0) return null;
                        var inline = new HostLink { Scheme = LinkScheme.Otzaria, Title = title };
                        int? oneBased = NonNegative(query, "index");
                        if (oneBased > 0) inline.Index = oneBased - 1;
                        return inline;
                    }
                    if (Noun(0) != "open" || segments.Length != 3) return null;
                    string kind = Noun(1);
                    if (kind != "book" && kind != "pdf") return null;
                    var link = new HostLink { Scheme = LinkScheme.Otzaria };
                    if (!SetBook(link, segments[2])) return null;
                    if (query.TryGetValue("source", out string source) && !string.IsNullOrWhiteSpace(source)
                        && !source.Trim().Equals("official", StringComparison.OrdinalIgnoreCase))
                        link.OtherLibrary = true;
                    if (kind == "book")
                    {
                        link.Index = NonNegative(query, "index");
                        link.Mark = query.ContainsKey("mark");
                        if (query.TryGetValue("m", out string markText) && !string.IsNullOrEmpty(markText)) link.MarkText = markText;
                    }
                    return link;
                }
                case "book":
                {
                    // book://<title>: the whole host-and-path is the title (a title may hold a '/')
                    string title = string.Join("/", segments.Select(DecodeSegment)).Trim();
                    if (title.Length == 0) return null;
                    var headings = fragment.Split('#').Select(part => DecodeSegment(part).Trim()).Where(part => part.Length > 0).ToList();
                    return new HostLink { Scheme = LinkScheme.Otzaria, Title = title, Headings = headings.Count > 0 ? headings : null };
                }
                case AppScheme:
                case LegacyAppScheme:
                {
                    if (Noun(0) != "book" || segments.Length != 2) return null;
                    var link = new HostLink { Scheme = LinkScheme.KitveiHakodesh };
                    if (!SetBook(link, segments[1])) return null;
                    link.Index = NonNegative(query, "index");
                    return link;
                }
                case "zayit":
                {
                    if (Noun(0) != "book" || segments.Length < 2) return null;
                    var link = new HostLink { Scheme = LinkScheme.Zayit };
                    if (!SetBook(link, segments[1])) return null;
                    if (segments.Length == 2) return link;
                    if (Noun(2) != "line" || segments.Length != 4 || !DigitsRe.IsMatch(segments[3])) return null;
                    if (!int.TryParse(segments[3].Trim(), out int lineId) || lineId <= 0) return null;
                    link.LineId = lineId;
                    return link;
                }
                case "seforim":
                {
                    if (Noun(0) != "line" || segments.Length != 2 || !DigitsRe.IsMatch(segments[1])) return null;
                    if (!int.TryParse(segments[1].Trim(), out int lineId) || lineId <= 0) return null;
                    return new HostLink { Scheme = LinkScheme.Zayit, LineId = lineId };
                }
                default:
                    return null;
            }
        }

        /// <summary>The book named by a path segment: an id (digits, above 0) or a title (anything else, decoded).</summary>
        private static bool SetBook(HostLink link, string segment)
        {
            string decoded = DecodeSegment(segment).Trim();
            if (decoded.Length == 0) return false;
            if (DigitsRe.IsMatch(decoded))
            {
                if (!int.TryParse(decoded, out int bookId) || bookId <= 0) return false;
                link.BookId = bookId;
            }
            else link.Title = decoded;
            return true;
        }

        /// <summary>Otzaria's inline-link names the book by its file path: the file name without .txt (html_link_handler.dart _getTitleFromPath).</summary>
        private static string TitleFromPath(string path)
        {
            string name = path.Split('/', '\\').Last();
            return Regex.Replace(name, @"\.(txt|text)$", "", RegexOptions.IgnoreCase).Trim();
        }

        private static int? NonNegative(Dictionary<string, string> query, string key)
        {
            if (!query.TryGetValue(key, out string value) || value == null || !DigitsRe.IsMatch(value)) return null;
            return int.TryParse(value.Trim(), out int n) ? n : (int?)null;
        }

        /// <summary>Keys lower-cased, first one wins; a bare key (&amp;mark) has a null value. '+' is a space.</summary>
        private static Dictionary<string, string> ParseQuery(string queryText)
        {
            var query = new Dictionary<string, string>();
            foreach (string part in queryText.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = DecodeQuery(eq >= 0 ? part.Substring(0, eq) : part).ToLowerInvariant();
                if (!query.ContainsKey(key)) query[key] = eq >= 0 ? DecodeQuery(part.Substring(eq + 1)) : null;
            }
            return query;
        }

        private static string DecodeQuery(string text) => DecodeSegment(text.Replace('+', ' '));

        private static string DecodeSegment(string text)
        {
            try { return Uri.UnescapeDataString(text); }
            catch (Exception) { return text; }
        }
    }
}
