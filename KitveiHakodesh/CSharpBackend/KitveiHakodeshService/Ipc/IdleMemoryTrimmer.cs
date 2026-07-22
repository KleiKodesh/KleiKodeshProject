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
public sealed class IdleMemoryTrimmer(
    FullTextSearchService fts,
    KitveiHakodeshService.Catalog.CatalogTocSearchService catalogToc,
    ILogger<IdleMemoryTrimmer> logger)
    : BackgroundService
{
    /// <summary>Idle time before trimming. Override with KHS_IDLE_TRIM_SECONDS (0 disables).</summary>
    private static readonly int IdleSeconds = ResolveIdleSeconds();

    private const int DefaultIdleSeconds = 120;
    private const int PollSeconds = 15;

    // Adaptive fast path: a heavy search leaves ~300 MB of reclaimable garbage (snippet
    // strings, result batches, SQLite page caches) sitting in Task Manager for the full
    // 120s idle window — long enough to read as a leak. When the working set is large,
    // trim after a much shorter idle instead; light usage (small working set) keeps the
    // gentle 120s cadence so pools aren't churned between ordinary page navigations.
    private const int HeavyIdleSeconds = 20;
    private const long HeavyWorkingSetBytes = 150L * 1024 * 1024;

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
            // Large working set → use the short threshold (never longer than the configured
            // one, so a small KHS_IDLE_TRIM_SECONDS override still wins).
            int idleThreshold = Environment.WorkingSet >= HeavyWorkingSetBytes
                ? Math.Min(HeavyIdleSeconds, IdleSeconds)
                : IdleSeconds;
            bool idleLongEnough = (DateTime.UtcNow.Ticks - last) >= TimeSpan.FromSeconds(idleThreshold).Ticks;
            bool alreadyTrimmed = last == lastTrimmedActivity;

            if (!idleLongEnough || alreadyTrimmed || fts.IsBusy || catalogToc.IsBusy) continue;

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

        // 1b. Drop the catalog TOC index's long-lived Lucene reader/searcher (segment
        //     term indexes, doc-values, stored-field buffers). Unlike the FTS reader
        //     (opened per search session, closed with it), this one stays open across
        //     idle periods by design, so it must be released explicitly. Reopens lazily
        //     on the next catalog search. No-op while a build holds the reader.
        catalogToc.ReleaseIdleResources();

        // 2. Compact and DECOMMIT the managed heap back to the OS. Two aggressive GCs on
        //    purpose: the decommit of freed segments (especially the LOH) is deferred to
        //    the SECOND aggressive collection (dotnet/runtime#78679), so a single Collect
        //    compacts but leaves the pages committed. Run the finalizer queue between them
        //    so anything a finalizer frees is decommitted by the second pass. With
        //    System.GC.RetainVM=false (see the csproj) the freed segments are handed back
        //    to the OS instead of parked on a standby list — this is what actually drops
        //    committed/idle RAM rather than merely paging it out.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // 3. Empty the working set: whatever pages remain resident (runtime, mapped index
        //    files touched by the last search) move to the OS standby list and fault back
        //    in on demand. Done AFTER the decommit so it only pushes out what truly stays.
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
