using FtsLib.Indexing;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FtsLibTest
{
    /// <summary>
    /// Harsh crash-recovery test for merges.
    ///
    /// Uses index_full_backup_before_merge (3× L1 segments + 1× L2 segment,
    /// pre-merge) as the stable source. For every crash scenario it:
    ///   1. Copies the backup into a fresh per-scenario work directory.
    ///   2. Recreates the exact crash state. Where the scenario involves a merge
    ///      target at its FINAL path, the target is produced by a REAL merge of
    ///      the real sources (via internal SegmentStore/SegmentMerger access) —
    ///      never by planting a copied file — so the recovery invariants match
    ///      what a genuine crash produces.
    ///   3. Constructs a new SeforimIndex (triggers Recover()).
    ///   4. Verifies the FULL result-ID set of every probe query against the
    ///      pristine backup — a single missing line ID anywhere in the doc-ID
    ///      space fails the scenario. (The old single-ID probe could not detect
    ///      loss of an entire source segment.)
    ///   5. Reports PASS / FAIL.
    ///
    /// Scenarios run in PARALLEL (each in its own work directory).
    ///
    /// Usage:
    ///   FtsLibTest.exe crashmergetest [--seq] [--dop N]
    /// </summary>
    internal static class CrashMergeTest
    {
        // Probe queries whose full result-ID sets are compared against the backup.
        //   "כי ביצחק" — the historical regression probe (multi-term AND, id 548)
        //   "אמר"      — ubiquitous single term: hits every doc-ID range, so the
        //                loss of ANY source segment shows up as missing IDs.
        private static readonly string[] ProbeQueries = { "כי ביצחק", "אמר" };

        // The stable source index — 3× L1 + 1× L2 segments, pre-merge state
        private static string BackupDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_full_backup_before_merge");

        // Per-scenario working directories — wiped and recreated for every run
        private static string WorkDirRoot =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "index_crashtest_work");

        private static string LogDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "merge_test_logs");

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool sequential = Array.Exists(args, a => a.Equals("--seq", StringComparison.OrdinalIgnoreCase));
            int  dop        = 4;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("--dop", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int d) && d > 0)
                    dop = d;
            if (sequential) dop = 1;

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
            Console.WriteLine($"║  Source   : {BackupDir}");
            Console.WriteLine($"║  Work     : {WorkDirRoot}_<scenario>");
            Console.WriteLine($"║  DB       : {dbPath}");
            Console.WriteLine($"║  Log      : {logPath}");
            Console.WriteLine($"║  Parallel : {dop} scenario(s) at a time");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            FtsLog.Separator("CRASH MERGE TEST START");

            // ── Expected result sets from the pristine backup ─────────────────────
            Console.WriteLine("║  Computing expected result sets from backup...");
            var expected = new Dictionary<string, HashSet<int>>();
            {
                var backupIndex = new SeforimIndex(BackupDir, dbPath);
                foreach (var q in ProbeQueries)
                {
                    var sw = Stopwatch.StartNew();
                    var set = new HashSet<int>(backupIndex.SearchIds(q));
                    sw.Stop();
                    expected[q] = set;
                    Console.WriteLine($"║    \"{q}\" → {set.Count:N0} ids  ({sw.ElapsedMilliseconds}ms)");
                    FtsLog.Write("CrashMergeTest", $"expected[{q}] = {set.Count:N0} ids");
                    if (set.Count == 0)
                    {
                        Console.WriteLine($"║  ABORT: probe \"{q}\" returned 0 results on the backup — backup is unusable.");
                        return;
                    }
                }
            }

            var scenarios = BuildScenarios();
            var results   = new List<(Scenario sc, bool ok, string output)>();
            var resultsLock = new object();

            var swAll = Stopwatch.StartNew();
            Parallel.ForEach(
                scenarios,
                new ParallelOptions { MaxDegreeOfParallelism = dop },
                sc =>
                {
                    var buf = new StringBuilder();
                    bool ok;
                    try
                    {
                        ok = RunScenario(sc, dbPath, expected, buf);
                    }
                    catch (Exception ex)
                    {
                        buf.AppendLine($"║     scenario runner threw: {ex.GetType().Name}: {ex.Message}");
                        ok = false;
                    }
                    lock (resultsLock)
                    {
                        results.Add((sc, ok, buf.ToString()));
                        Console.WriteLine($"║  [{results.Count,2}/{scenarios.Count}] {(ok ? "✓ PASS" : "✗ FAIL")}  {sc.Id}: {sc.Name}");
                    }
                });
            swAll.Stop();

            // ── Detailed per-scenario output, in scenario order ───────────────────
            results.Sort((a, b) => string.CompareOrdinal(a.sc.Id, b.sc.Id));
            Console.WriteLine("║");
            Console.WriteLine("╠══ DETAILS ═════════════════════════════════════════════════════════");
            int passed = 0, failed = 0;
            var failures = new List<string>();
            foreach (var (sc, ok, output) in results)
            {
                Console.WriteLine($"║  ── Scenario {sc.Id}: {sc.Name}  →  {(ok ? "✓ PASS" : "✗ FAIL")}");
                if (output.Length > 0) Console.Write(output);
                if (ok) passed++;
                else { failed++; failures.Add($"{sc.Id}: {sc.Name}"); }
            }

            Console.WriteLine("║");
            Console.WriteLine("╠══ SUMMARY ═════════════════════════════════════════════════════════");
            Console.WriteLine($"║  {scenarios.Count} scenarios: {passed} passed, {failed} FAILED  ({TestHelpers.FormatElapsed(swAll.Elapsed)})");
            if (failures.Count > 0)
            {
                Console.WriteLine("║  Failed scenarios (work dirs preserved for inspection):");
                foreach (var f in failures)
                    Console.WriteLine($"║    ✗ {f}");
            }
            Console.WriteLine($"║  Log: {logPath}");
            Console.WriteLine($"║  {(failed == 0 ? "✓  ALL PASS — index is crash-safe" : "✗  FAILURES DETECTED")}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
            FtsLog.Separator("CRASH MERGE TEST END");
        }

        // ── Scenario runner ───────────────────────────────────────────────────

        private static bool RunScenario(Scenario sc, string dbPath,
            Dictionary<string, HashSet<int>> expected, StringBuilder buf)
        {
            string workDir = WorkDirRoot + "_" + sc.Id;
            FtsLog.Separator($"SCENARIO {sc.Id}: {sc.Name}");

            // 1. Start from a clean backup copy
            PrepareWorkDir(workDir);
            FtsLog.Write("CrashMergeTest", $"[{sc.Id}] work dir prepared from backup");

            // 2. Recreate the crash state
            try
            {
                sc.Setup(workDir, dbPath);
                FtsLog.Write("CrashMergeTest", $"[{sc.Id}] setup complete");
                LogDirState(sc.Id, workDir, "after setup");
            }
            catch (Exception ex)
            {
                buf.AppendLine($"║     Setup threw: {ex.Message}");
                FtsLog.Write("CrashMergeTest", $"[{sc.Id}] setup EXCEPTION: {ex}");
                return false;
            }

            // 3. Construct SeforimIndex — triggers Recover()
            SeforimIndex index;
            try
            {
                index = new SeforimIndex(workDir, dbPath);
                FtsLog.Write("CrashMergeTest", $"[{sc.Id}] SeforimIndex constructed OK");
                LogDirState(sc.Id, workDir, "after recovery");
            }
            catch (Exception ex)
            {
                buf.AppendLine($"║     SeforimIndex threw: {ex.GetType().Name}: {ex.Message}");
                FtsLog.Write("CrashMergeTest", $"[{sc.Id}] SeforimIndex EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            // 4. WAL must be gone (or empty) after recovery
            string walPath = Path.Combine(workDir, "wal.log");
            if (File.Exists(walPath))
            {
                string content = File.ReadAllText(walPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    buf.AppendLine($"║     WAL not cleared after recovery! Content: {content.Substring(0, Math.Min(100, content.Length))}");
                    FtsLog.Write("CrashMergeTest", $"[{sc.Id}] WAL still has content after recovery: {content}");
                    return false;
                }
            }

            // 5. Full result-set verification against the backup
            bool ok = VerifyFullResults(index, sc, expected, buf);

            // 6. Clean up the work dir on pass; preserve it on failure
            if (ok)
                try { Directory.Delete(workDir, recursive: true); } catch { }
            else
                buf.AppendLine($"║     work dir preserved: {workDir}");

            return ok;
        }

        private static bool VerifyFullResults(SeforimIndex index, Scenario sc,
            Dictionary<string, HashSet<int>> expected, StringBuilder buf)
        {
            foreach (var q in ProbeQueries)
            {
                HashSet<int> got;
                var sw = Stopwatch.StartNew();
                try
                {
                    got = new HashSet<int>(index.SearchIds(q));
                }
                catch (Exception ex)
                {
                    buf.AppendLine($"║     search \"{q}\" threw: {ex.GetType().Name}: {ex.Message}");
                    FtsLog.Write("CrashMergeTest", $"[{sc.Id}] search EXCEPTION: {ex.Message}");
                    return false;
                }
                sw.Stop();

                if (sc.AllowEmpty && got.Count == 0)
                {
                    buf.AppendLine($"║     \"{q}\": 0 results (index wiped — expected) ✓ ({sw.ElapsedMilliseconds}ms)");
                    continue;
                }

                var exp = expected[q];
                if (!got.SetEquals(exp))
                {
                    int missing = exp.Count(id => !got.Contains(id));
                    int extra   = got.Count(id => !exp.Contains(id));
                    var sampleMissing = exp.Where(id => !got.Contains(id)).Take(5).ToArray();
                    buf.AppendLine($"║     \"{q}\": MISMATCH — {missing:N0} missing, {extra:N0} extra " +
                                   $"(expected {exp.Count:N0}, got {got.Count:N0})  ({sw.ElapsedMilliseconds}ms)");
                    if (sampleMissing.Length > 0)
                        buf.AppendLine($"║       first missing ids: {string.Join(", ", sampleMissing)}");
                    FtsLog.Write("CrashMergeTest",
                        $"[{sc.Id}] \"{q}\" MISMATCH missing={missing} extra={extra} expected={exp.Count} got={got.Count}");
                    return false;
                }

                buf.AppendLine($"║     \"{q}\": {got.Count:N0} ids — exact match ✓ ({sw.ElapsedMilliseconds}ms)");
                FtsLog.Write("CrashMergeTest", $"[{sc.Id}] \"{q}\" exact match ({got.Count:N0} ids)");
            }
            return true;
        }

        // ── Real-merge helpers (via InternalsVisibleTo) ───────────────────────

        /// <summary>
        /// Runs a REAL level merge to full completion (BEGIN_MERGE, k-way merge,
        /// rename, source deletion, END_MERGE) and clears the WAL — exactly what
        /// the production pipeline does. Crash states are then reconstructed by
        /// restoring stashed source files and rewriting the WAL.
        /// </summary>
        private static void RunRealMergeLevel(string dir, int level, int targetSegId)
        {
            var store = new SegmentStore(dir);
            store.RecoverReadOnly(); // rebuild live state from disk, no mutations
            store.Wal.Open();
            try
            {
                store.Merger.MergeLevel(level, targetSegId);
            }
            finally
            {
                store.Wal.Clear();
            }
        }

        /// <summary>Copies all segment files of a level to a stash directory (outside the work dir).</summary>
        private static string StashLevel(string dir, int level)
        {
            string stash = dir + "_stash_L" + level;
            if (Directory.Exists(stash)) Directory.Delete(stash, recursive: true);
            Directory.CreateDirectory(stash);
            foreach (var f in Directory.GetFiles(dir, $"seg_{level}_*.*"))
                File.Copy(f, Path.Combine(stash, Path.GetFileName(f)));
            return stash;
        }

        /// <summary>Restores selected segment IDs of a level from the stash into the work dir.</summary>
        private static void RestoreFromStash(string stash, string dir, int level, IEnumerable<int> segIds)
        {
            foreach (int sid in segIds)
            {
                foreach (var ext in new[] { ".dat", ".db" })
                {
                    string src = Path.Combine(stash, $"seg_{level}_{sid}{ext}");
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(dir, $"seg_{level}_{sid}{ext}"), overwrite: true);
                }
            }
        }

        private static void DeleteStash(string stash)
        {
            try { if (Directory.Exists(stash)) Directory.Delete(stash, recursive: true); } catch { }
        }

        /// <summary>
        /// Produces the canonical "killed during commit step 2" state:
        /// a REAL merged target at its final path, WAL showing BEGIN_MERGE without
        /// END_MERGE, and the given subset of source segments restored on disk.
        /// </summary>
        private static void SetupRealMidCommitState(
            string dir, int level, int targetSegId, Func<List<int>, IEnumerable<int>> survivorsOf)
        {
            var srcIds = AllSegments(dir, level).ConvertAll(s => s.Id);
            string stash = StashLevel(dir, level);
            try
            {
                RunRealMergeLevel(dir, level, targetSegId); // sources now deleted, target committed
                RestoreFromStash(stash, dir, level, survivorsOf(srcIds));
                WriteWal(dir,
                    $"BEGIN_MERGE level={level} sources={string.Join(",", srcIds)} target={targetSegId}\n");
            }
            finally
            {
                DeleteStash(stash);
            }
        }

        // ── Work dir helpers ──────────────────────────────────────────────────

        private static void PrepareWorkDir(string workDir)
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
            Directory.CreateDirectory(workDir);
            foreach (var file in Directory.GetFiles(BackupDir))
                File.Copy(file, Path.Combine(workDir, Path.GetFileName(file)));
        }

        private static void LogDirState(string scenarioId, string workDir, string label)
        {
            try
            {
                var files = Directory.GetFiles(workDir);
                FtsLog.Write($"CrashMergeTest.DirState[{scenarioId}:{label}]",
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

        /// <summary>Truncate an existing file in place to the given fraction.</summary>
        private static void TruncateInPlace(string path, double fraction)
        {
            long len = new FileInfo(path).Length;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                fs.SetLength(Math.Max(1, (long)(len * fraction)));
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
                // ── A: No crash at all — clean state, run ForceMerge normally ──
                new Scenario("A", "Clean state — no WAL, run ForceMerge normally", (dir, db) =>
                {
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
                }),

                // ── B: WAL exists with only BEGIN_MERGE, target not started ──
                new Scenario("B", "WAL has BEGIN_MERGE, no target, sources intact (re-run)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                }),

                // ── C: WAL + partial .dat.tmp (truncated mid-write) ──────────
                new Scenario("C", "WAL + truncated .dat.tmp (killed during dat write)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    WriteTruncated(segs[0].Dat,
                        Path.Combine(dir, "seg_2_99.dat.tmp"), fraction: 0.001);
                }),

                // ── D: WAL + full .dat.tmp, no .db.tmp ───────────────────────
                new Scenario("D", "WAL + complete .dat.tmp, .db.tmp missing (killed after dat write)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat.tmp"));
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

                // ── F: .dat final, .db still .tmp (killed between the two renames)
                // Target is REAL (a genuine merge product) but only half-renamed:
                // recovery must treat it as incomplete and re-run from the intact sources.
                new Scenario("F", "REAL target: .dat final, .db still .tmp (killed between renames)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99, survivorsOf: ids => ids);
                    // Demote the .db back to .tmp — as if its rename never happened.
                    File.Move(Path.Combine(dir, "seg_2_99.db"), Path.Combine(dir, "seg_2_99.db.tmp"));
                }),

                // ── G: REAL target committed, killed before ANY source deleted ──
                new Scenario("G", "REAL target final, all sources intact (killed before source deletion)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99, survivorsOf: ids => ids);
                }),

                // ── H: REAL target committed, killed mid-source-deletion ─────
                // THE historical data-loss bug: the old recovery deleted the complete
                // target and re-merged only the survivors, losing the first source.
                new Scenario("H", "REAL target final, FIRST source deleted, rest remain (mid-step-2 kill)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99,
                        survivorsOf: ids => ids.Skip(1));
                }),

                // ── H2: REAL target committed, only the LAST source remains ──
                // Old code: MergeLevel saw a single source, silently skipped the
                // re-run AND cleared the WAL — silent loss of every deleted source.
                new Scenario("H2", "REAL target final, only LAST source remains (late step-2 kill)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99,
                        survivorsOf: ids => ids.Skip(ids.Count - 1));
                }),

                // ── I: REAL target committed, ALL sources deleted, no END_MERGE ──
                new Scenario("I", "REAL target final, all sources deleted, no END_MERGE (classic Case B)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99,
                        survivorsOf: ids => new int[0]);
                }),

                // ── I2: torn target write — final path but truncated content ──
                // Rename landed but data did not (power loss). All sources intact:
                // full validation must reject the target and re-run the merge.
                new Scenario("I2", "Torn REAL target (.dat truncated) at final path, all sources intact", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99, survivorsOf: ids => ids);
                    TruncateInPlace(Path.Combine(dir, "seg_2_99.dat"), 0.3);
                }),

                // ── I3: torn target AND a missing source — unrecoverable ─────
                new Scenario("I3", "Torn REAL target + first source missing — must wipe (unrecoverable)", dir =>
                {
                    SetupRealMidCommitState(dir, level: 1, targetSegId: 99,
                        survivorsOf: ids => ids.Skip(1));
                    TruncateInPlace(Path.Combine(dir, "seg_2_99.dat"), 0.3);
                }, allowEmpty: true),

                // ── J: Pass 1 fully committed, killed before Pass 2 ──────────
                new Scenario("J", "REAL pass-1 merge committed, no WAL (killed between passes)", dir =>
                {
                    RunRealMergeLevel(dir, 1, 99);
                }),

                // ── K: Pass 1 done, killed mid-Pass-2 .dat.tmp write ─────────
                new Scenario("K", "REAL pass 1 done, Pass 2 killed mid-.dat.tmp write", dir =>
                {
                    RunRealMergeLevel(dir, 1, 99);
                    var l2segs = AllSegments(dir, 2);
                    var ids    = string.Join(",", l2segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=2 sources={ids} target=100\n");
                    WriteTruncated(l2segs[0].Dat,
                        Path.Combine(dir, "seg_3_100.dat.tmp"), fraction: 0.002);
                }),

                // ── L: Pass 1 done, Pass 2 fully committed except END_MERGE ──
                new Scenario("L", "REAL pass 1 + REAL pass 2 target, L2 sources gone, no END_MERGE", dir =>
                {
                    RunRealMergeLevel(dir, 1, 99);
                    var l2ids = AllSegments(dir, 2).ConvertAll(s => s.Id);
                    RunRealMergeLevel(dir, 2, 100);
                    WriteWal(dir,
                        $"BEGIN_MERGE level=2 sources={string.Join(",", l2ids)} target=100\n");
                }),

                // ── M: WAL truncated mid-line ────────────────────────────────
                new Scenario("M", "WAL file truncated mid-line (partial write of BEGIN_MERGE)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    string full = $"BEGIN_MERGE level=1 sources={ids} target=99\n";
                    WriteWal(dir, full.Substring(0, full.Length / 2));
                }),

                // ── N: BEGIN_MERGE but target AND sources both missing ───────
                new Scenario("N", "WAL has BEGIN_MERGE, target AND sources both missing (wipe + empty index)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    foreach (var s in segs) { File.Delete(s.Dat); File.Delete(s.Db); }
                    // Recovery wipes; EnsureStore catches CorruptIndexException and
                    // resets to an empty store — constructor succeeds, index is empty.
                }, allowEmpty: true),

                // ── O: Stale -shm/-wal sidecars next to live segments ────────
                new Scenario("O", "Stale .db-shm/.db-wal sidecar files next to live segments", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    File.WriteAllText(segs[0].Db + "-shm", "stale shm data");
                    File.WriteAllText(segs[0].Db + "-wal", "stale wal data");
                    File.WriteAllText(segs[1].Db + "-shm", "stale shm data");
                    var l2 = AllSegments(dir, 2)[0];
                    File.WriteAllText(l2.Db + "-wal", "stale wal data");
                }),

                // ── P: WAL with multiple BEGIN_MERGE stacked (no ENDs) ───────
                new Scenario("P", "WAL has multiple stacked BEGIN_MERGE without END_MERGE", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir,
                        "BEGIN_MERGE level=1 sources=25,30 target=88\n" +
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                }),

                // ── Q: WAL with completed merge + new orphaned BEGIN ─────────
                new Scenario("Q", "WAL has old BEGIN+END then a new orphaned BEGIN", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir,
                        "BEGIN_MERGE level=1 sources=25,30 target=88\n" +
                        "END_MERGE level=1 target=88\n" +
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                }),

                // ── Q2: aborted merge (shutdown) — BEGIN + ABORT_MERGE ───────
                new Scenario("Q2", "WAL has BEGIN_MERGE + ABORT_MERGE (merge aborted by shutdown)", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir,
                        $"BEGIN_MERGE level=1 sources={ids} target=99\n" +
                        "ABORT_MERGE level=1 target=99\n");
                }),

                // ── R: Target .dat exists (fake copy) but .db missing ────────
                // A copied .dat alone can never be mistaken for a committed target
                // because targetExists requires BOTH final files.
                new Scenario("R", "Target .dat exists but .db missing — partial rename", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat"));
                }),

                // ── S: Sidecar files next to the .tmp target ─────────────────
                new Scenario("S", "Stale sidecar files (.db.tmp-shm/.db.tmp-wal) next to .tmp target", dir =>
                {
                    var segs = AllSegments(dir, 1);
                    var ids  = string.Join(",", segs.ConvertAll(s => s.Id.ToString()));
                    WriteWal(dir, $"BEGIN_MERGE level=1 sources={ids} target=99\n");
                    File.Copy(segs[0].Dat, Path.Combine(dir, "seg_2_99.dat.tmp"));
                    File.Copy(segs[0].Db,  Path.Combine(dir, "seg_2_99.db.tmp"));
                    File.WriteAllText(Path.Combine(dir, "seg_2_99.db.tmp-shm"), "sidecar");
                    File.WriteAllText(Path.Combine(dir, "seg_2_99.db.tmp-wal"), "sidecar");
                }),

                // ── T: ForceMerge then verify single merged segment ──────────
                new Scenario("T", "Normal ForceMerge + verify on merged single-segment result", (dir, db) =>
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

                // ── Y: force merge: REAL pass 1 committed, killed between passes ──
                new Scenario("Y", "BEGIN_FORCE_MERGE + REAL pass-1 END_MERGE, killed between passes", dir =>
                {
                    var l1ids = AllSegments(dir, 1).ConvertAll(s => s.Id);
                    RunRealMergeLevel(dir, 1, 99);
                    WriteWal(dir,
                        "BEGIN_FORCE_MERGE\n" +
                        $"BEGIN_MERGE level=1 sources={string.Join(",", l1ids)} target=99\n" +
                        "END_MERGE level=1 target=99\n");
                }),

                // ── Z: All merges done, END_FORCE_MERGE missing ──────────────
                new Scenario("Z", "All level merges committed, END_FORCE_MERGE missing", (dir, db) =>
                {
                    var index = new SeforimIndex(dir, db);
                    index.ForceMerge();
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
            public readonly string                 Id;
            public readonly string                 Name;
            public readonly Action<string, string> Setup;  // (dir, dbPath)
            public readonly bool                   AllowEmpty;  // 0 results acceptable (wiped index)

            public Scenario(string id, string name, Action<string, string> setup,
                            bool allowEmpty = false)
            {
                Id         = id;
                Name       = name;
                Setup      = setup;
                AllowEmpty = allowEmpty;
            }

            public Scenario(string id, string name, Action<string> setup,
                            bool allowEmpty = false)
                : this(id, name, (dir, _) => setup(dir), allowEmpty) { }
        }
    }
}
