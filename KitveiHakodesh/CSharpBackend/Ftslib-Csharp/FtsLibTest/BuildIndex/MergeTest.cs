using FtsLib.Indexing;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Force-merge corruption diagnostic test.
    ///
    /// Step-by-step:
    ///   1. Build a full index (all lines) from scratch.
    ///   2. Search for "כי ביצחק" — verify correctness before merge.
    ///   3. Backup the index directory to {indexDir}_backup_before_merge.
    ///   4. Force-merge: collapse all segments into one per level.
    ///   5. Search for "כי ביצחק" — verify correctness after merge.
    ///   6. Report PASS / FAIL with full details.
    ///
    /// All FtsLog messages are redirected to:
    ///   {exe dir}\merge_test_logs\FtsLib_{timestamp}.log
    ///
    /// Usage:
    ///   FtsLibTest.exe mergetest [tier]   (default: full)
    ///
    /// The test never wipes an existing index — if one exists it reuses it
    /// (after asking) so you can re-run the merge/search phases without rebuilding.
    /// Pass "--rebuild" to always start fresh.
    ///
    ///   FtsLibTest.exe mergetest full --rebuild
    /// </summary>
    internal static class MergeTest
    {
        private const string ProbeQuery      = "כי ביצחק";
        private const int    ProbeRequiredId = 548;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 && !args[1].StartsWith("-") ? args[1] : "full";
            bool   rebuild   = Array.Exists(args, a =>
                string.Equals(a, "--rebuild", StringComparison.OrdinalIgnoreCase));

            string label, dbPath, indexDir;
            int    limit;
            try
            {
                var tier = TestHelpers.ResolveTier(tierLabel);
                label    = tier.Label;
                limit    = tier.Limit;
                dbPath   = BuildTest.ResolveDbPath();
                indexDir = TestHelpers.IndexDir(label);
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            // ── Set up dedicated log directory ────────────────────────────────────
            string logDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "merge_test_logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir,
                $"FtsLib_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            FtsLog.LogPath = logPath;
            FtsLog.Clear();

            Console.WriteLine();
            Console.WriteLine("╔══ MERGE TEST ══════════════════════════════════════════════════════");
            Console.WriteLine($"║  Tier      : {label.ToUpper()}");
            Console.WriteLine($"║  DB        : {dbPath}");
            Console.WriteLine($"║  Index dir : {indexDir}");
            Console.WriteLine($"║  Log       : {logPath}");
            Console.WriteLine($"║  Probe     : \"{ProbeQuery}\"  (must include id={ProbeRequiredId})");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            FtsLog.Separator("MERGE TEST START");
            FtsLog.Write("MergeTest",
                $"tier={label} limit={limit} rebuild={rebuild} dbPath={dbPath} indexDir={indexDir}");

            // ── Step 1: Build (or reuse) full index ───────────────────────────────
            bool indexExists = Directory.Exists(indexDir) &&
                               Directory.GetFiles(indexDir, "seg_*.dat").Length > 0;

            if (rebuild && indexExists)
            {
                Console.WriteLine("║  [--rebuild] wiping existing index...");
                FtsLog.Write("MergeTest", "wiping existing index for rebuild");
                Directory.Delete(indexDir, recursive: true);
                indexExists = false;
            }

            SeforimIndex index;
            if (indexExists)
            {
                Console.WriteLine("║");
                Console.WriteLine("║  ── STEP 1: Reusing existing index (skip build)");
                FtsLog.Write("MergeTest", "reusing existing index — skipping build");
                index = new SeforimIndex(indexDir, dbPath);
                LogSegmentState("after open", indexDir);
            }
            else
            {
                Console.WriteLine("║");
                Console.WriteLine($"║  ── STEP 1: Building full index ({(limit == 0 ? "all lines" : limit.ToString("N0") + " lines")})...");
                FtsLog.Separator("STEP 1: BUILD");

                if (Directory.Exists(indexDir))
                    Directory.Delete(indexDir, recursive: true);
                Directory.CreateDirectory(indexDir);

                index = new SeforimIndex(indexDir, dbPath);

                long   linesIndexed = 0;
                long   lastReport   = 0;
                var    swBuild      = Stopwatch.StartNew();
                var    swInterval   = Stopwatch.StartNew();

                Console.WriteLine($"║  {"Lines",12}  {"Elapsed",10}  {"Rate",12}");
                Console.WriteLine($"║  {new string('─', 12)}  {new string('─', 10)}  {new string('─', 12)}");

                try
                {
                    index.BuildIndex(
                        limit: limit,
                        onProgress: n =>
                        {
                            linesIndexed = n;
                            if (n - lastReport >= 200_000)
                            {
                                double dSec = swInterval.Elapsed.TotalSeconds;
                                string rate = dSec > 0 ? $"{(n - lastReport) / dSec:N0}/s" : "—";
                                Console.WriteLine(
                                    $"║  {n,12:N0}  " +
                                    $"{TestHelpers.FormatElapsed(swBuild.Elapsed),10}  {rate,12}");
                                lastReport = n;
                                swInterval.Restart();
                            }
                        });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"║  BUILD FAILED: {ex.Message}");
                    FtsLog.Write("MergeTest", "BUILD FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    return;
                }

                swBuild.Stop();
                Console.WriteLine($"║  Build complete: {linesIndexed:N0} lines in {TestHelpers.FormatElapsed(swBuild.Elapsed)}");
                FtsLog.Write("MergeTest",
                    $"build complete: {linesIndexed:N0} lines in {swBuild.ElapsedMilliseconds}ms");
                LogSegmentState("after build", indexDir);
            }

            // ── Step 2: Search BEFORE merge ───────────────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine($"║  ── STEP 2: Search BEFORE force-merge  (\"{ProbeQuery}\")");
            FtsLog.Separator("STEP 2: SEARCH BEFORE MERGE");

            var (beforeCount, beforeFound, beforeMs) = RunProbe(index, "BEFORE");
            if (!beforeFound)
            {
                Console.WriteLine($"║  [FAIL] Required id={ProbeRequiredId} NOT FOUND in {beforeCount} results before merge.");
                Console.WriteLine($"║  Index may already be corrupt. Check log: {logPath}");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
                FtsLog.Write("MergeTest", "FAIL: probe missing BEFORE merge — aborting");
                return;
            }
            Console.WriteLine($"║  PRE-MERGE:  {beforeCount} results  id={ProbeRequiredId} ✓  ({beforeMs}ms)");

            // ── Step 3: Backup index ──────────────────────────────────────────────
            string backupDir = indexDir + "_backup_before_merge";
            Console.WriteLine("║");
            Console.WriteLine($"║  ── STEP 3: Backing up index → {Path.GetFileName(backupDir)}");
            FtsLog.Separator("STEP 3: BACKUP");
            FtsLog.Write("MergeTest", $"backup: {indexDir} → {backupDir}");

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Console.WriteLine($"║  Removing old backup...");
                    Directory.Delete(backupDir, recursive: true);
                }
                CopyDirectory(indexDir, backupDir);
                LogSegmentState("backup", backupDir);
                Console.WriteLine($"║  Backup complete.");
                FtsLog.Write("MergeTest", "backup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"║  WARNING: backup failed: {ex.Message}");
                FtsLog.Write("MergeTest", "backup failed (non-fatal): " + ex.Message);
            }

            // ── Step 4: Force merge ───────────────────────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine("║  ── STEP 4: Force merge (MergeAllUnderWriteLock)...");
            FtsLog.Separator("STEP 4: FORCE MERGE");
            LogSegmentState("before ForceMerge", indexDir);

            var swMerge = Stopwatch.StartNew();
            try
            {
                index.ForceMerge();
                swMerge.Stop();
                Console.WriteLine($"║  Force merge complete in {swMerge.ElapsedMilliseconds}ms");
                FtsLog.Write("MergeTest",
                    $"ForceMerge complete in {swMerge.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                swMerge.Stop();
                Console.WriteLine($"║  [FAIL] ForceMerge threw: {ex.GetType().Name}: {ex.Message}");
                FtsLog.Write("MergeTest",
                    $"ForceMerge EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Console.WriteLine($"║  Check log: {logPath}");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
                return;
            }

            LogSegmentState("after ForceMerge", indexDir);

            // ── Step 5: Search AFTER merge ────────────────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine($"║  ── STEP 5: Search AFTER force-merge  (\"{ProbeQuery}\")");
            FtsLog.Separator("STEP 5: SEARCH AFTER MERGE");

            // Re-open the index so it sees the new merged segment state
            index = new SeforimIndex(indexDir, dbPath);

            var (afterCount, afterFound, afterMs) = RunProbe(index, "AFTER");

            // ── Step 6: Report ────────────────────────────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine("╠══ RESULT ══════════════════════════════════════════════════════════");
            Console.WriteLine($"║  PRE-MERGE  : {beforeCount} results  id={ProbeRequiredId} ✓  ({beforeMs}ms)");

            if (afterFound)
            {
                Console.WriteLine($"║  POST-MERGE : {afterCount} results  id={ProbeRequiredId} ✓  ({afterMs}ms)");
                Console.WriteLine("║");
                Console.WriteLine("║  ✓  PASS — force merge did not corrupt the index");
                FtsLog.Write("MergeTest",
                    $"PASS — before={beforeCount} after={afterCount} id={ProbeRequiredId} found in both");
            }
            else
            {
                Console.WriteLine($"║  POST-MERGE : {afterCount} results  id={ProbeRequiredId} MISSING  ({afterMs}ms)  ← CORRUPTION");
                Console.WriteLine("║");
                Console.WriteLine("║  ✗  FAIL — index is CORRUPT after force merge");
                Console.WriteLine($"║     Pre-merge backup preserved at: {backupDir}");
                Console.WriteLine($"║     Corrupted index at           : {indexDir}");
                FtsLog.Write("MergeTest",
                    $"FAIL — id={ProbeRequiredId} missing after merge. before={beforeCount} after={afterCount}");
            }

            Console.WriteLine($"║  Log: {logPath}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
            FtsLog.Separator("MERGE TEST END");
        }

        // ── Probe search ──────────────────────────────────────────────────────────

        private static (int count, bool found, long ms) RunProbe(SeforimIndex index, string label)
        {
            FtsLog.Write("MergeTest.Probe",
                $"[{label}] searching for \"{ProbeQuery}\", expecting id={ProbeRequiredId}");

            int  count = 0;
            bool found = false;
            var  sw    = Stopwatch.StartNew();

            try
            {
                foreach (var result in index.Search(ProbeQuery))
                {
                    count++;
                    if (result.LineId == ProbeRequiredId) found = true;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"║  [{label}] search threw: {ex.GetType().Name}: {ex.Message}");
                FtsLog.Write("MergeTest.Probe",
                    $"[{label}] EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return (0, false, sw.ElapsedMilliseconds);
            }

            sw.Stop();

            FtsLog.Write("MergeTest.Probe",
                $"[{label}] count={count} found={found} id={ProbeRequiredId} ms={sw.ElapsedMilliseconds}");

            // Show first 5 results
            if (count > 0)
            {
                int  shown    = 0;
                bool foundLogged = false;
                foreach (var result in index.Search(ProbeQuery))
                {
                    if (shown < 5)
                    {
                        Console.WriteLine(
                            $"║    [{result.LineId}] {TestHelpers.Truncate(result.BookTitle ?? "", 40)}" +
                            $"{(result.LineId == ProbeRequiredId ? "  ← required id" : "")}");
                        shown++;
                    }
                    if (result.LineId == ProbeRequiredId) foundLogged = true;
                }
                if (!foundLogged && found)
                    Console.WriteLine($"║    (id={ProbeRequiredId} found but not in first 5)");
                if (!found)
                    Console.WriteLine($"║    ✗ id={ProbeRequiredId} NOT in {count} results");
            }
            else
            {
                Console.WriteLine($"║    (0 results)");
            }

            return (count, found, sw.ElapsedMilliseconds);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void LogSegmentState(string label, string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    FtsLog.Write("MergeTest.SegState[" + label + "]", "directory does not exist");
                    return;
                }

                var datFiles = Directory.GetFiles(dir, "seg_*.dat");
                var dbFiles  = Directory.GetFiles(dir, "seg_*.db");
                var allFiles = Directory.GetFiles(dir);

                var sb = new StringBuilder();
                sb.AppendLine($"{datFiles.Length} .dat segment(s), {dbFiles.Length} .db file(s), {allFiles.Length} total files:");
                foreach (var f in datFiles)
                {
                    long sz = new FileInfo(f).Length;
                    sb.AppendLine($"  {Path.GetFileName(f),28}  {sz / 1_048_576.0:F1} MB  ({sz:N0}B)");
                }

                // Log non-segment files too
                var otherFiles = Array.FindAll(allFiles,
                    f => !f.EndsWith(".dat") && !f.EndsWith(".db") &&
                         !f.EndsWith(".db-shm") && !f.EndsWith(".db-wal"));
                if (otherFiles.Length > 0)
                {
                    sb.Append("  other: " + string.Join(", ",
                        Array.ConvertAll(otherFiles, Path.GetFileName)));
                }

                FtsLog.Write("MergeTest.SegState[" + label + "]", sb.ToString().TrimEnd());
                Console.WriteLine(
                    $"║  Segments ({label}): {datFiles.Length} .dat  {dbFiles.Length} .db");
            }
            catch (Exception ex)
            {
                FtsLog.Write("MergeTest.SegState[" + label + "]",
                    "could not read dir state: " + ex.Message);
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                string destFile = Path.Combine(dst, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }
            foreach (var subDir in Directory.GetDirectories(src))
            {
                CopyDirectory(subDir,
                    Path.Combine(dst, Path.GetFileName(subDir)));
            }
        }
    }
}
