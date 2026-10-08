namespace KitveiHakodeshService.Common;

using KitveiHakodeshService.SeforimDb;
using CoreCommon = global::KitveiHakodesh.Core.Common;

/// <summary>
/// Hosts Core's file and registry watchers for the lifetime of the service, and supplies the
/// POLICY they call back into: which indexes to rebuild when the database settles, and what to
/// do when the user switches databases from outside this process.
///
/// Startup vs. running:
///   - At startup each index checks its own change-stamp once (EnsureIndex/ing) and builds if
///     stale — that path is unchanged.
///   - While running, a long-lived service would otherwise never notice an in-place DB update
///     (the startup check is latched to run once). This fills that gap.
///
/// The MECHANISM is Core's: watching the database's directory rather than the one file (in WAL
/// mode the live write lands in the -wal sidecar, which a file-scoped watch would miss),
/// deciding when writing has stopped, and parking a thread on RegNotifyChangeKeyValue. None of
/// that is specific to this host, and it used to exist here in a second copy.
///
/// What stays here is what only a service can answer: it owns an ILogger, it can stop the
/// application, it knows FullTextSearchService and CatalogTocSearchService, and it reads the
/// KHS_DB_WATCH_* overrides that shrink the settle windows for tests.
/// </summary>
public sealed class DbChangeWatcher : IHostedService, IDisposable
{
    private readonly ILogger<DbChangeWatcher> _logger;
    private readonly FullTextSearchService _fts;
    private readonly Catalog.CatalogTocSearchService _catalogToc;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string? _dbPath;

    private CoreCommon.DbChangeWatcher? _fileWatcher;
    private CoreCommon.RegistryValueWatcher? _registryWatcher;

    public DbChangeWatcher(
        ILogger<DbChangeWatcher> logger,
        FullTextSearchService fts,
        Catalog.CatalogTocSearchService catalogToc,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _fts = fts;
        _catalogToc = catalogToc;
        _lifetime = lifetime;
        _dbPath = SeforimDbLocator.Resolve();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_dbPath))
            return Task.CompletedTask;

        // Watch the DB-choice registry value too: the user can SWITCH databases
        // (Otzaria <-> Zayit) from the hosted app, which writes the registry directly —
        // the service would otherwise keep serving the old DB from its per-process
        // caches. See OnPossibleDbSwitch for the response (clean restart).
        _registryWatcher = new CoreCommon.RegistryValueWatcher(
            SeforimDbLocator.RegistryKeyPath, OnPossibleDbSwitch, "khs-db-registry-watch");
        _registryWatcher.Start();

        _fileWatcher = new CoreCommon.DbChangeWatcher(
            _dbPath, OnSettled,
            settleWindow: TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_SETTLE_MS", 120_000)),
            maxDeferral: TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_MAX_MS", 900_000)));

        if (_fileWatcher.TryStart(out Exception? startError))
            _logger.LogInformation("DB change watcher started on {Path}", _dbPath);
        else if (startError is not null)
            // The OS refused a watch it should have accepted. A warning, not an aside: live DB
            // updates are now silently not happening until the next restart.
            _logger.LogWarning(startError,
                "DB change watcher could not start ({Path}) — live DB updates will be picked up on restart",
                _dbPath);
        else
            // No folder to watch — a removable drive, an unconfigured path. Ordinary.
            _logger.LogInformation(
                "DB change watcher not started ({Path}) — folder unavailable", _dbPath);

        return Task.CompletedTask;
    }

    private void OnSettled(bool forced)
    {
        try
        {
            // The cap ran out instead of the file going quiet, so this rebuild reads a file
            // that is STILL being written. It is the deliberate escape hatch against a file
            // that trickles forever, but it is also the one case where a rebuild can pick up
            // a half-written database — so say it, rather than letting it look routine.
            if (forced)
                _logger.LogWarning(
                    "seforim DB still changing after the maximum deferral — rebuilding anyway");

            // Each index recomputes its own stamp and rebuilds ONLY if it truly changed
            // (its stored ver stamp no longer matches the current file). A settle cycle
            // ending on a file whose content matches the built index costs one cheap
            // stamp read and does nothing.
            bool ftsChanged = _fts.RebuildIfDbChanged();
            bool catalogChanged = _catalogToc.RebuildIfDbChanged();
            if (ftsChanged || catalogChanged)
                _logger.LogInformation(
                    "seforim DB settled after changing — rebuilding indexes (fts={Fts}, catalog={Cat})",
                    ftsChanged, catalogChanged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DB change watcher: rebuild check failed");
        }
    }

    // ── Database SWITCH detection ──────────────────────────────────────────────
    //
    // The DB path is chosen via a registry value that BOTH the service RPC and the
    // hosted app write. A switch made through the service already restarts it
    // (Dispatcher.RestartSoon); a switch made OUTSIDE the service (hosted app settings,
    // wizard, manual registry edit) used to go unnoticed — the running process kept its
    // per-process caches (catalog cache, column probes, index instances, the watcher's
    // folder) pointed at the OLD database.
    //
    // Response: a clean service restart, the same thing the RPC path does. That makes
    // the "DB is static per process" invariant TRUE BY CONSTRUCTION instead of trying
    // to invalidate every cache in place. The fresh process re-resolves the path, and
    // because both index change-stamps INCLUDE the path, both the FTS and catalog
    // indexes detect the mismatch and reindex automatically. (Switching back to a DB
    // whose index stamp still matches costs nothing — the stamp matches, no rebuild.)
    private void OnPossibleDbSwitch()
    {
        string? current;
        try { current = SeforimDbLocator.Resolve(); }
        catch { return; }

        // Nothing resolvable now (the user cleared the setting, or the file went away). That is
        // not a SWITCH to act on: restarting would land in the same state, so wait for the next
        // registry change instead.
        if (current is null) return;
        if (string.Equals(current, _dbPath, StringComparison.OrdinalIgnoreCase)) return;

        _logger.LogInformation(
            "seforim DB switched outside the service ({Old} → {New}) — restarting so the fresh " +
            "process re-resolves everything; both index stamps include the path, so both reindex",
            _dbPath, current);
        _lifetime.StopApplication();
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

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        // Disposing the registry watcher closes the watched key, which releases the thread
        // blocked in RegNotifyChangeKeyValue; it is a background thread either way, so it can
        // never hold up process exit.
        _registryWatcher?.Dispose();
        _registryWatcher = null;
    }
}
