using System.Diagnostics;

namespace KitveiHakodeshService.Http;

/// <summary>
/// Ties this service's lifetime to the process that spawned it. The spawner passes its PID via
/// <c>KHS_OWNER_PID</c>; when that process exits — for ANY reason, including a hard kill that
/// skips the graceful "shutdown" op — this service stops itself, which tears down the loopback
/// host (<see cref="HttpHostServer.StopAsync"/>) and releases its port. That guarantees a spawned
/// host never leaks as an orphan holding its port.
///
/// No-op when <c>KHS_OWNER_PID</c> is unset (installed-service / standalone use, where lifetime
/// is owned by the SCM or the user, not a parent process).
/// </summary>
public sealed class OwnerWatcher(IHostApplicationLifetime lifetime, ILogger<OwnerWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? env = Environment.GetEnvironmentVariable("KHS_OWNER_PID");
        if (string.IsNullOrWhiteSpace(env) || !int.TryParse(env, out int pid) || pid <= 0)
            return; // not spawned with an owner — nothing to watch

        Process? owner = null;
        try { owner = Process.GetProcessById(pid); }
        catch { /* already gone */ }

        if (owner is null)
        {
            logger.LogInformation("Owner process {Pid} is not running at startup — stopping.", pid);
            lifetime.StopApplication();
            return;
        }

        logger.LogInformation("Watching owner process {Pid}; will stop when it exits.", pid);
        try
        {
            await owner.WaitForExitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // we're shutting down anyway
        }
        catch
        {
            // WaitForExitAsync can fail for a process we don't fully own — fall back to polling.
            while (!stoppingToken.IsCancellationRequested)
            {
                try { if (Process.GetProcessById(pid).HasExited) break; }
                catch { break; } // GetProcessById throws when the PID is gone → it exited
                try { await Task.Delay(1500, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Owner process {Pid} exited — stopping service.", pid);
            lifetime.StopApplication();
        }
    }
}
