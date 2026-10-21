using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// Owns the LIFECYCLE of the local file index (<see cref="LocalFileIndex"/>, NTFS MFT + USN
/// journal, directory-walk fallback): build once on first use, publish build status, start the
/// live/periodic update loops, rebuild on a stale journal or a reindex, and shape search hits
/// into <see cref="LocateDocumentsResult"/>. Moved out of the service's LocalFileIndexService so
/// any host can run the same thing; the host supplies only a log sink.
///
/// One instance per host lifetime; dispose on shutdown. The index is thread-safe for reads;
/// the background build task holds the single writer lock.
/// </summary>
public sealed class LocalFileIndexCoordinator : IDisposable
{
    private readonly Action<LocalFileIndexLogLevel, string, Exception?> _log;

    public LocalFileIndexCoordinator(Action<LocalFileIndexLogLevel, string, Exception?> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // ── Index path ────────────────────────────────────────────────────────────

    /// <summary>
    /// The on-disk Lucene index directory, and the excluded-folders list beside it:
    /// <see cref="LocalFileIndex.DefaultIndexPath"/>, i.e. "filesystemindex" beside this exe. For
    /// the installed service that is the KitveiHakodeshService folder under the install root,
    /// which the installer leaves alone on update.
    /// </summary>
    private static string IndexPath => LocalFileIndex.DefaultIndexPath;

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
    // Storage AND filtering both live in Core's LocalFileIndexExcludedFolders. These are thin pass-throughs
    // — never re-implement the prefix test here. Read on every search (never cached) so an
    // edit takes effect immediately with no reindex.

    /// <summary>The user-defined excluded folder list, freshly read from disk.</summary>
    public List<string> GetExcludedFolders()
        => LocalFileIndexExcludedFolders.Load(IndexPath);

    /// <summary>Persist the excluded folder list. Takes effect on the next search.</summary>
    public void SetExcludedFolders(IEnumerable<string> folders)
        => LocalFileIndexExcludedFolders.Save(IndexPath, folders);

    // ── Index instance ────────────────────────────────────────────────────────

    private LocalFileIndex? _index;
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private Task<LocalFileIndex>? _buildTask;

    // Guards the hand-off between searches and an idle release: a search registers itself
    // on the CURRENT index under this lock, and ReleaseIdleResources detaches the index
    // under the same lock only when no search is registered. So a search never runs on an
    // index that is being disposed, and a release never disposes one in use.
    private readonly object _useLock = new();
    private int _activeSearches;

    // ── Build status ──────────────────────────────────────────────────────────

    /// <summary>
    /// The crawler's latest progress message. Written from the build thread, read from
    /// request threads, so it is volatile.
    /// <para>Detail ONLY: whether a build is running is answered by <c>_buildTask</c>, never
    /// by this field being non-null. Because a build can end without clearing it, and a new
    /// build can start before publishing anything, its null-ness does not track the build
    /// lifetime — treating it as a boolean is exactly the bug <see cref="GetIndexStatus"/>
    /// documents.</para>
    /// </summary>
    private volatile string? _buildStatusMessage;

    /// <summary>Why the last build attempt failed (exception message); null when it succeeded
    /// or a new attempt is running. Reported by <see cref="GetIndexStatus"/>.</summary>
    private volatile string? _lastBuildFailure;

    /// <summary>Estimated build progress 0-100 (see LocalFileIndex.Build); -1 when unknown
    /// or no build runs.</summary>
    private volatile int _buildProgressPercent = -1;

    /// <summary>Why the index last (re)built from scratch — a full crawl of a drive, or a
    /// reindex — and when; null while every start only caught up from the journal.</summary>
    public IndexBuildReason? LastBuildReason => _lastBuildReason;
    private volatile IndexBuildReason? _lastBuildReason;

    /// <summary>A reindex's reason, carried to the build that follows it.</summary>
    private string? _pendingBuildReason;

    /// <summary>
    /// Placeholder status for the window between a build being queued and the crawler
    /// emitting its first progress message. English and diagnostic, like every other
    /// message the crawler produces — the user-facing wording lives in the frontend,
    /// which treats <c>Message</c> as an optional detail, not as display text.
    /// </summary>
    private const string BuildStartingMessage = "Starting index build";

    /// <summary>
    /// Whether the index is currently being built, and the crawler's latest progress
    /// message if so. Cheap and non-blocking by design — the frontend polls it while a
    /// search is outstanding, so it must never wait on <see cref="_buildGate"/> or the
    /// build task itself.
    /// </summary>
    public LocalFileIndexStatusResult GetIndexStatus()
    {
        // _index is assigned only after the build completes, so a non-null index means
        // ready. Checking it first keeps the common (warm) case a single field read.
        if (_index is not null) return new LocalFileIndexStatusResult { IsBuilding = false, BuildReason = _lastBuildReason };

        // Read the task ONCE: it can be replaced (by a reindex) or nulled (by a faulted
        // build being dropped) between reads, and a torn view here would be reported as a
        // state that never existed.
        var build = _buildTask;

        // The TASK is what says whether a build is running — not the progress message.
        // Deriving it from the message instead would strand the flag on when a build dies
        // before it can publish anything (the LocalFileIndex ctor can throw on a held write
        // lock), and would strand it off whenever an outgoing build's cleanup cleared a
        // message its successor had already posted.
        return new LocalFileIndexStatusResult
        {
            IsBuilding = build is not null && !build.IsCompleted,
            Message = _buildStatusMessage,
            BuildFailure = build is not null && !build.IsCompleted ? null : _lastBuildFailure,
            ProgressPercent = build is not null && !build.IsCompleted ? _buildProgressPercent : -1,
            BuildReason = _lastBuildReason,
        };
    }

    /// <summary>
    /// Ensures the index is open and fully built, then returns it.
    /// The build is run once; subsequent calls return the already-open instance.
    /// Progress is published to <see cref="_buildStatusMessage"/> so a waiting caller can be
    /// told the index is being built rather than just watching a "searching" spinner.
    /// <para>The build is SHARED, so it deliberately carries no caller's cancellation token:
    /// whoever asks first must not be able to cancel the build every other client is waiting on
    /// by closing their tab. <paramref name="ct"/> cancels only this caller's WAIT. The index
    /// instance is owned by the build task, so an abandoned wait leaks nothing.</para>
    /// </summary>
    private async Task<LocalFileIndex> EnsureIndexAsync(CancellationToken ct)
    {
        if (_index is not null) return _index;

        Task<LocalFileIndex> build;
        await _buildGate.WaitAsync(ct);
        try
        {
            if (_index is not null) return _index;
            // Run the (potentially slow) build on a thread-pool thread so we
            // don't block the host's async machinery.
            // Assigning the task is also what makes the status report "building": it covers
            // the build from the moment it is QUEUED, so a status poll landing before the
            // thread-pool actually starts it still sees a build in progress.
            _buildTask ??= Task.Run(BuildIndex);
            build = _buildTask;
        }
        finally
        {
            _buildGate.Release();
        }

        LocalFileIndex built;
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
            if (ours) lock (_useLock) _index = built;
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
    private LocalFileIndex BuildIndex()
    {
        _lastBuildFailure = null;
        LocalFileIndex index;
        try
        {
            index = new LocalFileIndex(IndexPath);
        }
        catch (Exception ex)
        {
            _lastBuildFailure = ex.Message;
            throw;
        }
        try
        {
            return BuildAndStartWatchers(index);
        }
        catch (Exception ex)
        {
            _lastBuildFailure = ex.Message;
            // Anything past the ctor throwing (starting the USN watchers, the rescan timer)
            // leaves an index nobody will ever be handed: dispose it here or its Lucene write
            // lock is held for the process lifetime. The faulted task is dropped by
            // EnsureIndexAsync, so the next search builds again from scratch.
            try { index.Dispose(); } catch { /* best effort */ }
            throw;
        }
    }

    private LocalFileIndex BuildAndStartWatchers(LocalFileIndex index)
    {
        // Publish the crawler's progress so GetIndexStatus can report "building" to the UI.
        // The flag was already raised by EnsureIndexAsync when it queued this build; here we
        // only refine it with real progress. Cleared in the finally: once the crawl is over
        // the caller is waiting on the watchers below, not on the index, and that wait is short.
        var crawlReasons = new List<string>();
        string? pending = Interlocked.Exchange(ref _pendingBuildReason, null);
        try
        {
            index.Build(
                msg =>
                {
                    _buildStatusMessage = msg;
                    _log(LocalFileIndexLogLevel.Debug, "[LocalFileIndex] " + msg, null);
                },
                CancellationToken.None,
                crawlReasons.Add,
                percent => _buildProgressPercent = percent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed crawl still leaves a usable (if incomplete) index — searches return
            // whatever was written. Only the failures below are fatal to the instance.
            _log(LocalFileIndexLogLevel.Error, "[LocalFileIndex] Index build failed", ex);
        }
        finally
        {
            _buildStatusMessage = null;
            _buildProgressPercent = -1;
            if (pending != null || crawlReasons.Count > 0)
            {
                string reason = pending ?? "full crawl — " + string.Join("; ", crawlReasons);
                _lastBuildReason = IndexBuildReason.Now(reason);
                _log(LocalFileIndexLogLevel.Information, "[LocalFileIndex] index built: " + reason, null);
            }
        }

        // Start the live USN journal update loop now that the initial build is done.
        // One background thread per NTFS drive, each blocked in kernel-wait — zero CPU.
        // When the journal cursor goes stale (very rare — requires the NTFS journal to
        // be recreated), the callback triggers a full rebuild automatically.
        index.StartLiveUpdates(CancellationToken.None, (message, ex) =>
        {
            if (ex != null)
                _log(LocalFileIndexLogLevel.Warning, "[LocalFileIndex] " + message, ex);
            else
                _log(LocalFileIndexLogLevel.Information, "[LocalFileIndex] " + message + " — triggering rebuild", null);

            // Reset and rebuild: dispose the stale index, clear state, kick off a fresh build.
            _ = Task.Run(async () =>
            {
                try { await ReindexAsync(CancellationToken.None, "live update failed: " + message); }
                catch (Exception rebuildEx)
                {
                    _log(LocalFileIndexLogLevel.Error, "[LocalFileIndex] Rebuild after live-update failure failed", rebuildEx);
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
            message => _log(LocalFileIndexLogLevel.Information, "[LocalFileIndex] " + message, null),
            (message, ex) => _log(LocalFileIndexLogLevel.Error, "[LocalFileIndex] " + message, ex));

        return index;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<LocateDocumentsResult> LocateAsync(string query, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new LocateDocumentsResult();

        var index = await AcquireIndexAsync(ct);
        int total;
        List<PathEntry> entries;
        try
        {
            entries = index.Search(query, drive: null, limit: max, total: out total);
        }
        finally
        {
            lock (_useLock) _activeSearches--;
        }

        // Apply user-defined folder exclusions AFTER the Lucene query, via the library so
        // this matches the net48 host exactly. Adjusts `total` for the dropped entries.
        entries = LocalFileIndexExcludedFolders.Filter(
            entries, GetExcludedFolders(), ref total);

        var result = new LocateDocumentsResult { Total = total };
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            // LocalFileIndex stores paths lower-cased; preserve that for display.
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
    /// Opens (or reopens, after an idle release) the index and registers one search on it.
    /// The caller MUST decrement <c>_activeSearches</c> under <c>_useLock</c> when done.
    /// Loops if an idle release detached the index between the open and the registration.
    /// </summary>
    private async Task<LocalFileIndex> AcquireIndexAsync(CancellationToken ct)
    {
        while (true)
        {
            var index = await EnsureIndexAsync(ct);
            lock (_useLock)
            {
                if (ReferenceEquals(_index, index))
                {
                    _activeSearches++;
                    return index;
                }
            }
            // Released (or reindexed) in between — open the current one instead.
        }
    }

    // ── Idle release ──────────────────────────────────────────────────────────

    /// <summary>
    /// True while the index is being built, a search is running on it, or a live USN delta /
    /// periodic rescan is writing it. The host's idle trimmer must not release it then.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            var build = _buildTask;
            if (build is not null && !build.IsCompleted) return true;
            if (Volatile.Read(ref _activeSearches) > 0) return true;
            return _index?.IsWriting == true;
        }
    }

    /// <summary>What the local-file index keeps resident right now (diagnostics).</summary>
    public string DescribeResidentState() => _index is null ? "closed" : "open (writer + reader + live watchers)";

    /// <summary>
    /// Idle memory release: closes the Lucene writer + reader (segment term indexes, stored-field
    /// buffers, the writer's RAM buffer) and stops the live USN watcher threads. Returns true if
    /// an index was released.
    ///
    /// Reload: the next search goes through <see cref="EnsureIndexAsync"/>, which reopens the
    /// index and runs <see cref="LocalFileIndex.Build"/>. Build reads the per-drive USN cursor
    /// files the live threads saved after every applied delta, so it applies exactly the file
    /// changes made while the index was closed (or does a full rebuild of a drive whose journal
    /// wrapped/was recreated), then restarts the live watchers. No change is lost.
    ///
    /// Skipped (returns false) when busy (see <see cref="IsBusy"/>), or when some drive has no
    /// live USN watcher (unelevated run): there the cursor cannot be read, so reopening would
    /// mean a full drive crawl on the next search — far worse than the memory it saves.
    /// Thread safety: takes the build gate without waiting (skip if a build/reindex holds it),
    /// then detaches the index under <c>_useLock</c> only when no search is registered.
    /// </summary>
    public bool ReleaseIdleResources()
    {
        if (!_buildGate.Wait(0)) return false;
        LocalFileIndex? detached;
        try
        {
            var build = _buildTask;
            if (build is not null && !build.IsCompleted) return false;

            lock (_useLock)
            {
                var index = _index;
                if (index is null || _activeSearches > 0 || index.IsWriting || index.HasUnwatchedDrives)
                    return false;
                detached = index;
                _index = null;
                _buildTask = null;
            }

            // Disposed while still holding the build gate, so a concurrent search's rebuild
            // cannot open a new writer before this one has released the Lucene write lock.
            detached.Dispose();
        }
        finally
        {
            _buildGate.Release();
        }

        _log(LocalFileIndexLogLevel.Debug, "[LocalFileIndex] Released idle index (reopens on next search)", null);
        return true;
    }

    /// <summary>
    /// Wipe the index and trigger a full rebuild. Fire-and-forget — the rebuild
    /// runs in the background; the next search call will block until it finishes.
    /// </summary>
    /// <param name="reason">Why, for the log and the status (<see cref="LastBuildReason"/>).</param>
    public async Task ReindexAsync(CancellationToken ct, string reason = "reindex requested")
    {
        Volatile.Write(ref _pendingBuildReason, reason);
        // Dispose the current index so the writer lock is released, then rebuild.
        await _buildGate.WaitAsync(ct);
        try
        {
            // Under _useLock so a search registering itself sees the index gone (it then
            // reopens) rather than racing the dispose.
            LocalFileIndex? old;
            lock (_useLock) { old = _index; _index = null; }
            old?.Dispose();
            _buildTask = null;
            // Stale the moment the index it described is gone. The rebuild republishes as
            // it crawls; until then the status carries no detail, only "building".
            _buildStatusMessage = null;
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
            catch (Exception ex) { _log(LocalFileIndexLogLevel.Error, "[LocalFileIndex] Rebuild after reindex failed", ex); }
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
                _log(LocalFileIndexLogLevel.Debug, "[LocalFileIndex] Warmup failed (non-fatal)", ex);
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

/// <summary>How serious a coordinator message is; the host maps it to its logger.</summary>
public enum LocalFileIndexLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}
