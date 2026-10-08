namespace KitveiHakodeshService.Catalog;

using KitveiHakodeshService.SeforimDb;

// Both the index and its lifecycle are Core's. The names are kept local because what is left
// on this side is the wire shape — status, results, and the superseded-search guard.
// global:: because this project has its own KitveiHakodeshService.KitveiHakodesh namespace,
// which would otherwise shadow the root one.
using CatalogTocIndex = global::KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndex;
using CatalogTocHit = global::KitveiHakodesh.Core.SeforimDbCatalog.CatalogTocHit;
using CatalogIndexCoordinator = global::KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndexCoordinator;
using CatalogIndexLogLevel = global::KitveiHakodesh.Core.SeforimDbCatalog.CatalogIndexLogLevel;

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
public sealed class CatalogTocSearchService
{
    private readonly ILogger<CatalogTocSearchService> _logger;

    // The LIFECYCLE is Core's: when to open, when to build, the bounded retry on failure, and
    // the claim-before-cancel reset ordering. All of that used to live here AND in the hosted
    // app as the same two hundred lines. What is left on this side is what only a service can
    // answer — where the database is, how to log, and the superseded-search guard.
    private readonly CatalogIndexCoordinator _coordinator;

    // A real constructor rather than a primary one: the coordinator closes over _searchCts,
    // and a field initializer may not reference an instance field.
    public CatalogTocSearchService(ILogger<CatalogTocSearchService> logger)
    {
        _logger = logger;

        // Resolved ONCE, deliberately. The service treats the database as static per process —
        // a switch made elsewhere restarts it (see DbChangeWatcher), so the fresh process
        // re-resolves everything. Re-reading per call would both cost a registry hit on every
        // keystroke and quietly let this one component follow a switch the rest of the service
        // has not followed. The hosted app passes a live lambda instead, because it genuinely
        // does support switching in place.
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
            },
            // See _searchCts: the reset ORDERING is Core's, the search source stays ours.
            takeActiveSearchCts: () => Interlocked.Exchange(ref _searchCts, null));
    }

    private static string ResolveIndexPath()
    {
        string? env = Environment.GetEnvironmentVariable("CATALOG_TOC_INDEX_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(AppContext.BaseDirectory, "CatalogTocIndex");
    }

    private bool HasDb => _coordinator.DatabasePath is not null;

    /// <summary>True while the background build runs — the idle memory trimmer must not
    /// fight it.</summary>
    public bool IsBusy => _coordinator.IsIndexing;

    /// <summary>
    /// Release the open Lucene reader/searcher so its retained memory is freed while the
    /// service is idle — the catalog counterpart to dropping the SQLite pools. Called by
    /// <see cref="Ipc.IdleMemoryTrimmer"/> during a trim. No-op (and safe) if no index is
    /// open or a build is in flight; the next search reopens the reader lazily. Kept
    /// cheap: it does not touch the index directory handle or trigger any rebuild.
    /// </summary>
    public void ReleaseIdleResources()
    {
        if (_coordinator.IsIndexing) return; // build owns the reader; leave it
        try
        {
            if (_coordinator.CurrentIndex is { } index && index.ReleaseIdleReader())
                _logger.LogDebug("catalog TOC index: released idle reader");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "catalog TOC index: idle reader release failed");
        }
    }

    /// <summary>
    /// Idempotent. Opens the existing index (even a stale one — no downtime) and, when
    /// the stored DB hash differs from the current one, kicks off a background rebuild
    /// that builds IN PLACE and stays searchable via a near-real-time reader. Called at
    /// startup by <see cref="CatalogTocIndexingStarter"/> and lazily from search/status.
    /// </summary>
    public void EnsureIndex() => _coordinator.EnsureIndex();

    /// <summary>
    /// Called by the DB change watcher while the service runs: if the seforim DB's
    /// current change-stamp no longer matches the built index's stamp, kick off a
    /// background rebuild (the old index keeps serving via the NRT reader meanwhile).
    /// Returns true if a rebuild was started.
    /// </summary>
    public bool RebuildIfDbChanged() => _coordinator.RebuildIfDbChanged();

    /// <summary>Wipe and rebuild from scratch (dev reset). Returns immediately.</summary>
    public void ResetIndex() => _coordinator.ResetIndex();

    /// <summary>Graceful shutdown: cancel the in-flight build and wait for it to unwind
    /// so the Lucene writer is disposed cleanly. The ver file is written only on full
    /// completion, so an interrupted build is treated as stale and simply rebuilt next
    /// run.</summary>
    public void Shutdown() => _coordinator.Shutdown();

    public CatalogTocStatus Status()
    {
        EnsureIndex();
        // A near-real-time reader during a build counts as ready (partial results).
        return new CatalogTocStatus
        {
            Ready = _coordinator.IsReadyToServe(),
            Indexing = _coordinator.IsIndexing,
            BuiltBooks = _coordinator.BuiltBooks,
            TotalBooks = _coordinator.TotalBooks,
            DbMissing = !HasDb,
        };
    }

    // A new search SUPERSEDES the previous in-flight one (latest-wins) so an abandoned
    // heavy query stops burning cores as soon as the user keeps typing.
    //
    // The coordinator is told how to reach it: a reset has to cancel the running search
    // BEFORE disposing the index that search is reading, or the query faults instead of
    // coming back superseded. The source lives here because search concurrency is this
    // host's business; only the reset ORDERING is shared.
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
        if (!_coordinator.TryGetSearchableIndex(out var index))
        {
            result.Ready = false;
            return result;
        }
        result.Ready = true;
        if (string.IsNullOrWhiteSpace(query)) return result;

        using var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _searchCts, cts);
        // Cancel the superseded search: without the `using` above, one CTS per keystroke-search
        // accumulates for the service's lifetime — each is finalizable and holds an internal
        // registration list. Cancel() first — disposing a CTS does not cancel it.
        //
        // We deliberately do NOT dispose `prev` here. Its owning Search call is still on
        // another thread reading that token, and disposing underneath it would throw
        // ObjectDisposedException into the middle of that search. Each Search disposes its
        // own CTS via the `using` above once it unwinds — Cancel() is what makes it unwind
        // promptly. ObjectDisposedException below: it already got there on its own.
        if (prev is not null)
        {
            try { prev.Cancel(); } catch (ObjectDisposedException) { }
        }
        try
        {
            // A reset racing in here disposes this index, which is the
            // ObjectDisposedException the catch below turns into Superseded.
            result.Results = index.Search(query, cts.Token);
        }
        catch (OperationCanceledException)
        {
            result.Superseded = true;   // a newer search took over — caller discards this
        }
        catch (Exception ex) when (ex is ObjectDisposedException
                                      or DirectoryNotFoundException
                                      or FileNotFoundException)
        {
            // ResetIndex ran under us: it disposes the index (searches are cancelled first, but
            // the last stretch of a query is not inside a lock) and deletes the directory the
            // reader's files live in. Neither is a search failure — same contract as a
            // supersede: the caller discards this and the rebuild serves the next one. Reported
            // as an error instead, the UI showed "no results" for a perfectly good query.
            result.Superseded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "catalog TOC search failed");
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
