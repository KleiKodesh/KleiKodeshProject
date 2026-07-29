using System.Collections.Generic;

namespace FtsLib.Snippets
{
    /// <summary>
    /// Immutable term→group lookup for one query, prepared ONCE and shared by every
    /// snippet build over that query's results — across result lines AND threads.
    ///
    /// Why this exists: SnippetBuilder used to rebuild the term→group map (plus two
    /// auxiliary term sets) from the raw query groups on EVERY result line. That
    /// made per-snippet cost O(#expanded terms) — at 27k+ expanded terms (*כי*),
    /// the rebuilds dwarfed the actual tokenize-and-window work by an order of
    /// magnitude. Preparing once makes per-line snippet cost independent of the
    /// expansion size.
    ///
    /// Thread safety: all state is frozen at construction; a Dictionary supports
    /// any number of concurrent readers as long as it is never mutated. Builders
    /// on parallel snippet threads share one instance with no locking and no
    /// per-thread copies.
    /// </summary>
    internal sealed class PreparedQueryGroups
    {
        /// <summary>Term → indices of every group that contains it. Frozen after construction.</summary>
        private readonly Dictionary<string, int[]> _termToGroups;

        /// <summary>
        /// Number of AND groups the sliding window must cover. For literal-term
        /// queries every term occurrence is its own group (duplicates included),
        /// matching the historical literal-path semantics.
        /// </summary>
        public int GroupCount { get; }

        /// <summary>True when the query has no terms at all — no snippet possible.</summary>
        public bool IsEmpty => GroupCount == 0;

        private PreparedQueryGroups(Dictionary<string, int[]> termToGroups, int groupCount)
        {
            _termToGroups = termToGroups;
            GroupCount    = groupCount;
        }

        /// <summary>Group indices for <paramref name="term"/>, or false when the term
        /// is not part of the query.</summary>
        public bool TryGetGroups(string term, out int[] groups)
            => _termToGroups.TryGetValue(term, out groups);

        /// <summary>True when <paramref name="term"/> is one of the query's terms —
        /// the highlight-membership test.</summary>
        public bool ContainsTerm(string term)
            => _termToGroups.ContainsKey(term);

        // ── Factories ─────────────────────────────────────────────────

        /// <summary>
        /// Prepares AND-group semantics: OR within each group, AND across groups
        /// (the shape produced by query expansion — one group per query token).
        /// A term may appear in several groups; it maps to all of them.
        /// </summary>
        public static PreparedQueryGroups FromGroups(
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups)
        {
            var building = new Dictionary<string, List<int>>(System.StringComparer.Ordinal);

            int groupCount = queryGroups?.Count ?? 0;
            for (int gi = 0; gi < groupCount; gi++)
            {
                foreach (var term in queryGroups[gi])
                {
                    if (!building.TryGetValue(term, out var indices))
                        building[term] = indices = new List<int>(1);
                    if (!indices.Contains(gi))
                        indices.Add(gi);
                }
            }

            return Freeze(building, groupCount);
        }

        /// <summary>
        /// Prepares literal-term semantics: every term occurrence is its own AND
        /// group, so a term repeated N times must appear at least N times in the
        /// window (duplicates map to multiple group indices).
        /// </summary>
        public static PreparedQueryGroups FromLiteralTerms(IReadOnlyCollection<string> queryTerms)
        {
            var building = new Dictionary<string, List<int>>(System.StringComparer.Ordinal);

            int g = 0;
            if (queryTerms != null)
            {
                foreach (var term in queryTerms)
                {
                    if (!building.TryGetValue(term, out var indices))
                        building[term] = indices = new List<int>(1);
                    indices.Add(g);
                    g++;
                }
            }

            return Freeze(building, g);
        }

        private static PreparedQueryGroups Freeze(
            Dictionary<string, List<int>> building, int groupCount)
        {
            var frozen = new Dictionary<string, int[]>(building.Count, System.StringComparer.Ordinal);
            foreach (var kv in building)
                frozen[kv.Key] = kv.Value.ToArray();
            return new PreparedQueryGroups(frozen, groupCount);
        }
    }
}
