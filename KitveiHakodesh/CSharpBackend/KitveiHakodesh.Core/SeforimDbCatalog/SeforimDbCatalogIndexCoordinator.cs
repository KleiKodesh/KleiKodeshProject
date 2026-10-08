using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimDbCatalog
{
    /// <summary>
    /// Owns the LIFECYCLE of the catalog index: when to open it, when to build, what to do when a
    /// build fails, and how to wipe and start over without racing the build it just cancelled.
    ///
    /// The index itself (<see cref="SeforimDbCatalogIndex"/>) knows how to search and how to
    /// build. It does not decide WHEN. That decision used to live in both hosts as the same
    /// ~200 lines — the same thirteen fields, the same three-attempt retry budget, the same
    /// claim-before-cancel reset ordering, down to the same explanatory comments — so a fix to
    /// one was a silent divergence from the other.
    ///
    /// THE INVARIANTS THIS CLASS EXISTS TO HOLD, each of which cost a bug to learn:
    ///
    ///   • Build at most once per process, but RE-ARM on failure, bounded. The latch is set
    ///     before the build and was never cleared when one threw, so a single transient error
    ///     (the database locked by an FTS rebuild, a full disk) left the index permanently
    ///     unbuilt and catalog search dead for the life of the process. Bounded at
    ///     <see cref="MaxBuildAttempts"/> so a build that always fails cannot be restarted
    ///     forever by every status poll and search keystroke.
    ///
    ///   • A failing build must NOT re-arm during a reset. A reset cancels the build, then
    ///     deletes the directory, then rebuilds. Re-arming from the cancelled build would let a
    ///     status poll start a build into a directory that is about to be deleted — and that
    ///     build's own failure would burn another attempt, so one unlucky reset could exhaust
    ///     the budget and kill catalog search entirely. Hence the reset CLAIMS ownership before
    ///     it cancels.
    ///
    ///   • A reset waits for the build to actually unwind before deleting anything. An earlier
    ///     version capped the wait and deleted regardless, while the build could still be
    ///     writing through the index object being disposed underneath it.
    ///
    ///   • Serve a STALE index rather than nothing. Opening succeeds first, the rebuild runs in
    ///     the background, and the switch is atomic — so a changed database never means downtime.
    ///
    /// WHAT STAYS WITH THE HOST: logging, and where the database is. Both are supplied — the
    /// log sink because a service has an ILogger and a desktop app has a console, and the path
    /// as a FUNCTION because the two hosts genuinely differ. The service resolves the database
    /// once at startup and restarts if it changes; the hosted app lets the user switch databases
    /// mid-session and expects the index to follow without a restart.
    ///
    /// Thread-safe. Callers are UI threads, background builds, and concurrent searches.
    /// </summary>
    public sealed class SeforimDbCatalogIndexCoordinator : IDisposable
    {
        /// <summary>How many times a failing build may be retried before giving up for the life
        /// of the process. Three: enough to ride out a transient lock, few enough that a build
        /// which always fails stops burning CPU on every status poll.</summary>
        public const int MaxBuildAttempts = 3;

        private readonly Func<string?> _resolveDatabasePath;
        private readonly string _indexPath;
        private readonly Action<CatalogIndexLogLevel, string, Exception?> _log;
        private readonly Func<CancellationTokenSource?>? _takeActiveSearchCts;

        private readonly object _lock = new object();
        private SeforimDbCatalogIndex? _index;
        private Task? _buildTask;
        private CancellationTokenSource? _buildCts;

        private volatile bool _isReady;      // an index (fresh or stale) is open and serving
        private volatile bool _isIndexing;
        private volatile bool _buildStarted; // latch: build at most once per process

        private int _failedBuilds;
        private bool _resetInFlight;
        private volatile int _builtBooks;
        private volatile int _totalBooks;

        /// <param name="resolveDatabasePath">Where the database is. A FUNCTION, not a string,
        /// because the hosts differ: the hosted app reads the setting fresh so a mid-session
        /// switch is followed without a restart, while the service closes over a value resolved
        /// once at startup and restarts instead. Returns null when there is no database — an
        /// ordinary state, not an error.</param>
        /// <param name="indexPath">The index directory. Fixed for the process.</param>
        /// <param name="log">Where to report. Called from background threads.</param>
        /// <param name="takeActiveSearchCts">Hands over the in-flight search's cancellation
        /// source, clearing it, so a RESET can stop that search before disposing the index it is
        /// reading — otherwise the query faults instead of coming back superseded. Each host owns
        /// its own search concurrency, so the source lives there; only the ordering is shared.
        /// Null when a host runs no cancellable searches.</param>
        public SeforimDbCatalogIndexCoordinator(
            Func<string?> resolveDatabasePath,
            string indexPath,
            Action<CatalogIndexLogLevel, string, Exception?> log,
            Func<CancellationTokenSource?>? takeActiveSearchCts = null)
        {
            if (resolveDatabasePath == null) throw new ArgumentNullException(nameof(resolveDatabasePath));
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath is required", nameof(indexPath));
            if (log == null) throw new ArgumentNullException(nameof(log));

            _resolveDatabasePath = resolveDatabasePath;
            _indexPath = indexPath;
            _log = log;
            _takeActiveSearchCts = takeActiveSearchCts;
        }

        public bool IsReady => _isReady;

        /// <summary>
        /// Whether a search can be served RIGHT NOW, opening a reader if one is not open yet.
        ///
        /// Not the same question as <see cref="IsReady"/>: during the very first build a
        /// near-real-time reader serves partial results, so readiness must not wait for the
        /// build to finish. Only a genuinely absent reader — no database, or the first build
        /// has not opened one in this split second — counts as not ready. Latches once true.
        /// </summary>
        public bool IsReadyToServe() => TryGetSearchableIndex(out _);

        /// <summary>
        /// The index to search, or false when there is nothing to search yet.
        ///
        /// Answers "is it ready?" and "which index?" in ONE call, resolving the database path
        /// once, because asking separately is a bug: between a ready-check and a GetIndex the
        /// path can change (the hosted app lets the user repoint mid-session) or the file can
        /// vanish, and the caller is left dereferencing null on what it was told was ready.
        /// </summary>
        public bool TryGetSearchableIndex(out SeforimDbCatalogIndex index)
        {
            string? databasePath = DatabasePath;
            if (databasePath == null)
            {
                index = null!;
                return false;
            }

            SeforimDbCatalogIndex candidate = GetIndex(databasePath);
            if (_isReady)
            {
                index = candidate;
                return true;
            }

            // Not latched yet: a reader has to exist. During the very first build the
            // near-real-time reader serves partial results, so this must not wait for the
            // build to finish — only a genuinely absent reader counts as not ready.
            if (!candidate.TryOpenActive())
            {
                index = null!;
                return false;
            }

            _isReady = true;
            index = candidate;
            return true;
        }
        public bool IsIndexing => _isIndexing;
        public int BuiltBooks => _builtBooks;
        public int TotalBooks => _totalBooks;

        /// <summary>The open index, or null when none is open. Does NOT open one — for a caller
        /// that wants to act on an index only if it already exists, such as releasing an idle
        /// reader during a memory trim.</summary>
        public SeforimDbCatalogIndex? CurrentIndex
        {
            get { lock (_lock) return _index; }
        }

        /// <summary>The database, or null when there is none to index.</summary>
        public string? DatabasePath
        {
            get
            {
                try
                {
                    string? path = _resolveDatabasePath();
                    return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
                }
                catch
                {
                    // A host that reads this from settings can throw; no database is the answer.
                    return null;
                }
            }
        }

        /// <summary>
        /// The open index, for searching. Null when there is no database to open one over.
        ///
        /// Opening is lazy and shared: ONE instance per directory, because a build publishes
        /// through it and a second instance would not see the new segments.
        /// </summary>
        public SeforimDbCatalogIndex? GetIndex()
        {
            string? databasePath = DatabasePath;
            return databasePath == null ? null : GetIndex(databasePath);
        }

        private SeforimDbCatalogIndex GetIndex(string databasePath)
        {
            lock (_lock)
            {
                return _index ?? (_index = new SeforimDbCatalogIndex(_indexPath, databasePath));
            }
        }

        /// <summary>
        /// Idempotent. Opens the existing index — even a stale one, so there is no downtime —
        /// and when its stored database hash differs from the current one, starts a background
        /// rebuild that builds in place and stays searchable meanwhile.
        /// </summary>
        public void EnsureIndex()
        {
            string? databasePath = DatabasePath;
            if (databasePath == null) return;

            // Fast path, unlocked: nothing to do and an index already serving.
            if (_buildStarted && _isReady) return;

            lock (_lock)
            {
                // The REAL latch, and it is plain _buildStarted, not "started AND ready".
                // During a cold first build _isReady is still false, so the weaker predicate
                // would let every status poll and search keystroke fall through and start
                // ANOTHER build — each one overwriting _buildCts and _buildTask, which is what
                // Shutdown and ResetIndexCore cancel and wait on. The earlier builds would then
                // be unreachable and still writing while a reset deletes the directory.
                if (_buildStarted) return;

                // Hashing stays INSIDE the lock, after the latch: in the common already-built
                // case this is never reached, so an EnsureIndex from a keystroke costs nothing.
                string currentHash;
                try { currentHash = SeforimDbCatalogIndex.ComputeDbHash(databasePath); }
                catch (Exception ex)
                {
                    _log(CatalogIndexLogLevel.Error, "catalog TOC index: could not hash seforim DB", ex);
                    return;
                }

                var index = GetIndex(databasePath);
                bool opened = index.TryOpenActive();
                if (opened) _isReady = true;
                if (opened && string.Equals(index.ActiveHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _buildStarted = true;
                    return;
                }

                _log(CatalogIndexLogLevel.Information,
                    opened
                        ? "catalog TOC index is stale (seforim DB changed) — rebuilding in the background, serving the old index meanwhile"
                        : "catalog TOC index missing — building",
                    null);

                _buildStarted = true;
                _isIndexing = true;
                _buildCts = new CancellationTokenSource();
                var token = _buildCts.Token;
                _buildTask = Task.Run(() => RunBuild(databasePath, currentHash, token), CancellationToken.None);
            }
        }

        private void RunBuild(string databasePath, string dbHash, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(_indexPath);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int docs = GetIndex(databasePath).BuildAndSwitch(
                    dbHash,
                    onProgress: (done, total) => { _builtBooks = done; _totalBooks = total; },
                    ct: ct);
                _isReady = true;
                _log(CatalogIndexLogLevel.Information,
                    $"catalog TOC index build complete — {docs} docs in {sw.Elapsed.TotalSeconds:F1}s (switched atomically)",
                    null);
            }
            catch (OperationCanceledException)
            {
                _log(CatalogIndexLogLevel.Information, "catalog TOC index build cancelled", null);
            }
            catch (Exception ex)
            {
                _log(CatalogIndexLogLevel.Error, "catalog TOC index build failed", ex);

                // Interlocked: two builds can be in flight across a re-arm, and a lost increment
                // would turn a bounded retry into an unbounded one.
                int failures = Interlocked.Increment(ref _failedBuilds);
                bool resetOwnsTheRebuild;
                lock (_lock) { resetOwnsTheRebuild = _resetInFlight; }

                if (resetOwnsTheRebuild)
                {
                    _log(CatalogIndexLogLevel.Information,
                        "catalog TOC index build failed during a reset — the reset will rebuild", null);
                }
                else if (failures < MaxBuildAttempts)
                {
                    lock (_lock) { _buildStarted = false; }
                }
                else
                {
                    _log(CatalogIndexLogLevel.Warning,
                        $"catalog TOC index: giving up after {failures} failed build attempts", null);
                }
            }
            finally
            {
                _isIndexing = false;
            }
        }

        /// <summary>
        /// If the database's current change-stamp no longer matches the built index's, start a
        /// background rebuild; the old index keeps serving meanwhile. Returns whether one was
        /// started. A spurious call where nothing changed costs one cheap stamp read.
        /// </summary>
        public bool RebuildIfDbChanged()
        {
            string? databasePath = DatabasePath;
            if (databasePath == null) return false;

            string current;
            try { current = SeforimDbCatalogIndex.ComputeDbHash(databasePath); }
            catch { return false; }

            lock (_lock)
            {
                // Already rebuilding for a change → don't stack another.
                if (_isIndexing) return false;
                string? active = GetIndex(databasePath).ActiveHash;
                if (string.Equals(active, current, StringComparison.OrdinalIgnoreCase))
                    return false; // unchanged — nothing to do

                // Re-arm EnsureIndex (it is latched to once-per-process), then let it do the
                // stamp compare and background build it already knows how to do.
                _buildStarted = false;
            }
            EnsureIndex();
            return true;
        }

        /// <summary>Wipe and rebuild from scratch. Returns immediately; the work is backgrounded.</summary>
        public void ResetIndex()
        {
            _ = Task.Run(() =>
            {
                // Fire-and-forget: without this catch a failure is an unobserved task exception
                // and the reset silently half-completes (index disposed, nothing rebuilding).
                try { ResetIndexCore(); }
                catch (Exception ex)
                {
                    _log(CatalogIndexLogLevel.Error, "catalog TOC index reset failed", ex);
                }
            });
        }

        private void ResetIndexCore()
        {
            Task? build;
            lock (_lock)
            {
                // Claimed before the cancel: from here a failing build defers to this reset
                // instead of re-arming the latch and racing a build into the directory that is
                // about to be deleted.
                _resetInFlight = true;
                _buildCts?.Cancel();
                build = _buildTask;
            }
            try
            {
                // Wait until the build has actually unwound. An earlier version capped this and
                // deleted the directory anyway, while BuildAndSwitch could still be writing
                // through the index disposed just below. This runs on a background task, so
                // waiting blocks no caller.
                if (build != null)
                {
                    if (!build.Wait(TimeSpan.FromSeconds(30)))
                        _log(CatalogIndexLogLevel.Warning,
                            "catalog TOC reset: build still unwinding after 30s — waiting before deleting the index",
                            null);
                    try { build.Wait(); } catch { /* cancelled or faulted */ }
                }

                // Searches run outside the lock, so cancel the in-flight one BEFORE disposing
                // the index it is reading; a query that still lands mid-dispose comes back
                // superseded. The search owns its CTS and may already have disposed it, which
                // simply means it finished on its own.
                CancelActiveSearch();

                lock (_lock)
                {
                    _index?.Dispose();
                    _index = null;
                    try
                    {
                        if (Directory.Exists(_indexPath)) Directory.Delete(_indexPath, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        _log(CatalogIndexLogLevel.Error, "catalog TOC index reset: delete failed", ex);
                    }
                    _isReady = false;
                    _isIndexing = false;
                    _buildStarted = false;
                    _buildTask = null;
                    _buildCts = null;
                }
            }
            finally
            {
                // Released before EnsureIndex so the rebuild it starts is a normal build whose
                // own failure may re-arm the latch again. Cleared even if a step above threw —
                // a stuck flag would silently disable the failure-retry path for good.
                lock (_lock) { _resetInFlight = false; }
            }
            _log(CatalogIndexLogLevel.Information, "catalog TOC index reset — rebuilding", null);
            EnsureIndex();
        }

        private void CancelActiveSearch()
        {
            try { _takeActiveSearchCts?.Invoke()?.Cancel(); }
            catch (ObjectDisposedException) { /* that search already finished and disposed it */ }
        }

        /// <summary>
        /// The database changed underneath us: drop everything and start again against the new
        /// one. For hosts that allow a mid-session switch.
        /// </summary>
        public void OnDatabasePathChanged()
        {
            lock (_lock)
            {
                if (_buildCts != null) { try { _buildCts.Cancel(); } catch { } }
                if (_index != null) { _index.Dispose(); _index = null; }
                _isReady = false;
                _buildStarted = false;
            }
            EnsureIndex();
        }

        /// <summary>
        /// Cancel the in-flight build and wait for it to unwind, so the Lucene writer is
        /// disposed cleanly. The ver file is written only on full completion, so an interrupted
        /// build is simply treated as stale and rebuilt next run.
        ///
        /// Does NOT dispose the index — a host may shut the build down and keep serving, and
        /// <see cref="Dispose"/> is what releases it.
        /// </summary>
        /// <param name="buildWaitTimeout">How long to wait for the build. Defaults to 25
        /// seconds, under a typical 30-second host shutdown budget.</param>
        public void Shutdown(TimeSpan? buildWaitTimeout = null)
        {
            Task? build;
            lock (_lock)
            {
                _buildCts?.Cancel();
                build = _buildTask;
            }
            if (build != null)
            {
                try { build.Wait(buildWaitTimeout ?? TimeSpan.FromSeconds(25)); }
                catch { /* cancelled or faulted */ }
            }
        }

        /// <summary>
        /// Shut the build down and release the index.
        /// </summary>
        /// <param name="buildWaitTimeout">How long to wait for the build, as
        /// <see cref="Shutdown"/>. A caller that already called Shutdown can pass
        /// <see cref="TimeSpan.Zero"/> rather than waiting a second full timeout — the build is
        /// cancelled either way, and a desktop app closing must not stall twice.</param>
        public void Dispose(TimeSpan? buildWaitTimeout = null)
        {
            Shutdown(buildWaitTimeout);
            lock (_lock)
            {
                _index?.Dispose();
                _index = null;
                _isReady = false;
            }
        }

        public void Dispose() => Dispose(null);
    }

    /// <summary>How serious a coordinator message is. Core does not know what a logger looks
    /// like — a service has an ILogger, a desktop app has a console — so it says this much and
    /// lets the host map it.</summary>
    public enum CatalogIndexLogLevel
    {
        Information,
        Warning,
        Error,
    }
}
