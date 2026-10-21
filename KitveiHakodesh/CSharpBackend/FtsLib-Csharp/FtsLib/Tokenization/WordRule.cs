using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Otzaria's word rule on plain text, ported from otzaria_search_engine v5.0.0
    /// hebrew_tokenizer.rs (next_token_boundaries, paired_reading_after, the token normalization
    /// of advance) and hebrew_query.rs (keep_second_of_paired_readings). The query parser uses
    /// it; <see cref="HtmlWordScanner"/> is the same rule, single-pass and HTML-aware. Indices
    /// are UTF-16 (string) indices.
    ///
    /// wildcards: the query parser's mode, where the wildcard chars * and ? are word chars (in
    /// text * is transparent and ? a separator).
    /// </summary>
    internal static class WordRule
    {
        static int CodePointAt(string s, int i, out int units)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                units = 2;
                return char.ConvertToUtf32(c, s[i + 1]);
            }
            units = 1;
            return c;
        }

        static bool IsWild(int c, bool wildcards) => wildcards && (c == '*' || c == '?');
        public static bool WordChar(int c, bool wildcards) => HebrewChars.IsWordChar(c) || IsWild(c, wildcards);
        public static bool Starts(int c, bool wildcards) => HebrewChars.StartsWord(c) || IsWild(c, wildcards);
        public static bool Transparent(int c, bool wildcards) => HebrewChars.IsTransparent(c) && !IsWild(c, wildcards);

        /// <summary>
        /// Otzaria next_token_boundaries: the next word at or after <paramref name="from"/>. A word
        /// starts at a <see cref="Starts"/> char and runs over word chars; a run of quotes and
        /// transparent chars between two word chars joins them when it holds no quote, one or two
        /// gereshes, or one gershayim. At the end of a word a geresh right after it (transparent
        /// chars allowed between) belongs to it; a gershayim does not.
        /// </summary>
        public static bool NextTokenBoundaries(string text, int from, bool wildcards, out int start, out int end)
        {
            start = end = -1;
            int i = from, n = text.Length;
            while (i < n)
            {
                int c = CodePointAt(text, i, out int u);
                if (Starts(c, wildcards)) break;
                i += u;
            }
            if (i >= n) return false;
            start = i;
            int pos = i;
            while (true)
            {
                while (pos < n)
                {
                    int c = CodePointAt(text, pos, out int u);
                    if (!WordChar(c, wildcards)) break;
                    pos += u;
                }
                int geresh = 0, gershayim = 0, firstQuoteEnd = 0;
                bool firstIsGeresh = false;
                int next = -1, nextAt = -1;
                int j = pos;
                while (j < n)
                {
                    int c = CodePointAt(text, j, out int u);
                    if (HebrewChars.IsQuote(c))
                    {
                        if (geresh + gershayim == 0)
                        {
                            firstQuoteEnd = j + u;
                            firstIsGeresh = HebrewChars.IsGeresh(c);
                        }
                        if (HebrewChars.IsGeresh(c)) geresh++; else gershayim++;
                    }
                    else if (!Transparent(c, wildcards))
                    {
                        next = c;
                        nextAt = j;
                        break;
                    }
                    j += u;
                }
                if (next >= 0 && WordChar(next, wildcards))
                {
                    if ((geresh == 0 && gershayim == 0) || (geresh == 1 && gershayim == 0)
                        || (geresh == 2 && gershayim == 0) || (geresh == 0 && gershayim == 1))
                    {
                        pos = nextAt;
                        continue;
                    }
                    end = pos;
                    return true;
                }
                end = firstIsGeresh ? firstQuoteEnd : pos;
                return true;
            }
        }

        static int SkipReadingSuffix(string text, int i)
        {
            while (i < text.Length)
            {
                int c = CodePointAt(text, i, out int u);
                if (!HebrewChars.IsReadingSuffix(c)) break;
                i += u;
            }
            return i;
        }

        /// <summary>
        /// Otzaria paired_reading_after: when the word at tokStart..tokEnd opens a paired reading
        /// (X) [Y] (ketiv and qere), Y's span and the index after its ']'.
        /// </summary>
        public static bool PairedReadingAfter(string text, int tokStart, int tokEnd,
                                              out int secondStart, out int secondEnd, out int afterPair)
        {
            secondStart = secondEnd = afterPair = -1;
            if (tokStart == 0 || text[tokStart - 1] != '(') return false;
            int i = SkipReadingSuffix(text, tokEnd);
            if (i >= text.Length || text[i] != ')') return false;
            i++;
            while (i < text.Length)
            {
                int c = CodePointAt(text, i, out int u);
                if (!HebrewChars.IsWhitespace(c)) break;
                i += u;
            }
            if (i >= text.Length || text[i] != '[') return false;
            int yStart = i + 1;
            if (!NextTokenBoundaries(text, yStart, false, out int s, out int e) || s != yStart) return false;
            int k = SkipReadingSuffix(text, e);
            if (k >= text.Length || text[k] != ']') return false;
            secondStart = s;
            secondEnd = e;
            afterPair = k + 1;
            return true;
        }

        /// <summary>Otzaria keep_second_of_paired_readings (its query sanitizer): every (X) [Y] in
        /// the query becomes " Y ".</summary>
        public static string KeepSecondOfPairedReadings(string query)
        {
            StringBuilder sb = null;
            int copied = 0, pos = 0;
            while (NextTokenBoundaries(query, pos, false, out int start, out int end))
            {
                pos = end;
                if (PairedReadingAfter(query, start, end, out int ys, out int ye, out int after))
                {
                    if (sb == null) sb = new StringBuilder(query.Length);
                    sb.Append(query, copied, start - 1 - copied);
                    sb.Append(' ');
                    sb.Append(query, ys, ye - ys);
                    sb.Append(' ');
                    copied = after;
                    pos = copied;
                }
            }
            if (sb == null) return query;
            sb.Append(query, copied, query.Length - copied);
            return sb.ToString();
        }

        /// <summary>
        /// A word's normalized term, as the index keeps it: Otzaria's advance (marks dropped,
        /// transparent chars dropped, a geresh ', two gereshes ", a gershayim ", presentation
        /// forms folded) and LowerCaser, then FtsLib's quote-free form.
        /// <paramref name="termBytes"/> = UTF-8 bytes of Otzaria's term before lowercasing,
        /// quotes included (what its RemoveLongFilter measures).
        /// </summary>
        public static string Normalize(string word, bool wildcards, out int termBytes)
        {
            var sb = new StringBuilder(word.Length);
            termBytes = 0;
            int last = 0, prev = 0; // kind of the term's last two chars: 0 other, 1 ', 2 "
            for (int i = 0; i < word.Length;)
            {
                int c = CodePointAt(word, i, out int u);
                i += u;
                if (HebrewChars.IsWordMark(c) && c != 0xFB1E) continue;
                if (Transparent(c, wildcards)) continue;
                if (HebrewChars.IsGeresh(c))
                {
                    if (last == 1)
                    {
                        termBytes--;
                        last = prev; prev = 0;
                        if (last != 2) { termBytes++; prev = last; last = 2; }
                    }
                    else { termBytes++; prev = last; last = 1; }
                    continue;
                }
                if (HebrewChars.IsGershayim(c))
                {
                    if (last != 2) { termBytes++; prev = last; last = 2; }
                    continue;
                }
                string folded = HebrewChars.FoldPresentationForm(c);
                if (folded != null)
                {
                    foreach (char f in folded)
                    {
                        if (HebrewChars.IsAttachedMark(f)) continue;
                        termBytes += f < 0x80 ? 1 : f < 0x800 ? 2 : 3;
                        HebrewChars.AppendLower(sb, f);
                        prev = last; last = 0;
                    }
                    continue;
                }
                termBytes += c < 0x80 ? 1 : c < 0x800 ? 2 : c < 0x10000 ? 3 : 4;
                HebrewChars.AppendLower(sb, c);
                prev = last; last = 0;
            }
            return sb.ToString();
        }
    }
}
