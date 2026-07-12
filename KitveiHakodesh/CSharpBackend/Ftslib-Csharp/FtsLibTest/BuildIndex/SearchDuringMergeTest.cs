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

            bool pass = mismatches == 0 && exceptions == 0 && postOk && concurrentEnough;
            Console.WriteLine($"║  {(pass ? "✓  PASS — searches ran concurrently with the merge, all result sets exact" : "✗  FAIL")}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");

            if (pass)
                try { Directory.Delete(WorkDir, recursive: true); } catch { }
        }
    }
}
