using System.Text.Encodings.Web;
using System.Text.Json;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// File-system search backed by the DocumentLocator <see cref="DocumentLocator.PathIndex"/>
/// running in-process. No named pipe, no JSON serialization — results are typed
/// <see cref="FileHit"/> objects fed directly into the MessagePack response.
///
/// The index directory defaults to the same path the standalone DocumentLocator
/// Windows service uses so both consumers share the same on-disk index. If the
/// index doesn't exist yet it is built on first use (the build can take a minute
/// on a cold machine; Vue's loading animation covers the wait).
///
/// Lifecycle: one <see cref="DocumentLocator.PathIndex"/> instance is created per
/// service lifetime and disposed on shutdown. The index is thread-safe for reads;
/// the background build task holds the single writer lock.
/// </summary>
public sealed class DocumentLocatorService(ILogger<DocumentLocatorService> logger) : IDisposable
{
    // ── Index path ────────────────────────────────────────────────────────────

    /// <summary>
    /// The on-disk Lucene index directory, and the excluded-folders list beside it.
    ///
    /// Resolved from <see cref="DocumentLocator.PathIndex.DefaultIndexPath"/> — the same
    /// constant the standalone DocumentLocator.Service.exe uses — rather than a path
    /// spelled out here. That is the point: this used to hard-code
    /// %ProgramData%\DocumentLocator\filesystemindex, a third location neither host
    /// agreed on, while claiming in a comment that it matched the net48 host.
    ///
    /// It does NOT follow that both hosts land in the same directory. DefaultIndexPath is
    /// AppDomain.CurrentDomain.BaseDirectory + "filesystemindex", which is per-process:
    /// the net48 service resolves it to the install folder, while this service resolves it
    /// to its own bin folder. This host is dev-only today (it is not in the installer
    /// payload; vite.config.ts spawns it from bin), so nothing user-facing depends on the
    /// two agreeing. If it ever ships, KitveiHakodesh.Core's AppFileLocator is the piece
    /// that would make them genuinely agree — wire it in here rather than re-spelling a
    /// literal path.
    /// </summary>
    private static string IndexPath => DocumentLocator.PathIndex.DefaultIndexPath;

    /// <summary>
    /// How often to rescan when there is no live USN watcher (i.e. not elevated).
    /// Bounds how stale search results can get; only used in that fallback case.
    ///
    /// Daily, matching Everything's no-NTFS "Folder Indexing" mode. A rescan is a full
    /// crawl of every indexed drive, so it is far too expensive to run frequently — and
    /// the pass is delta-based (existing entries are re-stamped, not re-analyzed), so a
    /// no-change rescan is much cheaper than the first build.
    /// </summary>
    private static readonly TimeSpan RescanInterval = TimeSpan.FromHours(24);

    // ── Excluded folders ──────────────────────────────────────────────────────
    // Storage AND filtering both live in the library's ExcludedFoldersPersistence, so
    // this host, the net48 DocumentLocator.Service, and the Vue settings dialog all share
    // one implementation and one excluded_folders.json format. These are thin pass-throughs
    // — never re-implement the prefix test here. Read on every search (never cached) so an
    // edit takes effect immediately with no reindex.

    /// <summary>The user-defined excluded folder list, freshly read from disk.</summary>
    public List<string> GetExcludedFolders()
        => DocumentLocator.ExcludedFoldersPersistence.Load(IndexPath);

    /// <summary>Persist the excluded folder list. Takes effect on the next search.</summary>
    public void SetExcludedFolders(IEnumerable<string> folders)
        => DocumentLocator.ExcludedFoldersPersistence.Save(IndexPath, folders);

    // ── Index instance ────────────────────────────────────────────────────────

    private DocumentLocator.PathIndex? _index;
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private Task<DocumentLocator.PathIndex>? _buildTask;

    /// <summary>
    /// Ensures the index is open and fully built, then returns it.
    /// The build is run once; subsequent calls return the already-open instance.
    /// Progress is swallowed — the caller's loading animation covers the wait.
    /// <para>The build is SHARED, so it deliberately carries no caller's cancellation token:
    /// whoever asks first must not be able to cancel the build every other client is waiting on
    /// by closing their tab. <paramref name="ct"/> cancels only this caller's WAIT. The index
    /// instance is owned by the build task, so an abandoned wait leaks nothing.</para>
    /// </summary>
    private async Task<DocumentLocator.PathIndex> EnsureIndexAsync(CancellationToken ct)
    {
        if (_index is not null) return _index;

        Task<DocumentLocator.PathIndex> build;
        await _buildGate.WaitAsync(ct);
        try
        {
            if (_index is not null) return _index;
            // Run the (potentially slow) build on a thread-pool thread so we
            // don't block the host's async machinery.
            _buildTask ??= Task.Run(BuildIndex);
            build = _buildTask;
        }
        finally
        {
            _buildGate.Release();
        }

        DocumentLocator.PathIndex built;
        try
        {
            built = await build.WaitAsync(ct);
        }
        catch when (build.IsFaulted)
        {
            // The BUILD failed (not our wait). Drop the memo so a later search starts a fresh
            // attempt instead of replaying this exception for the rest of the process lifetime.
            await _buildGate.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_buildTask, build)) _buildTask = null;
            }
            finally
            {
                _buildGate.Release();
            }
            throw;
        }

        bool ours;
        await _buildGate.WaitAsync(ct);
        try
        {
            ours = ReferenceEquals(_buildTask, build);
            if (ours) _index = built;
        }
        finally
        {
            _buildGate.Release();
        }
        if (ours) return built;

        // A Reindex dropped this build while we were waiting on it, so this instance is nobody's
        // index - dispose it (it has its own watcher threads) and take the current one instead.
        try { built.Dispose(); } catch { /* best effort */ }
        return await EnsureIndexAsync(ct);
    }

    /// <summary>Opens and builds the index, then starts the live/periodic update loops. Runs
    /// exactly once per index instance, memoized in <c>_buildTask</c>.</summary>
    private DocumentLocator.PathIndex BuildIndex()
    {
        var index = new DocumentLocator.PathIndex(IndexPath);
        try
        {
            return BuildAndStartWatchers(index);
        }
        catch
        {
            // Anything past the ctor throwing (starting the USN watchers, the rescan timer)
            // leaves an index nobody will ever be handed: dispose it here or its Lucene write
            // lock is held for the process lifetime. The faulted task is dropped by
            // EnsureIndexAsync, so the next search builds again from scratch.
            try { index.Dispose(); } catch { /* best effort */ }
            throw;
        }
    }

    private DocumentLocator.PathIndex BuildAndStartWatchers(DocumentLocator.PathIndex index)
    {
        try
        {
            index.Build(
                msg => logger.LogDebug("[DocumentLocator] {Message}", msg),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed crawl still leaves a usable (if incomplete) index — searches return
            // whatever was written. Only the failures below are fatal to the instance.
            logger.LogError(ex, "[DocumentLocator] Index build failed");
        }

        // Start the live USN journal update loop now that the initial build is done.
        // One background thread per NTFS drive, each blocked in kernel-wait — zero CPU.
        // When the journal cursor goes stale (very rare — requires the NTFS journal to
        // be recreated), the callback triggers a full rebuild automatically.
        index.StartLiveUpdates(CancellationToken.None, (message, ex) =>
        {
            if (ex != null)
                logger.LogWarning(ex, "[DocumentLocator] {Message}", message);
            else
                logger.LogInformation("[DocumentLocator] {Message} — triggering rebuild", message);

            // Reset and rebuild: dispose the stale index, clear state, kick off a fresh build.
            _ = Task.Run(async () =>
            {
                try { await ReindexAsync(CancellationToken.None); }
                catch (Exception rebuildEx)
                {
                    logger.LogError(rebuildEx, "[DocumentLocator] Rebuild after live-update failure failed");
                }
            });
        });

        // When we're not elevated the USN watcher can't start, and without it the
        // index is frozen at build time — it never sees files the user adds, deletes,
        // or renames. Reading the USN journal needs a raw volume handle, which is
        // admin-only by design, so there is no unprivileged substitute; the only
        // mitigation is to rescan periodically and bound how stale the index can get.
        // (Same approach Everything uses for its no-NTFS "Folder Indexing" mode.)
        // No-ops entirely when every drive has a live watcher.
        index.StartPeriodicRescan(
            RescanInterval,
            CancellationToken.None,
            message => logger.LogInformation("[DocumentLocator] {Message}", message),
            (message, ex) => logger.LogError(ex, "[DocumentLocator] {Message}", message));

        return index;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<LocateDocumentsResult> LocateAsync(string query, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new LocateDocumentsResult();

        var index = await EnsureIndexAsync(ct);

        int total;
        var entries = index.Search(query, drive: null, limit: max, total: out total);

        // Apply user-defined folder exclusions AFTER the Lucene query, via the library so
        // this matches the net48 host exactly. Adjusts `total` for the dropped entries.
        entries = DocumentLocator.ExcludedFoldersPersistence.Filter(
            entries, GetExcludedFolders(), ref total);

        var result = new LocateDocumentsResult { Total = total };
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            // PathIndex stores paths lower-cased; preserve that for display.
            string path = entry.Path ?? "";
            if (string.IsNullOrEmpty(path)) continue;

            int lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string fileName  = lastSep >= 0 ? path[(lastSep + 1)..] : path;
            string directory = lastSep >= 0 ? path[..lastSep] : "";

            long modified;
            if (!long.TryParse(entry.DateMs, out modified) || modified == 0)
            {
                try { modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds(); }
                catch { modified = 0; }
            }

            result.Results.Add(new FileHit
            {
                FileName     = fileName,
                Path         = directory,
                ModifiedDate = modified,
                AddinName    = entry.AddinName,
            });
        }

        if (result.Total <= 0) result.Total = result.Results.Count;
        return result;
    }

    /// <summary>
    /// Wipe the index and trigger a full rebuild. Fire-and-forget — the rebuild
    /// runs in the background; the next search call will block until it finishes.
    /// </summary>
    public async Task ReindexAsync(CancellationToken ct)
    {
        // Dispose the current index so the writer lock is released, then rebuild.
        await _buildGate.WaitAsync(ct);
        try
        {
            _index?.Dispose();
            _index = null;
            _buildTask = null;
        }
        finally
        {
            _buildGate.Release();
        }

        // Kick off a fresh build in the background. Fire-and-forget, but never unobserved:
        // a rebuild that fails here would otherwise leave no index and say nothing.
        _ = Task.Run(async () =>
        {
            try { await EnsureIndexAsync(CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "[DocumentLocator] Rebuild after reindex failed"); }
        });
    }

    /// <summary>
    /// Fire-and-forget warm-up: open the index and start the build so the first
    /// real query is fast. Errors are swallowed — it's a best-effort hint.
    /// </summary>
    public void Warmup()
    {
        _ = Task.Run(async () =>
        {
            try { await EnsureIndexAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[DocumentLocator] Warmup failed (non-fatal)");
            }
        });
    }

    public void Dispose()
    {
        _index?.Dispose();
        _index = null;
        _buildGate.Dispose();
    }
}
