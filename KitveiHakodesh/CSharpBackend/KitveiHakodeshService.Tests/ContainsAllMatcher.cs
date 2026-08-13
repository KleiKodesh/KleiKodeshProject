// Contains-all check helper — mirrors what CatalogTocIndex.Search actually treats as a
// match for a single query token, so the spec's contains-all check doesn't flag hits that
// only matched through a non-literal path (skeleton variant, ה-prefix, or the fuzzy
// fallback) as violations. See CatalogTocIndex.WordClause / VariantIndex for the real logic
// this mirrors.
using System.Collections.Generic;
using KitveiHakodeshService.Catalog;

namespace KitveiHakodeshService.Tests
{
    public static class ContainsAllMatcher
    {
        /// <summary>True when query token <paramref name="queryToken"/> is satisfied by
        /// SOME token in <paramref name="docTokens"/> — literally, as a חסר/מלא skeleton
        /// variant, as a ה-prefix variant, or (for 3+ char tokens) within the fuzzy
        /// fallback's edit-distance threshold (1 for ≤5 chars, 2 for longer).</summary>
        public static bool TokenMatches(string queryToken, HashSet<string> docTokens)
        {
            if (docTokens.Contains(queryToken)) return true;

            string? stripped = CatalogTocTextRules.StripHePrefix(queryToken);
            if (stripped is not null && docTokens.Contains(stripped)) return true;
            string withHe = "ה" + queryToken;
            if (docTokens.Contains(withHe)) return true;

            var qDecomp = CatalogTocTextRules.DecomposeSkeleton(queryToken);
            foreach (var docTok in docTokens)
                if (CatalogTocTextRules.AreSkeletonVariants(qDecomp, CatalogTocTextRules.DecomposeSkeleton(docTok)))
                    return true;

            if (queryToken.Length >= 3)
            {
                int maxEdits = queryToken.Length <= 5 ? 1 : 2;
                foreach (var docTok in docTokens)
                    if (LevenshteinWithin(queryToken, docTok, maxEdits)) return true;
            }

            return false;
        }

        /// <summary>True when the DAMERAU-Levenshtein distance between a and b is ≤ maxEdits.
        ///
        /// Damerau, not plain Levenshtein, because that is what Lucene's FuzzyQuery uses:
        /// it counts a transposition of two adjacent characters as ONE edit. Plain
        /// Levenshtein scores such a pair as 2 and would reject a hit the engine accepts —
        /// e.g. דעות / עדות (first two letters swapped) matches at maxEdits=1 in Lucene, so
        /// a plain-Levenshtein checker reported it as a contains-all violation when the
        /// search was in fact behaving as designed.
        ///
        /// Uses the full matrix (not two rolling rows) since the transposition term needs
        /// row i-2.</summary>
        private static bool LevenshteinWithin(string a, string b, int maxEdits)
        {
            if (System.Math.Abs(a.Length - b.Length) > maxEdits) return false;

            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int best = System.Math.Min(
                        System.Math.Min(d[i, j - 1] + 1, d[i - 1, j] + 1),
                        d[i - 1, j - 1] + cost);

                    // Transposition of two adjacent characters counts as a single edit.
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        best = System.Math.Min(best, d[i - 2, j - 2] + 1);

                    d[i, j] = best;
                }
            }
            return d[a.Length, b.Length] <= maxEdits;
        }
    }
}
