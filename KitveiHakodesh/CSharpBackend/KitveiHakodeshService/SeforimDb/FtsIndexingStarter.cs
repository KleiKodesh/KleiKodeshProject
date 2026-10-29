namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Kicks off background FTS indexing as soon as the service starts, so the index is
/// built (or resumed) while the user works — no search request needed to trigger it.
/// </summary>
public sealed class FtsIndexingStarter(FullTextSearchService fts) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the host's start: validating a large additional library reads it end to end, and
        // while StartAsync waits the pipe holds every request - the data host would look hung.
        return Task.Run(fts.EnsureIndexing, CancellationToken.None);
    }

    /// <summary>On host shutdown, cancel the background build cleanly (aborting any
    /// in-flight merge and releasing the index write lock) so the index is never left
    /// mid-merge — a dev restart/stop must not corrupt the index by hard-killing a build.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.Run(fts.Shutdown, CancellationToken.None);
        await base.StopAsync(cancellationToken);
    }
}
