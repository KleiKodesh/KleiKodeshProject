using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LuceneLib.Search;

namespace LuceneTest
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
                Console.WriteLine("Index not found — run 'LuceneTest build' first.");
                return;
            }

            // ── Queries to benchmark ──────────────────────────────────
            // Each entry: (label, query, expectedMinHits)
            // expectedMinHits = 0 means "just measure, don't assert a floor"
            var queries = new[]
            {
                // The slow query reported in KitveiHakodesh
                ("כי *יצחק  (wildcard suffix)",   "כי *יצחק",   0),

                // Variants to isolate where the cost is
                ("כי יצחק   (two literals)",       "כי יצחק",    0),
                ("*יצחק     (wildcard alone)",      "*יצחק",      0),
                ("יצחק      (literal alone)",       "יצחק",       0),
                ("כי        (literal alone)",       "כי",         0),

                // A known-fast query for baseline
                ("אברהם     (baseline literal)",    "אברהם",      0),
            };

            using (var searcher = new LuceneSearcher(indexDir))
            {
                // Warm up the JIT and OS file cache with one throwaway search
                searcher.Search("שלום").Count();
                Console.WriteLine("(warm-up done)");
                Console.WriteLine();

                foreach (var (label, query, minHits) in queries)
                    RunQuery(searcher, label, query, minHits);
            }
        }

        // ── Single query benchmark ────────────────────────────────────

        private static void RunQuery(LuceneSearcher searcher,
                                     string label, string query, int minHits)
        {
            Console.WriteLine($"Query : {label}");
            Console.WriteLine($"        \"{query}\"");

            // Run 3 times and report each — first run may be slower due to
            // term dictionary / posting list cache warming.
            const int Runs = 3;
            var times = new long[Runs];
            int hits  = 0;

            for (int r = 0; r < Runs; r++)
            {
                var sw = Stopwatch.StartNew();
                hits = searcher.Search(query).Count();
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
