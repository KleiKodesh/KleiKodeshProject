using KitveiHakodesh.Core.SeforimDbFullTextSearch;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Transport wrapper over <see cref="SeforimDbFtsIndexCoordinator"/> — the full-text index
/// lifecycle, search, related-form expansion and the result/stream shapes are all Core's.
/// What is left here is DI lifetime, the log hookup and the FTS_INDEX_PATH override.
///
/// Index location: FTS_INDEX_PATH if set (used as-is, never built into or wiped), else
/// "FtsIndex" next to the service binary (AppContext.BaseDirectory) — so deleting the
/// service folder deletes the index too.
/// </summary>
public sealed class FullTextSearchService
{
    private readonly SeforimDbFtsIndexCoordinator _coordinator;

    public FullTextSearchService(ILogger<FullTextSearchService> logger, SearchExpansionService expansion)
    {
        string? envIndexPath = Environment.GetEnvironmentVariable("FTS_INDEX_PATH");
        bool external = !string.IsNullOrWhiteSpace(envIndexPath);
        _coordinator = new SeforimDbFtsIndexCoordinator(
            databasePath: SeforimDbLocator.Resolve(),
            indexPath: external ? envIndexPath! : Path.Combine(AppContext.BaseDirectory, "FtsIndex"),
            indexIsExternal: external,
            log: (level, message, ex) =>
            {
                switch (level)
                {
                    case FtsIndexLogLevel.Error: logger.LogError(ex, "{Message}", message); break;
                    case FtsIndexLogLevel.Warning: logger.LogWarning(ex, "{Message}", message); break;
                    case FtsIndexLogLevel.Debug: logger.LogDebug(ex, "{Message}", message); break;
                    default: logger.LogInformation(ex, "{Message}", message); break;
                }
            },
            relatedFormExpander: expansion.Expander);
    }

    /// <summary>The Core coordinator, for the DB change watcher's rebuild policy.</summary>
    public SeforimDbFtsIndexCoordinator Coordinator => _coordinator;

    /// <summary>True while background work is running (index build or live search
    /// sessions) — the idle memory trimmer must not run then.</summary>
    public bool IsBusy => _coordinator.IsBusy;

    public string DescribeResidentState() => _coordinator.DescribeResidentState();

    /// <summary>Idle memory trim: drop the engine handle and cached catalog (see Core).
    /// Returns false (and releases nothing) while a build or search is running.</summary>
    public bool ReleaseIdleResources() => _coordinator.ReleaseIdleResources();

    public void EnsureIndexing() => _coordinator.EnsureIndexing();

    public bool RebuildIfDbChanged() => _coordinator.RebuildIfDbChanged();

    public void ResetIndex() => _coordinator.ResetIndex();

    public void Shutdown() => _coordinator.Shutdown();

    public FtsIndexStatus Status() => _coordinator.Status();

    public FtsSearchResult Search(
        string query, int cap, int maxWordDistance, bool requireOrdered, int contextWords) =>
        _coordinator.Search(query, cap, maxWordDistance, requireOrdered, contextWords);

    public FtsSnippetsResult Snippets(string query, IReadOnlyList<int> lineIds, bool requireOrdered, int contextWords) =>
        _coordinator.Snippets(query, lineIds, requireOrdered, contextWords);

    public FtsLinesSearchResult LinesSearch(string query, IReadOnlyList<FtsLineRange> ranges) =>
        _coordinator.LinesSearch(query, ranges);

    public Task StreamSearch(
        string query, int maxWordDistance, bool requireOrdered, int contextWords,
        Func<FtsStreamChunk, Task> emit, CancellationToken clientCt) =>
        _coordinator.StreamSearch(query, maxWordDistance, requireOrdered, contextWords, emit, clientCt);

    public Task StreamIndexingProgress(Func<FtsIndexStatus, Task> emit, CancellationToken ct) =>
        _coordinator.StreamIndexingProgress(emit, ct);
}
