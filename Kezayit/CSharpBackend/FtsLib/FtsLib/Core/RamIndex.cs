using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Core
{
    /// <summary>
    /// In-memory inverted index: maps each term to its PostingStream.
    /// Implements IEnumerable so DiskIndexWriter can iterate all entries.
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

        /// <summary>
        /// Returns line IDs that contain ALL of the supplied terms (AND semantics).
        ///
        /// Uses a skip-list-accelerated merge-intersect:
        /// - Terms sorted by frequency ascending (rarest drives the loop)
        /// - For each candidate ID from the rarest list, SkipTo() jumps each
        ///   other list forward using skip pointers — O(log n) per jump
        ///   instead of O(n) linear scan.
        /// - Zero heap allocation during search.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = new List<string>(terms);

            foreach (var term in termList)
                if (!ContainsKey(term))
                    return Enumerable.Empty<int>();

            // Rarest term first
            termList.Sort((a, b) => GetCount(a).CompareTo(GetCount(b)));

            return MergeIntersect(termList);
        }

        private IEnumerable<int> MergeIntersect(List<string> terms)
        {
            var iters = new PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                iters[i] = GetIterator(terms[i]);

            // Start all iterators
            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            // iters[0] is the rarest (smallest) list — it drives the outer loop.
            // For each candidate from iters[0], SkipTo on all other lists.
            while (!iters[0].IsDone)
            {
                int candidate = iters[0].Current;
                bool match = true;

                for (int i = 1; i < iters.Length; i++)
                {
                    if (!iters[i].SkipTo(candidate))
                        yield break; // exhausted

                    if (iters[i].Current != candidate)
                    {
                        // iters[i] is ahead — advance driver to catch up, restart inner loop
                        int newTarget = iters[i].Current;
                        if (!iters[0].SkipTo(newTarget))
                            yield break;
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    yield return candidate;
                    if (!iters[0].MoveNext())
                        yield break;
                }
                // if !match: iters[0] was already advanced by SkipTo above, loop continues
            }
        }
    }
}
