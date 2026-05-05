using System.Collections.Generic;

namespace FtsLib.Core
{
    /// <summary>
    /// Finds the minimum-span window in a token list that covers all query terms.
    /// Uses the classic two-pointer sliding-window algorithm — O(n) in token count.
    /// </summary>
    internal static class ProximityWindow
    {
        /// <summary>
        /// Scans <paramref name="tokens"/> and returns the tightest contiguous window
        /// (by raw character span) that contains at least one occurrence of every term
        /// in <paramref name="queryTerms"/>.
        /// </summary>
        /// <returns>
        /// <c>(winStart, winEnd, score)</c> where <c>winStart</c>/<c>winEnd</c> are raw
        /// character offsets into the original string and <c>score</c> is
        /// <c>winEnd - winStart</c>.  Returns <c>(-1, -1, int.MaxValue)</c> when at least
        /// one query term is absent from the token list entirely.
        /// </returns>
        internal static (int winStart, int winEnd, int score) Find(
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms)
        {
            // Build a frequency map of required terms.
            var need = new Dictionary<string, int>(queryTerms.Count);
            foreach (var t in queryTerms)
                need[t] = need.ContainsKey(t) ? need[t] + 1 : 1;

            int required = need.Count; // distinct terms we must cover
            int covered  = 0;

            int bestStart = -1, bestEnd = -1, bestScore = int.MaxValue;
            int L = 0;

            for (int R = 0; R < tokens.Count; R++)
            {
                // Expand window to the right.
                string rt = tokens[R].Normalized;
                if (need.ContainsKey(rt))
                {
                    need[rt]--;
                    if (need[rt] == 0) covered++;
                }

                // Shrink from the left while all terms are still covered.
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
                    if (need.ContainsKey(lt))
                    {
                        need[lt]++;
                        if (need[lt] == 1) covered--;
                    }
                    L++;
                }
            }

            return (bestStart, bestEnd, bestScore);
        }
    }
}
