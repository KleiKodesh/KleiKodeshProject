using CoreCommon = KitveiHakodesh.Core.Common;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Tests the settle-and-confirm timing of Core's DB change watcher with short windows:
///   1. It does NOT fire while the file keeps being written (each write re-arms).
///   2. After the file goes quiet for the settle window, it fires exactly once.
///   3. If the file is still changing when the window elapses, it waits again
///      rather than firing on a half-written file.
///   4. A write to the -wal SIDECAR is seen too — the reason the watch is directory-scoped.
///   5. A change late in the settle window never fires on an intermediate file state —
///      the confirm step, not the debounce, is what holds there.
///
/// Drives the REAL watcher through actual file writes, rather than poking its settle timer:
/// the timer is private to Core (it is how the watcher makes that call, not a separate job),
/// and going through FileSystemWatcher is what the service does in production anyway. That
/// means this test also covers the watch filter — a write to the -wal sidecar has to register,
/// which a file-scoped watch would miss.
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
        var settleWindow = TimeSpan.FromMilliseconds(1000);
        var maxDeferral = TimeSpan.FromSeconds(30);

        try
        {
            File.WriteAllText(db, "base");
            int fired = 0;
            var fingerprintsAtFire = new List<string>();

            using var watcher = new CoreCommon.DbChangeWatcher(
                db,
                onSettled: _ =>
                {
                    Interlocked.Increment(ref fired);
                    lock (fingerprintsAtFire)
                        fingerprintsAtFire.Add(CoreCommon.DbFileFingerprint.Compute(db));
                },
                settleWindow: settleWindow,
                maxDeferral: maxDeferral);

            if (!watcher.TryStart())
            {
                Fail("watcher would not start on a temp directory");
                return failures;
            }

            // ── 1 & 3: A burst of writes spaced closer than the settle window must NOT
            //           fire — each write re-arms. Simulate a long, chunked update. ──
            for (int i = 0; i < 6; i++)
            {
                File.AppendAllText(db, $"-chunk{i}");
                Thread.Sleep(450); // < 1000ms window → keeps deferring
            }
            if (Volatile.Read(ref fired) != 0)
                Fail($"settle: fired {fired}x DURING an ongoing write (should defer until quiet)");

            // ── 2: Now go quiet. After one settle window it must fire exactly once. ──
            if (!WaitUntil(() => Volatile.Read(ref fired) >= 1, 6000))
                Fail("settle: did not fire after the file went quiet");
            Thread.Sleep(1200); // give any spurious extra fire a chance
            if (Volatile.Read(ref fired) != 1)
                Fail($"settle: fired {fired}x for one settled change (expected exactly 1)");

            // The fingerprint captured at fire time must be the final, fully-written one.
            lock (fingerprintsAtFire)
            {
                if (fingerprintsAtFire.Count == 1
                    && fingerprintsAtFire[0] != CoreCommon.DbFileFingerprint.Compute(db))
                    Fail("settle: fired on a fingerprint that was not the final file state");
            }

            // ── 4: A later change to the -wal SIDECAR → one more settle → one more fire.
            //
            // Writing the sidecar rather than the database itself is the case the directory
            // watch exists for: in WAL mode the live write lands in "<db>-wal", so a watch
            // scoped to the one file would see nothing and never rebuild.
            File.AppendAllText(db + "-wal", "wal-write");
            if (!WaitUntil(() => Volatile.Read(ref fired) >= 2, 6000))
                Fail("settle: did not fire for a write to the -wal sidecar (watch filter too narrow?)");
            Thread.Sleep(1200);
            if (Volatile.Read(ref fired) != 2)
                Fail($"settle: expected 2 total fires, got {fired}");

            // ── 5: THE CONFIRM STEP — the guarantee the debounce alone does NOT give.
            //
            // In case 1 the writes land closer together than the window, so each one re-arms
            // the timer and the settle check never even runs: the debounce does all the work.
            // The confirm step covers a different hazard — the timer ELAPSING on a file that
            // has changed since the baseline was taken. Then firing would rebuild from a
            // half-written file, and the only thing preventing it is comparing the current
            // fingerprint with the one captured a window earlier.
            //
            // Going through real writes CANNOT reach it: the write raises an event that
            // re-arms the timer before the window expires, so the debounce always gets there
            // first and the branch never runs. (Verified — breaking the confirm check leaves a
            // write-driven test fully green.) So drive the window directly with Poke, which is
            // exactly what the filesystem event does, and change the file DURING the window
            // without announcing it.
            int before = Volatile.Read(ref fired);

            watcher.Poke();          // arm the window; baseline fingerprint captured now
            Thread.Sleep(300);       // still inside the 1000ms window
            File.AppendAllText(db, "-mid-window");  // the file moves, no re-arm from us

            // The timer now elapses on a file whose fingerprint no longer matches the baseline.
            // The confirm step must DEFER rather than fire.
            Thread.Sleep(1100);      // past the original window, well short of the next one
            if (Volatile.Read(ref fired) != before)
                Fail("settle: fired on a file that changed during the window (confirm step not holding)");

            // Left alone, the next window finds it unchanged and fires exactly once.
            if (!WaitUntil(() => Volatile.Read(ref fired) == before + 1, 6000))
                Fail("settle: did not fire once the file finally stopped changing");
            Thread.Sleep(1200);
            if (Volatile.Read(ref fired) != before + 1)
                Fail($"settle: expected exactly 1 fire after the deferral, got {Volatile.Read(ref fired) - before}");

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

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }
}
