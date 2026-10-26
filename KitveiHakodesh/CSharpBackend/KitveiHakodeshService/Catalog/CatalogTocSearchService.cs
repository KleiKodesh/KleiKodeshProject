namespace KitveiHakodeshService.Catalog;

using KitveiHakodeshService.SeforimDb;

// global:: because this project has its own KitveiHakodeshService.KitveiHakodesh namespace,
// which would otherwise shadow the root one.
using CatalogIndexCoordinator = global::KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndexCoordinator;
using CatalogIndexLogLevel = global::KitveiHakodesh.Core.SeforimDbCatalog.CatalogIndexLogLevel;

/// <summary>
/// Transport wrapper over <see cref="CatalogIndexCoordinator"/> — the catalog TOC-path index
/// (Lucene). Core owns the index, its lifecycle, the search with latest-wins supersession and
/// the status/result shapes. What stays here: DI lifetime, the log hookup, resolving the
/// database once per process, and the CATALOG_TOC_INDEX_PATH override.
///
/// Index location: CATALOG_TOC_INDEX_PATH if set, else "CatalogTocIndex" next to the service
/// binary (AppContext.BaseDirectory).
/// </summary>
public sealed class CatalogTocSearchService
{
    private readonly ILogger<CatalogTocSearchService> _logger;
    private readonly CatalogIndexCoordinator _coordinator;

    public CatalogTocSearchService(ILogger<CatalogTocSearchService> logger)
    {
        _logger = logger;

        // Resolved ONCE, deliberately. The service treats the database as static per process —
        // a switch made elsewhere restarts it (see DbChangeWatcher), so the fresh process
        // re-resolves everything.
        string? databasePath = SeforimDbLocator.Resolve();

        _coordinator = new CatalogIndexCoordinator(
            resolveDatabasePath: () => databasePath,
            indexPath: ResolveIndexPath(),
            log: (level, message, ex) =>
            {
                switch (level)
                {
                    case CatalogIndexLogLevel.Error: logger.LogError(ex, "{Message}", message); break;
                    case CatalogIndexLogLevel.Warning: logger.LogWarning(ex, "{Message}", message); break;
                    default: logger.LogInformation("{Message}", message); break;
                }
            });
    }

    private static string ResolveIndexPath()
    {
        string? env = Environment.GetEnvironmentVariable("CATALOG_TOC_INDEX_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(AppContext.BaseDirectory, "CatalogTocIndex");
    }

    /// <summary>The Core coordinator, for the DB change watcher's rebuild policy.</summary>
    public CatalogIndexCoordinator Coordinator => _coordinator;

    /// <summary>True while the background build runs — the idle memory trimmer must not
    /// fight it.</summary>
    public bool IsBusy => _coordinator.IsIndexing;

    public string DescribeResidentState() => _coordinator.DescribeResidentState();

    /// <summary>Release the idle Lucene reader during an idle memory trim (see Core).</summary>
    public void ReleaseIdleResources()
    {
        try
        {
            if (_coordinator.ReleaseIdleReader())
                _logger.LogDebug("catalog TOC index: released idle reader");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "catalog TOC index: idle reader release failed");
        }
    }

    public void EnsureIndex() => _coordinator.EnsureIndex();

    public bool RebuildIfDbChanged() => _coordinator.RebuildIfDbChanged();

    public void ResetIndex() => _coordinator.ResetIndex();

    public void Shutdown() => _coordinator.Shutdown();

    public CatalogTocStatus Status() => _coordinator.Status();

    public CatalogTocSearchResult Search(string query) => _coordinator.Search(query);

    public CatalogTitleSearchResult SearchTitles(string query) => _coordinator.SearchTitles(query);
}

/// <summary>Kicks off the catalog TOC index check/build at service start (hash compare
/// is cheap; a rebuild runs in the background and stays searchable via a near-real-time
/// reader), and unwinds a running build on shutdown.</summary>
public sealed class CatalogTocIndexingStarter(CatalogTocSearchService catalogToc) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        catalogToc.EnsureIndex();
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.Run(catalogToc.Shutdown, CancellationToken.None);
        await base.StopAsync(cancellationToken);
    }
}

// ── RPC DTOs ─────────────────────────────────────────────────────────────────────
// CatalogTocSearchResult, CatalogTitleSearchResult and CatalogTocStatus are Core's (SeforimDbCatalogModels).

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocSearchArgs
{
    public string? Query { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTitleSearchArgs
{
    public string? Query { get; set; }
}
