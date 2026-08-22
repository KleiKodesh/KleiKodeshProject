using System;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// A parsed otzaria://, zayit:// or kitveihakodeshapp:// deep link pointing at a
    /// line of a book.
    ///
    /// Otzaria format:
    ///   otzaria://open/book/&lt;bookId&gt;?index=&lt;lineIndex&gt;
    ///   optionally &amp;mark            → highlight the whole line
    ///   optionally &amp;m=&lt;url-encoded&gt; → highlight a specific span within the line
    ///   The reference is a 0-based POSITIONAL line/segment index (finest granularity).
    ///
    /// This app's own format (generated only by the frontend's utils/appDeepLink.ts,
    /// which holds the matching single definition of the scheme):
    ///   kitveihakodeshapp://book/&lt;bookId&gt;?index=&lt;lineIndex&gt;
    ///   Deliberately the same shape and the same query parameters as Otzaria, minus
    ///   the "open/" segment, so both are parsed by one code path and `index` means the
    ///   same thing in both: a 0-based POSITIONAL line index, never a DB row id.
    ///   LegacyAppScheme is the name this scheme used to have; links copied under it
    ///   are already sitting in users' documents, so they keep parsing.
    ///
    /// Zayit format:
    ///   zayit://book/&lt;bookId&gt;/line/&lt;lineId&gt;
    ///   Both numbers are DB primary-key IDs. lineId is the row id of the specific
    ///   line (NOT a positional index) — it only resolves on a machine running the
    ///   same DB version.
    /// </summary>
    public sealed class HostLink
    {
        public enum LinkScheme { Otzaria, Zayit, KitveiHakodesh }

        /// <summary>This app's own link scheme — the one place it is spelled.</summary>
        public const string AppScheme = "kitveihakodeshapp";

        /// <summary>The scheme this app's links used before AppScheme; still parsed.</summary>
        public const string LegacyAppScheme = "seforimapp";

        /// <summary>Which of the three link families this URL came from.</summary>
        public LinkScheme Scheme { get; private set; }

        public int BookId { get; private set; }

        /// <summary>Otzaria: the 0-based positional line index. Null for Zayit.</summary>
        public int? Index { get; private set; }

        /// <summary>Zayit: the DB line row-id. Null for Otzaria.</summary>
        public int? LineId { get; private set; }

        /// <summary>Otzaria &amp;mark — highlight the whole target line.</summary>
        public bool Mark { get; private set; }

        /// <summary>Otzaria &amp;m=… — the decoded text of the span to highlight. Null when absent.</summary>
        public string MarkText { get; private set; }

        // otzaria://open/book/<bookId>?<query>
        private static readonly Regex OtzariaRe = new Regex(
            @"^otzaria://open/book/(?<book>\d+)/?(?:\?(?<query>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // kitveihakodeshapp://book/<bookId>?<query> — same query grammar as Otzaria.
        // Built from the scheme constants so renaming the scheme is a one-line change.
        // The optional trailing slash is for links that made a round trip through a
        // browser or the shell, which may normalise the path before launching us.
        private static readonly Regex AppRe = new Regex(
            @"^(?:" + Regex.Escape(AppScheme) + "|" + Regex.Escape(LegacyAppScheme) +
            @")://book/(?<book>\d+)/?(?:\?(?<query>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // zayit://book/<bookId>/line/<lineId>
        private static readonly Regex ZayitRe = new Regex(
            @"^zayit://book/(?<book>\d+)/line/(?<line>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse a single URL string. Returns null unless it is a well-formed
        /// book link in one of the three families above — this app's own scheme (or the
        /// legacy spelling of it), otzaria:// or zayit://.
        /// </summary>
        public static HostLink TryParse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();

            // Otzaria and this app's own scheme share one body: same path shape, same query
            // grammar, same meaning for `index`. Only the recorded Scheme differs.
            var indexed = OtzariaRe.Match(url);
            var indexedScheme = LinkScheme.Otzaria;
            if (!indexed.Success)
            {
                indexed = AppRe.Match(url);
                indexedScheme = LinkScheme.KitveiHakodesh;
            }
            if (indexed.Success)
            {
                if (!int.TryParse(indexed.Groups["book"].Value, out int bookId)) return null;

                var link = new HostLink { Scheme = indexedScheme, BookId = bookId };
                ParseIndexedQuery(indexed.Groups["query"].Value, link);
                // Such a link without a resolvable index has no line to open.
                return link.Index.HasValue ? link : null;
            }

            var zy = ZayitRe.Match(url);
            if (zy.Success)
            {
                if (!int.TryParse(zy.Groups["book"].Value, out int bookId)) return null;
                if (!int.TryParse(zy.Groups["line"].Value, out int lineId)) return null;
                return new HostLink { Scheme = LinkScheme.Zayit, BookId = bookId, LineId = lineId };
            }

            return null;
        }

        // Shared by the Otzaria and KitveiHakodesh schemes — see TryParse.
        private static void ParseIndexedQuery(string query, HostLink link)
        {
            if (string.IsNullOrEmpty(query)) return;

            foreach (string part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                string val = eq >= 0 ? part.Substring(eq + 1) : null;

                switch (key.ToLowerInvariant())
                {
                    case "index":
                        // Negative is not a line. Left unset, so TryParse rejects the link
                        // rather than handing the frontend an index it cannot scroll to —
                        // worth doing now that any web page can hand us a URL.
                        if (int.TryParse(val, out int idx) && idx >= 0) link.Index = idx;
                        break;
                    case "mark":
                        // Bare flag: &mark highlights the whole line.
                        link.Mark = true;
                        break;
                    case "m":
                        // &m=<url-encoded text> highlights a specific span.
                        if (!string.IsNullOrEmpty(val))
                        {
                            try { link.MarkText = Uri.UnescapeDataString(val); }
                            catch { link.MarkText = val; }
                        }
                        break;
                }
            }
        }
    }
}
