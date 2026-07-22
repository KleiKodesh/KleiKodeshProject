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

        /// <summary>True when the edit distance between a and b is ≤ maxEdits. Short-circuits
        /// on length difference alone (an edit can change length by at most 1 per edit).</summary>
        private static bool LevenshteinWithin(string a, string b, int maxEdits)
        {
            if (System.Math.Abs(a.Length - b.Length) > maxEdits) return false;

            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = System.Math.Min(System.Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length] <= maxEdits;
        }
    }
}
