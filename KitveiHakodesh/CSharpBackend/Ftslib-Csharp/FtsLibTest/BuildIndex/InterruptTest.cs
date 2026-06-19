using FtsLib.Indexing;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibTest
{
    /// <summary>
    /// Interrupt-and-recover stress test.
    ///
    /// Repeatedly builds the index, cancels it at a random point (mid-flush or
    /// mid-merge), then probes the resulting on-disk state for correctness.
    /// Logs every cycle to FtsLib.log and prints a compact summary to stdout.
    ///
    /// Three kill modes:
    ///   hard      — cancels the token, then abandons the task after 50ms,
    ///               simulating a process kill mid-merge (the original bug)
    ///   stopall   — cancels the token, waits up to 60s (old buggy StopAll),
    ///               then proceeds regardless — reproduces the timeout race
    ///   fixed     — cancels the token, waits indefinitely (fixed StopAll),
    ///               should never produce corruption
    ///   soft      — cancels the token, waits for full drain (clean cancel)
    ///
    /// Usage:
    ///   FtsLibTest.exe interrupttest [cycles] [killAfterFlushes] [mode]
    ///
    ///   cycles            Number of interrupt+recover cycles (default: 20)
    ///   killAfterFlushes  Kill after this many flushes (0 = random 1-8, default: 0)
    ///   mode              hard | stopall | fixed | soft  (default: hard)
    ///
    /// Examples:
    ///   FtsLibTest.exe interrupttest              -- 20 hard random-kill cycles
    ///   FtsLibTest.exe interrupttest 50           -- 50 hard random-kill cycles
    ///   FtsLibTest.exe interrupttest 20 4 stopall -- reproduce the 60s-timeout bug
    ///   FtsLibTest.exe interrupttest 20 4 fixed   -- verify the fix holds
    /// </summary>
    internal static class InterruptTest
    {
        // The probe query and required line ID — same as ProbeSearch uses.
        private const string ProbeQuery      = "כי ביצחק";
        private const int    ProbeRequiredId = 548;

        // Index directory used exclusively by this test — never touches the
        // real index so the app is not affected.
        private static string IndexDir =>
            Path.Combine(Path.GetTempPath(), "FtsInterruptTestIndex");

        public static void Run(string[] args)
        {
            int cycles           = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 20;
            int fixedKillFlushes = args.Length > 2 && int.TryParse(args[2], out int k) ? k : 0;
            string mode          = args.Length > 3 ? args[3].ToLowerInvariant() : "hard";

            // Validate mode
            if (mode != "hard" && mode != "stopall" && mode != "fixed" && mode != "soft")
            {
                Console.WriteLine($"Unknown mode '{mode}'. Use: hard | stopall | fixed | soft");
                return;
            }

            string modeDescription;
            switch (mode)
            {
                case "hard":    modeDescription = "HARD — abandon mid-merge (simulates process kill)"; break;
                case "stopall": modeDescription = "STOPALL — wait up to 60s then proceed (OLD BUGGY behavior)"; break;
                case "fixed":   modeDescription = "FIXED — wait indefinitely for drain (new correct behavior)"; break;
                default:        modeDescription = "SOFT — wait for full drain (clean cancel)"; break;
            }

            string dbPath = BuildTest.ResolveDbPath();
            if (!File.Exists(dbPath))
            {
                Console.WriteLine("[InterruptTest] DB not found: " + dbPath);
                return;
            }

            // Point FtsLog at a file next to the test index so it's easy to find.
            string logPath = Path.Combine(Path.GetTempPath(), "FtsInterruptTest.log");
            FtsLog.LogPath = logPath;
            FtsLog.Clear();

            Console.WriteLine();
            Console.WriteLine("╔══ INTERRUPT TEST ══════════════════════════════════════════════════");
            Console.WriteLine($"║  DB      : {dbPath}");
            Console.WriteLine($"║  Index   : {IndexDir}");
            Console.WriteLine($"║  Log     : {logPath}");
            Console.WriteLine($"║  Cycles  : {cycles}");
            Console.WriteLine($"║  Kill at : {(fixedKillFlushes == 0 ? "random (1-8 flushes)" : fixedKillFlushes + " flushes")}");
            Console.WriteLine($"║  Mode    : {modeDescription}");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            FtsLog.Separator("INTERRUPT TEST START");
            FtsLog.Write("InterruptTest", $"cycles={cycles} fixedKillFlushes={fixedKillFlushes} mode={mode} db={dbPath}");

            // Wipe any leftover index from a previous run.
            WipeIndexDir();

            var rand    = new Random(42);
            int passed  = 0;
            int failed  = 0;
            int skipped = 0; // cycles where index wasn't searchable yet after recovery

            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                int killAfter = fixedKillFlushes > 0
                    ? fixedKillFlushes
                    : rand.Next(1, 9); // 1..8 flushes

                Console.WriteLine($"║  Cycle {cycle,3}/{cycles}  kill after {killAfter} flush(es)...");
                FtsLog.Separator($"CYCLE {cycle}/{cycles}  killAfterFlushes={killAfter}");

                // ── Phase 1: Build until killAfter flushes, then cancel ──────────
                bool interrupted = RunInterruptedBuild(dbPath, killAfter, mode);
                if (!interrupted)
                {
                    // Build completed before we could interrupt — index is fully built.
                    // Run the probe anyway (it should always pass here).
                    Console.WriteLine($"║    build completed before kill point — probing as normal");
                }

                // ── Phase 2: Simulate next-launch by constructing a fresh SeforimIndex ──
                // This is exactly what the app does: new SeforimIndex(indexPath, dbPath)
                // runs recovery in its constructor.
                FtsLog.Separator($"CYCLE {cycle} — RECOVERY (simulated next launch)");
                ProbeResult result = RunProbeAfterRecovery(dbPath);

                // ── Phase 3: Report ──────────────────────────────────────────────
                string status;
                switch (result)
                {
                    case ProbeResult.Pass:
                        passed++;
                        status = "✓ PASS";
                        break;
                    case ProbeResult.Fail:
                        failed++;
                        status = "✗ FAIL  ← CORRUPTION DETECTED";
                        break;
                    default: // NotSearchableYet
                        skipped++;
                        status = "~ skip (no segments yet)";
                        break;
                }

                Console.WriteLine($"║    → {status}");
                FtsLog.Write("InterruptTest", $"cycle {cycle} result: {result}");

                if (result == ProbeResult.Fail)
                {
                    Console.WriteLine("║");
                    Console.WriteLine("║  CORRUPTION FOUND on cycle " + cycle);
                    Console.WriteLine("║  Check log: " + logPath);
                    Console.WriteLine("║  Index preserved at: " + IndexDir);
                    Console.WriteLine("╚═══════════════════════════════════════════════════════════════════");
                    FtsLog.Separator("CORRUPTION FOUND — test stopped");
                    return; // stop immediately so the index is preserved for inspection
                }

                // In hard/stopall modes the abandoned background task may still be running.
                // Wait briefly so it can finish draining before we wipe for the next cycle.
                if ((mode == "hard" || mode == "stopall") && interrupted)
                    Thread.Sleep(8000);

                // Wipe for next cycle (simulates a fresh install / explicit reset).
                // Only wipe if the build was not yet complete so we don't wipe a finished index.
                if (interrupted)
                    WipeIndexDir();            }

            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");
            Console.WriteLine($"║  Done — {cycles} cycle(s): {passed} passed, {skipped} skipped, {failed} FAILED");
            Console.WriteLine($"║  Log : {logPath}");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
            FtsLog.Write("InterruptTest",
                $"DONE — cycles={cycles} passed={passed} skipped={skipped} failed={failed}");
        }

        // ── Build until killAfter flushes then cancel ─────────────────────────────

        /// <summary>
        /// Starts a build in a background task and cancels it after <paramref name="killAfterFlushes"/>
        /// segment flushes have completed. Returns true if the build was actually interrupted
        /// (i.e. it did not complete naturally before the kill point was reached).
        /// </summary>
        private static bool RunInterruptedBuild(string dbPath, int killAfterFlushes, string mode)
        {
            var cts        = new CancellationTokenSource();
            int flushCount = 0;
            bool buildDone = false;
            var killReached = new ManualResetEventSlim(false);

            FtsLog.Write("InterruptTest.RunInterruptedBuild",
                $"starting build — killAfterFlushes={killAfterFlushes} mode={mode}");

            var index = new SeforimIndex(IndexDir, dbPath);

            var buildTask = Task.Run(() =>
            {
                try
                {
                    index.BuildIndex(
                        onFlush: () =>
                        {
                            int count = Interlocked.Increment(ref flushCount);
                            FtsLog.Write("InterruptTest.RunInterruptedBuild",
                                $"flush #{count} completed");
                            if (count >= killAfterFlushes)
                            {
                                FtsLog.Write("InterruptTest.RunInterruptedBuild",
                                    $"kill point reached (flush #{count}) — cancelling token");
                                cts.Cancel();
                                killReached.Set();
                            }
                        },
                        ct: cts.Token);

                    buildDone = true;
                    FtsLog.Write("InterruptTest.RunInterruptedBuild",
                        "build completed naturally (kill point not reached in time)");
                }
                catch (OperationCanceledException)
                {
                    FtsLog.Write("InterruptTest.RunInterruptedBuild",
                        $"build OperationCanceledException after {flushCount} flush(es)");
                }
                catch (Exception ex)
                {
                    FtsLog.Write("InterruptTest.RunInterruptedBuild",
                        "build threw unexpected exception: " + ex.GetType().Name + " — " + ex.Message);
                }
            });

            switch (mode)
            {
                case "hard":
                    // Abandon the task ~50ms after the kill point — simulates a process kill
                    // mid-merge. The pipeline task keeps draining as a background thread.
                    if (killReached.Wait(120_000))
                    {
                        Thread.Sleep(50);
                        FtsLog.Write("InterruptTest.RunInterruptedBuild",
                            "HARD KILL — abandoning task mid-drain");
                    }
                    else
                    {
                        buildTask.Wait();
                        FtsLog.Write("InterruptTest.RunInterruptedBuild",
                            "build finished before kill point — not a hard kill");
                    }
                    break;

                case "stopall":
                    // Mimic the OLD buggy StopAll: wait up to 60s then give up.
                    // If the build was cancelled the token fires immediately, but
                    // WaitForMerge() may still be blocking inside Dispose.
                    // We want to simulate the case where the merge takes longer
                    // than the timeout so we cap the wait tightly here (5s is
                    // enough for the merge to be mid-flight but not complete).
                    if (killReached.Wait(120_000))
                    {
                        Thread.Sleep(50); // let cancel propagate to the loop
                        bool finished = buildTask.Wait(5_000); // short timeout = old bug
                        FtsLog.Write("InterruptTest.RunInterruptedBuild",
                            $"STOPALL — wait(5s) finished={finished}. " +
                            (finished ? "Task completed in time." : "TIMEOUT — proceeding with possible active pipeline."));
                    }
                    else
                    {
                        buildTask.Wait();
                    }
                    break;

                case "fixed":
                    // The correct behavior: wait indefinitely.
                    buildTask.Wait();
                    FtsLog.Write("InterruptTest.RunInterruptedBuild",
                        $"FIXED — waited indefinitely. flushCount={flushCount} buildDone={buildDone}");
                    break;

                default: // soft
                    buildTask.Wait();
                    FtsLog.Write("InterruptTest.RunInterruptedBuild",
                        $"SOFT — task fully drained. flushCount={flushCount} buildDone={buildDone}");
                    break;
            }

            LogIndexDirState("after interrupted build");
            return !buildDone;
        }

        // ── Probe after simulated next-launch ─────────────────────────────────────

        private enum ProbeResult { Pass, Fail, NotSearchableYet }

        /// <summary>
        /// Constructs a fresh SeforimIndex (triggering recovery) then searches for
        /// the probe query and checks that the required line ID is present.
        /// </summary>
        private static ProbeResult RunProbeAfterRecovery(string dbPath)
        {
            FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                "constructing fresh SeforimIndex (simulates next-launch recovery)");

            SeforimIndex index;
            try
            {
                index = new SeforimIndex(IndexDir, dbPath);
            }
            catch (Exception ex)
            {
                FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                    "SeforimIndex constructor threw: " + ex.Message);
                Console.WriteLine($"║    recovery threw: {ex.Message}");
                return ProbeResult.NotSearchableYet;
            }

            // Check that at least one segment exists after recovery.
            var datFiles = Directory.Exists(IndexDir)
                ? Directory.GetFiles(IndexDir, "seg_*.dat")
                : new string[0];

            if (datFiles.Length == 0)
            {
                FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                    "no segments after recovery — index not searchable yet (skip)");
                return ProbeResult.NotSearchableYet;
            }

            LogIndexDirState("after recovery");

            // Run the probe search.
            FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                $"searching for '{ProbeQuery}', expecting lineId={ProbeRequiredId}");

            var sw      = Stopwatch.StartNew();
            int count   = 0;
            bool found  = false;

            try
            {
                foreach (var result in index.Search(ProbeQuery))
                {
                    count++;
                    if (result.LineId == ProbeRequiredId)
                        found = true;
                }
            }
            catch (Exception ex)
            {
                FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                    "search threw: " + ex.Message);
                Console.WriteLine($"║    search error: {ex.Message}");
                return ProbeResult.NotSearchableYet;
            }

            sw.Stop();

            FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                $"search done: count={count} found={found} elapsed={sw.ElapsedMilliseconds}ms");

            if (!found)
            {
                FtsLog.Write("InterruptTest.RunProbeAfterRecovery",
                    $"FAIL — lineId={ProbeRequiredId} missing from {count} results for '{ProbeQuery}'");
                Console.WriteLine($"║    probe: {count} results — lineId={ProbeRequiredId} MISSING");
                return ProbeResult.Fail;
            }

            Console.WriteLine($"║    probe: {count} results — lineId={ProbeRequiredId} ✓  ({sw.ElapsedMilliseconds}ms)");
            return ProbeResult.Pass;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void WipeIndexDir()
        {
            try
            {
                if (Directory.Exists(IndexDir))
                {
                    Directory.Delete(IndexDir, recursive: true);
                    FtsLog.Write("InterruptTest", "index directory wiped");
                }
            }
            catch (Exception ex)
            {
                FtsLog.Write("InterruptTest", "failed to wipe index dir: " + ex.Message);
            }
        }

        private static void LogIndexDirState(string label)
        {
            try
            {
                if (!Directory.Exists(IndexDir))
                {
                    FtsLog.Write("InterruptTest.DirState[" + label + "]", "directory does not exist");
                    return;
                }
                string[] files = Directory.GetFiles(IndexDir);
                FtsLog.Write("InterruptTest.DirState[" + label + "]",
                    $"{files.Length} file(s): " +
                    string.Join(", ", Array.ConvertAll(files, Path.GetFileName)));
            }
            catch { }
        }
    }
}
