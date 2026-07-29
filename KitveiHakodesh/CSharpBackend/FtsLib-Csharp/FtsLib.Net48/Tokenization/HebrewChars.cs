using System.Runtime.CompilerServices;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Single source of truth for the per-character text rules shared by every path that
    /// normalizes text in FtsLib: the indexing/snippet scanner (HtmlWordScanner) and the query
    /// normalizer (FtsLib.Search.QueryParser). Change a rule here and every path picks it up —
    /// the index, the highlighter, and the query parser stay in lockstep.
    ///
    /// Every method is a tiny static with AggressiveInlining, so the JIT inlines it into the
    /// caller's loop: the emitted machine code is identical to the former hand-inlined branches.
    /// Non-ASCII code points are written numerically to keep the rules unambiguous.
    /// </summary>
    internal static class HebrewChars
    {
        // Hebrew block: alef U+05D0 .. tav U+05EA (includes final forms).
        /// <summary>Hebrew letters (alef-tav, incl. final forms) and ASCII a-z / A-Z.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLetter(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= 0x05D0 && c <= 0x05EA);

        /// <summary>ASCII lowercase (Hebrew and other chars pass through unchanged).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToLowerAscii(char c) => (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

        /// <summary>Lowercase ASCII letter — call AFTER ToLowerAscii. For script-boundary split.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLatinLower(char c) => c >= 'a' && c <= 'z';

        /// <summary>Nikud + cantillation stripped from words (U+0591..U+05C7), EXCEPT the three
        /// separator marks paseq (U+05C0), sof pasuq (U+05C3), nun hafukha (U+05C6).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsStrippableMark(char c)
            => c >= 0x0591 && c <= 0x05C7
            && c != 0x05C0 && c != 0x05C3 && c != 0x05C6;

        /// <summary>Quote chars INSIDE a Hebrew word (rashi/gershayim/gematria) — transparent
        /// connectors, not separators. dquote U+0022, gershayim U+05F4, apostrophe U+0027, geresh U+05F3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsIntraWordQuote(char c)
            => c == 0x0022 || c == 0x05F4 || c == 0x0027 || c == 0x05F3;
    }
}
