using System.Collections.Generic;
using System.Linq;
using FtsLib.Persistence;

namespace FtsLib.Index
{
    /// <summary>
    /// Public API for the full-text index.
    ///
    /// Ingestion
    /// ---------
    /// Builds a RamIndex until it reaches <see cref="FlushThreshold"/> distinct
    /// terms, then flushes to a level-0 segment on disk via <see cref="SegmentStore"/>.
    /// The SegmentStore applies LSM-style tiered merging (fanout=4, 3 levels) so
    /// the number of live segment files stays small throughout ingestion.
    ///
    /// When <see cref="SaveToDisk"/> is called, the final partial RamIndex is
    /// flushed, all remaining segments are force-merged into one, and the result
    /// is written as postings.dat + Meta.db — the same format DiskIndexReader
    /// expects, so the read path is completely unchanged.
    ///
    /// Search (RAM path)
    /// -----------------
    /// Still works against the in-memory RamIndex for the current (unflushed)
    /// segment only. Full cross-segment search goes through DiskIndexReader after
    /// SaveToDisk.
    /// </summary>
    public class IndexManager
    {
        /// <summary>
        /// Flush the RamIndex to disk when it reaches this many distinct terms.
        /// 500 000 keeps the Dictionary well within its efficient operating range
        /// while still holding a large chunk of the corpus in RAM at once.
        /// </summary>
        public int FlushThreshold { get; set; } = 500_000;

        private RamIndex      _ramIndex;
        private SegmentStore  _store;
        private readonly bool _useSkipList;

        public IndexManager(bool useSkipList = true)
        {
            _useSkipList = useSkipList;
            _ramIndex    = new RamIndex(useSkipList: useSkipList);
        }

        // ── Ingestion ────────────────────────────────────────────────

        public void Add(string term, int lineId)
        {
            _ramIndex.Add(term, lineId);

            if (_ramIndex.Count >= FlushThreshold)
                FlushRam();
        }

        /// <summary>
        /// Flush the current RamIndex to a segment and reset it.
        /// Called automatically when the threshold is hit, or can be called
        /// manually (e.g. at the end of ingestion before SaveToDisk).
        /// </summary>
        private void FlushRam()
        {
            if (_ramIndex.Count == 0) return;

            EnsureStore();
            _store.Flush(_ramIndex);
            _ramIndex = new RamIndex(useSkipList: _useSkipList);
        }

        private void EnsureStore()
        {
            // Store is created lazily so callers that never exceed the threshold
            // (small corpora) pay zero overhead.
            if (_store == null)
                _store = new SegmentStore(_storeDir ?? System.IO.Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory, "fts-segments"));
        }

        // Allow the caller to set the segment directory before ingestion starts.
        private string _storeDir;
        public void SetSegmentDir(string dir) { _storeDir = dir; }

        // ── Persist ──────────────────────────────────────────────────

        /// <summary>
        /// Flush any remaining RAM data, merge all segments, and write the
        /// final postings.dat + Meta.db that DiskIndexReader can open.
        /// </summary>
        public void SaveToDisk(string postingsPath, string indexDbPath)
        {
            if (_store == null)
            {
                // Never exceeded the threshold — use the fast single-pass writer
                DiskIndexWriter.Write(_ramIndex, postingsPath, indexDbPath);
                return;
            }

            // Flush the tail (may be < FlushThreshold terms)
            FlushRam();

            // Merge all segments and write final files
            _store.Commit(postingsPath, indexDbPath);
        }

        // ── RAM-only search (current unflushed segment) ───────────────

        public int TermCount      => _ramIndex.Count;
        public int GetTermCount(string term) => _ramIndex.GetCount(term);
        public int GetTermBytes(string term) => _ramIndex.GetBytes(term);
        public IEnumerable<int> IterateTerm(string term) =>
            _ramIndex.GetIterator(term).AsEnumerable();

        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = new List<string>(terms);

            foreach (var term in termList)
                if (!_ramIndex.ContainsKey(term))
                    return Enumerable.Empty<int>();

            termList.Sort((a, b) => _ramIndex.GetCount(a).CompareTo(_ramIndex.GetCount(b)));
            return MergeIntersect(termList);
        }

        private IEnumerable<int> MergeIntersect(List<string> terms)
        {
            var iters = new RamIndex.PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                iters[i] = _ramIndex.GetIterator(terms[i]);

            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            while (!iters[0].IsDone)
            {
                int  candidate = iters[0].Current;
                bool match     = true;

                for (int i = 1; i < iters.Length; i++)
                {
                    if (!iters[i].SkipTo(candidate))
                        yield break;

                    if (iters[i].Current != candidate)
                    {
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
            }
        }
    }
}
