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
    /// The on-disk Lucene index directory.  Matches the default used by the
    /// standalone DocumentLocator.Service.exe so both hosts share the same index.
    /// </summary>
    private static string IndexPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DocumentLocator",
            "filesystemindex");

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
    private Task? _buildTask;

    /// <summary>
    /// Ensures the index is open and fully built, then returns it.
    /// The build is run once; subsequent calls return the already-open instance.
    /// Progress is swallowed — the caller's loading animation covers the wait.
    /// </summary>
    private async Task<DocumentLocator.PathIndex> EnsureIndexAsync(CancellationToken ct)
    {
        if (_index is not null) return _index;

        await _buildGate.WaitAsync(ct);
        try
        {
            if (_index is not null) return _index;

            var index = new DocumentLocator.PathIndex(IndexPath);

            // Run the (potentially slow) build on a thread-pool thread so we
            // don't block the host's async machinery.
            if (_buildTask is null || _buildTask.IsCompleted)
            {
                _buildTask = Task.Run(() =>
                {
                    try
                    {
                        index.Build(
                            msg => logger.LogDebug("[DocumentLocator] {Message}", msg),
                            ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "[DocumentLocator] Index build failed");
                    }
                }, ct);
            }

            await _buildTask.WaitAsync(ct);
            _index = index;

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

            return _index;
        }
        finally
        {
            _buildGate.Release();
        }
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

        // Kick off a fresh build in the background (fire-and-forget).
        _ = EnsureIndexAsync(CancellationToken.None);
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
