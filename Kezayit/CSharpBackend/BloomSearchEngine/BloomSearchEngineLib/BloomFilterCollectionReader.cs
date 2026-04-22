using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionReader : IDisposable
    {
        private const int CacheLineSize = 64;
        private const int MaxPartialPerThread = 100;

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
                    int pad = (int)((CacheLineSize - (16 + byteLen) % CacheLineSize) % CacheLineSize);
                    if (pad > 0) r.ReadBytes(pad);
                }
            }
        }

        public SearchResult[] Search(string[] terms)
        {
            // Sort longest-first: longer Hebrew words are rarer, so they fail fast
            // on most filters and short-circuit the AND check early.
            var sorted = (string[])terms.Clone();
            Array.Sort(sorted, (a, b) => b.Length.CompareTo(a.Length));

            bool requireAll = sorted.Length > 1;

            int threads = Environment.ProcessorCount;
            int chunkSize = (_count + threads - 1) / threads;
            var threadResults = new ThreadSearchResults[threads];

            Parallel.For(0, threads, t =>
            {
                int start = t * chunkSize;
                int end = Math.Min(start + chunkSize, _count);
                if (start < _count)
                    threadResults[t] = SearchChunk(sorted, requireAll, start, end);
            });

            return MergeResults(threadResults, terms.Length);
        }

        private ThreadSearchResults SearchChunk(string[] terms, bool requireAll, int start, int end)
        {
            int maxScore = terms.Length;
            var perfect = new List<SearchResult>((end - start) / 10);
            var partial = new List<SearchResult>(MaxPartialPerThread);
            int lowestPartial = 0;

            for (int i = start; i < end; i++)
            {
                int score = ScoreFilter(_filters[i].Filter, terms, requireAll);

                if (score == maxScore)
                {
                    perfect.Add(new SearchResult { Id = _filters[i].Id, Score = score, FirstLineId = _filters[i].FirstLineId, LastLineId = _filters[i].LastLineId });
                }
                else if (score > 0)
                {
                    TryAddPartial(partial, ref lowestPartial, new SearchResult { Id = _filters[i].Id, Score = score, FirstLineId = _filters[i].FirstLineId, LastLineId = _filters[i].LastLineId });
                }
            }

            return new ThreadSearchResults { PerfectMatches = perfect, PartialMatches = partial };
        }

        // For AND queries (requireAll=true) returns immediately on the first missing term.
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

        private static void TryAddPartial(List<SearchResult> partial, ref int lowestPartial, SearchResult candidate)
        {
            if (partial.Count < MaxPartialPerThread)
            {
                partial.Add(candidate);
                if (partial.Count == MaxPartialPerThread)
                {
                    lowestPartial = int.MaxValue;
                    foreach (var p in partial)
                        if (p.Score < lowestPartial) lowestPartial = p.Score;
                }
                return;
            }

            if (candidate.Score <= lowestPartial) return;

            int worstIdx = 0, worstScore = partial[0].Score;
            for (int j = 1; j < partial.Count; j++)
                if (partial[j].Score < worstScore) { worstScore = partial[j].Score; worstIdx = j; }

            partial[worstIdx] = candidate;

            // Rescan for the new lowest after replacement
            lowestPartial = int.MaxValue;
            foreach (var p in partial)
                if (p.Score < lowestPartial) lowestPartial = p.Score;
        }

        private static SearchResult[] MergeResults(ThreadSearchResults[] threadResults, int maxScore)
        {
            var allPerfect = new List<SearchResult>();
            var allPartial = new List<SearchResult>();

            foreach (var r in threadResults)
            {
                if (r == null) continue;
                if (r.PerfectMatches != null) allPerfect.AddRange(r.PerfectMatches);
                if (r.PartialMatches != null) allPartial.AddRange(r.PartialMatches);
            }

            allPartial.Sort((a, b) => b.Score.CompareTo(a.Score));

            int neededPartials = allPerfect.Count < 100 ? Math.Min(100 - allPerfect.Count, allPartial.Count) : 0;
            var result = new SearchResult[allPerfect.Count + neededPartials];

            for (int i = 0; i < allPerfect.Count; i++) result[i] = allPerfect[i];
            for (int i = 0; i < neededPartials; i++) result[allPerfect.Count + i] = allPartial[i];

            return result;
        }

        public void Dispose() { }
    }

    public struct BloomFilterData { public BloomFilter Filter; public int Id; public int FirstLineId; public int LastLineId; }
    public class ThreadSearchResults { public List<SearchResult> PerfectMatches; public List<SearchResult> PartialMatches; }
    public struct SearchResult { public int Id; public int Score; public int FirstLineId; public int LastLineId; }
}
