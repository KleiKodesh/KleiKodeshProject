using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibTest
{
    /// <summary>
    /// Verifies Lucene-style search-during-merge behavior: searches must run
    /// concurrently with a force merge, never fail with IndexMergingException,
    /// and every search must return a complete, point-in-time-consistent result
    /// set (identical to the pre-merge truth, since a merge changes no content).
    ///
    /// Procedure:
    ///   1. Copy index_full_backup_before_merge (4 segments) to a work dir.
    ///   2. Compute expected result-ID sets for the probe queries.
    ///   3. Start ForceMerge on a background task (~1 minute on the full index).
    ///   4. While it runs, search in a tight loop on this thread; every result
    ///      set must match exactly; record latencies.
    ///   5. After the merge, verify again on the merged single segment.
    ///
    /// PASS requires: 0 exceptions, 0 mismatches, and a meaningful number of
    /// searches completed while the merge was in flight (proves concurrency).
    ///
    /// Usage:
    ///   FtsLibTest.exe searchduringmerge
    /// </summary>
    internal static class SearchDuringMergeTest
    {
        private static readonly string[] ProbeQueries = { "כי ביצחק", "אמר" };

        private static string BackupDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_full_backup_before_merge");

        private static string WorkDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_searchduringmerge_work");

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // --slow: additionally run a SLOW CONSUMER of a broad full-pipeline
            // Search (content fetch included) on a background thread for the whole
            // merge. Reproduces the production regression where a search that
            // streams results for minutes kept its SearchLease, a queued merge
            // commit then blocked the write lock, and every new search stalled
            // behind it. With leases scoped to segment reads only, foreground
            // latency must stay low even with the slow consumer running.
            bool slowConsumer = Array.Exists(args, a =>
                a.Equals("--slow", StringComparison.OrdinalIgnoreCase));

            if (!Directory.Exists(BackupDir) ||
                Directory.GetFiles(BackupDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"[SearchDuringMergeTest] Backup index not found at: {BackupDir}");
                Console.WriteLine("Run: FtsLibTest.exe mergetest full   to create it first.");
                return;
            }

            string dbPath = BuildTest.ResolveDbPath();

            Console.WriteLine();
            Console.WriteLine("╔══ SEARCH-DURING-MERGE TEST ════════════════════════════════════════");
            Console.WriteLine($"║  Source : {BackupDir}");
            Console.WriteLine($"║  Work   : {WorkDir}");
            Console.WriteLine($"║  DB     : {dbPath}");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            // 1. Fresh work dir from backup
            if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, recursive: true);
            Directory.CreateDirectory(WorkDir);
            foreach (var f in Directory.GetFiles(BackupDir))
                File.Copy(f, Path.Combine(WorkDir, Path.GetFileName(f)));

            var index = new SeforimIndex(WorkDir, dbPath);

            // 2. Expected sets (pre-merge truth; merge must not change content)
            var expected = new Dictionary<string, HashSet<int>>();
            foreach (var q in ProbeQueries)
            {
                expected[q] = new HashSet<int>(index.SearchIds(q));
                Console.WriteLine($"║  expected \"{q}\" → {expected[q].Count:N0} ids");
                if (expected[q].Count == 0)
                {
                    Console.WriteLine("║  ABORT: probe returned 0 results on the work copy.");
                    return;
                }
            }

            // 3. Force merge on a background task
            Console.WriteLine("║  Starting ForceMerge in background...");
            var mergeSw   = Stopwatch.StartNew();
            var mergeTask = Task.Run(() => index.ForceMerge());

            // 3b. Optional slow consumer: streams the full Search pipeline for a
            // broad query, sleeping between results, for the whole merge duration.
            var  slowCts   = new CancellationTokenSource();
            Task slowTask  = null;
            int  slowCount = 0;
            if (slowConsumer)
            {
                // "כי ביצחק" (2,179 results) at 50ms/result holds the enumeration
                // open for ~2 minutes — longer than the merge, like a user slowly
                // paging through results in the UI while the index builds.
                Console.WriteLine("║  Starting SLOW consumer (full-pipeline Search of \"כי ביצחק\", 50ms/result)...");
                slowTask = Task.Run(() =>
                {
                    try
                    {
                        foreach (var r in index.Search("כי ביצחק", ct: slowCts.Token))
                        {
                            Interlocked.Increment(ref slowCount);
                            Thread.Sleep(50);
                        }
                    }
                    catch (OperationCanceledException) { }
                });
                Thread.Sleep(1500); // let it acquire its lease and start streaming
            }

            // 4. Search loop while the merge runs
            int  searches      = 0;
            int  mismatches    = 0;
            int  exceptions    = 0;
            long maxLatencyMs  = 0;
            long totalLatency  = 0;
            var  failures      = new List<string>();
            int  qi            = 0;

            while (!mergeTask.IsCompleted)
            {
                string q  = ProbeQueries[qi++ % ProbeQueries.Length];
                var    sw = Stopwatch.StartNew();
                try
                {
                    var got = new HashSet<int>(index.SearchIds(q));
                    sw.Stop();

                    if (!got.SetEquals(expected[q]))
                    {
                        mismatches++;
                        int missing = expected[q].Count(id => !got.Contains(id));
                        int extra   = got.Count(id => !expected[q].Contains(id));
                        if (failures.Count < 5)
                            failures.Add($"\"{q}\": {missing} missing / {extra} extra (got {got.Count:N0})");
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    exceptions++;
                    if (failures.Count < 5)
                        failures.Add($"\"{q}\" threw {ex.GetType().Name}: {ex.Message}");
                }

                searches++;
                totalLatency += sw.ElapsedMilliseconds;
                if (sw.ElapsedMilliseconds > maxLatencyMs) maxLatencyMs = sw.ElapsedMilliseconds;
            }

            mergeSw.Stop();
            if (slowTask != null)
            {
                slowCts.Cancel();
                try { slowTask.Wait(10_000); } catch { }
                Console.WriteLine($"║  Slow consumer streamed {slowCount:N0} results before cancel.");
            }
            try { mergeTask.Wait(); }
            catch (Exception ex)
            {
                Console.WriteLine($"║  ForceMerge threw: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine("║  ✗  FAIL");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
                return;
            }

            // 5. Verify on the merged result
            bool postOk = true;
            foreach (var q in ProbeQueries)
            {
                var got = new HashSet<int>(index.SearchIds(q));
                if (!got.SetEquals(expected[q]))
                {
                    postOk = false;
                    Console.WriteLine($"║  POST-MERGE MISMATCH \"{q}\": expected {expected[q].Count:N0}, got {got.Count:N0}");
                }
            }
            int segCount = Directory.GetFiles(WorkDir, "seg_*.dat").Length;

            // ── Report ────────────────────────────────────────────────────────────
            double avgLatency = searches > 0 ? (double)totalLatency / searches : 0;
            Console.WriteLine("╠══ RESULT ══════════════════════════════════════════════════════════");
            Console.WriteLine($"║  Merge duration      : {mergeSw.ElapsedMilliseconds:N0}ms  (segments after: {segCount})");
            Console.WriteLine($"║  Searches during merge: {searches}  (avg {avgLatency:F0}ms, max {maxLatencyMs:N0}ms)");
            Console.WriteLine($"║  Mismatches          : {mismatches}");
            Console.WriteLine($"║  Exceptions          : {exceptions}");
            foreach (var f in failures) Console.WriteLine($"║    ✗ {f}");

            // Concurrency proof: with a ~1-minute merge and fast queries, far more
            // than 10 searches must complete while the merge is in flight. If they
            // were serialized behind the merge (the old behavior), this would be 0-1.
            bool concurrentEnough = searches >= 10;
            if (!concurrentEnough)
                Console.WriteLine($"║  ✗ only {searches} searches completed during the merge — searches appear to be blocked by it");

            // In --slow mode the point of the test is foreground latency: no search
            // may stall behind a merge commit queued on the slow consumer's lease.
            bool latencyOk = !slowConsumer || maxLatencyMs < 5_000;
            if (!latencyOk)
                Console.WriteLine($"║  ✗ max foreground latency {maxLatencyMs:N0}ms — searches are blocking behind the slow consumer's lease");

            bool pass = mismatches == 0 && exceptions == 0 && postOk && concurrentEnough && latencyOk;
            Console.WriteLine($"║  {(pass ? "✓  PASS — searches ran concurrently with the merge, all result sets exact" : "✗  FAIL")}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");

            if (pass)
                try { Directory.Delete(WorkDir, recursive: true); } catch { }
        }
    }
}
