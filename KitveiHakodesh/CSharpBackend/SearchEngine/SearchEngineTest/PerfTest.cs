using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SearchEngine.Search;

namespace SearchEngineTest
{
    /// <summary>
    /// Performance tests against the real full index built by the test app.
    /// Measures wall-clock time for queries that are reported as slow in
    /// KitveiHakodesh, so we can isolate whether the bottleneck is in
    /// LuceneLib or in the app layer above it.
    ///
    /// Run with:
    ///   LuceneTest perf [indexDir]
    ///
    /// indexDir defaults to the lucene_index folder next to the exe.
    /// </summary>
    internal static class PerfTest
    {
        public static void Run(string indexDir = null)
        {
            indexDir = indexDir
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");

            Console.WriteLine("=== PERFORMANCE TEST ===");
            Console.WriteLine($"Index: {indexDir}");
            Console.WriteLine();

            if (!LuceneSearcher.IndexExists(indexDir))
            {
                Console.WriteLine("Index not found ג€” run 'LuceneTest build' first.");
                return;
            }

            // ג”€ג”€ Queries to benchmark ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
            // Each entry: (label, query, expectedMinHits)
            // expectedMinHits = 0 means "just measure, don't assert a floor"
            var queries = new[]
            {
                // The slow query reported in KitveiHakodesh
                ("׳›׳™ *׳™׳¦׳—׳§  (wildcard suffix)",   "׳›׳™ *׳™׳¦׳—׳§",   0),

                // Variants to isolate where the cost is
                ("׳›׳™ ׳™׳¦׳—׳§   (two literals)",       "׳›׳™ ׳™׳¦׳—׳§",    0),
                ("*׳™׳¦׳—׳§     (wildcard alone)",      "*׳™׳¦׳—׳§",      0),
                ("׳™׳¦׳—׳§      (literal alone)",       "׳™׳¦׳—׳§",       0),
                ("׳›׳™        (literal alone)",       "׳›׳™",         0),

                // A known-fast query for baseline
                ("׳׳‘׳¨׳”׳     (baseline literal)",    "׳׳‘׳¨׳”׳",      0),
            };

            using (var searcher = new LuceneSearcher(indexDir))
            {
                // Warm up the JIT and OS file cache with one throwaway search
                searcher.SearchRowIds("׳©׳׳•׳").Count();
                Console.WriteLine("(warm-up done)");
                Console.WriteLine();

                foreach (var (label, query, minHits) in queries)
                    RunQuery(searcher, label, query, minHits);
            }
        }

        // ג”€ג”€ Single query benchmark ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static void RunQuery(LuceneSearcher searcher,
                                     string label, string query, int minHits)
        {
            Console.WriteLine($"Query : {label}");
            Console.WriteLine($"        \"{query}\"");

            // Run 3 times and report each ג€” first run may be slower due to
            // term dictionary / posting list cache warming.
            const int Runs = 3;
            var times = new long[Runs];
            int hits  = 0;

            for (int r = 0; r < Runs; r++)
            {
                var sw = Stopwatch.StartNew();
                hits = searcher.SearchRowIds(query).Count();
                sw.Stop();
                times[r] = sw.ElapsedMilliseconds;
            }

            long best = times.Min();
            long avg  = (long)times.Average();

            Console.WriteLine($"        hits={hits:N0}  " +
                              $"times=[{string.Join(", ", times.Select(t => t + "ms"))}]  " +
                              $"best={best}ms  avg={avg}ms");

            if (minHits > 0 && hits < minHits)
                Console.WriteLine($"  WARNING: expected >= {minHits} hits, got {hits}");

            // Flag anything over 1 second as slow
            if (best > 1000)
                Console.WriteLine($"  *** SLOW: best time {best}ms exceeds 1000ms threshold ***");
            else if (best > 200)
                Console.WriteLine($"  ** NOTICE: best time {best}ms exceeds 200ms");

            Console.WriteLine();
        }
    }
}

