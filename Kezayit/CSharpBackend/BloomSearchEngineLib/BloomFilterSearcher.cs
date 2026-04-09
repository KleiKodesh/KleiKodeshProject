using MinimalIndexer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterSearcher
    {
        private readonly string _id;

        public BloomFilterSearcher(string id = "lines") { _id = id; }

        public IEnumerable<SearchResultItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) yield break;

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                var hits = RunBloomSearch(reader, terms);
                if (hits.Length == 0) yield break;

                var perfectMatches = new ConcurrentQueue<SearchResultItem>();
                var partialMatches = new TopNPartialMatches(100);
                int perfectCount = 0;
                var done = new ManualResetEventSlim(false);

                Task.Run(() =>
                {
                    ProcessHits(hits, terms, terms.Length, perfectMatches, partialMatches);
                    done.Set();
                });

                // Stream perfect matches to caller as they arrive
                while (!done.IsSet || !perfectMatches.IsEmpty)
                {
                    if (perfectMatches.TryDequeue(out var r)) { perfectCount++; yield return r; }
                    else if (!done.IsSet) Thread.Sleep(1);
                }

                Console.WriteLine("[Search] {0} perfect + {1} partial", perfectCount, partialMatches.Count);

                foreach (var item in HydratePartialMatches(partialMatches, perfectCount))
                    yield return item;

                Console.WriteLine("[Search completed] RAM: {0:F2} MB", GC.GetTotalMemory(false) / (1024.0 * 1024.0));
            }
        }

        private static SearchResult[] RunBloomSearch(BloomFilterCollectionReader reader, string[] terms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hits = reader.Search(terms);
            sw.Stop();
            Console.WriteLine("Bloom completed in {0:F3}s, found {1} hits", sw.Elapsed.TotalSeconds, hits.Length);
            return hits;
        }

        private static IEnumerable<SearchResultItem> HydratePartialMatches(TopNPartialMatches partialMatches, int perfectCount)
        {
            if (perfectCount >= 100) yield break;

            int remaining = Math.Min(100 - perfectCount, partialMatches.Count);
            using (var db = new ZayitDbManager())
            {
                foreach (var p in partialMatches.GetTop(remaining))
                {
                    var meta = db.GetLineMetadata(p.LineId);
                    var content = db.GetLineContent(p.LineId).NormalizeText();
                    yield return new SearchResultItem
                    {
                        LineId = p.LineId,
                        BookId = meta.bookId,
                        BookTitle = meta.bookTitle,
                        TocText = meta.tocText,
                        Score = p.Score,
                        ProximityScore = p.ProximityScore,
                        Snippet = SearchEngineMatcher.ExtractSnippetFromCluster(content, p.ClusterStart, p.ClusterEnd)
                    };
                }
            }
        }

        private void ProcessHits(
            SearchResult[] hits,
            string[] terms,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            var queue = new BlockingCollection<SearchResult>(boundedCapacity: 2);

            var consumer = Task.Run(() => ConsumeChunks(queue, terms, maxScore, perfectMatches, partialMatches));

            foreach (var hit in hits) queue.Add(hit);
            queue.CompleteAdding();
            consumer.Wait();
        }

        private static void ConsumeChunks(
            BlockingCollection<SearchResult> queue,
            string[] terms,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            using (var db = new ZayitDbManager())
            {
                int chunksProcessed = 0;
                foreach (var hit in queue.GetConsumingEnumerable())
                {
                    ProcessChunk(db, hit, terms, maxScore, perfectMatches, partialMatches);

                    if (++chunksProcessed % 50 == 0 && GC.GetTotalMemory(false) > 300_000_000)
                        GC.Collect(1, GCCollectionMode.Optimized, false);
                }
            }
        }

        private static void ProcessChunk(
            ZayitDbManager db,
            SearchResult hit,
            string[] terms,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            foreach (var (lineId, content) in db.GetLineContentsChunk(hit.FirstLineId, hit.LastLineId))
            {
                string norm = content.NormalizeText();
                var match = SearchEngineMatcher.Match(norm, terms, hit.Score);
                if (match == null) continue;

                if (match.Words.Length == maxScore)
                    perfectMatches.Enqueue(BuildResult(db, lineId, match, norm));
                else
                    partialMatches.TryAdd(new PartialMatchData
                    {
                        LineId = lineId,
                        Score = match.Words.Length,
                        ProximityScore = match.ProximityScore,
                        ClusterStart = match.ClusterStart,
                        ClusterEnd = match.ClusterEnd
                    });
            }
        }

        private static SearchResultItem BuildResult(ZayitDbManager db, int lineId, MatchInfo match, string norm)
        {
            var meta = db.GetLineMetadata(lineId);
            return new SearchResultItem
            {
                LineId = lineId,
                BookId = meta.bookId,
                BookTitle = meta.bookTitle,
                TocText = meta.tocText,
                Score = match.Words.Length,
                ProximityScore = match.ProximityScore,
                Snippet = match.Snippet(norm)
            };
        }

        private struct PartialMatchData
        {
            public int LineId, Score, ClusterStart, ClusterEnd;
            public double ProximityScore;
        }

        private class TopNPartialMatches
        {
            private readonly object _lock = new object();
            private readonly SortedSet<PartialMatchData> _set;
            private readonly int _max;

            public TopNPartialMatches(int max)
            {
                _max = max;
                _set = new SortedSet<PartialMatchData>(Comparer<PartialMatchData>.Create((a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0) return c;
                    c = b.ProximityScore.CompareTo(a.ProximityScore);
                    return c != 0 ? c : a.LineId.CompareTo(b.LineId);
                }));
            }

            public void TryAdd(PartialMatchData m)
            {
                lock (_lock)
                {
                    if (_set.Count < _max) { _set.Add(m); return; }
                    var worst = _set.Max;
                    if (m.Score > worst.Score || (m.Score == worst.Score && m.ProximityScore > worst.ProximityScore))
                    {
                        _set.Remove(worst);
                        _set.Add(m);
                    }
                }
            }

            public PartialMatchData[] GetTop(int count) { lock (_lock) return _set.Take(count).ToArray(); }
            public int Count { get { lock (_lock) return _set.Count; } }
        }
    }
}
