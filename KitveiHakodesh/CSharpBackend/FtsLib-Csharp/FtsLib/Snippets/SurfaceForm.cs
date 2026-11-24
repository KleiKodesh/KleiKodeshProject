using System.Text;
using FtsLib.Tokenization;

namespace FtsLib.Snippets
{
    /// <summary>
    /// A word's surface form for find in an open text (SeforimLibrarySearchEngine #14): its term
    /// as the Rust engine's scanner gives it, read off the word's raw span. FtsLib's own terms are
    /// quote-free and mark-free; the surface form adds back what the user may ask to match:
    /// quotes (Otzaria's rule: a geresh between letters or after the word; two gereshes or a
    /// gershayim between letters as one gershayim) and, with marks kept, each letter's marks in
    /// canonical order (U+034F dropped). Letters are lowercased and presentation forms folded as
    /// in the term. Inline tags inside the word are skipped.
    /// </summary>
    internal static class SurfaceForm
    {
        /// <summary>The surface form of the word starting at <paramref name="start"/> (its first
        /// letter) and ending before <paramref name="end"/>.</summary>
        public static string Of(string raw, int start, int end, bool keepQuotes, bool keepMarks)
        {
            var sb = new StringBuilder(end - start);
            int i = start;
            while (i < end)
            {
                int c = raw[i];
                int len = 1;
                if (char.IsHighSurrogate(raw[i]) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
                {
                    c = char.ConvertToUtf32(raw[i], raw[i + 1]);
                    len = 2;
                }
                if (c == '<')
                {
                    int gt = raw.IndexOf('>', i);
                    if (gt < 0 || gt >= end) break;
                    i = gt + 1;
                    continue;
                }
                if (HebrewChars.IsQuote(c))
                {
                    if (keepQuotes)
                    {
                        bool lastIsGeresh = sb.Length > 0 && sb[sb.Length - 1] == '\'';
                        bool lastIsGershayim = sb.Length > 0 && sb[sb.Length - 1] == '"';
                        if (HebrewChars.IsGeresh(c))
                        {
                            if (lastIsGeresh) sb[sb.Length - 1] = '"';
                            else sb.Append('\'');
                        }
                        else if (!lastIsGershayim) sb.Append('"');
                    }
                }
                else if (HebrewChars.IsWordMark(c))
                {
                    if (keepMarks && c != 0xFB1E && c != 0x034F) PushMark(sb, c);
                }
                else if (HebrewChars.IsTransparent(c)) { }
                else if (HebrewChars.IsWordChar(c))
                {
                    string folded = HebrewChars.FoldPresentationForm(c);
                    if (folded == null) HebrewChars.AppendLower(sb, c);
                    else
                        foreach (char f in folded)
                        {
                            if (!HebrewChars.IsAttachedMark(f)) sb.Append(f);
                            else if (keepMarks) PushMark(sb, f);
                        }
                }
                else break;
                i += len;
            }
            // after the word a gershayim is not part of it (a geresh is)
            while (sb.Length > 0 && sb[sb.Length - 1] == '"') sb.Length--;
            return sb.ToString();
        }

        /// <summary><paramref name="form"/> without quotes.</summary>
        public static string WithoutQuotes(string form)
            => form.IndexOf('"') < 0 && form.IndexOf('\'') < 0 ? form : form.Replace("\"", "").Replace("'", "");

        /// <summary>Appends a mark in canonical order among the marks at the end (a stable
        /// insertion by combining class), as Rust's <c>chars::push_mark_canonical</c>.</summary>
        private static void PushMark(StringBuilder sb, int mark)
        {
            int cls = CombiningClass(mark);
            int at = sb.Length;
            if (cls != 0)
                while (at > 0)
                {
                    int k = CombiningClass(sb[at - 1]);
                    if (k <= cls || k == 0) break;
                    at--;
                }
            sb.Insert(at, (char)mark);
        }

        /// <summary>The Unicode canonical combining class of a term mark (Unicode 16), 0 for every
        /// other char: the Rust engine's <c>chars::combining_class</c>, generated from it
        /// (find in an open text with marks kept keeps each letter's marks in canonical order).</summary>
        public static int CombiningClass(int c)
        {
            if (c >= 0x0300 && c <= 0x0314) return 230;
            if (c == 0x0315) return 232;
            if (c >= 0x0316 && c <= 0x0319) return 220;
            if (c == 0x031A) return 232;
            if (c == 0x031B) return 216;
            if (c >= 0x031C && c <= 0x0320) return 220;
            if (c >= 0x0321 && c <= 0x0322) return 202;
            if (c >= 0x0323 && c <= 0x0326) return 220;
            if (c >= 0x0327 && c <= 0x0328) return 202;
            if (c >= 0x0329 && c <= 0x0333) return 220;
            if (c >= 0x0334 && c <= 0x0338) return 1;
            if (c >= 0x0339 && c <= 0x033C) return 220;
            if (c >= 0x033D && c <= 0x0344) return 230;
            if (c == 0x0345) return 240;
            if (c == 0x0346) return 230;
            if (c >= 0x0347 && c <= 0x0349) return 220;
            if (c >= 0x034A && c <= 0x034C) return 230;
            if (c >= 0x034D && c <= 0x034E) return 220;
            if (c == 0x034F) return 0;
            if (c >= 0x0350 && c <= 0x0352) return 230;
            if (c >= 0x0353 && c <= 0x0356) return 220;
            if (c == 0x0357) return 230;
            if (c == 0x0358) return 232;
            if (c >= 0x0359 && c <= 0x035A) return 220;
            if (c == 0x035B) return 230;
            if (c == 0x035C) return 233;
            if (c >= 0x035D && c <= 0x035E) return 234;
            if (c == 0x035F) return 233;
            if (c >= 0x0360 && c <= 0x0361) return 234;
            if (c == 0x0362) return 233;
            if (c >= 0x0363 && c <= 0x036F) return 230;
            if (c == 0x0591) return 220;
            if (c >= 0x0592 && c <= 0x0595) return 230;
            if (c == 0x0596) return 220;
            if (c >= 0x0597 && c <= 0x0599) return 230;
            if (c == 0x059A) return 222;
            if (c == 0x059B) return 220;
            if (c >= 0x059C && c <= 0x05A1) return 230;
            if (c >= 0x05A2 && c <= 0x05A7) return 220;
            if (c >= 0x05A8 && c <= 0x05A9) return 230;
            if (c == 0x05AA) return 220;
            if (c >= 0x05AB && c <= 0x05AC) return 230;
            if (c == 0x05AD) return 222;
            if (c == 0x05AE) return 228;
            if (c == 0x05AF) return 230;
            if (c == 0x05B0) return 10;
            if (c == 0x05B1) return 11;
            if (c == 0x05B2) return 12;
            if (c == 0x05B3) return 13;
            if (c == 0x05B4) return 14;
            if (c == 0x05B5) return 15;
            if (c == 0x05B6) return 16;
            if (c == 0x05B7) return 17;
            if (c == 0x05B8) return 18;
            if (c >= 0x05B9 && c <= 0x05BA) return 19;
            if (c == 0x05BB) return 20;
            if (c == 0x05BC) return 21;
            if (c == 0x05BD) return 22;
            if (c == 0x05BF) return 23;
            if (c == 0x05C1) return 24;
            if (c == 0x05C2) return 25;
            if (c == 0x05C4) return 230;
            if (c == 0x05C5) return 220;
            if (c == 0x05C7) return 18;
            return 0;
        }
    }
}
