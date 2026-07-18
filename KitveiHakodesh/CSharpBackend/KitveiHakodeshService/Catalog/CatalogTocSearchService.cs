namespace KitveiHakodeshService.Catalog;

using KitveiHakodeshService.SefroimDb;

/// <summary>
/// Service wrapper around <see cref="CatalogTocIndex"/> — the disk-based Lucene index
/// over full TOC paths behind the catalog search.
///
/// Index location: "CatalogTocIndex" next to the service binary (AppContext.BaseDirectory),
/// the same convention as the FTS index and the document-locator index.
///
/// Rebuilds: the ver file records the seforim-DB hash the committed index was built
/// from. When the hash differs (DB updated/replaced/switched, or index format bumped),
/// a rebuild starts on a background task and builds IN PLACE. Searches stay available
/// throughout: a near-real-time reader serves partial results as documents are indexed,
/// so results appear during the very first build too (see CatalogTocIndex.BuildAndSwitch).
/// </summary>
public sealed class CatalogTocSearchService(ILogger<CatalogTocSearchService> logger)
{
    private readonly string? _dbPath = SeforimDbLocator.Resolve();
    private readonly string _indexPath = ResolveIndexPath();

    private readonly object _lock = new();
    private CatalogTocIndex? _index;
    private Task? _buildTask;
    private CancellationTokenSource? _buildCts;

    private volatile bool _isReady;      // an index (fresh or stale) is open and serving
    private volatile bool _isIndexing;
    private volatile bool _buildStarted; // latch: build at most once per process
    private volatile int _builtBooks;
    private volatile int _totalBooks;

    private static string ResolveIndexPath()
    {
        string? env = Environment.GetEnvironmentVariable("CATALOG_TOC_INDEX_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(AppContext.BaseDirectory, "CatalogTocIndex");
    }

    private bool HasDb => !string.IsNullOrWhiteSpace(_dbPath) && File.Exists(_dbPath);

    /// <summary>True while the background build runs — the idle memory trimmer must not
    /// fight it.</summary>
    public bool IsBusy => _isIndexing;

    private CatalogTocIndex GetIndex()
    {
        lock (_lock) { return _index ??= new CatalogTocIndex(_indexPath, _dbPath!); }
    }

    /// <summary>
    /// Idempotent. Opens the existing index (even a stale one — no downtime) and, when
    /// the stored DB hash differs from the current one, kicks off a background rebuild
    /// that builds IN PLACE and stays searchable via a near-real-time reader. Called at
    /// startup by <see cref="CatalogTocIndexingStarter"/> and lazily from search/status.
    /// </summary>
    public void EnsureIndex()
    {
        if (!HasDb || _buildStarted && _isReady) return;
        lock (_lock)
        {
            if (_buildStarted) return;

            string currentHash;
            try { currentHash = CatalogTocIndex.ComputeDbHash(_dbPath!); }
            catch (Exception ex)
            {
                logger.LogError(ex, "catalog TOC index: could not hash seforim DB");
                return;
            }

            var index = GetIndex();
            bool opened = index.TryOpenActive();
            if (opened) _isReady = true; // stale or fresh — either way, keep serving

            if (opened && string.Equals(index.ActiveHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                _buildStarted = true; // up to date — nothing to build
                return;
            }

            logger.LogInformation(
                opened
                    ? "catalog TOC index is stale (seforim DB changed) — rebuilding in the background, serving the old index meanwhile"
                    : "catalog TOC index missing — building");

            _buildStarted = true;
            _isIndexing = true;
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;
            _buildTask = Task.Run(() => RunBuild(currentHash, token), CancellationToken.None);
        }
    }

    private void RunBuild(string dbHash, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_indexPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int docs = GetIndex().BuildAndSwitch(
                dbHash,
                onProgress: (done, total) => { _builtBooks = done; _totalBooks = total; },
                ct: ct);
            _isReady = true;
            logger.LogInformation(
                "catalog TOC index build complete — {Docs} docs in {Elapsed:F1}s (switched atomically)",
                docs, sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("catalog TOC index build cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "catalog TOC index build failed");
        }
        finally
        {
            _isIndexing = false;
        }
    }

    /// <summary>Wipe and rebuild from scratch (dev reset). Returns immediately.</summary>
    public void ResetIndex()
    {
        _ = Task.Run(() =>
        {
            Task? build;
            lock (_lock)
            {
                _buildCts?.Cancel();
                build = _buildTask;
            }
            try { build?.Wait(TimeSpan.FromSeconds(30)); } catch { /* cancelled */ }

            lock (_lock)
            {
                _index?.Dispose();
                _index = null;
                try { if (Directory.Exists(_indexPath)) Directory.Delete(_indexPath, recursive: true); }
                catch (Exception ex) { logger.LogError(ex, "catalog TOC index reset: delete failed"); }
                _isReady = false;
                _isIndexing = false;
                _buildStarted = false;
                _buildTask = null;
                _buildCts = null;
            }
            logger.LogInformation("catalog TOC index reset — rebuilding");
            EnsureIndex();
        });
    }

    /// <summary>Graceful shutdown: cancel the in-flight build and wait for it to unwind
    /// so the Lucene writer is disposed cleanly. The ver file is written only on full
    /// completion, so an interrupted build is treated as stale and simply rebuilt next
    /// run.</summary>
    public void Shutdown()
    {
        Task? build;
        lock (_lock)
        {
            _buildCts?.Cancel();
            build = _buildTask;
        }
        try { build?.Wait(TimeSpan.FromSeconds(25)); } catch { /* cancelled */ }
    }

    public CatalogTocStatus Status()
    {
        EnsureIndex();
        // A near-real-time reader during a build counts as ready (partial results).
        bool ready = _isReady || (HasDb && GetIndex().TryOpenActive());
        if (ready) _isReady = true;
        return new CatalogTocStatus
        {
            Ready = ready,
            Indexing = _isIndexing,
            BuiltBooks = _builtBooks,
            TotalBooks = _totalBooks,
            DbMissing = !HasDb,
        };
    }

    // A new search SUPERSEDES the previous in-flight one (latest-wins) so an abandoned
    // heavy query stops burning cores as soon as the user keeps typing.
    private CancellationTokenSource? _searchCts;

    /// <summary>Run a catalog TOC-path search. Results are NEVER capped; ordering is
    /// (Level, TreeOrder) only.</summary>
    public CatalogTocSearchResult Search(string query)
    {
        EnsureIndex();
        var result = new CatalogTocSearchResult();

        // Ready as soon as a reader exists — during a build the near-real-time reader
        // serves partial results, so we don't gate on the build finishing. Only report
        // not-ready when there is genuinely no reader yet (DB missing, or the very first
        // build hasn't opened its reader in this split second).
        bool ready = _isReady || (HasDb && GetIndex().TryOpenActive());
        if (!ready)
        {
            result.Ready = false;
            return result;
        }
        _isReady = true;
        result.Ready = true;
        if (string.IsNullOrWhiteSpace(query)) return result;

        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _searchCts, cts);
        prev?.Cancel();
        try
        {
            result.Results = GetIndex().Search(query, cts.Token);
        }
        catch (OperationCanceledException)
        {
            result.Superseded = true;   // a newer search took over — caller discards this
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "catalog TOC search failed");
            result.Error = ex.Message;
        }
        finally
        {
            Interlocked.CompareExchange(ref _searchCts, null, cts);
        }
        return result;
    }
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

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocSearchArgs
{
    public string? Query { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocSearchResult
{
    /// <summary>False while no index is available yet (first build) — retry later.</summary>
    public bool Ready { get; set; }
    public List<CatalogTocHit> Results { get; set; } = new();
    /// <summary>True when a newer search cancelled this one — discard this response.</summary>
    public bool Superseded { get; set; }
    public string? Error { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocStatus
{
    public bool Ready { get; set; }
    public bool Indexing { get; set; }
    public int BuiltBooks { get; set; }
    public int TotalBooks { get; set; }
    public bool DbMissing { get; set; }
}
