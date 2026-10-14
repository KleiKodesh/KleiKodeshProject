using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using KitveiHakodeshService.SeforimDb;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Brings the service's idle memory footprint down to a few MB.
///
/// A heavy search leaves ~300 MB resident: pooled SQLite connections keep their page
/// caches (up to 64 MB each), and the GC holds the heap pages it grew serializing the
/// result. None of that is needed while idle — the OS file cache still has the hot DB
/// pages, so the next search warms back up quickly.
///
/// After <see cref="IdleSeconds"/> with no RPC activity (and no background work — an FTS,
/// catalog or local-file index build, a search, or a live local-file update), this:
///   1. drops the SQLite connection pools (frees every per-connection page cache),
///   2. asks Core to release every idle-time cache/handle — each reloads lazily and
///      correctly on next use, and each skips itself if that component is busy:
///        - catalog TOC index: Lucene reader + its variant lookup tables,
///        - local-file index: Lucene writer + reader and the live USN watcher threads
///          (reopened on the next search, catching up from the saved USN cursors),
///        - FTS: engine handle + the enrichment's cached book/category catalog,
///        - SeforimDbService's cached book/category catalog,
///        - the static zstd dictionary bytes cache,
///   3. runs an aggressive compacting GC (returns heap to the OS),
///   4. empties the working set (resident pages move to the OS standby list; they page
///      back in on demand, so this is a real handback, not lost work).
///
/// The service only calls these; what each one releases, and how it reloads, is Core's.
/// One trim per idle period: activity re-arms it. Trimming is skipped while busy so it
/// never fights an index build or an in-flight search.
/// </summary>
public sealed class IdleMemoryTrimmer(
    FullTextSearchService fts,
    KitveiHakodeshService.Catalog.CatalogTocSearchService catalogToc,
    KitveiHakodeshService.LocalFiles.LocalFileIndexService localFiles,
    SeforimDbService seforimDb,
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

    // Read by the diagnostics report (GET /khs-diag).
    public static double SecondsSinceActivity =>
        TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastActivityTicks)).TotalSeconds;
    public static int TrimCount { get; private set; }
    public static DateTime? LastTrimUtc { get; private set; }
    public static double LastTrimBeforeMb { get; private set; }
    public static double LastTrimAfterMb { get; private set; }

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
        // A trim can only release what is idle at that moment; a component still finishing
        // (a search session closing, a catalog query) skips itself. So each idle period gets a
        // follow-up trim on the next poll. After that, background work with no RPC (file-change
        // updates, DB watcher) can regrow memory, so a later trim also runs whenever private
        // commit has grown RegrowthBytes past what the last trim left.
        bool followUpPending = false;
        long privateAfterLastTrim = long.MaxValue;
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
            bool alreadyTrimmed = last == lastTrimmedActivity
                && !followUpPending
                && PrivateBytes() < privateAfterLastTrim + RegrowthBytes;

            if (!idleLongEnough || alreadyTrimmed || fts.IsBusy || catalogToc.IsBusy || localFiles.IsBusy) continue;

            try
            {
                Trim();
                followUpPending = last != lastTrimmedActivity; // first trim of this idle period
                lastTrimmedActivity = last;
                privateAfterLastTrim = PrivateBytes();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "idle memory trim failed");
            }
        }
    }

    private const long RegrowthBytes = 8L * 1024 * 1024;

    private static long PrivateBytes()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        return p.PrivateMemorySize64;
    }

    /// <summary>Trim now, regardless of idle state (GET /khs-diag/trim).</summary>
    public void TrimNow() => Trim();

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

        // 1c. The rest of Core's idle-time state. Each call is Core's and is safe against a
        //     request arriving concurrently: a busy component skips itself, and everything
        //     released reloads lazily on its next use.
        ReleaseSafely("local-file index", () => localFiles.ReleaseIdleResources());
        ReleaseSafely("full-text search", () => fts.ReleaseIdleResources());
        ReleaseSafely("seforim catalog cache", seforimDb.ClearCatalogCache);
        ReleaseSafely("zstd dictionary cache",
            global::KitveiHakodesh.Core.SeforimDb.SeforimDbLineContentCodec.ClearDictionaryCache);

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

        TrimCount++;
        LastTrimUtc = DateTime.UtcNow;
        LastTrimBeforeMb = before / 1048576.0;
        LastTrimAfterMb = Environment.WorkingSet / 1048576.0;
        logger.LogInformation("idle memory trim: working set {Before:N1} MB → {After:N1} MB",
            before / 1048576.0, Environment.WorkingSet / 1048576.0);
    }

    /// <summary>One failing release must not stop the others or the GC that follows.</summary>
    private void ReleaseSafely(string what, Action release)
    {
        try { release(); }
        catch (Exception ex) { logger.LogWarning(ex, "idle memory trim: releasing {What} failed", what); }
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
