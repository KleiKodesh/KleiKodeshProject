using System.Collections.Concurrent;
using System.Diagnostics;
using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Hosting;
using KitveiHakodeshService.Ipc;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SeforimDb;

namespace KitveiHakodeshService.Http;

/// <summary>
/// The processes this service is running for. It lives exactly as long as someone uses it -
/// the console instance (dev, portable copy) and the installed Windows service alike. An idle
/// service with no program around is pure overhead, and every host restarts it on demand
/// (KitveiHakodeshServiceLauncher: users hold SERVICE_START), so it simply goes away:
///   • the spawner passes its PID in <c>KHS_OWNER_PID</c> (the dev server, or the first host);
///   • every host that uses this instance - including one that found it already running -
///     registers its PID (<c>registerOwner</c>) and releases it when its last window closes
///     (<c>releaseOwner</c>);
///   • <see cref="OwnerWatcher"/> checks every few seconds which owners are still alive (a host
///     that crashed never releases) and stops the service when none is left.
/// Never in the middle of long-running work - an index build (FTS, catalog, file index), an
/// update download, a HebrewBooks catalog walk: each would only restart from scratch on the
/// next start, so the stop waits until they are done. A service that never had an owner (started by the installer to build the
/// indexes) stops once it has been idle for <see cref="OwnerlessIdleLimit"/>. In service mode
/// stopping the data host stops the service (Hosting/UserSessionBinding); the SCM shows it
/// stopped and the next host to need it starts it again.
/// </summary>
public sealed class OwnerRegistry(
    IHostApplicationLifetime lifetime,
    FullTextSearchService fts,
    CatalogTocSearchService catalogToc,
    LocalFileIndexService localFiles,
    KitveiHakodeshService.KitveiHakodesh.Updater updater,
    KitveiHakodeshService.HebrewBooks.HebrewBooksCatalogUpdateService hebrewBooksCatalog,
    KitveiHakodeshService.SeforimDb.SeforimLibraryDownloadService libraryDownload,
    ILogger<OwnerRegistry> logger)
{
    private static readonly TimeSpan OwnerlessIdleLimit = TimeSpan.FromMinutes(5);

    /// <summary>A short grace before stopping: the reply to releaseOwner goes out first, and a
    /// host that is just starting (a pane reopened, another window) may register meanwhile.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    /// <summary>pid -> process start time, so a recycled pid is not mistaken for the owner.</summary>
    private readonly ConcurrentDictionary<int, DateTime> _owners = new();
    private int _everHadOwner;

    public void Register(int pid)
    {
        if (pid <= 0) return;
        // Counted even when the process is already gone: a spawner that died before we got
        // here still means "stop when nobody is left", which the next poll then does.
        Interlocked.Exchange(ref _everHadOwner, 1);
        DateTime? started = StartTimeOf(pid);
        if (started is null) return;
        _owners[pid] = started.Value;
        Volatile.Write(ref _lastDeferral, null); // a later ownerless stretch reports its deferral afresh
        logger.LogInformation("Owner {Pid} registered ({Count} owner(s))", pid, _owners.Count);
        updater.RequestCheck();
    }

    /// <summary>The owner is going away; stops the service at once when it was the last one.</summary>
    public void Release(int pid)
    {
        _owners.TryRemove(pid, out _);
        logger.LogInformation("Owner {Pid} released ({Count} owner(s) left)", pid, _owners.Count);
        if (_owners.IsEmpty) StopSoon("the last owner released it");
    }

    /// <summary>Drops owners that have exited; stops the service when none is left (or, never
    /// having had one, once it has been idle long enough).</summary>
    public void PruneExited()
    {
        foreach (var (pid, started) in _owners)
        {
            DateTime? now = StartTimeOf(pid);
            if (now is null || now.Value != started)
            {
                _owners.TryRemove(pid, out _);
                logger.LogInformation("Owner {Pid} has exited", pid);
            }
        }
        if (!_owners.IsEmpty) return;
        if (Volatile.Read(ref _everHadOwner) == 1)
            StopSoon("no owner process is running any more");
        else if (WindowsServiceMode.IsWindowsService
                 && IdleMemoryTrimmer.SecondsSinceActivity >= OwnerlessIdleLimit.TotalSeconds)
            StopSoon($"no program has used it for {OwnerlessIdleLimit.TotalMinutes:N0} minutes");
    }

    /// <summary>The first background job a stop would cut short, or null. While one runs the
    /// stop is deferred; the watcher's next poll tries again.</summary>
    private string? BusyWith() =>
        fts.IsBusy ? "full-text index"
        : catalogToc.IsBusy ? "catalog index"
        : localFiles.IsBusy ? "local-file index"
        : updater.IsBusy ? "update check"
        : hebrewBooksCatalog.IsBusy ? "HebrewBooks catalog walk"
        : libraryDownload.IsBusy ? "library download"
        : null;

    /// <summary>The last deferral logged, so a long job is reported once, not every poll.</summary>
    private string? _lastDeferral;

    private void StopSoon(string why) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(StopGrace);
            if (!_owners.IsEmpty) return;
            string? busy = BusyWith();
            if (busy is not null)
            {
                // Long-running work in progress; PruneExited retries on the next poll.
                if (Interlocked.Exchange(ref _lastDeferral, busy) != busy)
                    logger.LogInformation("Stop deferred: {Busy} is running", busy);
                return;
            }
            logger.LogInformation("Stopping: {Why}", why);
            lifetime.StopApplication();
        });

    /// <summary>The process's start time (tells a recycled pid apart), null when it is gone.
    /// A live process whose start time cannot be read (access refused) gets DateTime.MinValue:
    /// still alive, compared by pid alone - dropping it would stop the service under a host
    /// that is still running.</summary>
    private static DateTime? StartTimeOf(int pid)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch { return null; } // not running
        using (process)
        {
            try { return process.HasExited ? null : process.StartTime; }
            catch { return DateTime.MinValue; }
        }
    }
}

/// <summary>
/// Polls the <see cref="OwnerRegistry"/> (see there). The spawner's <c>KHS_OWNER_PID</c> is the
/// first owner (console only); a console instance with no owner at all (someone ran the exe by
/// hand) is never stopped.
/// </summary>
public sealed class OwnerWatcher(OwnerRegistry owners, ILogger<OwnerWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? env = Environment.GetEnvironmentVariable("KHS_OWNER_PID");
        if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out int spawner) && spawner > 0)
        {
            owners.Register(spawner);
            logger.LogInformation("Spawned by {Pid}; stopping when no owner is left", spawner);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
            owners.PruneExited();
        }
    }
}
