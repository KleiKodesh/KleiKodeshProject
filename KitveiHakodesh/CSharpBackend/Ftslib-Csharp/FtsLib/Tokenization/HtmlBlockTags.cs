using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Block-tag detection and HTML entity whitespace handling,
    /// shared by <see cref="HtmlWordScanner"/> subclasses.
    /// All methods are allocation-free on the hot path.
    /// </summary>
    internal static class HtmlBlockTags
    {
        /// <summary>
        /// Returns true if the tag name in <paramref name="name"/>[0..<paramref name="len"/>)
        /// is a block-level HTML element (acts as a word separator).
        /// Works directly on the raw char buffer — no string allocation.
        /// </summary>
        internal static bool IsBlockTag(char[] name, int len)
        {
            if (len == 0) return false;

            // Skip leading '/' (closing tag) or '!' (comment/doctype)
            int start = (name[0] == '/' || name[0] == '!') ? 1 : 0;
            int tlen  = len - start;
            if (tlen == 0) return false;

            char c0 = name[start];
            if (c0 >= 'A' && c0 <= 'Z') c0 = (char)(c0 | 32);

            switch (tlen)
            {
                case 1:
                    return c0 == 'p';

                case 2:
                {
                    char c1 = name[start + 1];
                    if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    if (c0 == 'b' && c1 == 'r') return true; // br
                    if (c0 == 'h' && c1 == 'r') return true; // hr
                    if (c0 == 'l' && c1 == 'i') return true; // li
                    if (c0 == 'u' && c1 == 'l') return true; // ul
                    if (c0 == 'o' && c1 == 'l') return true; // ol
                    if (c0 == 't' && c1 == 'r') return true; // tr
                    if (c0 == 't' && c1 == 'd') return true; // td
                    if (c0 == 't' && c1 == 'h') return true; // th
                    if (c0 == 'd' && c1 == 'd') return true; // dd
                    if (c0 == 'd' && c1 == 't') return true; // dt
                    if (c0 == 'h')
                    {
                        char d = c1 >= 'A' && c1 <= 'Z' ? (char)(c1 | 32) : c1;
                        return d >= '1' && d <= '6';          // h1–h6
                    }
                    return false;
                }

                case 3:
                {
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    if (c0 == 'd' && c1 == 'i' && c2 == 'v') return true; // div
                    if (c0 == 'p' && c1 == 'r' && c2 == 'e') return true; // pre
                    if (c0 == 'n' && c1 == 'a' && c2 == 'v') return true; // nav
                    return false;
                }

                case 4:
                {
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    char c3 = name[start + 3]; if (c3 >= 'A' && c3 <= 'Z') c3 = (char)(c3 | 32);
                    if (c0 == 'm' && c1 == 'a' && c2 == 'i' && c3 == 'n') return true; // main
                    return false;
                }

                case 5:
                {
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    char c3 = name[start + 3]; if (c3 >= 'A' && c3 <= 'Z') c3 = (char)(c3 | 32);
                    char c4 = name[start + 4]; if (c4 >= 'A' && c4 <= 'Z') c4 = (char)(c4 | 32);
                    if (c0 == 't' && c1 == 'a' && c2 == 'b' && c3 == 'l' && c4 == 'e') return true; // table
                    if (c0 == 'a' && c1 == 's' && c2 == 'i' && c3 == 'd' && c4 == 'e') return true; // aside
                    return false;
                }

                default:
                    return MatchesLongBlockTag(name, start, tlen);
            }
        }

        private static bool MatchesLongBlockTag(char[] name, int start, int tlen)
        {
            var sb = new StringBuilder(tlen);
            for (int i = start; i < start + tlen; i++)
            {
                char c = name[i];
                if (c >= 'A' && c <= 'Z') c = (char)(c | 32);
                sb.Append(c);
            }
            switch (sb.ToString())
            {
                case "header": case "footer": case "figure":  case "section":
                case "article": case "caption": case "figcaption": case "blockquote":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Tags that END the current word: every block-level element, plus sup/sub —
        /// their content (footnote and reference markers) is a separate logical token,
        /// not part of the word they interrupt. All other tags are TRANSPARENT: inline
        /// formatting (&lt;b&gt;, &lt;i&gt;, &lt;small&gt;, &lt;span&gt;, …) interrupts
        /// words mid-letter in ~1% of corpus lines (emphasised letters, e.g.
        /// ורא&lt;b&gt;ה&lt;/b&gt;), and breaking the word there indexed unfindable
        /// fragments (corpus verified 2026-07-22: ~1,450 of 130k sampled lines).
        /// </summary>
        internal static bool IsWordBreakTag(char[] name, int len)
        {
            if (IsBlockTag(name, len)) return true;

            int start = (len > 0 && (name[0] == '/' || name[0] == '!')) ? 1 : 0;
            int tlen  = len - start;
            if (tlen != 3) return false;

            char c0 = name[start];     if (c0 >= 'A' && c0 <= 'Z') c0 = (char)(c0 | 32);
            char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
            char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
            return c0 == 's' && c1 == 'u' && (c2 == 'p' || c2 == 'b'); // sup, sub
        }

        /// <summary>
        /// Advances <paramref name="i"/> past a well-formed entity's closing ';'
        /// (entity = '&amp;' at <paramref name="i"/>, then a ≤10-char name/number,
        /// then ';'). Leaves <paramref name="i"/> unchanged when malformed, so the
        /// caller consumes just the '&amp;'.
        ///
        /// No whitespace-vs-other classification: the scanner treats EVERY entity
        /// as a word separator. Corpus verified 2026-07-22 — there are no letter
        /// entities at all (zero '&amp;#…;' lines); real occurrences are whitespace
        /// (&amp;nbsp;, &amp;thinsp;) or punctuation (&amp;amp;, &amp;lt;, &amp;gt;),
        /// all of which separate words. The old classifier let non-whitespace
        /// entities pass INVISIBLY, joining the fragments around them into one
        /// unfindable term.
        /// </summary>
        internal static void SkipEntity(string text, int len, ref int i)
        {
            int start = i + 1;
            int end   = start;

            while (end < len && end - start < 10 && text[end] != ';')
                end++;

            if (end < len && text[end] == ';' && end > start)
                i = end; // advance past ';'
        }
    }
}
