using System;
using System.Collections.Generic;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionReader : IDisposable
    {
        private const int MaxPartials = 100;

        private BloomFilterData[] _filters;
        private int _count;
        public short ChunkSize { get; private set; }

        public BloomFilterCollectionReader(string id)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", $"{id}.dat");
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var r = new BinaryReader(fs))
            {
                _count = r.ReadInt32();
                ChunkSize = r.ReadInt16();
                r.ReadInt32();
                _filters = new BloomFilterData[_count];
                for (int i = 0; i < _count; i++)
                {
                    int bits = r.ReadInt32(), hashes = r.ReadInt32();
                    int firstLineId = r.ReadInt32(), lastLineId = r.ReadInt32();
                    int byteLen = (bits + 7) / 8;
                    _filters[i] = new BloomFilterData { Filter = new BloomFilter(r.ReadBytes(byteLen), bits, hashes), Id = i, FirstLineId = firstLineId, LastLineId = lastLineId };
                }
            }
        }

        /// <summary>
        /// Scans all Bloom filters in a single sequential pass and yields hits lazily.
        /// Perfect hits (all terms present) are yielded immediately in DB order so the
        /// caller can start DB verification before the scan finishes.
        /// Partial hits accumulate and are yielded after all perfect hits.
        /// </summary>
        public IEnumerable<SearchResult> Search(string[] terms)
        {
            // Sort longest-first: longer Hebrew words are rarer, so they fail fast
            // on most filters and short-circuit the AND check early.
            var sorted = (string[])terms.Clone();
            Array.Sort(sorted, (a, b) => b.Length.CompareTo(a.Length));

            bool requireAll = sorted.Length > 1;
            int maxScore = sorted.Length;

            var partials = new List<SearchResult>(MaxPartials);
            int lowestPartial = 0;

            for (int i = 0; i < _count; i++)
            {
                int score = ScoreFilter(_filters[i].Filter, sorted, requireAll);

                if (score == maxScore)
                {
                    yield return new SearchResult
                    {
                        Id = _filters[i].Id,
                        Score = score,
                        FirstLineId = _filters[i].FirstLineId,
                        LastLineId = _filters[i].LastLineId
                    };
                }
                else if (score > 0)
                {
                    TryAddPartial(partials, ref lowestPartial, new SearchResult
                    {
                        Id = _filters[i].Id,
                        Score = score,
                        FirstLineId = _filters[i].FirstLineId,
                        LastLineId = _filters[i].LastLineId
                    });
                }
            }

            // Yield partials sorted by score descending after all perfect hits
            partials.Sort((a, b) => b.Score.CompareTo(a.Score));
            foreach (var p in partials)
                yield return p;
        }

        // For AND queries (requireAll=true) returns 0 immediately on the first missing term.
        // Terms should be pre-sorted longest-first so the rarest term is checked first.
        private static int ScoreFilter(BloomFilter filter, string[] terms, bool requireAll)
        {
            int score = 0;
            for (int j = 0; j < terms.Length; j++)
            {
                if (filter.Contains(terms[j]))
                    score++;
                else if (requireAll)
                    return 0;
            }
            return score;
        }

        private static void TryAddPartial(List<SearchResult> partials, ref int lowestPartial, SearchResult candidate)
        {
            if (partials.Count < MaxPartials)
            {
                partials.Add(candidate);
                if (partials.Count == MaxPartials)
                {
                    lowestPartial = int.MaxValue;
                    foreach (var p in partials)
                        if (p.Score < lowestPartial) lowestPartial = p.Score;
                }
                return;
            }

            if (candidate.Score <= lowestPartial) return;

            int worstIdx = 0, worstScore = partials[0].Score;
            for (int j = 1; j < partials.Count; j++)
                if (partials[j].Score < worstScore) { worstScore = partials[j].Score; worstIdx = j; }

            partials[worstIdx] = candidate;

            lowestPartial = int.MaxValue;
            foreach (var p in partials)
                if (p.Score < lowestPartial) lowestPartial = p.Score;
        }

        public void Dispose() { }
    }

    public struct BloomFilterData { public BloomFilter Filter; public int Id; public int FirstLineId; public int LastLineId; }
    public struct SearchResult { public int Id; public int Score; public int FirstLineId; public int LastLineId; }
}
