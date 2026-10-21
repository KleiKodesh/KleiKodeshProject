using System;
using System.Runtime.CompilerServices;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Single source of truth for the per-character text rules shared by every path that
    /// normalizes text in FtsLib: the indexing/snippet scanner (HtmlWordScanner) and the query
    /// parser (FtsLib.Search.QueryParser, via WordRule). Change a rule here and every path picks
    /// it up: the index, the highlighter, and the query parser stay in lockstep.
    ///
    /// The word rules are Otzaria's tokenizer (otzaria_search_engine v5.0.0,
    /// hebrew_tokenizer.rs / hebrew_query.rs), on code points. "Alphanumeric" and "lowercase"
    /// are Rust's char::is_alphanumeric and char::to_lowercase (<see cref="UnicodeTables"/>,
    /// generated from the toolchain the seforim engine is built with).
    /// Non-ASCII code points are written numerically to keep the rules unambiguous.
    /// </summary>
    internal static class HebrewChars
    {
        // Hebrew block: alef U+05D0 .. tav U+05EA (includes final forms).
        /// <summary>Hebrew letters alef-tav (U+05D0..U+05EA, final forms included).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHebrewLetter(int c) => c >= 0x05D0 && c <= 0x05EA;

        /// <summary>Otzaria is_attached_mark: Hebrew points and te'amim (U+0591..U+05C7) except
        /// maqaf (U+05BE), paseq (U+05C0), sof pasuq (U+05C3), nun hafukha (U+05C6).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAttachedMark(int c)
            => c >= 0x0591 && c <= 0x05C7 && c != 0x05BE && c != 0x05C0 && c != 0x05C3 && c != 0x05C6;

        /// <summary>Otzaria is_word_mark: an attached Hebrew mark, a general combining mark
        /// (U+0300..U+036F) or U+FB1E. Marks belong to the word they sit on but never start one;
        /// the term drops them.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWordMark(int c) => IsAttachedMark(c) || (c >= 0x0300 && c <= 0x036F) || c == 0xFB1E;

        /// <summary>Otzaria is_invisible_char: zero-width and bidi controls (U+200B..U+200F,
        /// U+202A..U+202E, U+2066..U+2069, U+FEFF).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInvisible(int c)
            => (c >= 0x200B && c <= 0x200F) || (c >= 0x202A && c <= 0x202E) || (c >= 0x2066 && c <= 0x2069) || c == 0xFEFF;

        /// <summary>Otzaria is_transparent: chars that neither break a word nor belong to it
        /// (letter, period, letter, period, letter is one word): * [ ] ^ $ \ + . ~ and backtick,
        /// and the invisible chars.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTransparent(int c)
            => c == '*' || c == '[' || c == ']' || c == '^' || c == '$' || c == '\\' || c == '+' || c == '.'
            || c == '~' || c == '`' || IsInvisible(c);

        /// <summary>Otzaria is_geresh: ', geresh U+05F3, typographic U+2018 / U+2019.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsGeresh(int c) => c == '\'' || c == 0x05F3 || c == 0x2018 || c == 0x2019;

        /// <summary>Otzaria is_gershayim: ", gershayim U+05F4, typographic U+201C / U+201D.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsGershayim(int c) => c == '"' || c == 0x05F4 || c == 0x201C || c == 0x201D;

        /// <summary>A geresh or a gershayim.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsQuote(int c) => IsGeresh(c) || IsGershayim(c);

        /// <summary>Rust char::is_alphanumeric.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAlphanumeric(int c)
        {
            if (c < 0x80) return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (IsHebrewLetter(c)) return true;
            return InRanges(UnicodeTables.AlphanumericRanges, c);
        }

        /// <summary>Otzaria is_word_char: a Hebrew letter, an ASCII letter or digit, or any other
        /// non-ASCII alphanumeric char (letters and digits of every script: no Hebrew/Latin split)
        /// or word mark.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWordChar(int c)
        {
            if (c < 0x80) return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (IsHebrewLetter(c)) return true;
            return IsWordMark(c) || InRanges(UnicodeTables.AlphanumericRanges, c);
        }

        /// <summary>Otzaria starts_token: a word char that may start a word (not a mark). Also what
        /// counts as a letter of a word.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StartsWord(int c)
        {
            if (c < 0x80) return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (IsHebrewLetter(c)) return true;
            return !IsWordMark(c) && InRanges(UnicodeTables.AlphanumericRanges, c);
        }

        /// <summary>Otzaria's reading suffix (paired readings): anything but a word char or a bracket.</summary>
        public static bool IsReadingSuffix(int c)
            => !IsWordChar(c) && c != '(' && c != ')' && c != '[' && c != ']' && c != '<' && c != '>';

        /// <summary>Rust char::is_whitespace (Unicode White_Space).</summary>
        public static bool IsWhitespace(int c)
            => (c >= 0x09 && c <= 0x0D) || c == 0x20 || c == 0x85 || c == 0xA0 || c == 0x1680
            || (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000;

        static bool InRanges(int[] ranges, int c)
        {
            int lo = 0, hi = ranges.Length / 2 - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (c < ranges[2 * mid]) hi = mid - 1;
                else if (c > ranges[2 * mid + 1]) lo = mid + 1;
                else return true;
            }
            return false;
        }

        /// <summary>Appends Rust's char::to_lowercase of <paramref name="c"/> (Otzaria's LowerCaser),
        /// as UTF-16.</summary>
        public static void AppendLower(System.Text.StringBuilder sb, int c)
        {
            if (c < 0x80)
            {
                sb.Append((char)((c >= 'A' && c <= 'Z') ? (c | 32) : c));
                return;
            }
            int i = Array.BinarySearch(UnicodeTables.LowercaseFrom, c);
            if (i >= 0) sb.Append(UnicodeTables.LowercaseTo[i]);
            else if (c < 0x10000) sb.Append((char)c);
            else sb.Append(char.ConvertFromUtf32(c));
        }

        /// <summary>Otzaria fold_presentation_form: Hebrew presentation forms (U+FB1D..U+FB4F) as
        /// their letters and marks; null for any other char.</summary>
        public static string FoldPresentationForm(int c)
        {
            switch (c)
            {
                case 0xFB1D: return "יִ";
                case 0xFB1E: return "";
                case 0xFB1F: return "ײַ";
                case 0xFB20: return "ע";
                case 0xFB21: return "א";
                case 0xFB22: return "ד";
                case 0xFB23: return "ה";
                case 0xFB24: return "כ";
                case 0xFB25: return "ל";
                case 0xFB26: return "ם";
                case 0xFB27: return "ר";
                case 0xFB28: return "ת";
                case 0xFB2A: return "שׁ";
                case 0xFB2B: return "שׂ";
                case 0xFB2C: return "שּׁ";
                case 0xFB2D: return "שּׂ";
                case 0xFB2E: return "אַ";
                case 0xFB2F: return "אָ";
                case 0xFB30: return "אּ";
                case 0xFB31: return "בּ";
                case 0xFB32: return "גּ";
                case 0xFB33: return "דּ";
                case 0xFB34: return "הּ";
                case 0xFB35: return "וּ";
                case 0xFB36: return "זּ";
                case 0xFB38: return "טּ";
                case 0xFB39: return "יּ";
                case 0xFB3A: return "ךּ";
                case 0xFB3B: return "כּ";
                case 0xFB3C: return "לּ";
                case 0xFB3E: return "מּ";
                case 0xFB40: return "נּ";
                case 0xFB41: return "סּ";
                case 0xFB43: return "ףּ";
                case 0xFB44: return "פּ";
                case 0xFB46: return "צּ";
                case 0xFB47: return "קּ";
                case 0xFB48: return "רּ";
                case 0xFB49: return "שּ";
                case 0xFB4A: return "תּ";
                case 0xFB4B: return "וֹ";
                case 0xFB4C: return "בֿ";
                case 0xFB4D: return "כֿ";
                case 0xFB4E: return "פֿ";
                case 0xFB4F: return "אל";
                default: return null;
            }
        }

        /// <summary>Otzaria's MAX_TOKEN_BYTES (its RemoveLongFilter::limit(128)): a word whose term
        /// (normalized, before lowercasing, a geresh ' or gershayim " counting one byte) has this
        /// many UTF-8 bytes or more is not indexed or searched, but it is still a word (it keeps
        /// its position in word distances).</summary>
        public const int MaxTermBytes = 128;

        /// <summary>True when a term of <paramref name="bytes"/> UTF-8 bytes is indexed (tantivy's
        /// RemoveLongFilter keeps len &lt; limit).</summary>
        public static bool IsIndexableLength(int bytes) => bytes < MaxTermBytes;

        // ── Former FtsLib word model (kept for the CharBench benchmark) ──────────────────────

        /// <summary>Hebrew letters (alef-tav, incl. final forms) and ASCII a-z / A-Z.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLetter(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= 0x05D0 && c <= 0x05EA);

        /// <summary>ASCII lowercase (Hebrew and other chars pass through unchanged).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToLowerAscii(char c) => (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

        /// <summary>Lowercase ASCII letter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLatinLower(char c) => c >= 'a' && c <= 'z';

        /// <summary>Nikud + cantillation (U+0591..U+05C7), EXCEPT paseq (U+05C0), sof pasuq
        /// (U+05C3), nun hafukha (U+05C6).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsStrippableMark(char c)
            => c >= 0x0591 && c <= 0x05C7
            && c != 0x05C0 && c != 0x05C3 && c != 0x05C6;

        /// <summary>dquote U+0022, gershayim U+05F4, apostrophe U+0027, geresh U+05F3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsIntraWordQuote(char c)
            => c == 0x0022 || c == 0x05F4 || c == 0x0027 || c == 0x05F3;
    }
}
