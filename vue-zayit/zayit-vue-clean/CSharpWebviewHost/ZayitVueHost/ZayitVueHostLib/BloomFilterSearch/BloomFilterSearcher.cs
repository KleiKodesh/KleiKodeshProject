using MinimalIndexer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zayit.Services;

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

            int maxScore = terms.Length;

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                short chunkSize = reader.ChunkSize;
                var hits = reader.Search(terms);
                if (hits.Length == 0) yield break;

                var perfectMatches = new ConcurrentQueue<SearchResultItem>();
                var partialMatches = new TopNPartialMatches(100);
                int perfectCount = 0;
                var done = new ManualResetEventSlim(false);

                Task.Run(() =>
                {
                    RunProducerConsumer(hits, terms, chunkSize, maxScore, perfectMatches, partialMatches);
                    done.Set();
                });

                while (!done.IsSet || !perfectMatches.IsEmpty)
                {
                    if (perfectMatches.TryDequeue(out var r)) { perfectCount++; yield return r; }
                    else if (!done.IsSet) Thread.Sleep(1);
                }

                if (perfectCount < 100)
                {
                    foreach (var p in partialMatches.GetTop(100 - perfectCount))
                    {
                        var meta = DbService.GetLineMetadata(p.LineId);
                        var content = DbService.GetLineContent(p.LineId).NormalizeText();
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
        }

        private void RunProducerConsumer(SearchResult[] hits, string[] terms, short chunkSize, int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches, TopNPartialMatches partialMatches)
        {
            var queue = new BlockingCollection<(SearchResult hit, int chunkSize)>(boundedCapacity: 2);

            var consumer = Task.Run(() =>
            {
                foreach (var (hit, cs) in queue.GetConsumingEnumerable())
                {
                    int i = 0;
                    foreach (var line in DbService.GetLineContentsChunk(hit.Id, cs))
                    {
                        var norm = line.NormalizeText();
                        var match = SearchEngineMatcher.Match(norm, terms, hit.Score);
                        if (match != null)
                        {
                            int lineId = hit.Id * cs + i;
                            if (match.Words.Length == maxScore)
                            {
                                var meta = DbService.GetLineMetadata(lineId);
                                perfectMatches.Enqueue(new SearchResultItem
                                {
                                    LineId = lineId, BookId = meta.bookId, BookTitle = meta.bookTitle,
                                    TocText = meta.tocText, Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore, Snippet = match.Snippet(norm)
                                });
                            }
                            else
                            {
                                partialMatches.TryAdd(new PartialMatch
                                {
                                    LineId = lineId, Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore,
                                    ClusterStart = match.ClusterStart, ClusterEnd = match.ClusterEnd
                                });
                            }
                        }
                        i++;
                    }
                }
            });

            foreach (var hit in hits) queue.Add((hit, chunkSize));
            queue.CompleteAdding();
            consumer.Wait();
        }

        private struct PartialMatch
        {
            public int LineId, Score, ClusterStart, ClusterEnd;
            public double ProximityScore;
        }

        private class TopNPartialMatches
        {
            private readonly object _lock = new object();
            private readonly SortedSet<PartialMatch> _set;
            private readonly int _max;

            public TopNPartialMatches(int max)
            {
                _max = max;
                _set = new SortedSet<PartialMatch>(Comparer<PartialMatch>.Create((a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0) return c;
                    c = b.ProximityScore.CompareTo(a.ProximityScore);
                    return c != 0 ? c : a.LineId.CompareTo(b.LineId);
                }));
            }

            public void TryAdd(PartialMatch m)
            {
                lock (_lock)
                {
                    if (_set.Count < _max) { _set.Add(m); return; }
                    var worst = _set.Max;
                    if (m.Score > worst.Score || (m.Score == worst.Score && m.ProximityScore > worst.ProximityScore))
                    { _set.Remove(worst); _set.Add(m); }
                }
            }

            public PartialMatch[] GetTop(int count) { lock (_lock) return _set.Take(count).ToArray(); }
            public int Count { get { lock (_lock) return _set.Count; } }
        }
    }
}
