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
                // Store only minimal data for partial matches - much less RAM!
                var partialMatches = new ConcurrentBag<PartialMatchData>();
                int perfectCount = 0;

                var processingComplete = new ManualResetEventSlim(false);

                // Start producer thread in background
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
                if (perfectCount < 100 && partialMatches.Count > 0)
                {
                    int remaining = Math.Min(100 - perfectCount, partialMatches.Count);

                    // Sort partial matches by lightweight data
                    var topPartials = partialMatches
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.ProximityScore)
                        .ThenBy(x => x.LineId)
                        .Take(remaining)
                        .ToArray();

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
                                Snippet = snippet  // Generated only for top results
                            };
                        }
                    }
                }
            }
        }

        private void ProcessHitsProducerConsumer(
            SearchResult[] hits,
            string[] terms,
            short chunkSize,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            ConcurrentBag<PartialMatchData> partialMatches)
        {
            // Queue for passing chunks from producer to consumers
            var chunkQueue = new BlockingCollection<ChunkWork>(boundedCapacity: 3);

            int consumerCount = Environment.ProcessorCount;
            var consumers = new Task[consumerCount];

            // Start consumer threads
            for (int i = 0; i < consumerCount; i++)
            {
                consumers[i] = Task.Run(() =>
                    ConsumerThread(chunkQueue, terms, chunkSize, maxScore, perfectMatches, partialMatches));
            }

            // Producer thread (runs on this thread)
            ProducerThread(hits, chunkSize, chunkQueue);

            // Wait for all consumers to finish
            Task.WaitAll(consumers);
        }

        private void ProducerThread(SearchResult[] hits, short chunkSize, BlockingCollection<ChunkWork> chunkQueue)
        {
            using (var db = new ZayitDbManager())
            {
                foreach (var hit in hits)
                {
                    var lines = db.GetLineContentsChunk(hit.Id, chunkSize).ToArray();

                    var work = new ChunkWork
                    {
                        Hit = hit,
                        Lines = lines
                    };

                    chunkQueue.Add(work);
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
            ConcurrentBag<PartialMatchData> partialMatches)
        {
            using (var db = new ZayitDbManager())
            {
                foreach (var work in chunkQueue.GetConsumingEnumerable())
                {
                    int minRequiredWords = work.Hit.Score;
                    int i = 0;

                    foreach (var line in work.Lines)
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
                                // Partial match - store only minimal data, NO SNIPPET
                                partialMatches.Add(new PartialMatchData
                                {
                                    LineId = lineId,
                                    Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore,
                                    ClusterStart = match.ClusterStart,
                                    ClusterEnd = match.ClusterEnd
                                    // No snippet - will generate later if needed
                                });
                            }
                        }

                        i++;
                    }

                    // Clear the lines array after processing to help GC
                    work.Lines = null;
                }
            }
        }

        // Lightweight struct for partial matches - saves RAM!
        private struct PartialMatchData
        {
            public int LineId;
            public int Score;
            public double ProximityScore;
            public int ClusterStart;  // Store position instead of snippet
            public int ClusterEnd;    // Store position instead of snippet
        }

        private class ChunkWork
        {
            public SearchResult Hit { get; set; }
            public string[] Lines { get; set; }
        }
    }
}