using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MinimalIndexer
{
    internal class SearchEngine
    {
        readonly ZayitDbManager _db;
        readonly BloomFilterManager filterManager;

        internal SearchEngine()
        {
            _db = new ZayitDbManager();
            filterManager = new BloomFilterManager(_db);
        }

        internal void CreateIndex()
        {
            Console.WriteLine($"\n=== Creating Bloom Filter Index ===");

            var stopwatch = Stopwatch.StartNew();
            filterManager.CreateBloomFilters();
            stopwatch.Stop();

            Console.WriteLine("\n=== Index Creation Complete ===");
            Console.WriteLine($"Total time: {stopwatch.Elapsed.TotalMinutes:F2} minutes ({stopwatch.Elapsed.TotalSeconds:F1} seconds)");
        }

        internal void SearchIndex(string query)
        {
            Console.WriteLine($"\n=== Searching for: {query} ===\n");
            var totalStopwatch = Stopwatch.StartNew();
            var stageStopwatch = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(query))
            {
                totalStopwatch.Stop();
                Console.WriteLine("No valid query provided.");
                return;
            }

            var splitQuery = query.ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            stageStopwatch.Restart();
            var bloomResults = filterManager.SearchBloomFilters(splitQuery);
            Console.WriteLine($"Bloom filter search: {stageStopwatch.Elapsed.TotalSeconds:F3} seconds. Count: {bloomResults.values.Length}");

            stageStopwatch.Restart();
            var results = ProcessCandidateBlocks(bloomResults.chunkSize, bloomResults.values, splitQuery).ToArray();
            Console.WriteLine($"Processing candidate blocks: {stageStopwatch.Elapsed.TotalSeconds:F3} seconds");

            totalStopwatch.Stop();
            Console.WriteLine($"\n=== Search Complete ===");
            Console.WriteLine($"Total matched lines: {results.Length}");
            Console.WriteLine($"Total search time: {totalStopwatch.Elapsed.TotalSeconds:F3} seconds");

            //foreach (var line in results)
            //{
            //    Console.WriteLine($"[{line.BookTitle}, {line.Toc}] {line.Content}");
            //    Console.WriteLine("----------------------------------------------");
            //}
        }

        List<LineWithMetadata> ProcessCandidateBlocks(
     short chunkSize,
     (int BookId, int ChunkId)[] candidateBlocks,
     string[] queryTerms)
        {
            var results = new List<LineWithMetadata>(256);

            foreach (var line in _db.GetLinesForBlocks(candidateBlocks, chunkSize))
            {
                string normalized = TextNormalizer.Normalize(line.Content);
                var match = SearchEngineMatcher.Match(normalized, queryTerms);
                if (match == null)
                    continue;

                var modified = line;
                modified.Content = match.Snippet(normalized);
                results.Add(modified);
            }

            return results;
        }
    }
}