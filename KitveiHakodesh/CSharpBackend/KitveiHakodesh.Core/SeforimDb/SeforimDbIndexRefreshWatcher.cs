using System;
using System.Threading;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;

namespace KitveiHakodesh.Core.SeforimDb;

/// <summary>
/// Watches the seforim database while a host runs and keeps its indexes current:
///   - when the file SETTLES after a change, and is ready to index, asks the FTS and catalog
///     indexes to rebuild if (and only if) their change-stamps no longer match;
///   - when the file settled but is not ready (another app is mid-update), re-checks in a minute;
///   - when the DB-choice registry value now resolves to a DIFFERENT database, reports a switch.
///
/// The MECHANISM (directory watch, settle detection, RegNotifyChangeKeyValue) is
/// <see cref="DbChangeWatcher"/> and <see cref="RegistryValueWatcher"/>. This class is the
/// POLICY on top. The host supplies the log sink and what to do on a switch (the service
/// restarts itself so every per-process cache re-resolves; both index stamps include the path,
/// so both indexes reindex automatically).
/// </summary>
public sealed class SeforimDbIndexRefreshWatcher : IDisposable
{
    /// <summary>Default settle window: how long the file must be quiet.</summary>
    public static readonly TimeSpan DefaultSettleWindow = TimeSpan.FromMilliseconds(120_000);
    /// <summary>Default cap on how long a trickling write may defer the rebuild.</summary>
    public static readonly TimeSpan DefaultMaxDeferral = TimeSpan.FromMilliseconds(900_000);

    private static readonly TimeSpan NotReadyRetryDelay = TimeSpan.FromMinutes(1);

    private readonly string? _dbPath;
    private readonly SeforimDbFtsIndexCoordinator _fts;
    private readonly SeforimDbCatalogIndexCoordinator _catalog;
    private readonly Action _onDatabaseSwitched;
    private readonly Action<FtsIndexLogLevel, string, Exception?> _log;
    private readonly TimeSpan _settleWindow;
    private readonly TimeSpan _maxDeferral;

    private DbChangeWatcher? _fileWatcher;
    private RegistryValueWatcher? _registryWatcher;
    /// <summary>Re-check after a settle that found the file still being updated by another app:
    /// the watcher only fires on CHANGES, and the update may finish without one more.</summary>
    private Timer? _notReadyRetry;
    /// <summary>Guards _notReadyRetry and _disposed: OnSettled runs on the settle timer AND the
    /// retry timer, and a retry armed after Dispose would outlive the host.</summary>
    private readonly object _retryLock = new();
    private bool _disposed;

    /// <param name="databasePath">The database the host resolved at startup (static per process).</param>
    /// <param name="onDatabaseSwitched">Called when the DB-choice setting now resolves to another file.</param>
    public SeforimDbIndexRefreshWatcher(
        string? databasePath,
        SeforimDbFtsIndexCoordinator fts,
        SeforimDbCatalogIndexCoordinator catalog,
        Action onDatabaseSwitched,
        Action<FtsIndexLogLevel, string, Exception?> log,
        TimeSpan? settleWindow = null,
        TimeSpan? maxDeferral = null)
    {
        _dbPath = databasePath;
        _fts = fts;
        _catalog = catalog;
        _onDatabaseSwitched = onDatabaseSwitched;
        _log = log;
        _settleWindow = settleWindow ?? DefaultSettleWindow;
        _maxDeferral = maxDeferral ?? DefaultMaxDeferral;
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_dbPath)) return;

        // Watch the DB-choice registry value too: the user can SWITCH databases
        // (Otzaria <-> Zayit) from another app, which writes the registry directly.
        _registryWatcher = new RegistryValueWatcher(
            SeforimDbPathResolver.RegistryKeyPath, OnPossibleDbSwitch, "khs-db-registry-watch");
        _registryWatcher.Start();

        _fileWatcher = new DbChangeWatcher(_dbPath!, OnSettled, _settleWindow, _maxDeferral);

        if (_fileWatcher.TryStart(out Exception? startError))
            _log(FtsIndexLogLevel.Information, $"DB change watcher started on {_dbPath}", null);
        else if (startError is not null)
            // The OS refused a watch it should have accepted. A warning, not an aside: live DB
            // updates are now silently not happening until the next restart.
            _log(FtsIndexLogLevel.Warning,
                $"DB change watcher could not start ({_dbPath}) — live DB updates will be picked up on restart",
                startError);
        else
            // No folder to watch — a removable drive, an unconfigured path. Ordinary.
            _log(FtsIndexLogLevel.Information,
                $"DB change watcher not started ({_dbPath}) — folder unavailable", null);
    }

    private void OnSettled(bool forced)
    {
        try
        {
            // The cap ran out instead of the file going quiet, so this rebuild reads a file
            // that is STILL being written — say it, rather than letting it look routine.
            if (forced)
                _log(FtsIndexLogLevel.Warning,
                    "seforim DB still changing after the maximum deferral — rebuilding anyway", null);

            // Quiet is not the same as finished: Otzaria parks its download beside the file
            // and Zayit extracts in place. Indexing now would read half a library.
            // A FORCED settle has given up waiting for quiet, so only the completeness checks apply.
            if (_dbPath is not null &&
                !SeforimDbReadiness.IsReadyToIndex(_dbPath, out string notReady, ignoreQuietPeriod: forced))
            {
                _log(FtsIndexLogLevel.Information,
                    $"seforim DB settled but not ready to index ({notReady}) — checking again in a minute", null);
                lock (_retryLock)
                {
                    if (_disposed) return;
                    _notReadyRetry?.Dispose();
                    _notReadyRetry = new Timer(_ => OnSettled(forced), null, NotReadyRetryDelay, Timeout.InfiniteTimeSpan);
                }
                return;
            }

            // Each index recomputes its own stamp and rebuilds ONLY if it truly changed.
            bool ftsChanged = _fts.RebuildIfDbChanged();
            bool catalogChanged = _catalog.RebuildIfDbChanged();
            if (ftsChanged || catalogChanged)
                _log(FtsIndexLogLevel.Information,
                    $"seforim DB settled after changing — rebuilding indexes (fts={ftsChanged}, catalog={catalogChanged})",
                    null);
        }
        catch (Exception ex)
        {
            _log(FtsIndexLogLevel.Error, "DB change watcher: rebuild check failed", ex);
        }
    }

    private void OnPossibleDbSwitch()
    {
        string? current;
        try { current = SeforimDbPathResolver.Resolve().Path; }
        catch { return; }

        // Nothing resolvable now (the user cleared the setting, or the file went away). That is
        // not a SWITCH to act on: wait for the next registry change instead.
        if (current is null) return;
        if (string.Equals(current, _dbPath, StringComparison.OrdinalIgnoreCase)) return;

        _log(FtsIndexLogLevel.Information,
            $"seforim DB switched outside the service ({_dbPath} → {current}) — restarting so the fresh " +
            "process re-resolves everything; both index stamps include the path, so both reindex", null);
        _onDatabaseSwitched();
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        lock (_retryLock)
        {
            _disposed = true;
            _notReadyRetry?.Dispose();
            _notReadyRetry = null;
        }
        // Closes the watched key, which releases the thread blocked in RegNotifyChangeKeyValue.
        _registryWatcher?.Dispose();
        _registryWatcher = null;
    }
}
