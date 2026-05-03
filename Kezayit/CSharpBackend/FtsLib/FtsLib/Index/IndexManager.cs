using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Index
{
    /// <summary>
    /// Public API for the full-text index.
    /// Finds which lines contain all search terms (AND semantics).
    /// </summary>
    public class IndexManager
    {
        private readonly RamIndex _index;

        public IndexManager(bool useSkipList = true)
        {
            _index = new RamIndex(useSkipList: useSkipList);
        }

        public int TermCount => _index.Count;

        public int GetTermCount(string term) => _index.GetCount(term);
        public int GetTermBytes(string term) => _index.GetBytes(term);
        public IEnumerable<int> IterateTerm(string term) => _index.GetIterator(term).AsEnumerable();

        public void Add(string term, int lineId)
        {
            _index.Add(term, lineId);
        }

        /// <summary>Persists the index to disk. Overwrites any existing files.</summary>
        public void SaveToDisk(string postingsPath, string indexDbPath)
        {
            Persistence.DiskIndexWriter.Write(_index, postingsPath, indexDbPath);
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
                if (!_index.ContainsKey(term))
                    return Enumerable.Empty<int>();

            // Rarest term first
            termList.Sort((a, b) => _index.GetCount(a).CompareTo(_index.GetCount(b)));

            return MergeIntersect(termList);
        }

        private IEnumerable<int> MergeIntersect(List<string> terms)
        {
            var iters = new RamIndex.PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                iters[i] = _index.GetIterator(terms[i]);

            // Start all iterators
            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            // iters[0] is the rarest (smallest) list — it drives the outer loop.
            // For each candidate from iters[0], SkipTo on all other lists.
            while (!iters[0].IsDone)
            {
                int  candidate = iters[0].Current;
                bool match     = true;

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
