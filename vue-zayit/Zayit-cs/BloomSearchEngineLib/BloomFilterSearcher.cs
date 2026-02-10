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

                // Start processing hits in parallel - results stream in as they're found
                stopwatch.Restart();

                var perfectMatches = new ConcurrentQueue<SearchResultItem>();
                var partialMatches = new ConcurrentBag<SearchResultItem>();
                int perfectCount = 0;
                int totalProcessed = 0;

                var processingComplete = new ManualResetEventSlim(false);

                // Start parallel processing in background
                Task.Run(() =>
                {
                    ProcessHitsParallel(hits, terms, chunkSize, maxScore, perfectMatches, partialMatches);
                    processingComplete.Set();
                });

                // Yield perfect matches as they arrive
                while (!processingComplete.IsSet || !perfectMatches.IsEmpty)
                {
                    if (perfectMatches.TryDequeue(out var result))
                    {
                        perfectCount++;
                        totalProcessed++;
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
                if (totalProcessed < 100 && partialMatches.Count > 0)
                {
                    int remaining = Math.Min(100 - totalProcessed, partialMatches.Count);

                    // Sort partial matches
                    var sortedPartials = partialMatches.OrderByDescending(x => x.Score)
                                                      .ThenByDescending(x => x.ProximityScore)
                                                      .ThenBy(x => x.LineId)
                                                      .Take(remaining);

                    foreach (var result in sortedPartials)
                    {
                        yield return result;
                    }
                }
            }
        }

        private void ProcessHitsParallel(
            SearchResult[] hits,
            string[] terms,
            short chunkSize,
            int maxScore,
            ConcurrentQueue<SearchResultItem> perfectMatches,
            ConcurrentBag<SearchResultItem> partialMatches)
        {
            int threadCount = Environment.ProcessorCount;

            // Create one DB connection per thread to avoid contention
            var threadLocalDbs = new ThreadLocal<ZayitDbManager>(
                () => new ZayitDbManager(),
                trackAllValues: true);

            try
            {
                // Process hits in parallel
                Parallel.ForEach(
                    hits,
                    new ParallelOptions { MaxDegreeOfParallelism = threadCount },
                    () => new { Perfect = new List<SearchResultItem>(10), Partial = new List<SearchResultItem>(10) },
                    (hit, loopState, localResults) =>
                    {
                        var db = threadLocalDbs.Value;
                        int minRequiredWords = hit.Score;

                        var lines = db.GetLineContentsChunk(hit.Id, chunkSize);
                        int i = 0;

                        foreach (var line in lines)
                        {
                            string normalizedLine = line.NormalizeText();
                            var match = SearchEngineMatcher.Match(normalizedLine, terms, minRequiredWords);

                            if (match != null)
                            {
                                var result = new SearchResultItem
                                {
                                    LineId = hit.Id * chunkSize + i,
                                    Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore,
                                    Snippet = match.Snippet(normalizedLine)
                                };

                                if (result.Score == maxScore)
                                    localResults.Perfect.Add(result);
                                else
                                    localResults.Partial.Add(result);
                            }

                            i++;
                        }

                        return localResults;
                    },
                    (localResults) =>
                    {
                        // Stream perfect matches immediately
                        foreach (var result in localResults.Perfect)
                        {
                            perfectMatches.Enqueue(result);
                        }

                        // Collect partial matches for later sorting
                        foreach (var result in localResults.Partial)
                        {
                            partialMatches.Add(result);
                        }
                    }
                );
            }
            finally
            {
                // Dispose all thread-local DB connections
                if (threadLocalDbs.IsValueCreated)
                {
                    foreach (var db in threadLocalDbs.Values)
                    {
                        db?.Dispose();
                    }
                }
                threadLocalDbs.Dispose();
            }
        }
    }
}