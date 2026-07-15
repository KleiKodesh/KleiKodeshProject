namespace KitveiHakodeshService.SefroimDb;

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
}
