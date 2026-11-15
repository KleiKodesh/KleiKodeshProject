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

        /// <summary>True for a sup or sub tag name (open or close; the scanner drops the '/').</summary>
        internal static bool IsScriptTag(char[] name, int len)
        {
            int start = (len > 0 && name[0] == '/') ? 1 : 0;
            if (len - start != 3) return false;
            char c2 = (char)(name[start + 2] | 32);
            return (name[start] | 32) == 's' && (name[start + 1] | 32) == 'u' && (c2 == 'p' || c2 == 'b');
        }

        static bool IsLetterOrMark(char c) => HebrewChars.IsHebrewLetter(c) || HebrewChars.IsAttachedMark(c);

        /// <summary>A Hebrew letter, or a presentation form that folds to one letter and its marks.</summary>
        static bool IsScriptLetter(char c)
        {
            string folded = HebrewChars.FoldPresentationForm(c);
            if (folded == null) return HebrewChars.IsHebrewLetter(c);
            if (folded.Length == 0 || !HebrewChars.IsHebrewLetter(folded[0])) return false;
            for (int i = 1; i < folded.Length; i++)
                if (!HebrewChars.IsAttachedMark(folded[i])) return false;
            return true;
        }

        /// <summary>Zero-width chars (bidi controls, CGJ, U+FB1E) that must not change how a
        /// neighbouring sup / sub is classified.</summary>
        static bool IsScriptInvisible(char c) => HebrewChars.IsInvisible(c) || c == '\u034F' || c == '\uFB1E';

        /// <summary>The tag name of the body text[from..to): an optional '/', then an ASCII
        /// alphanumeric run of 1..10 chars. Returns false when there is none.</summary>
        static bool ScriptTagName(string text, int from, int to, out bool closing, out int nameStart, out int nameLen)
        {
            closing = from < to && text[from] == '/';
            nameStart = closing ? from + 1 : from;
            int i = nameStart;
            while (i < to && IsAsciiAlnum(text[i])) i++;
            nameLen = i - nameStart;
            return nameLen >= 1 && nameLen <= 10;
        }

        static bool IsAsciiAlnum(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        /// <summary>An inline (transparent) tag body text[from..to): neither a word-break tag nor sup/sub.</summary>
        static bool IsInlineTag(string text, int from, int to)
        {
            if (!ScriptTagName(text, from, to, out _, out int ns, out int nl)) return true;
            var name = text.ToCharArray(ns, nl);
            return !IsWordBreakTag(name, nl);
        }

        /// <summary>Skips zero-width chars and inline tags forward from i (bounded by end).</summary>
        static int TrimFormattingStart(string text, int i, int end)
        {
            while (true)
            {
                while (i < end && IsScriptInvisible(text[i])) i++;
                if (i >= end || text[i] != '<') return i;
                int gt = text.IndexOf('>', i + 1, end - i - 1);
                if (gt < 0 || !IsInlineTag(text, i + 1, gt)) return i;
                i = gt + 1;
            }
        }

        /// <summary>Skips zero-width chars and inline tags backward: returns the new end of text[0..end).</summary>
        static int TrimFormattingEnd(string text, int end)
        {
            while (true)
            {
                while (end > 0 && IsScriptInvisible(text[end - 1])) end--;
                if (end == 0 || text[end - 1] != '>') return end;
                int lt = end - 2 < 0 ? -1 : text.LastIndexOf('<', end - 2);
                if (lt < 0) return end;
                if (text.IndexOf('>', lt + 1, end - 2 - lt) >= 0 || !IsInlineTag(text, lt + 1, end - 1)) return end;
                end = lt;
            }
        }

        static int TrimContextEnd(string text, int end)
        {
            while (true)
            {
                int t = TrimFormattingEnd(text, end);
                while (t > 0 && HebrewChars.IsWordMark(text[t - 1])) t--;
                if (t == end) return end;
                end = t;
            }
        }

        static int TrimContextStart(string text, int i, int end)
        {
            while (true)
            {
                int t = TrimFormattingStart(text, i, end);
                while (t < end && HebrewChars.IsWordMark(text[t])) t++;
                if (t == i) return i;
                i = t;
            }
        }

        /// <summary>The word footnote-marker in the tag body text[from..to) (case-insensitive, whole word).</summary>
        static bool MarksFootnote(string text, int from, int to)
        {
            const string W = "footnote-marker";
            for (int i = from; i + W.Length <= to; i++)
            {
                if (string.Compare(text, i, W, 0, W.Length, System.StringComparison.OrdinalIgnoreCase) != 0) continue;
                bool before = i > from && (IsAsciiAlnum(text[i - 1]) || text[i - 1] == '_');
                int j = i + W.Length;
                bool after = j < to && (IsAsciiAlnum(text[j]) || text[j] == '_');
                if (!before && !after) return true;
            }
            return false;
        }

        /// <summary>
        /// An open &lt;sup&gt; / &lt;sub&gt; whose '&lt;' is at <paramref name="lt"/> and
        /// '&gt;' at <paramref name="gt"/> is TRANSPARENT (the word continues through it,
        /// like &lt;b&gt;) when it sits between letters and wraps a hanging letter (issue #37,
        /// Otzaria engine 5babbce is_word_internal_script): its attributes do not hold the
        /// word footnote-marker (always splits); before '&lt;' (skipping marks, bidi / CGJ
        /// chars and inline tags) is a Hebrew letter or a presentation form of one; its
        /// content (inline tags and zero-width chars skipped) is either ONE such letter with
        /// optional marks (no mark required) or Hebrew letters and marks with at least one
        /// mark (issue #2 H58); the content ends at the matching close tag, and after it
        /// (same skipping) comes a Hebrew letter. Every other sup / sub ends the word. Same
        /// rule as hebrew-fts html_tags::is_transparent_sup.
        /// </summary>
        internal static bool IsTransparentSup(string text, int lt, int gt)
        {
            if (!ScriptTagName(text, lt + 1, gt, out bool closing, out int ns, out int nl) || closing) return false;
            if (MarksFootnote(text, lt + 1, gt)) return false;
            int before = TrimContextEnd(text, lt);
            if (before == 0 || !IsScriptLetter(text[before - 1])) return false;
            int n = text.Length;
            int content = TrimFormattingStart(text, gt + 1, n);
            if (content >= n || !IsScriptLetter(text[content])) return false;
            // one hanging letter (marks optional) ...
            int rest = TrimContextStart(text, content + 1, n);
            if (rest >= n || text[rest] != '<')
            {
                // ... or a vocalized run of letters and marks (at least one mark)
                int end = text.IndexOf('<', content);
                if (end < 0) return false;
                bool mark = false;
                for (int i = content; i < end; i++)
                {
                    char c = text[i];
                    if (!IsLetterOrMark(c) && !IsScriptInvisible(c)) return false;
                    if (HebrewChars.IsAttachedMark(c)) mark = true;
                }
                if (!mark) return false;
                rest = TrimContextStart(text, end, n);
            }
            if (rest >= n || text[rest] != '<') return false;
            int closeGt = text.IndexOf('>', rest + 1);
            if (closeGt < 0) return false;
            if (!ScriptTagName(text, rest + 1, closeGt, out bool isClose, out int cs, out int cl) || !isClose
                || cl != nl || string.Compare(text, cs, text, ns, nl, System.StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            int after = TrimContextStart(text, closeGt + 1, n);
            return after < n && IsScriptLetter(text[after]);
        }

        /// <summary>
        /// Tags that END the current word: every block-level element, plus sup/sub —
        /// their content (footnote and reference markers) is a separate logical token,
        /// not part of the word they interrupt (the scanner makes a &lt;sup&gt; that
        /// IsTransparentSup accepts transparent instead). All other tags are TRANSPARENT: inline
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
