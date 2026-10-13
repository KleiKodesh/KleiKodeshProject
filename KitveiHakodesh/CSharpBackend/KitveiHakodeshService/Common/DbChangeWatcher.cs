namespace KitveiHakodeshService.Common;

using KitveiHakodeshService.SeforimDb;
using FtsIndexLogLevel = global::KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsIndexLogLevel;
using IndexRefreshWatcher = global::KitveiHakodesh.Core.SeforimDb.SeforimDbIndexRefreshWatcher;

/// <summary>
/// Hosts Core's <see cref="IndexRefreshWatcher"/> for the lifetime of the service. The policy
/// (which indexes rebuild when the database settles, retry while another app is mid-update,
/// what counts as a database switch) is Core's. What stays here: the IHostedService lifetime,
/// the log hookup, the KHS_DB_WATCH_* test overrides, and the service's answer to a switch —
/// a clean restart (StopApplication), so the fresh process re-resolves everything.
/// </summary>
public sealed class DbChangeWatcher : IHostedService, IDisposable
{
    private readonly IndexRefreshWatcher _watcher;

    public DbChangeWatcher(
        ILogger<DbChangeWatcher> logger,
        FullTextSearchService fts,
        Catalog.CatalogTocSearchService catalogToc,
        IHostApplicationLifetime lifetime)
    {
        _watcher = new IndexRefreshWatcher(
            SeforimDbLocator.Resolve(),
            fts.Coordinator,
            catalogToc.Coordinator,
            onDatabaseSwitched: lifetime.StopApplication,
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
            settleWindow: TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_SETTLE_MS", 120_000)),
            maxDeferral: TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_MAX_MS", 900_000)));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher.Start();
        return Task.CompletedTask;
    }

    /// <summary>A positive millisecond override from the environment, else the default. Tests
    /// shrink the settle windows this way; production uses 2 min / 15 min.</summary>
    private static double EnvMs(string name, double fallback)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        return double.TryParse(v, out double ms) && ms > 0 ? ms : fallback;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _watcher.Dispose();
}
