namespace KitveiHakodeshService.Catalog;

using KitveiHakodeshService.SefroimDb;

/// <summary>
/// Service wrapper around <see cref="CatalogTocIndex"/> — Lucene full-text search over
/// full TOC paths, replacing the catalog page's manual per-query TOC heuristics.
///
/// Index location: "CatalogTocIndex" next to the service binary (AppContext.BaseDirectory),
/// the same convention as the FTS index and the document-locator index — deleting the
/// service folder deletes the index with it.
///
/// Rebuild trigger: catalogtoc.ver records the HASH of the seforim DB (path+size+mtime,
/// see CatalogTocIndex.ComputeDbHash) the index was built from. Any change to the DB
/// file — updated content, replaced file, switched database — changes the hash, and the
/// next EnsureIndex() wipes and rebuilds in the background (mirrors how the FTS index
/// rebuilds on a source-DB change). The ver file is written only after a completed
/// build, so an interrupted build also rebuilds from scratch.
/// </summary>
public sealed class CatalogTocSearchService(ILogger<CatalogTocSearchService> logger)
{
    private readonly string? _dbPath = SeforimDbLocator.Resolve();
    private readonly string _indexPath = ResolveIndexPath();

    private readonly object _lock = new();
    private CatalogTocIndex? _index;
    private Task? _buildTask;
    private CancellationTokenSource? _buildCts;

    private volatile bool _isReady;
    private volatile bool _isIndexing;
    private volatile bool _buildStarted;   // latch: build at most once per process
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

    private string VerFile => Path.Combine(_indexPath, "catalogtoc.ver");

    private CatalogTocIndex GetIndex()
    {
        lock (_lock) { return _index ??= new CatalogTocIndex(_indexPath, _dbPath!); }
    }

    /// <summary>
    /// Idempotent. Ready immediately when the stored DB hash matches; otherwise wipes
    /// the stale index and kicks off a background rebuild. Called at startup by
    /// <see cref="CatalogTocIndexingStarter"/> and lazily from search/status.
    /// </summary>
    public void EnsureIndex()
    {
        if (!HasDb || _isReady) return;
        lock (_lock)
        {
            if (_isReady || _buildStarted) return;

            string currentHash;
            try { currentHash = CatalogTocIndex.ComputeDbHash(_dbPath!); }
            catch (Exception ex)
            {
                logger.LogError(ex, "catalog TOC index: could not hash seforim DB");
                return;
            }

            // Completed build against the SAME DB (hash match) → ready, no rebuild.
            string builtFrom = "";
            if (File.Exists(VerFile))
            {
                try { builtFrom = File.ReadAllText(VerFile).Trim(); } catch { }
                if (string.Equals(builtFrom, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _isReady = true;
                    _buildStarted = true;
                    return;
                }
                logger.LogInformation(
                    "catalog TOC index was built from a different seforim DB (hash {Old} → {New}) — rebuilding",
                    Shorten(builtFrom), Shorten(currentHash));
            }

            // Stale or missing — wipe whatever is there and rebuild from scratch.
            try { if (Directory.Exists(_indexPath)) Directory.Delete(_indexPath, recursive: true); }
            catch (Exception ex) { logger.LogError(ex, "catalog TOC index: stale-index wipe failed"); }

            _buildStarted = true;
            _isIndexing = true;
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;
            _buildTask = Task.Run(() => RunBuild(currentHash, token), CancellationToken.None);
        }
    }

    private static string Shorten(string hash) => hash.Length > 12 ? hash[..12] : hash;

    private void RunBuild(string dbHash, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_indexPath);
            var index = GetIndex();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            logger.LogInformation("catalog TOC index build starting — {Index}", _indexPath);

            int docs = index.Build(
                onProgress: (done, total) => { _builtBooks = done; _totalBooks = total; },
                ct: ct);

            // Record the source-DB hash only after a COMPLETED build.
            File.WriteAllText(VerFile, dbHash);
            _isReady = true;
            logger.LogInformation(
                "catalog TOC index build complete — {Docs} docs in {Elapsed:F1}s", docs, sw.Elapsed.TotalSeconds);
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

    /// <summary>Graceful shutdown: cancel the in-flight build and wait for it to unwind so
    /// the Lucene writer is disposed cleanly (never hard-killed mid-commit).</summary>
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
        return new CatalogTocStatus
        {
            Ready = _isReady,
            Indexing = _isIndexing,
            BuiltBooks = _builtBooks,
            TotalBooks = _totalBooks,
            DbMissing = !HasDb,
        };
    }

    // A new search SUPERSEDES the previous in-flight one (latest-wins, like the FTS
    // service) so an abandoned heavy query stops burning cores as soon as the user
    // keeps typing.
    private CancellationTokenSource? _searchCts;

    /// <summary>Run a catalog TOC-path search. Results are NEVER capped.</summary>
    public CatalogTocSearchResult Search(string query, bool dedupAncestors)
    {
        EnsureIndex();
        var result = new CatalogTocSearchResult();
        if (!HasDb || !_isReady)
        {
            result.Ready = false;   // still building (or no DB)
            return result;
        }
        result.Ready = true;
        if (string.IsNullOrWhiteSpace(query)) return result;

        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _searchCts, cts);
        prev?.Cancel();
        try
        {
            result.Results = GetIndex().Search(query, dedupAncestors, cts.Token);
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

/// <summary>Kicks off the catalog TOC index check/build at service start (hash compare is
/// cheap; a rebuild runs in the background), and unwinds a running build on shutdown.</summary>
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
    /// <summary>Suppress hits whose matched TOC ancestor also matched (manual Pass 3).
    /// Default true — pass false to see the raw match set.</summary>
    public bool DedupAncestors { get; set; } = true;
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocSearchResult
{
    /// <summary>False while the index is still building (or no seforim DB) — retry later.</summary>
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
