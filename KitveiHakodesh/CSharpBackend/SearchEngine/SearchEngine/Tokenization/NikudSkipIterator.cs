using System;
using System.Collections.Generic;

namespace SearchEngine.Tokenization
{
    /// <summary>
    /// Walks a raw string (which may contain nikud, cantillation marks, and HTML tags)
    /// and finds all occurrences of a plain (nikud-free) term using two strategies:
    ///
    ///   Strategy A — IndexOf fast path:
    ///     Calls string.IndexOf(term) repeatedly. Works when the source string contains
    ///     the term exactly as-is (no nikud interleaved). O(n) native string search,
    ///     zero per-occurrence allocation.
    ///
    ///   Strategy B — char-by-char iterator (fallback):
    ///     Used only when strategy A returns zero results — i.e. the source has nikud
    ///     interleaved and the plain term cannot be found verbatim. Walks the source
    ///     once, skipping nikud/cantillation at every position, and checks whether the
    ///     term matches the letters at the current position. Reports the actual raw
    ///     source indices (including any surrounding nikud characters).
    ///
    /// Both strategies return the same contract: a list of (RawStart, RawEnd) pairs
    /// where RawStart is the index of the first letter of the match in the source
    /// string, and RawEnd is the index just past the last letter of the match
    /// (nikud that immediately follows the last letter is included in RawEnd so the
    /// highlight tag closes after the full rendered glyph).
    ///
    /// Only nikud and cantillation (U+0591–U+05C7) are skipped. HTML tags, spaces,
    /// punctuation, and all other characters are treated as ordinary non-letter bytes
    /// and terminate a match attempt.
    /// </summary>
    public static class NikudSkipIterator
    {
        // Nikud / cantillation range — same as HebrewAnalyzer.
        private const char NikudStart = '\u0591';
        private const char NikudEnd   = '\u05C7';

        /// <summary>
        /// Returns all (RawStart, RawEnd) occurrences of <paramref name="term"/> in
        /// <paramref name="source"/>, ignoring nikud/cantillation in the source.
        /// The term itself must be plain (no nikud).
        /// </summary>
        public static List<(int RawStart, int RawEnd)> FindAll(string source, string term)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(term))
                return new List<(int, int)>(0);

            // Strategy A: try plain IndexOf first.
            var results = TryIndexOf(source, term);
            if (results.Count > 0)
                return results;

            // Strategy B: char-by-char walk skipping nikud.
            return WalkAndMatch(source, term);
        }

        // ── Strategy A ────────────────────────────────────────────────

        private static List<(int RawStart, int RawEnd)> TryIndexOf(string source, string term)
        {
            var list = new List<(int, int)>();
            int start = 0;
            while (true)
            {
                int pos = source.IndexOf(term, start, StringComparison.Ordinal);
                if (pos < 0) break;
                // RawEnd: advance past any trailing nikud that belongs to the last letter.
                int end = pos + term.Length;
                while (end < source.Length && IsNikud(source[end]))
                    end++;
                list.Add((pos, end));
                start = pos + 1;
            }
            return list;
        }

        // ── Strategy B ────────────────────────────────────────────────

        /// <summary>
        /// Walks <paramref name="source"/> once. At every non-nikud position, attempts
        /// to match <paramref name="term"/> by advancing through the source while
        /// skipping nikud. Collects all matches.
        /// </summary>
        private static List<(int RawStart, int RawEnd)> WalkAndMatch(string source, string term)
        {
            var list = new List<(int, int)>();
            int sourceLen = source.Length;
            int termLen   = term.Length;

            for (int i = 0; i < sourceLen; i++)
            {
                // Skip nikud at the outer walk level so every letter is a candidate start.
                if (IsNikud(source[i])) continue;

                // Try matching term starting at source[i].
                int si = i;      // position in source
                int ti = 0;      // position in term

                while (ti < termLen && si < sourceLen)
                {
                    char sc = source[si];

                    // Skip nikud in the source during the match attempt.
                    if (IsNikud(sc)) { si++; continue; }

                    if (sc != term[ti]) break;

                    si++;
                    ti++;
                }

                if (ti == termLen)
                {
                    // Full match. RawStart = i (first letter).
                    // RawEnd = si, advanced past any trailing nikud.
                    while (si < sourceLen && IsNikud(source[si]))
                        si++;
                    list.Add((i, si));
                }
            }

            return list;
        }

        // ── Helper ────────────────────────────────────────────────────

        private static bool IsNikud(char c) => c >= NikudStart && c <= NikudEnd;
    }
}
