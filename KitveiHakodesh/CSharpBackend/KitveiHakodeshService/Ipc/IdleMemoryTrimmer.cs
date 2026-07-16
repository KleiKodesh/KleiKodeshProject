using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using KitveiHakodeshService.SefroimDb;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Brings the service's idle memory footprint down to a few MB.
///
/// A heavy search leaves ~300 MB resident: pooled SQLite connections keep their page
/// caches (up to 64 MB each), and the GC holds the heap pages it grew serializing the
/// result. None of that is needed while idle — the OS file cache still has the hot DB
/// pages, so the next search warms back up quickly.
///
/// After <see cref="IdleSeconds"/> with no RPC activity (and no background work — index
/// build or live search sessions), this:
///   1. drops the SQLite connection pools (frees every per-connection page cache),
///   2. runs an aggressive compacting GC (returns heap to the OS),
///   3. empties the working set (resident pages move to the OS standby list; they page
///      back in on demand, so this is a real handback, not lost work).
///
/// One trim per idle period: activity re-arms it. Trimming is skipped while busy so it
/// never fights an index build or an in-flight search.
/// </summary>
public sealed class IdleMemoryTrimmer(FullTextSearchService fts, ILogger<IdleMemoryTrimmer> logger)
    : BackgroundService
{
    /// <summary>Idle time before trimming. Override with KHS_IDLE_TRIM_SECONDS (0 disables).</summary>
    private static readonly int IdleSeconds = ResolveIdleSeconds();

    private const int DefaultIdleSeconds = 120;
    private const int PollSeconds = 15;

    // Last RPC activity, in UTC ticks. Touched by the dispatcher on every request.
    private static long _lastActivityTicks = DateTime.UtcNow.Ticks;

    public static void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    private static int ResolveIdleSeconds()
    {
        string? env = Environment.GetEnvironmentVariable("KHS_IDLE_TRIM_SECONDS");
        return int.TryParse(env, out int s) ? s : DefaultIdleSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (IdleSeconds <= 0) return; // disabled

        long lastTrimmedActivity = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(PollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }

            long last = Interlocked.Read(ref _lastActivityTicks);
            bool idleLongEnough = (DateTime.UtcNow.Ticks - last) >= TimeSpan.FromSeconds(IdleSeconds).Ticks;
            bool alreadyTrimmed = last == lastTrimmedActivity;

            if (!idleLongEnough || alreadyTrimmed || fts.IsBusy) continue;

            try
            {
                Trim();
                lastTrimmedActivity = last;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "idle memory trim failed");
            }
        }
    }

    private void Trim()
    {
        long before = Environment.WorkingSet;

        // 1. Release pooled SQLite connections → frees their page caches. Pools refill
        //    transparently on the next query.
        SqliteConnection.ClearAllPools();

        // 2. Full compacting GC, aggressive mode: also compacts the LOH and hands freed
        //    heap segments back to the OS instead of retaining them for reuse.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // 3. Move remaining resident pages to the OS standby list. They fault back in on
        //    demand (standby pages are cheap to reclaim), so idle cost drops to a few MB.
        NativeMemory_.EmptyWorkingSet(NativeMemory_.GetCurrentProcess());

        logger.LogInformation("idle memory trim: working set {Before:N1} MB → {After:N1} MB",
            before / 1048576.0, Environment.WorkingSet / 1048576.0);
    }
}

internal static partial class NativeMemory_
{
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyWorkingSet(IntPtr hProcess);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetCurrentProcess();
}
