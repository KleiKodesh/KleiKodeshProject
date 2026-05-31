using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Silent probe search — no HTML report, no browser launch.
    /// Runs a fixed set of diagnostic queries against an index directory and
    /// prints a compact summary to stdout. Designed to be run periodically
    /// against a live (mid-build) index to track correctness over time.
    ///
    /// Usage:
    ///   FtsLibTest.exe probe &lt;indexDir&gt;
    ///
    /// The index directory is passed as an absolute or relative path, not a tier
    /// label, so this command works against any directory including the mid-build
    /// nomerge/merge directories.
    ///
    /// Key assertion: line id=548 must appear in results for "כי ביצחק".
    /// </summary>
    internal static class ProbeSearch
    {
        // Queries to probe, each with the required IDs that must be present.
        private static readonly ProbeCase[] Probes =
        {
            new ProbeCase("כי ביצחק",         requiredIds: new[] { 548 }),
            new ProbeCase("כי יצחק~",          requiredIds: new[] { 548 }),
            new ProbeCase("כי %יצחק",          requiredIds: new[] { 548 }),
            new ProbeCase("%יצחק",             requiredIds: null),
            new ProbeCase("שויתי לנגדי תמיד",  requiredIds: null),
            new ProbeCase("אברהם יצחק יעקב",   requiredIds: null),
        };

        public static void Run(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: FtsLibTest.exe probe <indexDir>");
                return;
            }

            string indexDir = args[1];
            string dbPath   = BuildTest.ResolveDbPath();

            Console.WriteLine();
            Console.WriteLine($"╔══ PROBE  {DateTime.Now:HH:mm:ss} ══");
            Console.WriteLine($"║  Dir : {indexDir}");

            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine("║  [SKIP] Directory does not exist yet.");
                Console.WriteLine("╚══════════════════════════════════════");
                return;
            }

            // ── Segment inventory ─────────────────────────────────────
            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            if (datFiles.Length == 0)
            {
                Console.WriteLine("║  [SKIP] No segments yet.");
                Console.WriteLine("╚══════════════════════════════════════");
                return;
            }

            Array.Sort(datFiles);
            long totalBytes = 0;
            Console.WriteLine($"║  Segments ({datFiles.Length}):");
            foreach (var f in datFiles)
            {
                long sz = new FileInfo(f).Length;
                totalBytes += sz;
                Console.WriteLine($"║    {Path.GetFileName(f),20}  {sz / 1_048_576.0,6:F1} MB");
            }
            Console.WriteLine($"║  Total index size: {totalBytes / 1_048_576.0:F1} MB");

            // ── Queries ───────────────────────────────────────────────
            SeforimIndex index;
            try
            {
                index = new SeforimIndex(indexDir, dbPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"║  [SKIP] Could not open index (locked mid-build?): {ex.Message.Split('\n')[0]}");
                Console.WriteLine("╚══════════════════════════════════════");
                return;
            }

            bool anyFail = false;
            foreach (var probe in Probes)
            {
                var sw      = Stopwatch.StartNew();
                var results = new List<SearchResult>();
                try
                {
                    foreach (var r in index.Search(probe.Query))
                        results.Add(r);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"║  [{probe.Query}]  ERROR: {ex.Message.Split('\n')[0]}");
                    anyFail = true;
                    continue;
                }
                sw.Stop();

                // Check required IDs
                bool idOk = true;
                var missing = new List<int>();
                if (probe.RequiredIds != null)
                {
                    var idSet = new HashSet<int>(results.Count);
                    foreach (var r in results) idSet.Add(r.LineId);
                    foreach (int id in probe.RequiredIds)
                        if (!idSet.Contains(id)) { missing.Add(id); idOk = false; }
                }

                string status = idOk ? "✓" : $"✗ MISSING ids: {string.Join(",", missing)}";
                if (!idOk) anyFail = true;

                Console.WriteLine($"║  [{probe.Query,-24}]  {results.Count,6:N0} results  {sw.ElapsedMilliseconds,5} ms  {status}");
            }

            Console.WriteLine($"║  Overall: {(anyFail ? "✗ FAIL" : "✓ PASS")}");
            Console.WriteLine("╚══════════════════════════════════════");
        }

        private sealed class ProbeCase
        {
            public readonly string Query;
            public readonly int[]  RequiredIds;
            public ProbeCase(string query, int[] requiredIds)
            {
                Query       = query;
                RequiredIds = requiredIds;
            }
        }
    }
}
