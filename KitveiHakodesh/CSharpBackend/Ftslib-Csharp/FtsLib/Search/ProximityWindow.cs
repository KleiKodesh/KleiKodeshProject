using FtsLib.Tokenization;
using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Finds the minimum-span window in a token list that covers all query groups.
    /// Uses the classic two-pointer sliding-window algorithm — O(n) in token count.
    ///
    /// Each group is a set of alternative terms (OR semantics): the window must
    /// contain at least one term from every group. This correctly handles fuzzy and
    /// wildcard queries where multiple expanded forms are alternatives for one slot.
    /// </summary>
    internal static class ProximityWindow
    {
        /// <summary>
        /// Scans <paramref name="tokens"/> and returns the tightest contiguous window
        /// (by raw character span) that contains at least one occurrence of every group
        /// in <paramref name="queryGroups"/>.
        ///
        /// Each group is a collection of alternative terms (OR within the group).
        /// Across groups the semantics are AND — every group must be satisfied.
        /// </summary>
        /// <returns>
        /// <c>(winStart, winEnd, score)</c> where <c>score = winEnd - winStart</c>.
        /// Returns <c>(-1, -1, int.MaxValue)</c> when at least one group has no
        /// representative in the token list.
        /// </returns>
        internal static (int winStart, int winEnd, int score) Find(
            List<TextToken>                          tokens,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups)
        {
            if (queryGroups == null || queryGroups.Count == 0)
                return (-1, -1, int.MaxValue);

            // Map every term to its group index so we can track coverage.
            // A term that appears in multiple groups is assigned to the first one.
            var termToGroup = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int g = 0; g < queryGroups.Count; g++)
                foreach (var t in queryGroups[g])
                    if (!termToGroup.ContainsKey(t))
                        termToGroup[t] = g;

            // Per-group: how many tokens in the current window satisfy this group.
            var groupCount = new int[queryGroups.Count];
            int covered    = 0;   // number of groups with at least one token in window
            int required   = queryGroups.Count;

            int bestStart = -1, bestEnd = -1, bestScore = int.MaxValue;
            int L = 0;

            for (int R = 0; R < tokens.Count; R++)
            {
                // Expand window to the right.
                string rt = tokens[R].Normalized;
                if (termToGroup.TryGetValue(rt, out int rg))
                {
                    if (groupCount[rg]++ == 0) covered++;
                }

                // Shrink from the left while all groups are still covered.
                while (covered == required)
                {
                    int span = tokens[R].RawEnd - tokens[L].RawStart;
                    if (span < bestScore)
                    {
                        bestScore = span;
                        bestStart = tokens[L].RawStart;
                        bestEnd   = tokens[R].RawEnd;
                    }

                    string lt = tokens[L].Normalized;
                    if (termToGroup.TryGetValue(lt, out int lg))
                    {
                        if (--groupCount[lg] == 0) covered--;
                    }
                    L++;
                }
            }

            return (bestStart, bestEnd, bestScore);
        }

        /// <summary>
        /// Convenience overload for single-term-per-group queries (literal searches).
        /// Each term is treated as its own group of one.
        /// </summary>
        internal static (int winStart, int winEnd, int score) Find(
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms)
        {
            // Wrap each term as a single-element group.
            var groups = new List<IReadOnlyCollection<string>>(queryTerms.Count);
            foreach (var t in queryTerms)
                groups.Add(new[] { t });
            return Find(tokens, groups);
        }
    }
}
