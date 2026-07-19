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
    private readonly string? _dbPath;

    private FileSystemWatcher? _watcher;
    private SettleDetector? _settle;

    public DbChangeWatcher(
        ILogger<DbChangeWatcher> logger,
        FullTextSearchService fts,
        Catalog.CatalogTocSearchService catalogToc)
    {
        _logger = logger;
        _fts = fts;
        _catalogToc = catalogToc;
        _dbPath = SeforimDbLocator.Resolve();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_dbPath))
            return Task.CompletedTask;

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _settle?.Dispose();
        _settle = null;
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
