using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FtsLibTest
{
    /// <summary>
    /// Periodically searches both nomerge and merge index directories for
    /// "כי ביצחק" and reports whether line id=548 is present.
    /// Runs until both builds are complete (no more segments being written)
    /// or until the user presses Ctrl+C.
    ///
    /// Usage:
    ///   FtsLibTest.exe monitor [tier]   tier = 500k (default) | 1m | 3m | full
    /// </summary>
    internal static class MonitorTest
    {
        private const string Query      = "כי ביצחק";
        private const int    RequiredId = 548;
        private const int    IntervalMs = 60_000; // 1 minute between checks

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "500k";
            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath      = BuildTest.ResolveDbPath();
            string nomergeDir  = IndexDir(label, "nomerge");
            string mergeDir    = IndexDir(label, "merge");

            Console.WriteLine($"╔══ MONITOR — {label.ToUpper()} ══");
            Console.WriteLine($"║  Query      : {Query}");
            Console.WriteLine($"║  Required id: {RequiredId}");
            Console.WriteLine($"║  DB         : {dbPath}");
            Console.WriteLine($"║  NOMERGE dir: {nomergeDir}");
            Console.WriteLine($"║  MERGE dir  : {mergeDir}");
            Console.WriteLine($"║  Interval   : {IntervalMs / 1000}s");
            Console.WriteLine("╚══ Press Ctrl+C to stop ══");
            Console.WriteLine();

            int iteration = 0;
            while (true)
            {
                iteration++;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ── Iteration {iteration} ──────────────────────────");

                CheckIndex(dbPath, nomergeDir, "NOMERGE", label);
                CheckIndex(dbPath, mergeDir,   "MERGE",   label);

                Console.WriteLine();
                Thread.Sleep(IntervalMs);
            }
        }

        private static void CheckIndex(string dbPath, string indexDir, string label, string tier)
        {
            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine($"  [{label}] directory not found yet");
                return;
            }

            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            if (datFiles.Length == 0)
            {
                Console.WriteLine($"  [{label}] no segments yet");
                return;
            }

            // List segments on disk
            long totalBytes = 0;
            foreach (var f in datFiles) totalBytes += new FileInfo(f).Length;
            Console.WriteLine($"  [{label}] {datFiles.Length} segment(s)  {totalBytes / 1_048_576.0:F0} MB");

            // Search
            try
            {
                var index   = new SeforimIndex(indexDir, dbPath);
                var sw      = Stopwatch.StartNew();
                var results = new List<SearchResult>();
                foreach (var r in index.Search(Query)) results.Add(r);
                sw.Stop();

                bool found = false;
                foreach (var r in results)
                    if (r.LineId == RequiredId) { found = true; break; }

                string status = found ? "PASS id=548 FOUND" : "FAIL id=548 NOT FOUND";
                Console.WriteLine($"  [{label}] {results.Count:N0} results  {sw.ElapsedMilliseconds} ms  → {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{label}] search error: {ex.Message}");
            }
        }

        private static string IndexDir(string tier, string suffix) =>
            Path.Combine(
                Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')),
                "Release",
                $"index_{tier.ToLowerInvariant()}_{suffix}");
    }
}
