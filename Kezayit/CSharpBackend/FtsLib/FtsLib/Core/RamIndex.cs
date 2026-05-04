using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Core
{
    /// <summary>
    /// In-memory inverted index: maps each term to its PostingStream.
    /// Implements IEnumerable so DiskIndexWriter can iterate all entries.
    ///
    /// Search algorithms are provided by PostingMatcher — the same algorithms
    /// used by IndexReader, so both index types behave identically.
    /// </summary>
    public sealed class RamIndex : Dictionary<string, RamIndexEntry>
    {
        private readonly bool _useSkipList;

        public RamIndex(bool useSkipList = true)
        {
            _useSkipList = useSkipList;
        }

        public void Add(string term, int lineId)
        {
            if (!TryGetValue(term, out var e))
            {
                e = new RamIndexEntry(_useSkipList);
                this[term] = e;
            }
            e.Add(lineId);
        }

        public int GetCount(string term) =>
            TryGetValue(term, out var e) ? e.Stream.Count : 0;

        public PostingIterator GetIterator(string term)
        {
            if (!TryGetValue(term, out var e))
                return PostingIterator.Empty;
            return new PostingIterator(e.Stream.Buffer, e.Stream.ByteLength,
                                       e.Skip, e.SkipLen);
        }

        // ── AND search ───────────────────────────────────────────────

        /// <summary>
        /// Returns line IDs that contain ALL of the supplied terms (AND semantics).
        /// Rarest term drives the outer loop; PostingMatcher.Intersect handles the merge.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = new List<string>(terms);

            foreach (var term in termList)
                if (!ContainsKey(term))
                    return Enumerable.Empty<int>();

            // Rarest term first
            termList.Sort((a, b) => GetCount(a).CompareTo(GetCount(b)));

            return AndMerge(termList);
        }

        // ── OR search ────────────────────────────────────────────────

        /// <summary>
        /// Returns line IDs that contain ANY of the supplied terms (OR semantics).
        /// Results are in ascending order with no duplicates.
        /// </summary>
        public IEnumerable<int> SearchOr(IEnumerable<string> terms)
        {
            var started = StartedIterators(terms, skipMissing: true);
            if (started.Count == 0) return Enumerable.Empty<int>();
            if (started.Count == 1) return started[0].AsEnumerable();
            return PostingMatcher.Union(started.ToArray());
        }

        // ── Mixed AND/OR search ──────────────────────────────────────

        /// <summary>
        /// Mixed AND/OR search.
        /// Each group is a set of terms joined by OR; all groups are joined by AND.
        ///
        /// Example:
        ///   Search(new[]{ new[]{"כי","אשר"}, new[]{"ביצחק"} })
        ///   → lines containing ("כי" OR "אשר") AND "ביצחק"
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<IEnumerable<string>> groups)
        {
            var groupIters = new List<PostingIterator>();

            foreach (var group in groups)
            {
                var started = StartedIterators(group, skipMissing: true);
                if (started.Count == 0)
                    return Enumerable.Empty<int>(); // AND: one group empty → no results

                if (started.Count == 1)
                    groupIters.Add(started[0]);
                else
                    groupIters.Add(new UnionIterator(started.ToArray()));
            }

            if (groupIters.Count == 0) return Enumerable.Empty<int>();
            if (groupIters.Count == 1) return groupIters[0].AsEnumerable();

            return PostingMatcher.Intersect(groupIters.ToArray());
        }

        // ── Helpers ──────────────────────────────────────────────────

        private IEnumerable<int> AndMerge(List<string> terms)
        {
            var iters = new PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                iters[i] = GetIterator(terms[i]);

            // Start all iterators
            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            foreach (var id in PostingMatcher.Intersect(iters))
                yield return id;
        }

        /// <summary>
        /// Loads iterators for the given terms and advances each one once.
        /// Missing terms are skipped when skipMissing is true.
        /// </summary>
        private List<PostingIterator> StartedIterators(IEnumerable<string> terms, bool skipMissing)
        {
            var result = new List<PostingIterator>();
            foreach (var term in terms)
            {
                if (!ContainsKey(term))
                {
                    if (!skipMissing) return null;
                    continue;
                }
                var it = GetIterator(term);
                if (it.MoveNext()) result.Add(it);
            }
            return result;
        }
    }
}
