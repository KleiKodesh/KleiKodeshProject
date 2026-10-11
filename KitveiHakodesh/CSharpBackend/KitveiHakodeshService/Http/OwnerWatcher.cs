using System.Collections.Concurrent;
using System.Diagnostics;
using KitveiHakodeshService.Hosting;

namespace KitveiHakodeshService.Http;

/// <summary>
/// The processes a console instance of this service is running for. A console instance (dev,
/// or a portable copy / dev checkout where the Windows service is not installed) has no SCM to
/// own its lifetime, so it lives exactly as long as someone uses it:
///   • the spawner passes its PID in <c>KHS_OWNER_PID</c> (the dev server, or the first host);
///   • every host that uses this instance - including one that found it already running -
///     registers its PID (<c>registerOwner</c>) and releases it when its last window closes
///     (<c>releaseOwner</c>);
///   • <see cref="OwnerWatcher"/> checks every few seconds which owners are still alive (a host
///     that crashed never releases) and stops the service when none is left.
/// The installed Windows service ignores all of this: its lifetime is the SCM's.
/// </summary>
public sealed class OwnerRegistry(IHostApplicationLifetime lifetime, ILogger<OwnerRegistry> logger)
{
    /// <summary>pid -> process start time, so a recycled pid is not mistaken for the owner.</summary>
    private readonly ConcurrentDictionary<int, DateTime> _owners = new();
    private int _everHadOwner;

    /// <summary>The installed service never stops for lack of owners.</summary>
    public static bool IsActive => !WindowsServiceMode.IsWindowsService;

    public void Register(int pid)
    {
        if (!IsActive || pid <= 0) return;
        // Counted even when the process is already gone: a spawner that died before we got
        // here still means "stop when nobody is left", which the next poll then does.
        Interlocked.Exchange(ref _everHadOwner, 1);
        DateTime? started = StartTimeOf(pid);
        if (started is null) return;
        _owners[pid] = started.Value;
        logger.LogInformation("Owner {Pid} registered ({Count} owner(s))", pid, _owners.Count);
    }

    /// <summary>The owner is going away; stops the service at once when it was the last one.</summary>
    public void Release(int pid)
    {
        if (!IsActive) return;
        _owners.TryRemove(pid, out _);
        logger.LogInformation("Owner {Pid} released ({Count} owner(s) left)", pid, _owners.Count);
        if (_owners.IsEmpty) StopSoon("the last owner released it");
    }

    /// <summary>Drops owners that have exited; stops the service when none is left.</summary>
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
        if (_owners.IsEmpty && Volatile.Read(ref _everHadOwner) == 1)
            StopSoon("no owner process is running any more");
    }

    private void StopSoon(string why) =>
        _ = Task.Run(async () =>
        {
            // A short grace: the reply to releaseOwner goes out first, and a host that is just
            // starting (another window opening) may register in the meantime.
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!_owners.IsEmpty) return;
            logger.LogInformation("Stopping: {Why}", why);
            lifetime.StopApplication();
        });

    private static DateTime? StartTimeOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : process.StartTime;
        }
        catch { return null; } // gone, or not ours to inspect
    }
}

/// <summary>
/// Polls the <see cref="OwnerRegistry"/> (see there). The spawner's <c>KHS_OWNER_PID</c> is the
/// first owner; with no owner at all (someone ran the exe by hand) nothing is watched.
/// </summary>
public sealed class OwnerWatcher(OwnerRegistry owners, ILogger<OwnerWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OwnerRegistry.IsActive) return;

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
