using KitveiHakodeshService.Common;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Tests the settle-and-confirm timing of the DB change watcher
/// (<see cref="DbChangeWatcher.SettleDetector"/>) directly, with short windows:
///   1. It does NOT fire while the file keeps being written (each write re-arms).
///   2. After the file goes quiet for the settle window, it fires exactly once.
///   3. If the file is still changing when the window elapses, it waits again
///      rather than firing on a half-written file.
///   4. A stray poke with no real change still costs only one settle cycle.
/// Uses a plain temp file + a poke on each write (what the FileSystemWatcher does).
/// </summary>
public static class WatcherE2E
{
    public static int Run()
    {
        int failures = 0;
        void Fail(string m) { failures++; Console.Error.WriteLine("  FAIL: " + m); }

        string dir = Path.Combine(Path.GetTempPath(), $"settle-e2e-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "seforim.db");
        var settleWindow = TimeSpan.FromMilliseconds(600);
        var maxDeferral = TimeSpan.FromSeconds(30);

        try
        {
            File.WriteAllText(db, "base");
            int fired = 0;
            var stampsAtFire = new List<string>();
            var settle = new DbChangeWatcher.SettleDetector(
                db,
                onSettled: () => { Interlocked.Increment(ref fired); stampsAtFire.Add(DbChangeStamp.Compute(db)); },
                logger: null,
                settleWindow: settleWindow,
                maxDeferral: maxDeferral);
            using var _ = settle;

            // ── 1 & 3: A burst of writes spaced closer than the settle window must NOT
            //           fire — each write re-arms. Simulate a long, chunked update. ──
            for (int i = 0; i < 6; i++)
            {
                File.AppendAllText(db, $"-chunk{i}");
                settle.Poke();
                Thread.Sleep(300); // < 600ms window → keeps deferring
            }
            if (fired != 0)
                Fail($"settle: fired {fired}× DURING an ongoing write (should defer until quiet)");

            // ── 2: Now go quiet. After one settle window it must fire exactly once. ──
            if (!WaitUntil(() => Volatile.Read(ref fired) >= 1, 4000))
                Fail("settle: did not fire after the file went quiet");
            Thread.Sleep(1000); // give any spurious extra fire a chance
            if (fired != 1)
                Fail($"settle: fired {fired}× for one settled change (expected exactly 1)");

            // The stamp captured at fire time must be the final, fully-written one.
            if (stampsAtFire.Count == 1 && stampsAtFire[0] != DbChangeStamp.Compute(db))
                Fail("settle: fired on a stamp that was not the final file state");

            // ── 4: A single later poke → one more settle → exactly one more fire. ──
            File.AppendAllText(db, "-more");
            settle.Poke();
            if (!WaitUntil(() => Volatile.Read(ref fired) >= 2, 4000))
                Fail("settle: did not fire for a second, later change");
            Thread.Sleep(1000);
            if (fired != 2)
                Fail($"settle: expected 2 total fires, got {fired}");

            Console.WriteLine(failures == 0
                ? "watcher settle E2E: OK (defers through a chunked write, fires once when quiet, once per settle)"
                : "watcher settle E2E: FAILED");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        return failures;
    }

    private static bool WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (cond()) return true;
            Thread.Sleep(50);
        }
        return cond();
    }
}
