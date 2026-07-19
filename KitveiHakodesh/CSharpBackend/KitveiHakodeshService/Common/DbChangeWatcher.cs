namespace KitveiHakodeshService.Common;

using KitveiHakodeshService.SefroimDb;

/// <summary>
/// Watches the seforim DB file for changes WHILE the service runs, and asks each derived
/// index (FTS, catalog TOC) to rebuild if the DB actually changed.
///
/// Startup vs. running:
///   - At startup each index checks its own change-stamp once (EnsureIndex/ing) and
///     builds if stale — that path is unchanged.
///   - While running, a long-lived service would otherwise never notice an in-place DB
///     update (the startup check is latched to run once). This watcher fills that gap.
///
/// It watches the DB's DIRECTORY (filter "&lt;dbname&gt;*"), not the single file, because
/// in SQLite WAL mode the live write lands in "&lt;db&gt;-wal" — a file-scoped watcher
/// would miss it. Idle cost is minimal: FileSystemWatcher is event-driven (kernel
/// ReadDirectoryChangesW), not a poll loop — ~10 KB managed + a small kernel buffer, no
/// CPU when nothing changes.
///
/// The settle-and-confirm timing (never react mid-write; wait for the file to go quiet
/// AND stop changing before rebuilding) lives in <see cref="SettleDetector"/>.
/// </summary>
public sealed class DbChangeWatcher : IHostedService, IDisposable
{
    private readonly ILogger<DbChangeWatcher> _logger;
    private readonly FullTextSearchService _fts;
    private readonly Catalog.CatalogTocSearchService _catalogToc;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string? _dbPath;

    private FileSystemWatcher? _watcher;
    private SettleDetector? _settle;
    private Thread? _registryThread;
    private Microsoft.Win32.RegistryKey? _watchedRegKey;
    private volatile bool _stopping;

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
        // (Otzaria ↔ Zayit) from the hosted app, which writes the registry directly —
        // the service would otherwise keep serving the old DB from its per-process
        // caches. See StartRegistryWatch for the response (clean restart).
        StartRegistryWatch();

        string? folder = Path.GetDirectoryName(_dbPath);
        string name = Path.GetFileName(_dbPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _logger.LogInformation("DB change watcher not started — folder unavailable ({Folder})", folder);
            return Task.CompletedTask;
        }

        _settle = new SettleDetector(_dbPath, OnSettled, _logger);
        try
        {
            _watcher = new FileSystemWatcher(folder, name + "*")
            {
                // Minimum buffer — we watch one file (+ its -wal/-shm/-journal sidecars).
                InternalBufferSize = 4096,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                             | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            _watcher.EnableRaisingEvents = true;
            _logger.LogInformation("DB change watcher started on {Folder} ({Name}*)", folder, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB change watcher could not start — live DB updates will be picked up on restart");
        }
        return Task.CompletedTask;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => _settle?.Poke();

    private void OnSettled()
    {
        try
        {
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

    // ── Database SWITCH detection (registry watch) ─────────────────────────────
    //
    // The DB path is chosen via a registry value that BOTH the service RPC and the
    // hosted app write. A switch made through the service already restarts it
    // (Dispatcher.RestartSoon); a switch made OUTSIDE the service (hosted app settings,
    // wizard, manual registry edit) used to go unnoticed — the running process kept its
    // per-process caches (catalog cache, column probes, index instances, this watcher's
    // folder) pointed at the OLD database.
    //
    // Response: a clean service restart, the same thing the RPC path does. That makes
    // the "DB is static per process" invariant TRUE BY CONSTRUCTION instead of trying
    // to invalidate every cache in place. The fresh process re-resolves the path, and
    // because both index change-stamps INCLUDE the path, both the FTS and catalog
    // indexes detect the mismatch and reindex automatically. (Switching back to a DB
    // whose index stamp still matches costs nothing — the stamp matches, no rebuild.)
    //
    // Detection is event-driven: RegNotifyChangeKeyValue parks one background thread in
    // the kernel until something under the key changes — no polling, no idle CPU. Each
    // wake costs one registry read + string compare.

    private void StartRegistryWatch()
    {
        _registryThread = new Thread(RegistryWatchLoop)
        {
            IsBackground = true,
            Name = "khs-db-registry-watch",
        };
        _registryThread.Start();
    }

    private void RegistryWatchLoop()
    {
        const uint REG_NOTIFY_CHANGE_NAME = 0x1;      // subkey created/deleted
        const uint REG_NOTIFY_CHANGE_LAST_SET = 0x4;  // value written

        while (!_stopping)
        {
            Microsoft.Win32.RegistryKey? key = null;
            try
            {
                key = OpenDeepestExistingAncestor(out bool watchSubtree);
                if (key is null) return; // no HKCU\Software?? nothing to watch
                _watchedRegKey = key;

                // Blocks until a change under the key (or until the key is closed on stop).
                int rc = RegNotifyChangeKeyValue(key.Handle, watchSubtree,
                    REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, IntPtr.Zero, false);
                if (_stopping) return;
                if (rc != 0) { Thread.Sleep(30_000); continue; } // transient — re-arm

                OnPossibleDbSwitch();
                // Loop re-arms: the notification is one-shot, and re-opening the key
                // also picks up a now-deeper existing ancestor (quieter watch).
            }
            catch
            {
                if (!_stopping) Thread.Sleep(30_000);
            }
            finally
            {
                _watchedRegKey = null;
                key?.Dispose();
            }
        }
    }

    /// <summary>Open the deepest existing ancestor of the DB-choice key. Watching the
    /// exact key (no subtree) is quietest; when it doesn't exist yet, watch the nearest
    /// existing parent WITH subtree so we see the key being created.</summary>
    private static Microsoft.Win32.RegistryKey? OpenDeepestExistingAncestor(out bool watchSubtree)
    {
        string path = SeforimDbLocator.RegistryKeyPath;
        watchSubtree = false;
        while (true)
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
            if (key is not null) return key;
            int cut = path.LastIndexOf('\\');
            if (cut < 0) return Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software");
            path = path[..cut];
            watchSubtree = true; // an ancestor watch must see the subtree to catch creation
        }
    }

    private void OnPossibleDbSwitch()
    {
        string current;
        try { current = SeforimDbLocator.Resolve(); }
        catch { return; }
        if (string.Equals(current, _dbPath, StringComparison.OrdinalIgnoreCase)) return;

        _logger.LogInformation(
            "seforim DB switched outside the service ({Old} → {New}) — restarting so the fresh " +
            "process re-resolves everything; both index stamps include the path, so both reindex",
            _dbPath, current);
        _lifetime.StopApplication();
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(
        Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey, bool bWatchSubtree,
        uint dwNotifyFilter, IntPtr hEvent, bool fAsynchronous);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopping = true;
        _watcher?.Dispose();
        _watcher = null;
        _settle?.Dispose();
        _settle = null;
        // Closing the watched key releases the blocked RegNotifyChangeKeyValue wait;
        // the thread is background anyway, so it can never hold up process exit.
        try { _watchedRegKey?.Dispose(); } catch { }
        _watchedRegKey = null;
    }

    /// <summary>
    /// Debounce-to-quiescence for a file that is being written. A DB update (especially
    /// a full re-download/replace) is a long operation with many writes and pauses;
    /// rebuilding mid-write would be wasted work or would index a half-written file. So
    /// this NEVER fires immediately: after activity (<see cref="Poke"/>) it waits a
    /// generous quiet window, then CONFIRMS the file has actually stopped changing (its
    /// change-stamp is unchanged over the window). If it moved during the wait, it waits
    /// another full window — repeatedly — until the file settles, bounded by a hard cap
    /// so a continuously (slowly) written file still eventually fires. When it finally
    /// settles it calls <c>onSettled</c> exactly once for that cycle.
    ///
    /// Timings are overridable via KHS_DB_WATCH_SETTLE_MS / KHS_DB_WATCH_MAX_MS (tests
    /// shrink them; production defaults are 2 min / 15 min).
    /// </summary>
    public sealed class SettleDetector : IDisposable
    {
        private readonly string _dbPath;
        private readonly Action _onSettled;
        private readonly ILogger? _logger;
        private readonly TimeSpan _settleWindow;
        private readonly TimeSpan _maxDeferral;

        private readonly object _lock = new();
        private Timer? _timer;
        private string? _lastSeenStamp;
        private DateTime _deferStartUtc;

        public SettleDetector(string dbPath, Action onSettled, ILogger? logger = null,
            TimeSpan? settleWindow = null, TimeSpan? maxDeferral = null)
        {
            _dbPath = dbPath;
            _onSettled = onSettled;
            _logger = logger;
            _settleWindow = settleWindow ?? TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_SETTLE_MS", 120_000));
            _maxDeferral = maxDeferral ?? TimeSpan.FromMilliseconds(EnvMs("KHS_DB_WATCH_MAX_MS", 900_000));
        }

        /// <summary>Signal activity — (re)arms the settle window without firing.</summary>
        public void Poke()
        {
            lock (_lock)
            {
                if (_timer is null)
                {
                    _deferStartUtc = DateTime.UtcNow;
                    _lastSeenStamp = SafeStamp();
                    _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
                }
                _timer.Change(_settleWindow, Timeout.InfiniteTimeSpan);
            }
        }

        private void Tick()
        {
            string current = SafeStamp();
            lock (_lock)
            {
                bool stable = current.Length > 0 && current == _lastSeenStamp;
                bool capReached = DateTime.UtcNow - _deferStartUtc >= _maxDeferral;

                if (!stable && !capReached)
                {
                    // Still moving (a slow writer paused > the window, then wrote again)
                    // or momentarily unreadable — reset the baseline and wait again.
                    _lastSeenStamp = current;
                    _timer?.Change(_settleWindow, Timeout.InfiniteTimeSpan);
                    return;
                }

                _timer?.Dispose();
                _timer = null;
                _lastSeenStamp = null;
                if (capReached && !stable)
                    _logger?.LogInformation(
                        "DB change watcher: file still changing after {Cap} — rebuilding anyway", _maxDeferral);
            }

            _onSettled();
        }

        private string SafeStamp()
        {
            try { return DbChangeStamp.Compute(_dbPath); }
            catch { return ""; }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private static double EnvMs(string name, double fallback)
        {
            string? v = Environment.GetEnvironmentVariable(name);
            return double.TryParse(v, out double ms) && ms > 0 ? ms : fallback;
        }
    }
}
