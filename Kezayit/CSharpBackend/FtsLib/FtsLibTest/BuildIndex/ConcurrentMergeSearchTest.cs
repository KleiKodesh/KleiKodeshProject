using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibTest
{
    /// <summary>
    /// Hammers searches concurrently while Optimize() (force-merge) is running,
    /// looking for any search that loses line id=548 mid-merge.
    ///
    /// The race window: MergeLevel() deletes source segments BEFORE calling
    /// PromoteSegment(). A search that snapshots live paths in that window sees
    /// neither the sources (deleted) nor the target (not yet registered) and
    /// returns fewer results than expected.
    ///
    /// Usage:
    ///   FtsLibTest.exe concurrentmerge [tier]
    ///
    /// Reuses the index built by "beforeafter" if it exists; otherwise builds first.
    /// </summary>
    internal static class ConcurrentMergeSearchTest
    {
        private const string ProbeQuery  = "כי ביצחק";
        private const int    RequiredId  = 548;

        // How many parallel search threads to run during the merge.
        private const int SearchThreads = 8;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            int    limit;
            try
            {
                var tier = TestHelpers.ResolveTier(tierLabel);
                label = tier.Label;
                limit = tier.Limit;
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath = BuildTest.ResolveDbPath();

            // Prefer the beforeafter index (already built, no merge yet applied to it).
            // If it doesn't exist, build a fresh nomerge index.
            string indexDir = PickOrBuildIndex(label, limit, dbPath);
            if (indexDir == null) return;

            Console.WriteLine();
            Console.WriteLine($"╔══ CONCURRENT MERGE SEARCH TEST — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Probe : \"{ProbeQuery}\"  (must contain id={RequiredId})");
            Console.WriteLine($"║  Search threads during merge: {SearchThreads}");
            PrintSegments(indexDir, "║  ");

            // Baseline: how many results does a clean search return before merge?
            var index = new SeforimIndex(indexDir, dbPath);
            int baselineCount = CountResults(index);
            Console.WriteLine($"║  Baseline (pre-merge): {baselineCount:N0} results");
            Console.WriteLine("╠══ STARTING MERGE + CONCURRENT SEARCHES ══");

            // Shared counters
            int totalSearches  = 0;
            int failSearches   = 0;   // searches where id=548 was missing
            int wrongCount     = 0;   // searches where result count != baseline
            var failLog        = new List<string>();
            var failLogLock    = new object();
            var cts            = new CancellationTokenSource();

            // Launch search threads — they loop until the merge is done.
            // All threads share the same SeforimIndex instance, exactly as the real
            // app does (SearchHandler holds one shared SeforimIndex). Each Search()
            // call takes a fresh live-paths snapshot under the store lock, so
            // concurrent calls naturally race against the merge's PromoteSegment().
            var searchTasks = new Task[SearchThreads];
            for (int t = 0; t < SearchThreads; t++)
            {
                int threadId = t;
                searchTasks[t] = Task.Run(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var sw      = Stopwatch.StartNew();
                        var ids     = new List<int>();
                        bool error  = false;
                        try
                        {
                            foreach (var r in index.Search(ProbeQuery, ct: cts.Token))
                                ids.Add(r.LineId);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            error = true;
                            lock (failLogLock)
                                failLog.Add($"[T{threadId}] EXCEPTION: {ex.Message.Split('\n')[0]}");
                        }
                        sw.Stop();

                        if (!error)
                        {
                            bool found = ids.Contains(RequiredId);
                            Interlocked.Increment(ref totalSearches);
                            if (!found)
                            {
                                Interlocked.Increment(ref failSearches);
                                lock (failLogLock)
                                    failLog.Add($"[T{threadId}] FAIL id=548 MISSING  count={ids.Count}  {sw.ElapsedMilliseconds}ms");
                            }
                            if (ids.Count != baselineCount)
                            {
                                Interlocked.Increment(ref wrongCount);
                                lock (failLogLock)
                                    failLog.Add($"[T{threadId}] WRONG COUNT  got={ids.Count}  expected={baselineCount}  {sw.ElapsedMilliseconds}ms");
                            }
                        }
                    }
                });
            }

            // Run Optimize() on the main thread while search threads hammer it.
            var swMerge = Stopwatch.StartNew();
            index.Optimize();
            swMerge.Stop();
            Console.WriteLine($"║  Optimize done in {TestHelpers.FormatElapsed(swMerge.Elapsed)}");

            // Give search threads a moment to finish any in-flight search, then stop.
            Thread.Sleep(500);
            cts.Cancel();
            Task.WaitAll(searchTasks);

            // Post-merge baseline
            int afterCount = CountResults(index);

            // ── Report ────────────────────────────────────────────────
            Console.WriteLine("╠══ RESULTS ══");
            Console.WriteLine($"║  Total searches during merge : {totalSearches:N0}");
            Console.WriteLine($"║  Searches with id=548 MISSING: {failSearches:N0}  {(failSearches > 0 ? "✗ RACE CONFIRMED" : "✓ no race")}");
            Console.WriteLine($"║  Searches with wrong count   : {wrongCount:N0}");
            Console.WriteLine($"║  Baseline before merge       : {baselineCount:N0}");
            Console.WriteLine($"║  Result count after merge    : {afterCount:N0}");

            if (failLog.Count > 0)
            {
                Console.WriteLine("╠══ FAILURE LOG (first 20) ══");
                int show = Math.Min(20, failLog.Count);
                for (int i = 0; i < show; i++)
                    Console.WriteLine($"║  {failLog[i]}");
            }

            Console.WriteLine($"║  Overall: {(failSearches > 0 || wrongCount > 0 ? "✗ RACE BUG FOUND" : "✓ PASS — no race detected")}");
            Console.WriteLine("╚══ DONE ══");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static int CountResults(SeforimIndex index)
        {
            int n = 0;
            foreach (var r in index.Search(ProbeQuery)) n++;
            return n;
        }

        /// <summary>
        /// Returns the path to a usable pre-built (no-merge) index.
        /// Looks for index_{label}_fresh — built by the "buildfresh" command.
        /// Never builds automatically; if not found, prints instructions and returns null.
        /// </summary>
        private static string PickOrBuildIndex(string label, int limit, string dbPath)
        {
            string baseDir   = AppDomain.CurrentDomain.BaseDirectory;
            string freshDir  = Path.Combine(baseDir, $"index_{label}_fresh");

            if (Directory.Exists(freshDir))
            {
                var segs = Directory.GetFiles(freshDir, "seg_*.dat");
                if (segs.Length >= 2)
                {
                    Console.WriteLine($"Using index: {freshDir}  ({segs.Length} segments)");
                    return freshDir;
                }
                if (segs.Length == 1)
                {
                    Console.WriteLine($"ERROR: {freshDir} has only 1 segment — already merged. Run 'buildfresh {label}' first.");
                    return null;
                }
            }

            Console.WriteLine($"No fresh index found at: {freshDir}");
            Console.WriteLine($"Run first:  FtsLibTest.exe buildfresh {label}");
            return null;
        }

        private static void PrintSegments(string indexDir, string prefix)
        {
            var files = Directory.GetFiles(indexDir, "seg_*.dat");
            Array.Sort(files);
            long total = 0;
            foreach (var f in files)
            {
                long sz = new FileInfo(f).Length;
                total += sz;
                Console.WriteLine($"{prefix}{Path.GetFileName(f),22}  {sz / 1_048_576.0,6:F1} MB");
            }
            Console.WriteLine($"{prefix}Total: {files.Length} segment(s)  {total / 1_048_576.0:F1} MB");
        }
    }
}
