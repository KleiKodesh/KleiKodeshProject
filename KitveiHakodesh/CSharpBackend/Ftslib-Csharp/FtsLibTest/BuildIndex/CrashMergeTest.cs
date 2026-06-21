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
    /// Harsh crash-recovery test for force merge.
    ///
    /// Uses index_full_backup_before_merge (4 segments, pre-merge) as the
    /// stable source. For every crash scenario it:
    ///   1. Copies the backup into a fresh work directory.
    ///   2. Manually mutilates the directory to simulate the exact crash state.
    ///   3. Constructs a new SeforimIndex (triggers Recover()).
    ///   4. Runs the probe search — must find id=548 in results for "כי ביצחק".
    ///   5. Reports PASS / FAIL.
    ///
    /// Crash scenarios tested (all phases of the commit sequence):
    ///   A  — killed before merge starts (no WAL, no tmp)
    ///   B  — killed after WAL BEGIN_MERGE, before any file written
    ///   C  — killed while writing .dat.tmp (partial/truncated file)
    ///   D  — killed after .dat.tmp complete, before .db.tmp written
    ///   E  — killed after .db.tmp complete, before File.Move
    ///   F  — killed after .dat renamed to final, before .db renamed
    ///   G  — killed after both renamed, before any source deleted
    ///   H  — killed after first source deleted, rest remain
    ///   I  — killed after ALL sources deleted, before END_MERGE  (Case B)
    ///   J  — Pass 1 fully complete, killed before Pass 2 writes anything
    ///   K  — Pass 1 complete, killed mid-Pass-2 .dat.tmp write
    ///   L  — Pass 1 complete, Pass 2 Case B (sources gone, target exists, no END_MERGE)
    ///   M  — WAL truncated mid-line (partial write)
    ///   N  — WAL has BEGIN_MERGE but target AND sources both missing (Case D — must wipe+rebuild)
    ///   O  — Stale -shm/-wal sidecar files next to existing segments
    ///   P  — Multiple stacked BEGIN_MERGE without END_MERGE (old-format WAL)
    ///
    /// Usage:
    ///   FtsLibTest.exe crashmergetest
    /// </summary>
    internal static class CrashMergeTest
    {
        private const string ProbeQuery      = "כי ביצחק";
        private const int    ProbeRequiredId = 548;

        // The stable source index — 4 segments, pre-merge state
        private static string BackupDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_full_backup_before_merge");

        // Working directory — wiped and recreated for every scenario
        private static string WorkDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_crashtest_work");

        // Log directory
        private static string LogDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "merge_test_logs");

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (!Directory.Exists(BackupDir) ||
                Directory.GetFiles(BackupDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"[CrashMergeTest] Backup index not found at: {BackupDir}");
                Console.WriteLine("Run: FtsLibTest.exe mergetest full   to create it first.");
                return;
            }

            string dbPath  = BuildTest.ResolveDbPath();
            string logPath = Path.Combine(LogDir,
                $"CrashMerge_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            Directory.CreateDirectory(LogDir);
            FtsLog.LogPath = logPath;
            FtsLog.Clear();

            Console.WriteLine();
            Console.WriteLine("╔══ CRASH MERGE TEST ════════════════════════════════════════════════");
            Console.WriteLine($"║  Source  : {BackupDir}");
            Console.WriteLine($"║  Work    : {WorkDir}");
            Console.WriteLine($"║  DB      : {dbPath}");
            Console.WriteLine($"║  Log     : {logPath}");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            FtsLog.Separator("CRASH MERGE TEST START");

            var scenarios = BuildScenarios();
            int passed = 0, failed = 0;
            var failures = new List<string>();

            foreach (var sc in scenarios)
            {
                Console.WriteLine($"║");
                Console.WriteLine($"║  ── Scenario {sc.Id}: {sc.Name}");
                FtsLog.Separator($"SCENARIO {sc.Id}: {sc.Name}");

                bool ok = RunScenario(sc, dbPath);
                if (ok)
                {
                    passed++;
                    Console.WriteLine($"║     ✓ PASS");
                    FtsLog.Write("CrashMergeTest", $"Scenario {sc.Id} PASS");
                }
                else
                {
                    failed++;
                    failures.Add($"{sc.Id}: {sc.Name}");
                    Console.WriteLine($"║     ✗ FAIL  ← CORRUPTION / WRONG RESULT");
                    FtsLog.Write("CrashMergeTest", $"Scenario {sc.Id} FAIL");
                }
            }

            // Clean up work dir on full pass
            if (failed == 0)
                try { Directory.Delete(WorkDir, recursive: true); } catch { }

            Console.WriteLine("║");
            Console.WriteLine("╠══ SUMMARY ═════════════════════════════════════════════════════════");
            Console.WriteLine($"║  {scenarios.Count} scenarios: {passed} passed, {failed} FAILED");
            if (failures.Count > 0)
            {
                Console.WriteLine("║  Failed scenarios:");
                foreach (var f in failures)
                    Console.WriteLine($"║    ✗ {f}");
            }
            Console.WriteLine($"║  Log: {logPath}");
            Console.WriteLine($"║  {(failed == 0 ? "✓  ALL PASS — index is crash-safe" : "✗  FAILURES DETECTED")}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
            FtsLog.Separator("CRASH MERGE TEST END");
        }

        // ── Scenario runner ───────────────────────────────────────────────────

        private static bool RunScenario(Scenario sc, string dbPath)
        {
            // 1. Start from clean backup copy
            PrepareWorkDir();
            FtsLog.Write("CrashMergeTest", $"work dir prepared from backup");

            // 2. Mutilate to simulate crash state
            try
            {
                sc.Setup(WorkDir, dbPath);
                FtsLog.Write("CrashMergeTest", $"setup complete");
                LogDirState("after setup");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"║     Setup threw: {ex.Message}");
                FtsLog.Write("CrashMergeTest", $"setup EXCEPTION: {ex.Message}");
                return false;
            }

            // 3. Construct SeforimIndex — triggers Recover()
            SeforimIndex index;
            try
            {
                index = new SeforimIndex(WorkDir, dbPath);
                FtsLog.Write("CrashMergeTest", "SeforimIndex constructed OK");
                LogDirState("after recovery");
            }
            catch (Exception ex)
            {
                // CorruptIndexException with wipe is only acceptable for Case D
                if (sc.ExpectWipe && ex is FtsLib.Indexing.CorruptIndexException)
                {
                    FtsLog.Write("CrashMergeTest", $"got expected CorruptIndexException (Case D wipe): {ex.Message}");
                    Console.WriteLine($"║     Expected wipe (Case D): {ex.Message.Split('\n')[0]}");
                    return true; // wipe is the correct behaviour for this scenario
                }
                Console.WriteLine($"║     SeforimIndex threw: {ex.GetType().Name}: {ex.Message}");
                FtsLog.Write("CrashMergeTest", $"SeforimIndex EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            // If we expected a wipe but didn't get one, that's a fail
            if (sc.ExpectWipe)
            {
                Console.WriteLine("║     Expected CorruptIndexException (wipe) but none thrown");
                FtsLog.Write("CrashMergeTest", "expected wipe but SeforimIndex succeeded — FAIL");
                return false;
            }

            // 4. Verify WAL is gone after recovery
            string walPath = Path.Combine(WorkDir, "wal.log");
            if (File.Exists(walPath))
            {
                string content = File.ReadAllText(walPath);
                // WAL must be empty or absent after clean recovery
                if (!string.IsNullOrWhiteSpace(content))
                {
                    Console.WriteLine($"║     WAL not cleared after recovery! Content: {content.Substring(0, Math.Min(100, content.Length))}");
                    FtsLog.Write("CrashMergeTest", $"WAL still has content after recovery: {content}");
                    return false;
                }
            }

            // 5. Probe search
            return RunProbe(index, sc.Id, sc.AllowEmpty);
        }

        private static bool RunProbe(SeforimIndex index, string scenarioId, bool allowEmpty = false)
        {
            int  count = 0;
            bool found = false;
            var  sw    = Stopwatch.StartNew();
            try
            {
                foreach (var r in index.Search(ProbeQuery))
                {
                    count++;
                    if (r.LineId == ProbeRequiredId) found = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"║     Search threw: {ex.GetType().Name}: {ex.Message}");
                FtsLog.Write("CrashMergeTest", $"[{scenarioId}] search EXCEPTION: {ex.Message}");
                return false;
            }
            sw.Stop();

            FtsLog.Write("CrashMergeTest",
                $"[{scenarioId}] probe: count={count} found={found} allowEmpty={allowEmpty} ms={sw.ElapsedMilliseconds}");

            if (allowEmpty && count == 0)
            {
                Console.WriteLine($"║     count=0 (index wiped — expected) ✓ ({sw.ElapsedMilliseconds}ms)");
                return true;
            }

            if (!found)
            {
                Console.WriteLine($"║     count={count}, id={ProbeRequiredId} MISSING ({sw.ElapsedMilliseconds}ms)");
                return false;
            }
            Console.WriteLine($"║     count={count}, id={ProbeRequiredId} ✓ ({sw.ElapsedMilliseconds}ms)");
            return true;
        }

        // ── Work dir helpers ──────────────────────────────────────────────────

        private static void PrepareWorkDir()
        {
            if (Directory.Exists(WorkDir))
                Directory.Delete(WorkDir, recursive: true);
            Directory.CreateDirectory(WorkDir);
            foreach (var file in Directory.GetFiles(BackupDir))
                File.Copy(file, Path.Combine(WorkDir, Path.GetFileName(file)));
        }

        private static void LogDirState(string label)
        {
            try
            {
                var files = Directory.GetFiles(WorkDir);
                FtsLog.Write($"CrashMergeTest.DirState[{label}]",
                    $"{files.Length} file(s): " +
                    string.Join(", ", Array.ConvertAll(files, Path.GetFileName)));
            }
            catch { }
        }

        // ── Helpers for creating crash states ─────────────────────────────────

        /// <summary>Write a wal.log containing exactly the given content.</summary>
        private static void WriteWal(string dir, string content)
        {
            File.WriteAllText(Path.Combine(dir, "wal.log"), content, Encoding.UTF8);
        }

        /// <summary>Create a partial (truncated) copy of a file at targetPath.</summary>
        private static void WriteTruncated(string sourcePath, string targetPath, double fraction = 0.3)
        {
            byte[] src  = File.ReadAllBytes(sourcePath);
            int    keep = Math.Max(1, (int)(src.Length * fraction));
            using (var fs = new FileStream(targetPath, FileMode.Create))
                fs.Write(src, 0, keep);
        }

        private sealed class SegInfo
        {
            public readonly string Dat;
            public readonly string Db;
            public readonly int    Level;
            public readonly int    Id;
            public SegInfo(string dat, string db, int level, int id)
            { Dat = dat; Db = db; Level = level; Id = id; }
        }

        private static SegInfo FirstSegment(string dir, int level)
        {
            foreach (var f in Directory.GetFiles(dir, $"seg_{level}_*.dat"))
            {
                var parts = Path.GetFileNameWithoutExtension(f).Split('_');
                int id    = int.Parse(parts[2]);
                string db = f.Replace(".dat", ".db");
                return new SegInfo(f, db, level, id);
            }
            throw new InvalidOperationException($"No L{level} segment found in {dir}");
        }

        private static List<SegInfo> AllSegments(string dir, int level)
        {
            var result = new List<SegInfo>();
            foreach (var f in Directory.GetFiles(dir, $"seg_{level}_*.dat"))
            {
                var parts = Path.GetFileNameWithoutExtension(f).Split('_');
                int id    = int.Parse(parts[2]);
                string db = f.Replace(".dat", ".db");
                result.Add(new SegInfo(f, db, level, id));
            }
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result;
        }

        // ── Scenario definitions ──────────────────────────────────────────────

        private static List<Scenario> BuildScenarios()
        {
            return new List<Scenario>
            {
                // ── A: No crash at all — clean state, no WAL ─────────────────
                new Scenario("A", "Clean state — no WAL, run ForceMerge normally", (dir, db) =>
                {
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
                }),

                // ── B: WAL exists with only BEGIN_MERGE, target not started ──
                new Scenario("B", "WAL has BEGIN_MERGE, no target, sources intact (Case A/C)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // No target file created — pure Case C
                }),

                // ── C: WAL + partial .dat.tmp (truncated mid-write) ──────────
                new Scenario("C", "WAL + truncated .dat.tmp (killed during dat write)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // Write a heavily truncated .dat.tmp (first 512 bytes only)
                    WriteTruncated(segs[0].Dat,
                        Path.Combine(dir, "seg_2_99.dat.tmp"), fraction: 0.001);
                }),

                // ── D: WAL + full .dat.tmp, no .db.tmp ───────────────────────
                new Scenario("D", "WAL + complete .dat.tmp, .db.tmp missing (killed after dat write)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // Copy first source dat as a plausible (wrong) .dat.tmp
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat.tmp"));
                    // No .db.tmp
                }),

                // ── E: WAL + both .tmp files complete, before rename ─────────
                new Scenario("E", "WAL + both .tmp complete, before File.Move (killed just before rename)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat.tmp"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db.tmp"));
                }),

                // ── F: .dat renamed to final, .db still .tmp ────────────────
                new Scenario("F", "WAL + .dat final, .db still .tmp (killed between renames)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // .dat is in its final position; .db is still .tmp
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db.tmp"));
                    // Sources still exist
                }),

                // ── G: Both renamed final, all sources still exist ────────────
                new Scenario("G", "WAL + target final, all sources intact (killed before source deletion)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    // All sources still exist — Case A
                }),

                // ── H: Target final, first source deleted, rest remain ────────
                new Scenario("H", "WAL + target final, first source deleted, rest remain", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    // Delete only the first source
                    File.Delete(segs[0].Dat);
                    File.Delete(segs[0].Db);
                }),

                // ── I: Target final, ALL sources deleted, no END_MERGE (Case B)
                new Scenario("I", "WAL + target final, all sources deleted, no END_MERGE (Case B)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    // Delete ALL L1 sources — only target and L2 remain
                    foreach (var s in segs)
                    {
                        File.Delete(s.Dat);
                        File.Delete(s.Db);
                    }
                }),

                // ── J: Pass 1 fully committed, killed before Pass 2 ──────────
                // WAL has BEGIN+END for pass 1, no entry for pass 2 yet
                // (i.e. WAL was cleared between passes — clean state mid-merge)
                new Scenario("J", "Pass 1 fully committed, no WAL for Pass 2 (killed between passes)", dir =>
                {
                    // Simulate: L1 segs merged into a new L2 seg, sources deleted, WAL clear
                    // The L2 seg from backup (seg_2_20) is still there.
                    // We also copy one of the L1 segs as a second L2 seg — now L2 has 2 segs.
                    var l1segs = AllSegments(dir, 1);
                    // Copy first L1 as a second L2 segment (id=99)
                    File.Copy(l1segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(l1segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    // Delete all L1 sources (simulating completed pass 1)
                    foreach (var s in l1segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    // No WAL — clean state between passes
                }),

                // ── K: Pass 1 done, killed mid-Pass-2 .dat.tmp write ─────────
                new Scenario("K", "Pass 1 done, Pass 2 killed mid-.dat.tmp write", dir =>
                {
                    var l1segs = AllSegments(dir, 1);
                    // Promote L1 into a second L2 segment
                    File.Copy(l1segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(l1segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    foreach (var s in l1segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    // Now WAL has a BEGIN_MERGE for the L2 merge, with a partial .tmp
                    var l2segs = AllSegments(dir, 2);
                    var ids    = string.Join(",", l2segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=2 sources={ids} target=100\n");
                    WriteTruncated(l2segs[0].Dat,
                        Path.Combine(dir, "seg_3_100.dat.tmp"), fraction: 0.002);
                }),

                // ── L: Pass 1 done, Pass 2 Case B (sources gone, target exists, no END_MERGE)
                new Scenario("L", "Pass 1 done, Pass 2 Case B (all L2 sources gone, target exists, no END_MERGE)", dir =>
                {
                    var l1segs = AllSegments(dir, 1);
                    File.Copy(l1segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(l1segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    foreach (var s in l1segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    var l2segs = AllSegments(dir, 2);
                    var ids    = string.Join(",", l2segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=2 sources={ids} target=100\n");
                    // Target exists
                    File.Copy(l2segs[0].Dat, Path.Combine(dir, "seg_3_100.dat"));
                    File.Copy(l2segs[0].Db,  Path.Combine(dir, "seg_3_100.db"));
                    // All L2 sources deleted
                    foreach (var s in l2segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                }),

                // ── M: WAL truncated mid-line ────────────────────────────────
                new Scenario("M", "WAL file truncated mid-line (partial write of BEGIN_MERGE)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    // Write a BEGIN_MERGE line that is cut off halfway
                    string full = $"BEGIN_MERGE level=1 sources={ids} target=99\n";
                    string half = full.Substring(0, full.Length / 2);
                    WriteWal(dir, half);
                }),

                // ── N: WAL says BEGIN_MERGE but BOTH target and sources missing (Case D)
                new Scenario("N", "Case D — WAL has BEGIN_MERGE, target AND sources both missing (wipe + empty index)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // Delete all L1 sources AND no target — truly unrecoverable
                    foreach (var s in segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    // No target file either — recovery must wipe and produce an empty store
                    // The only live segment left is the L2 one; but the WAL targets L1 sources
                    // that don't exist. Recovery wipes everything.
                    // NOTE: EnsureStore catches CorruptIndexException internally and resets to
                    // a fresh empty store — so the SeforimIndex constructor succeeds but the
                    // index is now empty. That is correct behaviour; a fresh build is needed.
                    // The test verifies this by checking 0 results (index is empty).
                }, allowEmpty: true),

                // ── O: Stale -shm/-wal sidecars next to live segments ─────────
                new Scenario("O", "Stale .db-shm/.db-wal sidecar files next to live segments", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    // Drop phantom sidecar files next to the first two L1 segments
                    File.WriteAllText(segs[0].Db + "-shm", "stale shm data");
                    File.WriteAllText(segs[0].Db + "-wal", "stale wal data");
                    File.WriteAllText(segs[1].Db + "-shm", "stale shm data");
                    // Also one for the L2 segment
                    var l2 = FirstSegment(dir, 2);
                    File.WriteAllText(l2.Db + "-wal", "stale wal data");
                }),

                // ── P: WAL with multiple BEGIN_MERGE stacked (no ENDs) ───────
                new Scenario("P", "WAL has multiple stacked BEGIN_MERGE without END_MERGE", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    // Two BEGIN_MERGEs with no ENDs — last one wins per Analyze() logic
                    string wal =
                        $"BEGIN_MERGE level=1 sources=25,30 target=88\n" +
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n";
                    WriteWal(dir, wal);
                    // No target file, sources intact — should re-run merge
                }),

                // ── Q: WAL with completed merge + new BEGIN (stale END then new BEGIN) ──
                new Scenario("Q", "WAL has old BEGIN+END then a new orphaned BEGIN", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    string wal =
                        $"BEGIN_MERGE level=1 sources=25,30 target=88\n" +
                        $"END_MERGE level=1 target=88\n" +
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n";
                    WriteWal(dir, wal);
                    // sources still intact, no target — Case C
                }),

                // ── R: Target .dat exists but .db is completely missing ───────
                new Scenario("R", "Target .dat exists but .db missing — partial rename (dat done, db failed)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    // Only .dat renamed, .db never got there
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    // No seg_2_99.db — sources intact
                }),

                // ── S: Sidecar files next to the .tmp target ─────────────────
                new Scenario("S", "Stale sidecar files (.db.tmp-shm/.db.tmp-wal) next to .tmp target", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat.tmp"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db.tmp"));
                    // Sidecar files alongside the .db.tmp
                    File.WriteAllText(Path.Combine(dir, "seg_2_99.db.tmp-shm"), "sidecar");
                    File.WriteAllText(Path.Combine(dir, "seg_2_99.db.tmp-wal"), "sidecar");
                }),

                // ── T: ForceMerge then search on the merged result ────────────
                new Scenario("T", "Normal ForceMerge + probe on merged single-segment result", (dir, db) =>
                {
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
                    var dats = Directory.GetFiles(dir, "seg_*.dat");
                    if (dats.Length != 1)
                        throw new InvalidOperationException(
                            $"Expected 1 segment after force merge, got {dats.Length}");
                }),

                // ── U: Two consecutive ForceMerges (idempotent) ──────────────
                new Scenario("U", "Two consecutive ForceMerges — must be idempotent", (dir, db) =>
                {
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
                    index.ForceMerge();
                }),

                // ── V: WAL is empty file (zero bytes) ────────────────────────
                new Scenario("V", "WAL is a zero-byte file", dir =>
                {
                    File.WriteAllBytes(Path.Combine(dir, "wal.log"), new byte[0]);
                }),

                // ── W: WAL contains only whitespace/newlines ─────────────────
                new Scenario("W", "WAL contains only whitespace and newlines", dir =>
                {
                    WriteWal(dir, "\n\n\r\n   \n");
                }),

                // ── X: BEGIN_FORCE_MERGE only — killed before first level merge ──
                new Scenario("X", "BEGIN_FORCE_MERGE only — killed before first level merge", dir =>
                {
                    WriteWal(dir, "BEGIN_FORCE_MERGE\n");
                }),

                // ── Y: BEGIN_FORCE_MERGE + pass 1 fully committed, killed between passes ──
                new Scenario("Y", "BEGIN_FORCE_MERGE + pass 1 END_MERGE, killed between passes", dir =>
                {
                    var l1segs = AllSegments(dir, 1);
                    File.Copy(l1segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                    File.Copy(l1segs[0].Db,  Path.Combine(dir, "seg_2_99.db"));
                    foreach (var s in l1segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    var ids = string.Join(",", l1segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir,
                        "BEGIN_FORCE_MERGE\n" +
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n" +
                        "END_MERGE level=1 target=99\n");
                    // L2 now has 2 segs (original + seg_2_99) — resume should merge them
                }),

                // ── Z: All merges done, END_FORCE_MERGE missing (killed at the very end) ──
                // The correct way to simulate this: actually run ForceMerge to produce a real
                // fully-merged segment, then rewrite the WAL to look like END_FORCE_MERGE was
                // never written. Recovery must find PendingForceMerge=true, PendingMerge=null,
                // call ResumeForceMerge, find nothing left to merge, and clean up correctly.
                new Scenario("Z", "All level merges committed, END_FORCE_MERGE missing", (dir, db) =>
                {
                    // Step 1: actually run ForceMerge so the single segment on disk is real
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
                    // Step 2: rewrite the WAL to simulate the crash — force merge done but
                    // END_FORCE_MERGE not written yet
                    var dats  = Directory.GetFiles(dir, "seg_*.dat");
                    var parts = Path.GetFileNameWithoutExtension(dats[0]).Split('_');
                    int level = int.Parse(parts[1]);
                    int segId = int.Parse(parts[2]);
                    WriteWal(dir,
                        "BEGIN_FORCE_MERGE\n" +
                        $"BEGIN_MERGE level={level - 1} sources=0 target={segId}\n" +
                        $"END_MERGE level={level - 1} target={segId}\n");
                }),
            };
        }

        // ── Scenario type ─────────────────────────────────────────────────────

        private sealed class Scenario
        {
            public readonly string              Id;
            public readonly string              Name;
            public readonly Action<string, string> Setup;  // (dir, dbPath)
            public readonly bool                ExpectWipe;
            public readonly bool                AllowEmpty;  // 0 results is acceptable (wiped index)

            public Scenario(string id, string name, Action<string, string> setup,
                            bool expectWipe = false, bool allowEmpty = false)
            {
                Id         = id;
                Name       = name;
                Setup      = setup;
                ExpectWipe = expectWipe;
                AllowEmpty = allowEmpty;
            }

            // Convenience ctor for scenarios that don't need dbPath
            public Scenario(string id, string name, Action<string> setup,
                            bool expectWipe = false, bool allowEmpty = false)
                : this(id, name, (dir, _) => setup(dir), expectWipe, allowEmpty) { }
        }
    }
}
