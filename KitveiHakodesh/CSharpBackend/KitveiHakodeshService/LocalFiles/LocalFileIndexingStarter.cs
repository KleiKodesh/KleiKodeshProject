namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// Builds (or reopens and catches up) the local file index shortly after the service starts,
/// so the first file search of the session finds it ready instead of waiting on the crawl.
/// Without this the index was built only on the first search: every service restart (reboot,
/// owner-watcher stop) meant "waiting for index" on the first query of the day.
/// <para>Delayed half a minute: the FTS and catalog starters, and the app's own first
/// requests, get the disk and CPU first; the file crawl is the least urgent of the three.</para>
/// </summary>
public sealed class LocalFileIndexingStarter(LocalFileIndexService localFiles) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }
        // Fire-and-forget inside the coordinator; a failed build is logged and the next
        // search retries it, exactly as before.
        localFiles.Warmup();
    }
}
