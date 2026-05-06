using System;
using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Generates כתיב חסר / כתיב מלא spelling variants of a normalised Hebrew term
    /// by inserting ו and י at every consonant-boundary position in the stem.
    ///
    /// Algorithm (mirrors the TypeScript implementation in hebrewKetivExpander.ts):
    ///   1. Detect and preserve common grammatical suffixes (ים, ין, ית, ות, ון, ה)
    ///      so their vowel letters are not stripped and not used as insertion points.
    ///   2. Strip all ו/י from the stem to get the bare consonant skeleton.
    ///   3. For each gap between consecutive consonants in the skeleton, try inserting
    ///      ו, י, or nothing — 3^gaps combinations, capped at MaxVariants.
    ///   4. Reattach the preserved suffix to every variant.
    ///   5. Deduplicate and exclude the original term.
    ///
    /// Only applies to plain literal terms.  Wildcard and fuzzy terms are left
    /// unchanged — wildcards already cover spelling variants, and fuzzy already
    /// covers edit-distance variants.
    /// </summary>
    internal static class KetivExpander
    {
        private const char Yod = '\u05D9'; // י
        private const char Vav = '\u05D5'; // ו

        /// <summary>Hard cap on the number of variants returned per term.</summary>
        public const int MaxVariants = 40;

        // Suffixes to preserve verbatim — ordered longest-first so greedy match works.
        private static readonly string[] PreservedSuffixes =
        {
            "\u05D5\u05D9\u05D5\u05EA", // ויות
            "\u05D9\u05D5\u05EA",       // יות
            "\u05D5\u05EA",             // ות
            "\u05D9\u05DF",             // ין
            "\u05D9\u05DD",             // ים
            "\u05D9\u05EA",             // ית
            "\u05D5\u05DF",             // ון
            "\u05D4",                   // ה
        };

        // Hebrew letter range: alef (U+05D0) through tav (U+05EA), excluding ו (U+05D5) and י (U+05D9).
        private static bool IsHebrewConsonant(char c)
            => c >= '\u05D0' && c <= '\u05EA' && c != Vav && c != Yod;

        /// <summary>
        /// Returns all כתיב spelling variants of <paramref name="term"/>,
        /// excluding the original term itself.
        /// Returns an empty list when the term is too short to expand or has no gaps.
        /// </summary>
        public static List<string> Expand(string term)
        {
            if (string.IsNullOrEmpty(term) || term.Length < 2)
                return new List<string>();

            // Step 1: detect and strip suffix
            string stem   = term;
            string suffix = string.Empty;
            foreach (var s in PreservedSuffixes)
            {
                if (term.EndsWith(s, StringComparison.Ordinal) && term.Length > s.Length)
                {
                    stem   = term.Substring(0, term.Length - s.Length);
                    suffix = s;
                    break;
                }
            }

            // Step 2: strip ו/י from stem to get consonant skeleton
            var skeletonChars = new List<char>(stem.Length);
            foreach (char c in stem)
                if (c != Vav && c != Yod)
                    skeletonChars.Add(c);

            string skeleton = new string(skeletonChars.ToArray());

            // Need at least 2 consonants to have a gap
            var consonantPositions = new List<int>();
            for (int i = 0; i < skeleton.Length; i++)
                if (IsHebrewConsonant(skeleton[i]))
                    consonantPositions.Add(i);

            if (consonantPositions.Count < 2)
                return new List<string>();

            // Step 3: enumerate insertions at up to 5 gaps (cap combinatorial explosion)
            int gaps          = consonantPositions.Count - 1;
            int effectiveGaps = Math.Min(gaps, 5);

            var variants = new HashSet<string>(StringComparer.Ordinal);

            // Always include the bare skeleton + suffix (covers מלא→חסר direction)
            string bareVariant = skeleton + suffix;
            if (bareVariant != term) variants.Add(bareVariant);

            int total = (int)Math.Pow(3, effectiveGaps);
            for (int mask = 0; mask < total; mask++)
            {
                // Build insertion list for this mask
                var insertions = new List<(int position, char character)>(effectiveGaps);
                int m = mask;
                for (int g = 0; g < effectiveGaps; g++)
                {
                    int choice = m % 3;
                    m /= 3;
                    if (choice == 1) insertions.Add((consonantPositions[g], Vav));
                    else if (choice == 2) insertions.Add((consonantPositions[g], Yod));
                    // choice == 0: no insertion at this gap
                }

                if (insertions.Count == 0) continue;

                // Apply insertions in order, adjusting offsets as we go
                var sb     = new System.Text.StringBuilder(skeleton);
                int offset = 0;
                foreach (var (pos, ch) in insertions)
                {
                    sb.Insert(pos + 1 + offset, ch);
                    offset++;
                }

                string variant = sb.ToString() + suffix;
                if (variant != term) variants.Add(variant);
                if (variants.Count >= MaxVariants) break;
            }

            return new List<string>(variants);
        }
    }
}
