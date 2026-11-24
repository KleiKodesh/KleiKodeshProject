namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Kicks off background FTS indexing as soon as the service starts, so the index is
/// built (or resumed) while the user works — no search request needed to trigger it.
/// </summary>
public sealed class FtsIndexingStarter(FullTextSearchService fts) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        fts.EnsureIndexing();
        return Task.CompletedTask;
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
