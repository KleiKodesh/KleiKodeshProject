using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Static helpers shared by <see cref="HtmlTextScanner"/> subclasses:
    /// block-tag detection and HTML entity handling.
    /// All methods are allocation-free on the hot path.
    /// </summary>
    internal static class HtmlScannerHelpers
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
        /// Handles an HTML entity starting at <paramref name="i"/>+1.
        /// Advances <paramref name="i"/> past the closing ';'.
        /// Returns true if the entity is a whitespace separator (caller should flush word).
        /// </summary>
        internal static bool IsWhitespaceEntity(string text, int len, ref int i)
        {
            int start = i + 1;
            int end   = start;

            while (end < len && end - start < 10 && text[end] != ';')
                end++;

            if (end >= len || text[end] != ';')
                return false; // malformed — leave i unchanged, treat '&' as separator

            i = end; // advance past ';'

            int elen = end - start;
            if (elen == 0) return false;

            char e0 = text[start];

            if (e0 == 'n' && elen == 4
                && text[start+1]=='b' && text[start+2]=='s' && text[start+3]=='p')
                return true; // &nbsp;

            if (e0 == 'e' && elen == 4
                && text[start+1]=='n' && text[start+2]=='s' && text[start+3]=='p')
                return true; // &ensp;

            if (e0 == 'e' && elen == 4
                && text[start+1]=='m' && text[start+2]=='s' && text[start+3]=='p')
                return true; // &emsp;

            // Numeric whitespace entities: &#160; &#8194; &#8195; &#8201;
            if (e0 == '#' && elen > 1)
            {
                int val = 0;
                bool ok = true;
                for (int k = start + 1; k < end; k++)
                {
                    char d = text[k];
                    if (d < '0' || d > '9') { ok = false; break; }
                    val = val * 10 + (d - '0');
                }
                if (ok && (val == 160 || val == 8194 || val == 8195 || val == 8201))
                    return true;
            }

            return false; // all other entities are invisible
        }
    }
}
