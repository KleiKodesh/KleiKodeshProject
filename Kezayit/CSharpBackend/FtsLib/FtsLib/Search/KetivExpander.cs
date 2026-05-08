using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLib.Search
{
    /// <summary>
    /// Generates כתיב חסר / כתיב מלא spelling variants of a normalised Hebrew term
    /// by inserting ו and י at every consonant-boundary position in the stem.
    ///
    /// Algorithm:
    ///   1. Detect and preserve common grammatical suffixes (ים, ין, ית, ות, ון, ה,
    ///      תי, נו, כם, כן, הם, הן, ך) so their vowel letters are not stripped.
    ///   2. Guard word-initial and word-final ו/י as consonantal — do not strip them.
    ///   3. Strip all unprotected ו/י from the stem to get the bare consonant skeleton.
    ///   4. Single-deletion pass: remove each unprotected ו/י from the stem one at a
    ///      time (מלא → חסר direction). The suffix is never touched.
    ///   5. Skeleton insertion pass: for each gap between consecutive consonants in
    ///      the skeleton, try inserting ו, י, or nothing — 3^gaps combinations,
    ///      capped at MaxVariants. Reject masks where two consecutive gaps both
    ///      insert (artificial adjacent-vowel-letter pair rule).
    ///   6. Original stem insertion pass: insert ו or י after each non-final consonant
    ///      in the original stem. Skips if the exact same character already follows
    ///      (prevents identical adjacent pairs). Catches variants unreachable from
    ///      the skeleton alone, e.g. קויתי → קיויתי.
    ///   7. Reattach the preserved suffix to every variant.
    ///   8. Deduplicate and exclude the original term.
    ///
    /// Known limitations — require a root lexicon to fix properly:
    ///   - Medial consonantal ו/י (roots שוב, בוא, חיה) are not protected.
    ///   - יום → ים is generated (different word, unavoidable without lexicon).
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
            "\u05EA\u05D9",             // תי  (1cs verbal)
            "\u05E0\u05D5",             // נו  (1cp)
            "\u05DB\u05DD",             // כם  (2mp)
            "\u05DB\u05DF",             // כן  (2fp)
            "\u05D4\u05DD",             // הם  (3mp)
            "\u05D4\u05DF",             // הן  (3fp)
            "\u05DA",                   // ך   (2fs/2ms pronominal)
            "\u05D4",                   // ה   (he locale / 3fs — last, single char)
        };

        private static bool IsHebrewConsonant(char c)
            => c >= '\u05D0' && c <= '\u05EA' && c != Vav && c != Yod;

        /// <summary>
        /// Returns true if the ו or י at <paramref name="index"/> in
        /// <paramref name="stem"/> is consonantal and must not be stripped or deleted.
        ///
        /// Guards:
        ///   - Word-initial ו or י (index == 0)
        ///   - Word-final ו or י (index == stem.Length - 1)
        /// </summary>
        private static bool IsConsonantal(string stem, int index)
        {
            char c = stem[index];
            if (c != Vav && c != Yod) return false;
            return index == 0 || index == stem.Length - 1;
        }

        /// <summary>
        /// Returns all כתיב spelling variants of <paramref name="term"/>,
        /// excluding the original term itself.
        /// Returns an empty list when the term is too short or has no gaps.
        /// </summary>
        public static List<string> Expand(string term, int maxVariants = MaxVariants)
        {
            if (string.IsNullOrEmpty(term) || term.Length < 2)
                return new List<string>();

            // Step 1: detect and strip a grammatical suffix
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

            // Step 2: identify protected (consonantal) ו/י positions in stem
            var protectedIndices = new HashSet<int>();
            for (int i = 0; i < stem.Length; i++)
                if (IsConsonantal(stem, i))
                    protectedIndices.Add(i);

            // Step 3: build consonant skeleton (strip unprotected ו/י)
            // Track which positions in the skeleton are consonants for insertion later.
            var skeletonBuilder          = new StringBuilder(stem.Length);
            var skeletonConsonantIndices = new List<int>();

            for (int i = 0; i < stem.Length; i++)
            {
                char c             = stem[i];
                bool isVowelLetter = (c == Vav || c == Yod) && !protectedIndices.Contains(i);
                if (!isVowelLetter)
                {
                    int posInSkeleton = skeletonBuilder.Length;
                    skeletonBuilder.Append(c);
                    if (IsHebrewConsonant(c) || protectedIndices.Contains(i))
                        skeletonConsonantIndices.Add(posInSkeleton);
                }
            }

            string skeleton = skeletonBuilder.ToString();

            var variants = new HashSet<string>(StringComparer.Ordinal);

            // Step 4: single-deletion pass (מלא → חסר)
            // Remove each unprotected ו/י from the STEM one at a time, then
            // reattach the suffix.  The loop is intentionally bounded by stem.Length —
            // never term.Length — so that vowel letters inside a preserved suffix
            // (e.g. the י of ים) are never deleted, which would produce a spurious
            // form such as גלגלם from גלגלים.
            for (int i = 0; i < stem.Length; i++)
            {
                char c = stem[i];
                if (c != Vav && c != Yod) continue;
                if (protectedIndices.Contains(i)) continue;

                // Build stem-without-this-char, then reattach suffix.
                var buf = new char[stem.Length - 1];
                stem.AsSpan(0, i).CopyTo(buf);
                stem.AsSpan(i + 1).CopyTo(buf.AsSpan(i));
                string deletion = new string(buf) + suffix;

                if (deletion.Length >= 2 && deletion != term)
                    variants.Add(deletion);
            }

            if (skeletonConsonantIndices.Count < 2)
            {
                // Even with fewer than 2 skeleton consonants, still run the
                // original-stem insertion pass below before returning.
                goto OriginalStemPass;
            }

            // Always include the bare skeleton + suffix (maximally חסר form)
            {
                string bareVariant = skeleton + suffix;
                if (bareVariant != term) variants.Add(bareVariant);
            }

            // Step 5: skeleton insertion pass (חסר → מלא)
            // For each gap between consecutive consonants in the skeleton, try inserting
            // ו, י, or nothing.  Cap at 4 effective gaps (81 combinations max).
            //
            // Plausibility rule: reject any mask where two consecutive gaps both
            // receive an insertion — that would create an artificial adjacent
            // vowel-letter pair. Natural adjacency (one inserted + one already in
            // the skeleton) is legitimate and is allowed through.
            {
                int gaps          = skeletonConsonantIndices.Count - 1;
                int effectiveGaps = Math.Min(gaps, 4);
                int total         = (int)Math.Pow(3, effectiveGaps);

                for (int mask = 0; mask < total && variants.Count < maxVariants; mask++)
                {
                    // Decode base-3 mask: digit g = {0=none, 1=ו, 2=י} for gap g
                    var insertions = new List<(int afterIndex, char ch)>(effectiveGaps);
                    int m = mask;
                    for (int g = 0; g < effectiveGaps; g++)
                    {
                        int choice = m % 3;
                        m /= 3;
                        if (choice == 1) insertions.Add((skeletonConsonantIndices[g], Vav));
                        else if (choice == 2) insertions.Add((skeletonConsonantIndices[g], Yod));
                    }

                    if (insertions.Count == 0) continue;

                    // Reject masks where two consecutive gaps both insert —
                    // artificial adjacent vowel-letter pair.
                    bool hasAdjacentInsertions = false;
                    for (int i = 0; i < insertions.Count - 1; i++)
                    {
                        if (insertions[i + 1].afterIndex == insertions[i].afterIndex + 1)
                        {
                            hasAdjacentInsertions = true;
                            break;
                        }
                    }
                    if (hasAdjacentInsertions) continue;

                    // Apply insertions left-to-right, adjusting offset as chars are added.
                    var sb     = new StringBuilder(skeleton);
                    int offset = 0;
                    foreach (var (afterIndex, ch) in insertions)
                    {
                        sb.Insert(afterIndex + 1 + offset, ch);
                        offset++;
                    }

                    string variant = sb.ToString() + suffix;
                    if (variant != term) variants.Add(variant);
                }
            }

            OriginalStemPass:

            // Step 6: original stem insertion pass
            // Insert ו or י after each non-final consonant in the original stem.
            // This catches variants unreachable from the skeleton alone — e.g.
            // קויתי → קיויתי — because protected letters or existing vowel letters
            // in the stem collapse skeleton gaps.
            //
            // Rule: skip if the exact same character already follows the insertion
            // point (prevents identical adjacent pairs like וו or יי).
            // Skip the last position in the stem (would insert between stem and suffix).
            for (int i = 0; i < stem.Length - 1 && variants.Count < maxVariants; i++)
            {
                if (!IsHebrewConsonant(stem[i])) continue;

                foreach (char ch in new[] { Vav, Yod })
                {
                    // Skip if this exact character already follows — identical adjacent pair
                    if (stem[i + 1] == ch) continue;

                    var sb = new StringBuilder(stem);
                    sb.Insert(i + 1, ch);
                    string variant = sb.ToString() + suffix;
                    if (variant != term && variant.Length >= 2)
                        variants.Add(variant);
                }
            }

            return new List<string>(variants);
        }
    }
}