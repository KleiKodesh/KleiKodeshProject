using SearchEngine.Tokenization;
using System;
using System.Collections.Generic;
using System.Text;

namespace SearchEngine.Snippets
{
    /// <summary>
    /// Generates highlighted HTML snippets using direct string lookup rather than
    /// Lucene's token-stream highlighter.
    ///
    /// Algorithm
    /// ---------
    /// 1. For each query term group, call NikudSkipIterator.FindAll for every
    ///    OR-alternative term. Strategy A (IndexOf) is tried first; strategy B
    ///    (char-by-char walk) is the fallback when nikud is interleaved in the source.
    ///    All occurrences across all terms in a group are unioned.
    ///
    /// 2. If any group has zero hits the line is a false positive — return NoMatch.
    ///
    /// 3. Find the tightest window: the combination of one hit per group (in group
    ///    order) that minimises total char gap = sum of (nextHit.RawStart - prevHit.RawEnd).
    ///    Score = that total gap. Overlapping hits contribute 0 gap.
    ///
    /// 4. Expand the window by contextChars on each side (clamped to string bounds).
    ///
    /// 5. Render: copy rawHtml[snapStart..snapEnd] to a string, inserting
    ///    &lt;mark&gt; / &lt;/mark&gt; around each hit span. HTML tags in the
    ///    source pass through unchanged.
    ///
    /// 6. isWeakMatch = score &gt; maxCharDistance.
    ///
    /// Thread safety: all state is local to each Build call. No shared mutable state.
    /// </summary>
    internal static class SnippetGenerator
    {
        private const string PreTag  = "<mark>";
        private const string PostTag = "</mark>";

        /// <summary>Default characters of context shown on each side of the match window.</summary>
        public const int DefaultContextChars = 200;

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Builds a highlighted HTML snippet.
        ///
        /// queryGroups  — one group per AND slot; each group is a set of OR-alternative
        ///                plain (nikud-free) stems. For wildcard queries the stem is the
        ///                pattern with '*' stripped (e.g. "ישר" for "ישר*").
        /// maxCharDistance — gap score threshold; results above this are flagged isWeakMatch.
        /// contextChars    — raw chars of context to show on each side of the match.
        /// isWeakMatch     — output: true when the gap score exceeds maxCharDistance.
        /// </summary>
        public static SnippetResult Build(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            int                                        maxCharDistance,
            int                                        contextChars,
            out bool                                   isWeakMatch)
        {
            isWeakMatch = false;

            if (string.IsNullOrEmpty(rawHtml) || queryGroups == null || queryGroups.Count == 0)
                return SnippetResult.NoMatch;

            // Step 1 — collect all occurrences per group.
            var occurrencesByGroup = new List<List<(int RawStart, int RawEnd)>>(queryGroups.Count);
            for (int g = 0; g < queryGroups.Count; g++)
            {
                var groupHits = new List<(int, int)>();
                foreach (string term in queryGroups[g])
                {
                    if (string.IsNullOrEmpty(term)) continue;
                    var hits = NikudSkipIterator.FindAll(rawHtml, term);
                    Console.WriteLine($"[SnippetGenerator] group={g} term=\"{term}\" hits={hits.Count}");
                    foreach (var hit in hits)
                        groupHits.Add(hit);
                }
                groupHits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                occurrencesByGroup.Add(Deduplicate(groupHits));
            }

            // Step 2 — false positive check.
            for (int g = 0; g < queryGroups.Count; g++)
            {
                if (occurrencesByGroup[g].Count == 0)
                {
                    Console.WriteLine($"[SnippetGenerator] group={g} has zero hits — NoMatch");
                    return SnippetResult.NoMatch;
                }
            }

            // Step 3 — tightest window.
            var (windowHits, gapScore) = FindTightestWindow(occurrencesByGroup);

            if (windowHits.Count == 0)
            {
                isWeakMatch = false;
                return SnippetResult.NoMatch;
            }

            // Step 4 — expand by contextChars.
            int matchStart = windowHits[0].RawStart;
            int matchEnd   = windowHits[windowHits.Count - 1].RawEnd;
            int snapStart  = Math.Max(0,              matchStart - contextChars);
            int snapEnd    = Math.Min(rawHtml.Length, matchEnd   + contextChars);

            // Step 5 — render.
            string html = Render(rawHtml, snapStart, snapEnd, windowHits);

            // Step 6 — weak match flag.
            isWeakMatch = gapScore > maxCharDistance;

            return new SnippetResult(html, gapScore, gapScore, true);
        }

        // ── Tightest window ───────────────────────────────────────────

        /// <summary>
        /// Finds the combination of one hit per group that minimises the total char
        /// gap between adjacent hits in the window. Groups may appear in any order.
        ///
        /// Uses a sliding window over hits sorted by RawStart. Maintains a count of
        /// how many groups are covered; shrinks from the left when all groups are
        /// covered to find the tightest span. Gap = span - sum of match lengths.
        /// </summary>
        private static (List<(int RawStart, int RawEnd)> Hits, int Score) FindTightestWindow(
            List<List<(int RawStart, int RawEnd)>> occurrencesByGroup)
        {
            int numGroups = occurrencesByGroup.Count;

            if (numGroups == 1)
            {
                var h = occurrencesByGroup[0][0];
                return (new List<(int RawStart, int RawEnd)> { h }, 0);
            }

            // Flatten all hits with group labels, sorted by RawStart.
            var all = new List<(int RawStart, int RawEnd, int Group)>();
            for (int g = 0; g < numGroups; g++)
                foreach (var h in occurrencesByGroup[g])
                    all.Add((h.RawStart, h.RawEnd, g));
            all.Sort((a, b) => a.RawStart.CompareTo(b.RawStart));

            int total = all.Count;

            // Sliding window: track how many hits of each group are in the window.
            var countInWindow = new int[numGroups];
            int groupsCovered = 0;
            int bestScore     = int.MaxValue;
            int bestLeft      = -1;
            int bestRight     = -1;
            int left          = 0;

            for (int right = 0; right < total; right++)
            {
                int rg = all[right].Group;
                if (countInWindow[rg] == 0) groupsCovered++;
                countInWindow[rg]++;

                // Shrink from left while all groups still covered.
                while (groupsCovered == numGroups)
                {
                    // Compute gap score for this window:
                    // gap = (rightEnd - leftStart) - sum of all hit lengths in window.
                    // Simpler proxy that still finds the tightest window: use span only.
                    int span = all[right].RawEnd - all[left].RawStart;
                    if (span < bestScore)
                    {
                        bestScore = span;
                        bestLeft  = left;
                        bestRight = right;
                    }

                    int lg = all[left].Group;
                    countInWindow[lg]--;
                    if (countInWindow[lg] == 0) groupsCovered--;
                    left++;
                }
            }

            if (bestLeft < 0)
                return (new List<(int RawStart, int RawEnd)>(), int.MaxValue);

            // Reconstruct: pick one hit per group from the best window.
            // Use the first occurrence of each group in [bestLeft..bestRight].
            var chosen = new (int RawStart, int RawEnd)[numGroups];
            var filled = new bool[numGroups];
            int filledCount = 0;
            for (int i = bestLeft; i <= bestRight && filledCount < numGroups; i++)
            {
                int g = all[i].Group;
                if (!filled[g])
                {
                    chosen[g] = (all[i].RawStart, all[i].RawEnd);
                    filled[g] = true;
                    filledCount++;
                }
            }

            // Sort chosen hits by RawStart for rendering.
            var result = new List<(int RawStart, int RawEnd)>(numGroups);
            for (int g = 0; g < numGroups; g++)
                result.Add(chosen[g]);
            result.Sort((a, b) => a.RawStart.CompareTo(b.RawStart));

            // Gap score = sum of gaps between adjacent hits.
            int gapScore = 0;
            for (int i = 1; i < result.Count; i++)
            {
                int gap = result[i].RawStart - result[i - 1].RawEnd;
                if (gap > 0) gapScore += gap;
            }

            return (result, gapScore);
        }

        // ── Renderer ──────────────────────────────────────────────────

        /// <summary>
        /// Copies rawHtml[snapStart..snapEnd], inserting mark tags around each hit.
        /// HTML tags in the source pass through unchanged.
        /// Prepends/appends '…' when the snippet is a sub-range of the source.
        /// </summary>
        private static string Render(
            string                           rawHtml,
            int                              snapStart,
            int                              snapEnd,
            List<(int RawStart, int RawEnd)> hits)
        {
            var sb = new StringBuilder(snapEnd - snapStart + hits.Count * 20);

            if (snapStart > 0) sb.Append('…');

            // Filter and sort hits to those within the snippet range.
            var visibleHits = new List<(int Start, int End)>(hits.Count);
            foreach (var (rs, re) in hits)
            {
                if (re <= snapStart || rs >= snapEnd) continue;
                int start = rs < snapStart ? snapStart : rs;
                int end   = re > snapEnd   ? snapEnd   : re;
                visibleHits.Add((start, end));
            }
            visibleHits.Sort((a, b) => a.Start.CompareTo(b.Start));

            int pos = snapStart;
            foreach (var (start, end) in visibleHits)
            {
                if (start > pos)
                    sb.Append(rawHtml, pos, start - pos);

                sb.Append(PreTag);
                sb.Append(rawHtml, start, end - start);
                sb.Append(PostTag);
                pos = end;
            }

            if (pos < snapEnd)
                sb.Append(rawHtml, pos, snapEnd - pos);

            if (snapEnd < rawHtml.Length) sb.Append('…');

            return sb.ToString();
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Removes duplicate and fully-overlapping hits from a sorted list.
        /// Input must already be sorted by RawStart ascending.
        /// </summary>
        private static List<(int RawStart, int RawEnd)> Deduplicate(
            List<(int RawStart, int RawEnd)> sorted)
        {
            if (sorted.Count <= 1) return sorted;
            var result = new List<(int RawStart, int RawEnd)>(sorted.Count) { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                var prev = result[result.Count - 1];
                var curr = sorted[i];
                if (curr.RawStart >= prev.RawEnd)
                    result.Add(curr);
            }
            return result;
        }
    }
}
