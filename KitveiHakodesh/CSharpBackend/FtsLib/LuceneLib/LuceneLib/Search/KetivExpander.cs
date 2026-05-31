using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneLib.Search
{
    /// <summary>
    /// Generates כתיב חסר / כתיב מלא spelling variants of a normalised Hebrew term
    /// by inserting ו and י at every consonant-boundary position in the stem.
    ///
    /// Ported from FtsLib.Search.KetivExpander — pure string logic, no index dependency.
    /// See the FtsLib source for the full algorithm description.
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
            "\u05EA\u05D9",             // תי
            "\u05E0\u05D5",             // נו
            "\u05DB\u05DD",             // כם
            "\u05DB\u05DF",             // כן
            "\u05D4\u05DD",             // הם
            "\u05D4\u05DF",             // הן
            "\u05DA",                   // ך
            "\u05D4",                   // ה
        };

        private static bool IsHebrewConsonant(char c)
            => c >= '\u05D0' && c <= '\u05EA' && c != Vav && c != Yod;

        private static bool IsConsonantal(string stem, int index)
        {
            char c = stem[index];
            if (c != Vav && c != Yod) return false;
            return index == 0 || index == stem.Length - 1;
        }

        /// <summary>
        /// Returns all כתיב spelling variants of <paramref name="term"/>,
        /// excluding the original term itself.
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
            for (int i = 0; i < stem.Length; i++)
            {
                char c = stem[i];
                if (c != Vav && c != Yod) continue;
                if (protectedIndices.Contains(i)) continue;

                var buf = new char[stem.Length - 1];
                stem.AsSpan(0, i).CopyTo(buf);
                stem.AsSpan(i + 1).CopyTo(buf.AsSpan(i));
                string deletion = new string(buf) + suffix;

                if (deletion.Length >= 2 && deletion != term)
                    variants.Add(deletion);
            }

            if (skeletonConsonantIndices.Count < 2)
                goto OriginalStemPass;

            // Always include the bare skeleton + suffix (maximally חסר form)
            {
                string bareVariant = skeleton + suffix;
                if (bareVariant != term) variants.Add(bareVariant);
            }

            // Step 5: skeleton insertion pass (חסר → מלא)
            {
                int gaps          = skeletonConsonantIndices.Count - 1;
                int effectiveGaps = Math.Min(gaps, 4);
                int total         = (int)Math.Pow(3, effectiveGaps);

                for (int mask = 0; mask < total && variants.Count < maxVariants; mask++)
                {
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
            for (int i = 0; i < stem.Length - 1 && variants.Count < maxVariants; i++)
            {
                if (!IsHebrewConsonant(stem[i])) continue;

                foreach (char ch in new[] { Vav, Yod })
                {
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
