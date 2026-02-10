using MinimalIndexer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

                // Process hits in parallel
                stopwatch.Restart();
                var results = ProcessHitsParallel(hits, terms, chunkSize, maxScore);
                stopwatch.Stop();
                Console.WriteLine(
                    "Verification completed in {0:F3} seconds, found {1} results",
                    stopwatch.Elapsed.TotalSeconds,
                    results.Count
                );

                // Yield results: perfect matches first, then best partial matches (max 100 total)
                int perfectCount = 0;
                var partialMatches = new List<SearchResultItem>(results.Count);

                foreach (var item in results)
                {
                    if (item.Score == maxScore)
                    {
                        perfectCount++;
                        yield return item;
                    }
                    else
                    {
                        partialMatches.Add(item);
                    }
                }

                // Fill remaining slots with best partial matches
                if (perfectCount < 100 && partialMatches.Count > 0)
                {
                    int remaining = Math.Min(100 - perfectCount, partialMatches.Count);

                    // Sort partial matches
                    partialMatches.Sort((a, b) =>
                    {
                        int scoreComp = b.Score.CompareTo(a.Score);
                        if (scoreComp != 0) return scoreComp;

                        int proxComp = b.ProximityScore.CompareTo(a.ProximityScore);
                        if (proxComp != 0) return proxComp;

                        return a.LineId.CompareTo(b.LineId);
                    });

                    for (int i = 0; i < remaining; i++)
                    {
                        yield return partialMatches[i];
                    }
                }
            }
        }

        private List<SearchResultItem> ProcessHitsParallel(
            SearchResult[] hits,
            string[] terms,
            short chunkSize,
            int maxScore)
        {
            var allResults = new ConcurrentBag<SearchResultItem>();
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
                    () => new List<SearchResultItem>(100), // Thread-local result list
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
                                localResults.Add(new SearchResultItem
                                {
                                    LineId = hit.Id * chunkSize + i,
                                    Score = match.Words.Length,
                                    ProximityScore = match.ProximityScore,
                                    Snippet = match.Snippet(normalizedLine)
                                });
                            }

                            i++;
                        }

                        return localResults;
                    },
                    (localResults) =>
                    {
                        // Merge thread-local results into global collection
                        foreach (var result in localResults)
                        {
                            allResults.Add(result);
                        }
                    }
                );

                return new List<SearchResultItem>(allResults);
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