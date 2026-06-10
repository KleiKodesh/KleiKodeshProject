using FtsLib.Search;
using System.Collections.Generic;

namespace FtsLib.Indexing
{
    /// <summary>
    /// In-memory inverted index: maps each term to its PostingStream + skip list.
    ///
    /// Search algorithms are provided by PostingIntersector — the same algorithms
    /// used by IndexReader, so both index types behave identically.
    /// </summary>
    internal sealed class RamIndex : Dictionary<string, RamIndexEntry>
    {
        private readonly bool _useSkipList;

        public RamIndex(bool useSkipList = true) { _useSkipList = useSkipList; }

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

        // ── AND ──────────────────────────────────────────────────────

        public IEnumerable<int> Search(IEnumerable<string> terms) =>
            PostingIntersector.AndSearch(terms, GetIterator, GetCount);

        // ── OR ───────────────────────────────────────────────────────

        public IEnumerable<int> SearchOr(IEnumerable<string> terms) =>
            PostingIntersector.OrSearch(terms, GetIterator);

        // ── Mixed AND/OR ─────────────────────────────────────────────

        public IEnumerable<int> Search(IEnumerable<IEnumerable<string>> groups) =>
            PostingIntersector.MixedSearch(groups, GetIterator);
    }
}
