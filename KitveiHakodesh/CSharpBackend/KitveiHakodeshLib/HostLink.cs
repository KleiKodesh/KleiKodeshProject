using System;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// A parsed otzaria:// or zayit:// deep link pointing at a line of a book.
    ///
    /// Otzaria format:
    ///   otzaria://open/book/&lt;bookId&gt;?index=&lt;lineIndex&gt;
    ///   optionally &amp;mark            → highlight the whole line
    ///   optionally &amp;m=&lt;url-encoded&gt; → highlight a specific span within the line
    ///   The reference is a 0-based POSITIONAL line/segment index (finest granularity).
    ///
    /// Zayit format:
    ///   zayit://book/&lt;bookId&gt;/line/&lt;lineId&gt;
    ///   Both numbers are DB primary-key IDs. lineId is the row id of the specific
    ///   line (NOT a positional index) — it only resolves on a machine running the
    ///   same DB version.
    /// </summary>
    public sealed class HostLink
    {
        public enum LinkScheme { Otzaria, Zayit }

        /// <summary>"otzaria" or "zayit".</summary>
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
            @"^otzaria://open/book/(?<book>\d+)(?:\?(?<query>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // zayit://book/<bookId>/line/<lineId>
        private static readonly Regex ZayitRe = new Regex(
            @"^zayit://book/(?<book>\d+)/line/(?<line>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse a single URL string. Returns null when it is neither a
        /// well-formed otzaria:// nor zayit:// book link.
        /// </summary>
        public static HostLink TryParse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();

            var oz = OtzariaRe.Match(url);
            if (oz.Success)
            {
                if (!int.TryParse(oz.Groups["book"].Value, out int bookId)) return null;

                var link = new HostLink { Scheme = LinkScheme.Otzaria, BookId = bookId };
                ParseOtzariaQuery(oz.Groups["query"].Value, link);
                // An Otzaria link without a resolvable index has no line to open.
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

        private static void ParseOtzariaQuery(string query, HostLink link)
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
                        if (int.TryParse(val, out int idx)) link.Index = idx;
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
