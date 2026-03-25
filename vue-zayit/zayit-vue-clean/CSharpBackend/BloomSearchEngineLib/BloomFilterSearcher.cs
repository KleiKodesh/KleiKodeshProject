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

        public BloomFilterSearcher(string id = "lines")
        {
            _id = id;
        }

        public IEnumerable<SearchResultItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                yield break;

            var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                yield break;

            int maxScore = terms.Length;

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                short chunkSize = reader.ChunkSize;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var hits = reader.Search(terms);
                stopwatch.Stop();
                Console.WriteLine(
                    "Bloom completed in {0:F3} seconds, found {1} hits",
                    stopwatch.Elapsed.TotalSeconds,
                    hits.Length
                );

                if (hits.Length == 0)
                    yield break;

                // Start processing hits with producer-consumer pattern
                stopwatch.Restart();

                var perfectMatches = new ConcurrentQueue<SearchResultItem>();
                var partialMatches = new TopNPartialMatches(100);
                int perfectCount = 0;

                var processingComplete = new ManualResetEventSlim(false);

                // Start background processing (producer + consumer)
                Task.Run(() =>
                {
                    ProcessHitsProducerConsumer(hits, terms, chunkSize, maxScore, perfectMatches, partialMatches);
                    processingComplete.Set();
                });

                // Main thread consumes perfect matches as they arrive
                while (!processingComplete.IsSet || !perfectMatches.IsEmpty)
                {
                    if (perfectMatches.TryDequeue(out var result))
                    {
                        perfectCount++;
                        yield return result;
                    }
                    else if (!processingComplete.IsSet)
                    {
                        // Wait a bit for more results
                        Thread.Sleep(1);
                    }
                }

                stopwatch.Stop();
                Console.WriteLine(
                    "Verification completed in {0:F3} seconds, found {1} perfect + {2} partial results",
                    stopwatch.Elapsed.TotalSeconds,
                    perfectCount,
                    partialMatches.Count
                );

                // After all perfect matches, yield best partial matches up to 100 total
                if (perfectCount < 100)
                {
                    int remaining = Math.Min(100 - perfectCount, partialMatches.Count);
                    var topPartials = partialMatches.GetTop(remaining);

                    // Now hydrate only the top partial matches with full metadata AND snippets
                    using (var db = new ZayitDbManager())
                    {
                        foreach (var partial in topPartials)
                        {
                            var metadata = db.GetLineMetadata(partial.LineId);

                            // Fetch line content to generate snippet on-demand
                            var lineContent = db.GetLineContent(partial.LineId);
                            var normalizedContent = lineContent.NormalizeText();
                            var snippet = SearchEngineMatcher.ExtractSnippetFromCluster(
                                normalizedContent,
                                partial.ClusterStart,
                                partial.ClusterEnd);

                            yield return new SearchResultItem
                            {
                                LineId = partial.LineId,
                                BookId = metadata.bookId,
                                BookTitle = metadata.bookTitle,
                                TocText = metadata.tocText,
                                Score = partial.Score,
                                ProximityScore = partial.ProximityScore,
                                Snippet = snippet
                            };
                        }
                    }
                }

                LogMemoryUsage("Search completed");
            }
        }

        private void ProcessHitsProducerConsumer(
            SearchResult[] hits,
            string[] terms,
            short chunkSize,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            // Simple queue for producer-consumer communication
            var chunkQueue = new BlockingCollection<ChunkWork>(boundedCapacity: 2);

            // Start single consumer thread
            var consumerTask = Task.Run(() =>
                ConsumerThread(chunkQueue, terms, chunkSize, maxScore, perfectMatches, partialMatches));

            // Producer runs on this thread
            ProducerThread(hits, chunkSize, chunkQueue);

            // Wait for consumer to finish
            consumerTask.Wait();
        }

        private void ProducerThread(SearchResult[] hits, short chunkSize, BlockingCollection<ChunkWork> chunkQueue)
        {
            using (var db = new ZayitDbManager())
            {
                foreach (var hit in hits)
                {
                    var work = new ChunkWork
                    {
                        Hit = hit,
                        ChunkSize = chunkSize
                    };

                    chunkQueue.Add(work); // Blocks if queue is full
                }
            }

            // Signal that production is complete
            chunkQueue.CompleteAdding();
        }

        private void ConsumerThread(
            BlockingCollection<ChunkWork> chunkQueue,
            string[] terms,
            short chunkSize,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            using (var db = new ZayitDbManager())
            {
                int chunksProcessed = 0;

                foreach (var work in chunkQueue.GetConsumingEnumerable())
                {
                    int minRequiredWords = work.Hit.Score;
                    int i = 0;

                    // Stream lines one at a time instead of loading entire chunk
                    foreach (var line in db.GetLineContentsChunk(work.Hit.Id, work.ChunkSize))
                    {
                        string normalizedLine = line.NormalizeText();
                        var match = SearchEngineMatcher.Match(normalizedLine, terms, minRequiredWords);

                        if (match != null)
                        {
                            int lineId = work.Hit.Id * chunkSize + i;

                            if (match.Words.Length == maxScore)
                            {
                                // Perfect match - get full metadata immediately
                                var metadata = db.GetLineMetadata(lineId);

                                var result = new SearchResultItem
                                {
                                    LineId = lineId,
                                    BookId = metadata.bookId,
                                    BookTitle = metadata.bookTitle,
                                    TocText = metadata.tocText,
                                    Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore,
                                    Snippet = match.Snippet(normalizedLine)
                                };

                                perfectMatches.Enqueue(result);
                            }
                            else
                            {
                                // Partial match - only keep if it's in top 100
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

                        i++;
                    }

                    chunksProcessed++;

                    // Periodic cleanup every 50 chunks
                    if (chunksProcessed % 50 == 0)
                    {
                        if (GC.GetTotalMemory(false) > 300_000_000) // 300MB threshold
                        {
                            GC.Collect(1, GCCollectionMode.Optimized, false);
                        }
                    }
                }
            }
        }

        private void LogMemoryUsage(string location)
        {
            var mb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            Console.WriteLine($"[{location}] RAM: {mb:F2} MB");
        }

        // Lightweight struct for partial matches
        private struct PartialMatchData
        {
            public int LineId;
            public int Score;
            public double ProximityScore;
            public int ClusterStart;
            public int ClusterEnd;
        }

        private class ChunkWork
        {
            public SearchResult Hit { get; set; }
            public int ChunkSize { get; set; }
        }

        // Thread-safe priority queue that only keeps top N items
        private class TopNPartialMatches
        {
            private readonly object _lock = new object();
            private readonly SortedSet<PartialMatchData> _topMatches;
            private readonly int _maxSize;

            public TopNPartialMatches(int maxSize)
            {
                _maxSize = maxSize;
                _topMatches = new SortedSet<PartialMatchData>(
                    Comparer<PartialMatchData>.Create((a, b) =>
                    {
                        int cmp = b.Score.CompareTo(a.Score);
                        if (cmp != 0) return cmp;

                        cmp = b.ProximityScore.CompareTo(a.ProximityScore);
                        if (cmp != 0) return cmp;

                        return a.LineId.CompareTo(b.LineId);
                    }));
            }

            public void TryAdd(PartialMatchData match)
            {
                lock (_lock)
                {
                    if (_topMatches.Count < _maxSize)
                    {
                        _topMatches.Add(match);
                    }
                    else
                    {
                        var worst = _topMatches.Max;

                        if (match.Score > worst.Score ||
                            (match.Score == worst.Score && match.ProximityScore > worst.ProximityScore))
                        {
                            _topMatches.Remove(worst);
                            _topMatches.Add(match);
                        }
                    }
                }
            }

            public PartialMatchData[] GetTop(int count)
            {
                lock (_lock)
                {
                    return _topMatches.Take(count).ToArray();
                }
            }

            public int Count
            {
                get { lock (_lock) return _topMatches.Count; }
            }
        }
    }
}