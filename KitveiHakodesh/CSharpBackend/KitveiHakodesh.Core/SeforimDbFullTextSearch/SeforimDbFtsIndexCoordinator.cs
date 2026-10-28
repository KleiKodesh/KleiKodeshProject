using KitveiHakodesh.Core.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch;

/// <summary>
/// Owns the LIFECYCLE of the full-text index: when to build, the background build loop, build
/// generations, reset, progress state, readiness/retry while the database is being written, and
/// the live search sessions. Moved out of the service's FullTextSearchService so the host keeps
/// only DI lifetime, logging and the RPC wire shapes.
///
/// The index builds in the background from the seforim DB and becomes searchable segment by
/// segment as it is written (partial results before the build finishes). A completed build
/// writes fts.ver so later runs skip rebuilding; every build session writes fts.src at start so
/// even an INTERRUPTED build records which DB it came from (resume is refused when that no
/// longer matches).
///
/// WHAT STAYS WITH THE HOST: where the database and the index are (supplied — the service
/// reads FTS_INDEX_PATH, Core reads no environment variables), and logging (a sink, mapped by
/// the host).
/// </summary>
public sealed class SeforimDbFtsIndexCoordinator
{
    /// <summary>Prefix for the fts.ver stamp. Bump when the FtsLib on-disk segment
    /// format or the indexing pipeline changes, so existing indexes rebuild even when
    /// the source DB is unchanged.</summary>
    // fts2 (2026-07-22): tokenizer change — inline tags (<b>, <small>, …) are now
    // word-transparent and HTML entities are word separators, so indexed terms
    // differ from fts1 indexes; the stamp mismatch forces a clean rebuild.
    // Core's constant, not a second copy of the string: the two legs must agree about what
    // "fts2" means or each would consider the other's index stale.
    private const string FtsVersion = SeforimDbFtsIndexer.IndexFormatVersion;

    private readonly string? _dbPath;
    private readonly string _indexPath;
    private readonly Action<FtsIndexLogLevel, string, Exception?> _log;
    /// <summary>The related-form query rewrite ('+' inflections, '=' synonyms). Null = queries pass through.</summary>
    private readonly SeforimDbFtsRelatedFormExpander? _expander;

    /// <param name="databasePath">The seforim DB to index and search; null when none was found.</param>
    /// <param name="indexPath">Where the index lives.</param>
    /// <param name="indexIsExternal">True when the host was handed an index to use as-is
    /// (the service's FTS_INDEX_PATH) — never built into, never wiped.</param>
    /// <param name="log">The host's log sink.</param>
    /// <param name="relatedFormExpander">Rewrites a query with related forms when a search asks
    /// for '+' or '='-marked words; null leaves queries unexpanded.</param>
    public SeforimDbFtsIndexCoordinator(
        string? databasePath, string indexPath, bool indexIsExternal,
        Action<FtsIndexLogLevel, string, Exception?> log,
        SeforimDbFtsRelatedFormExpander? relatedFormExpander = null)
    {
        _expander = relatedFormExpander;
        _dbPath = databasePath;
        _indexPath = indexPath;
        _external = indexIsExternal;
        _log = log;
    }

    /// <summary>The build mechanics — the at-start and at-completion stamps, resume state,
    /// write-lock retry, background priority and the force merge — shared with the hosted app
    /// so both legs stamp and resume an index identically. Only built once there is a database
    /// to index; RunBuild is never reached without one.</summary>
    private SeforimDbFtsIndexer Indexer =>
        _indexer ??= new SeforimDbFtsIndexer(_indexPath, _dbPath!);
    private SeforimDbFtsIndexer? _indexer;

    /// <summary>Snippeting, the word-distance filter and short-snippet flagging — all
    /// Core's, and the same code the hosted app runs, so one query cannot come back ranked or
    /// rendered differently in the two apps.</summary>
    private SeforimDbFtsSearcher Searcher => _searcher ??= new SeforimDbFtsSearcher();
    private SeforimDbFtsSearcher? _searcher;

    /// <summary>One-shot searches (<see cref="Search"/>) in flight. Streaming searches are
    /// tracked by <c>_sessions</c>; these are not, so they get their own counter. Changed
    /// under <c>_lock</c> so <see cref="ReleaseIdleResources"/> can test-and-release atomically.</summary>
    private int _activeOneShotSearches;
    // True when FTS_INDEX_PATH was supplied — use that index as-is, never build into it.
    private readonly bool _external;

    private readonly object _lock = new();
    /// <summary>Core owns the engine; this host only holds the handle. Replaced on a wipe
    /// via Invalidate, which is why it is not readonly.</summary>
    private SeforimDbFtsEngine? _engine;
    private Task? _buildTask;
    /// <summary>Cancels the background build. INVARIANT: only ResetIndex and Shutdown may
    /// cancel this, and ResetIndex must bump <see cref="_buildGeneration"/> first. A
    /// canceller that skips the bump lets the dying build's finally clear _isIndexing
    /// without setting _buildFailed, and subscribers are then stuck on a state that is
    /// neither ready, indexing, nor terminal — the hole _buildFailed was added to close.</summary>
    private CancellationTokenSource? _buildCts;

    private volatile bool _isReady;
    private volatile bool _isIndexing;
    private volatile bool _buildStarted;   // latch: build at most once per process (no colliding retries)
    private double _pct;   // progress % — plain double (atomic enough for status reads)
    private volatile int _processed;
    private volatile int _total;

    /// <summary>Set for the duration of the final force merge. That phase reports no lines,
    /// so _pct cannot move during it; without this flag the UI could only show a bar frozen
    /// at its last value for minutes and users read that as a hung build.</summary>
    private volatile bool _isMerging;
    private volatile int _mergePassesDone;
    private volatile int _mergePassesExpected;

    /// <summary>
    /// Identifies the build generation that currently owns the status fields. Bumped by
    /// every reset, so a build cancelled by that reset can be recognised as STALE.
    ///
    /// This is what makes a zombie build harmless. DoReset waits only 30s for the old
    /// build to unwind; past that it deletes the directory anyway (documented, and the
    /// right call — a build wedged on a merge must not block the reset forever). But the
    /// zombie is still running, and its progress callbacks re-marked _isReady while its
    /// finally cleared _isIndexing — over the NEW build's state. The reset then looked
    /// finished while the index was a half-deleted directory, so EnsureIndexing early-
    /// returned on _isReady and searched a corpus that was not there. Background-priority
    /// builds made the 30s timeout easier to hit, but the hole predates them.
    ///
    /// Every write from build-owned code goes through IsCurrentBuild(myGeneration) first.
    /// </summary>
    private volatile int _buildGeneration;

    /// <summary>True when <paramref name="generation"/> still owns the status fields —
    /// i.e. no reset has superseded that build.</summary>
    private bool IsCurrentBuild(int generation) => _buildGeneration == generation;

    /// <summary>
    /// Set when a build ended without producing a searchable index and without being
    /// cancelled — it threw, or it processed nothing. This is a TERMINAL state and it has
    /// to be one: ready+idle was the only terminal condition the progress stream knew, so a
    /// failed build left every subscriber blocked forever on a state that is neither ready
    /// nor indexing. The stream stayed open (one leaked connection per page load), the UI
    /// sat at 0% with no error, and because the once-per-process latch was still armed
    /// nothing ever retried. Cleared when a build starts or a reset is requested.
    /// </summary>
    private volatile bool _buildFailed;

    /// <summary>
    /// Set once <see cref="Shutdown"/> begins, so nothing starts a NEW build afterwards.
    /// Without it a reset draining concurrently with shutdown could null out _buildTask,
    /// let Shutdown see "no build to wait for" and return, and then start a fresh build
    /// from its own finally — leaving the process to exit mid-merge, which is exactly the
    /// abrupt kill Shutdown exists to avoid.
    /// </summary>
    private volatile bool _shuttingDown;

    /// <summary>One-shot retry of EnsureIndexing while the database file is not ready to index
    /// (another app is writing it). Searches and status polls also retry; this covers a quiet
    /// app with nobody asking.</summary>
    private Timer? _notReadyRetry;
    private string? _lastNotReadyReason;
    private static readonly TimeSpan NotReadyRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>True from the moment a reset is accepted until its wipe-and-rebuild has been
    /// handed off. Guards against a second reset stacking on the first — see ResetIndex.</summary>
    private bool _resetInFlight;

    private bool HasDb => !string.IsNullOrWhiteSpace(_dbPath) && File.Exists(_dbPath);


    /// <summary>True while background work is running (index build or live search
    /// sessions) — the idle memory trimmer must not run then.</summary>
    public bool IsBusy => _isIndexing || !_sessions.IsEmpty || Volatile.Read(ref _activeOneShotSearches) > 0;

    /// <summary>What the FTS side keeps resident right now (diagnostics).</summary>
    public string DescribeResidentState()
    {
        lock (_lock) return $"engine={(_engine is null ? "none" : "created")} sessions={_sessions.Count}";
    }

    /// <summary>
    /// Idle memory release: drops the open engine handle (the next search reopens it lazily
    /// via <see cref="GetEngine"/>) and the search enrichment's cached catalog (reloaded on
    /// the next search). Returns true if anything was released.
    ///
    /// Skipped while a build runs (the build publishes through the SAME engine instance —
    /// see SeforimDbFtsEngine), or while any streaming or one-shot search is in flight. The
    /// check and the release happen under <c>_lock</c>, and a one-shot search registers under
    /// the same lock before it touches the engine; a streaming search registers in
    /// <c>_sessions</c> before it touches the engine, so a release either sees it and skips,
    /// or completes first and the search then opens a fresh engine.
    ///
    /// Note: under the FTS5 engine every search opens and disposes its own SQLite
    /// connections (Pooling=false), so the engine object itself retains little; this mainly
    /// matters for engines that keep segment handles open.
    /// </summary>
    public bool ReleaseIdleResources()
    {
        lock (_lock)
        {
            if (_isIndexing || !_sessions.IsEmpty || _activeOneShotSearches > 0) return false;
            bool released = _engine?.IsOpen == true;
            _engine?.Invalidate();
            return released;
        }
    }

    private bool SegmentsExist()
    {
        try { return Directory.Exists(_indexPath) && Directory.GetFiles(_indexPath, FtsLib.Fts5.Fts5SeforimIndex.IndexFileName).Length > 0; }
        catch { return false; }
    }

    private SeforimDbFtsEngine GetEngine()
    {
        lock (_lock) { return _engine ??= new SeforimDbFtsEngine(_indexPath, _dbPath!); }
    }

    /// <summary>
    /// Idempotent. Marks the index ready if a completed build already exists, otherwise
    /// kicks off a background build. Called at startup and lazily from search/status.
    /// </summary>
    public void EnsureIndexing()
    {
        if (!HasDb || _isReady || _shuttingDown) return;
        lock (_lock)
        {
            if (_isReady || _shuttingDown) return;

            // An explicitly-provided index is used as-is — never built into. Re-check
            // cheaply each call so it flips ready once the external index has segments.
            if (_external)
            {
                if (SegmentsExist()) _isReady = true;
                return;
            }

            // Build at most once per process — a failed build must not auto-restart and
            // collide with its own still-in-flight async segment writes. It resumes from
            // the progress file on the next service start.
            if (_buildStarted) return;

            // Another app may be writing the database right now (an Otzaria update, a Zayit
            // reinstall). Comparing stamps against a half-written file would wipe the index
            // and rebuild from half a library. Wait, without latching, and try again.
            if (!SeforimDbReadiness.IsReadyToIndex(_dbPath!, out string notReady))
            {
                if (notReady != _lastNotReadyReason)
                    _log(FtsIndexLogLevel.Information, $"FTS: waiting for the seforim DB — {notReady}", null);
                _lastNotReadyReason = notReady;
                // Meanwhile serve a COMPLETED index if there is one, as the catalog index does.
                // Its provenance is checked once the file is ready: the DB-change watcher keeps
                // re-checking an update in progress, and RebuildIfDbChanged wipes it if stale.
                if (Indexer.HasCompletedIndex()) _isReady = true;
                else
                    _notReadyRetry ??= new Timer(_ => { _notReadyRetry = null; EnsureIndexing(); },
                                                 null, NotReadyRetryDelay, Timeout.InfiniteTimeSpan);
                return;
            }
            _lastNotReadyReason = null;

            // Provenance: validate whatever is on disk against the CURRENT database and clear
            // anything that cannot be trusted. Core's, and the same check the hosted app runs —
            // resuming a watermark that belongs to a different database permanently skips every
            // line below it (the 2026-07-28 incident, where a DB swap under an incomplete build
            // left the whole chumash-parshanut corpus unsearchable).
            // Migration: the FTS5 trial kept its index in a separate "FtsIndexFts5"
            // folder; the index lives in "FtsIndex" again (an old FtsLib index there is wiped by
            // Prepare). Best effort — a folder still held open is retried next start.
            try
            {
                string trialFolder = Path.Combine(AppContext.BaseDirectory, "FtsIndexFts5");
                if (Directory.Exists(trialFolder)) Directory.Delete(trialFolder, recursive: true);
            }
            catch (Exception ex) { _log(FtsIndexLogLevel.Debug, "FTS trial folder not removed yet", ex); }

            SeforimDbFtsIndexPlan plan;
            try
            {
                plan = Indexer.Prepare();
            }
            catch (Exception ex)
            {
                _log(FtsIndexLogLevel.Error, "FTS stale-index wipe failed", ex);
                return;
            }

            if (plan.DbUnreadable)
                _log(FtsIndexLogLevel.Warning,
                    "FTS: seforim DB could not be read right now — keeping the existing index unverified", null);

            if (plan.KeptUnverifiableCompletedIndex)
                _log(FtsIndexLogLevel.Information,
                    "FTS provenance stamp is an older format — keeping the completed index", null);

            if (plan.WipedStaleState)
                _engine?.Invalidate(); // never let an already-open engine answer over the wiped directory

            // A completed service-owned build against the CURRENT DB → ready, no rebuild.
            if (plan.IsReady)
            {
                _isReady = true;
                _buildStarted = true;
                return;
            }

            NoteBuildReason(_pendingBuildReason ?? plan.BuildReason ?? "build needed");
            _pendingBuildReason = null;

            _buildStarted = true;
            _isIndexing = true;
            _buildFailed = false;   // a fresh attempt clears the terminal failure state
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;
            // Captured under _lock so the build carries the generation it was born into;
            // a later reset bumps the counter and every write this build attempts is
            // then recognised as stale. See _buildGeneration.
            int generation = _buildGeneration;
            _buildTask = Task.Run(() => RunBuild(token, generation));
        }
    }

    private void RunBuild(CancellationToken ct, int generation)
    {
        try
        {
            var engine = GetEngine();

            // Everything mechanical about a build — the at-start and at-completion stamps, the
            // resume state, the write-lock retry, background priority, the force merge and
            // clearing the watermark — is Core's, and is the SAME code the hosted app runs.
            // What stays here is this host's own bookkeeping: the generation guard that stops a
            // superseded build writing over the one that replaced it, and the progress fields
            // the RPC stream reads.
            // Existing segments (resume) are already searchable.
            if (IsCurrentBuild(generation) && SegmentsExist()) _isReady = true;

            _log(FtsIndexLogLevel.Information, $"FTS build starting — index={_indexPath}", null);

            // Progress lands in the log too, not only the RPC stream: a headless build (no UI
            // attached) otherwise gives no ETA. Every 5 %, so a full build logs ~20 lines.
            int lastLoggedStep = -1;
            var buildClock = System.Diagnostics.Stopwatch.StartNew();

            bool ok = Indexer.Build(
                engine,
                // Core's own count, so the status stream reports a real total even on a corpus
                // too small to produce a single line tick.
                onTotalKnown: counted =>
                {
                    if (IsCurrentBuild(generation)) _total = (int)Math.Min(counted, int.MaxValue);
                },
                onProgress: (indexed, totalLines) =>
                {
                    // A reset that gave up waiting for this build leaves it running; its
                    // progress must not be written over the new build's state.
                    if (!IsCurrentBuild(generation)) return;
                    if (totalLines <= 0) return;
                    _pct = Math.Min(99.9, indexed * 100.0 / totalLines);
                    _processed = (int)Math.Min(indexed, int.MaxValue);
                    if (!_isReady && SegmentsExist()) _isReady = true; // first segment → searchable
                    NotifyProgress();
                    int step = (int)(_pct / 5);
                    if (step > lastLoggedStep)
                    {
                        lastLoggedStep = step;
                        _log(FtsIndexLogLevel.Information,
                            $"FTS build {_pct:F0}% — {indexed:N0}/{totalLines:N0} lines, {buildClock.Elapsed:hh\\:mm\\:ss} elapsed", null);
                    }
                },
                onSegmentFlushed: () =>
                {
                    if (!IsCurrentBuild(generation)) return; // see onProgress
                    if (!_isReady && SegmentsExist()) _isReady = true;
                    NotifyProgress();
                },
                onMergeProgress: (done, expected) =>
                {
                    if (!IsCurrentBuild(generation)) return; // see onProgress
                    _isMerging = true;
                    _mergePassesDone = done;
                    _mergePassesExpected = expected;
                    NotifyProgress();
                    _log(FtsIndexLogLevel.Information,
                        $"FTS merge pass {done}/{expected} — {buildClock.Elapsed:hh\\:mm\\:ss} elapsed", null);
                },
                // Core asks this before stamping completion. A superseded build answers false
                // and nothing is recorded — its segments are already gone.
                isStillCurrent: () =>
                {
                    if (IsCurrentBuild(generation)) return true;
                    _log(FtsIndexLogLevel.Information,
                        "FTS build finished after a reset superseded it — discarding its result", null);
                    return false;
                },
                cancellationToken: ct);

            if (ok && IsCurrentBuild(generation))
            {
                _pct = 100.0;
                _isMerging = false;   // the merge is part of the build; it is over too
                _isReady = true;
                _log(FtsIndexLogLevel.Information, $"FTS build complete — {_indexPath}", null);
            }
            else if (!ok && IsCurrentBuild(generation) && !_isReady)
            {
                // Build returned false with nothing searchable: an empty corpus, or a session
                // that only replayed WAL recovery. No stamp is written and the latch stays
                // armed, so nothing here will retry — terminal, not "still working".
                _log(FtsIndexLogLevel.Information,
                    "FTS build processed no lines and produced no searchable index — reporting terminal", null);
                _buildFailed = true;
            }
        }
        catch (OperationCanceledException)
        {
            _log(FtsIndexLogLevel.Information, "FTS build cancelled (reset)", null);
        }
        catch (SeforimDbFtsIndexLockedException ex)
        {
            // Retries exhausted — the write lock is still held (unusually long-lived prior
            // instance, or a genuinely concurrent writer). Re-arm the build latch so a later
            // EnsureIndexing() (search / status poll / next start) can resume, rather than
            // abandoning the index permanently and leaving every restart to re-resume a
            // never-finishing build.
            _log(FtsIndexLogLevel.Warning, "FTS index write-lock still held after retries — will resume later", ex);
            // Only if this build still owns the state: re-arming a latch that a concurrent
            // reset is holding on purpose would let a search's EnsureIndexing start a build
            // into the directory that reset is still deleting.
            lock (_lock) { if (IsCurrentBuild(generation)) _buildStarted = false; }
        }
        catch (Exception ex)
        {
            _log(FtsIndexLogLevel.Error, "FTS index build failed", ex);
            // Terminal, unless a segment already made the index searchable: nothing retries
            // (the latch stays armed), so subscribers must be told this is the end rather
            // than waiting for progress that will never come. See _buildFailed.
            if (IsCurrentBuild(generation) && !_isReady) _buildFailed = true;
        }
        finally
        {
            // A superseded build must not clear _isIndexing — that flag now belongs to the
            // rebuild the reset started, and clearing it made the progress stream hit its
            // terminal state (idle) and close while that rebuild was still running.
            if (IsCurrentBuild(generation))
            {
                _isIndexing = false;
                // Whatever ended this build — success, cancellation, or a throw out of the
                // merge itself — the merge phase is not running any more. Leaving this set
                // would strand the UI on "finishing" after the stream has closed.
                _isMerging = false;
                NotifyProgress();   // terminal (or failed) state — wake subscribers so streams close
            }
        }
    }

    /// <summary>
    /// Called by the DB change watcher while the service runs: if the seforim DB's
    /// current change-stamp no longer matches what fts.ver recorded at build time, wipe
    /// and rebuild the index in the background. Returns true if a rebuild was started.
    /// A spurious call where nothing changed costs one cheap stamp read.
    /// </summary>
    public bool RebuildIfDbChanged()
    {
        if (_external || !HasDb) return false;
        // Not now: a file still being written reads as "changed". The watcher asks again.
        if (!SeforimDbReadiness.IsReadyToIndex(_dbPath!, out _)) return false;

        // Core's check, read-only and cheap: fts.ver (completed) or fts.src (in progress) —
        // either is a valid provenance record to invalidate, because a DB swapped MID-BUILD
        // must cancel and restart rather than append the new DB's lines onto the old DB's
        // segments. No stamp means nothing built yet, and an older-format stamp is not
        // comparable; both answer "not stale" so a live index is never wiped over them.
        string? staleBecause;
        try { staleBecause = Indexer.WhyIndexIsStale(); } catch { return false; }
        if (staleBecause == null) return false;

        ResetIndex("changed while running: " + staleBecause); // cancels the in-flight build, wipes, re-arms, and rebuilds
        return true;
    }

    /// <summary>Wipe the FTS index and rebuild it from scratch (dev "reset search index").
    /// Runs on a background thread and returns immediately — progress is streamed.</summary>
    /// <param name="reason">Why, for the log and the status (<see cref="LastBuildReason"/>).</param>
    public void ResetIndex(string reason = "reset requested")
    {
        if (_external) return; // an externally-supplied index (FTS_INDEX_PATH) isn't ours to wipe
        if (_shuttingDown) return; // a late watcher callback must not wipe the index on the way out

        // One reset at a time. A second DoReset running concurrently would cancel the
        // rebuild the first one just started, and the two finallys would race to null
        // _buildTask/_buildCts — leaving a live build that no later reset or shutdown could
        // cancel, to be hard-killed mid-merge at process exit. Reachable without any user
        // action: the DB-change watcher calls RebuildIfDbChanged on every settle, and a DB
        // written in bursts settles repeatedly. Dropping the extra request is right because
        // the reset already in flight ends in a full rebuild from the current DB.
        lock (_lock)
        {
            if (_resetInFlight)
            {
                _log(FtsIndexLogLevel.Information, "FTS reset already in progress — ignoring the duplicate request", null);
                return;
            }
            _resetInFlight = true;
            _pendingBuildReason = reason;
        }

        // Report "indexing, 0%" from the moment the reset is REQUESTED, not from whenever the
        // background wipe finishes draining. The flags used to stay ready+idle until DoReset's
        // final step, and ready+idle is a progress stream's TERMINAL state — so a stream opened
        // between the reset RPC and that step (the app reset reloads the page right after the
        // call) closed on its first snapshot and the UI never showed the rebuild.
        //
        // _buildStarted is latched here and held through the whole wipe so no concurrent
        // EnsureIndexing (every search, status call, and stream open runs it) can start a build
        // into the directory the wipe is about to delete; DoReset re-arms it at the end.
        lock (_lock)
        {
            // Supersede the running build FIRST: from here its callbacks and its finally
            // are recognised as stale and stop writing these fields, so the state set
            // below cannot be clobbered by a build that outlives DoReset's 30s wait.
            _buildGeneration++;
            _isReady = false;
            _isIndexing = true;
            _buildStarted = true;
            _buildFailed = false;   // a reset is a fresh attempt, not the old failure
            _pct = 0; _processed = 0; _total = 0;
            _isMerging = false; _mergePassesDone = 0; _mergePassesExpected = 0;
        }
        NotifyProgress();

        _ = Task.Run(() =>
        {
            // Fire-and-forget: without this catch a failure is an unobserved task exception and
            // the reset half-completes (index dropped, nothing rebuilding) with nothing logged.
            try { DoReset(); }
            catch (Exception ex) { _log(FtsIndexLogLevel.Error, "FTS index reset failed", ex); }
        });
    }

    private void DoReset()
    {
        try
        {
            // 1) Stop the running build (its `using IndexWriteLock` releases on return) and wait.
            Task? build;
            lock (_lock)
            {
                _buildCts?.Cancel();
                build = _buildTask;
            }
            // The build MUST be gone before the directory is deleted, and the wait therefore
            // escalates instead of expiring. Deleting under a live build is not merely untidy:
            // Directory.Delete removes write.lock itself, so the zombie's OS lock stops
            // excluding anything and the rebuild happily takes a FRESH lock on the same
            // directory — two writers, interleaved segment writes, a corrupt index. (The
            // 30s cap that used to sit here is what made that reachable; a build wedged on a
            // long merge simply outlived it.)
            //
            // A cancelled build unwinds in seconds: BuildIndex observes the token between
            // lines and IndexWriter.Dispose drains the flush+merge pipeline. Waiting the
            // whole time is safe — this already runs on a background task, so no caller is
            // blocked — and 30s is generous enough that reaching the second stage means
            // something is genuinely wedged, which is worth saying out loud rather than
            // silently corrupting the index.
            if (build != null)
            {
                if (!build.Wait(TimeSpan.FromSeconds(30)))
                    _log(FtsIndexLogLevel.Warning,
                        "FTS reset: the cancelled build has not unwound after 30s (likely a long merge) — "
                        + "still waiting; the index directory cannot be deleted while it writes", null);
                // Unconditional: returns at once when the wait above already completed, and
                // surfaces a build that ended in a fault rather than by cancellation.
                try { build.Wait(); }
                catch (Exception ex) { _log(FtsIndexLogLevel.Information, "FTS reset: prior build ended with an exception", ex); }
            }

            // 2) Cancel in-flight search sessions so nothing keeps reading the old segments, then WAIT
            //    for them to unwind. Cancelling is not releasing: a search that is mid-fetch still
            //    holds its lease on the segment files, and deleting the directory under it is exactly
            //    what produces the "could not delete" path below. Every session removes itself in its
            //    own finally, so an empty dictionary means every reader has let go.
            // ObjectDisposedException: that session finished and disposed its CTS while we were
            // iterating — it is already gone, which is the state we are asking for.
            foreach (var kv in _sessions)
            {
                try { kv.Value.Cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            for (int i = 0; i < 50 && !_sessions.IsEmpty; i++) Thread.Sleep(100); // bounded: up to 5s
            if (!_sessions.IsEmpty)
                _log(FtsIndexLogLevel.Warning,
                    $"FTS reset: {_sessions.Count} search session(s) still unwinding after 5s — deleting anyway", null);
            _sessions.Clear();

            // 3) Detach the in-memory state, then delete the directory OUTSIDE the lock. The
            //    recursive delete of a multi-gigabyte index takes seconds, and holding _lock
            //    through it (as this used to) blocked every status stream, Status() call, and
            //    search for the whole delete — they all enter EnsureIndexing, which takes the
            //    lock. Nothing can repopulate _index while the delete runs: searches bail on
            //    !_isReady before ever calling GetEngine, and EnsureIndexing is latched out by
            //    the _buildStarted ResetIndex set.
            lock (_lock)
            {
                _engine?.Invalidate(); // drop the SegmentStore so it holds no file references
                // Step 1 guarantees no build is running by now, and the generation bump in
                // ResetIndex stopped a superseded one from writing these fields at all.
                // This re-asserts against the narrow case of a build that had already
                // passed its IsCurrentBuild check when the bump landed.
                _isReady = false;
                _isIndexing = true;
                _buildStarted = true;
                // _buildFailed too: it is TERMINAL, so a superseded build that set it inside
                // this window would make the rebuild's very first status snapshot terminal
                // and close every progress stream immediately — no rebuild progress shown at
                // all, the mirror image of the bug the early flag-setting fixed.
                _buildFailed = false;
            }
            try
            {
                if (Directory.Exists(_indexPath))
                    Directory.Delete(_indexPath, recursive: true);
            }
            catch (Exception ex) { _log(FtsIndexLogLevel.Error, $"FTS reset: could not delete {_indexPath}", ex); }
        }
        finally
        {
            // Re-arm and rebuild even when a step above failed: a partially-deleted index is
            // exactly what EnsureIndexing's provenance stamps exist to catch on the next build,
            // while bailing out with _isIndexing stuck true would freeze every status display
            // on "indexing" forever with nothing running.
            lock (_lock)
            {
                // _isIndexing deliberately stays TRUE across the handoff below. Clearing it
                // here published "not ready, not indexing" — which no longer just looked
                // idle, it is now the Failed-adjacent state a status reader can act on — for
                // the whole window until EnsureIndexing starts the rebuild (which opens
                // SQLite and stamps the DB first). It also made IsBusy false, so the idle
                // memory trimmer could fire between the wipe and the rebuild.
                _buildStarted = false;
                _pct = 0; _processed = 0; _total = 0;
                _isMerging = false; _mergePassesDone = 0; _mergePassesExpected = 0;
                _buildTask = null;
                _buildCts = null;
                // Released HERE, before the rebuild is started — not after it. The wipe is
                // already done, so nothing is left for another reset to race. Holding the
                // flag across EnsureIndexing instead made RebuildIfDbChanged DROP a real DB
                // change that landed in that window: it would defer to "the reset already in
                // flight", while that reset had already stamped and started building from the
                // PREVIOUS database, so the new one waited for the next watcher settle.
                _resetInFlight = false;
            }
            _log(FtsIndexLogLevel.Information, "FTS index reset — rebuilding from scratch", null);
            EnsureIndexing();
            lock (_lock)
            {
                // EnsureIndexing either started a build (it set _isIndexing itself) or found
                // nothing to do. Only in the latter case is "not indexing" the truth.
                if (_buildTask == null) _isIndexing = false;
            }
            NotifyProgress();
        }
    }

    /// <summary>
    /// Graceful shutdown: cancel the in-flight build and WAIT for it to unwind so the
    /// index write lock is released and the on-disk index is left clean and resumable —
    /// never abruptly killed mid-merge (the crash-during-merge path that risks
    /// corruption). Called on host shutdown so a dev restart/stop never hard-kills a
    /// build in progress.
    /// </summary>
    public void Shutdown()
    {
        _notReadyRetry?.Dispose();
        _notReadyRetry = null;
        Task? build;
        lock (_lock)
        {
            // Before anything else: no new build may start from here on. A reset draining
            // concurrently nulls _buildTask in its finally and then calls EnsureIndexing —
            // without this flag that call could start a build after Shutdown had already
            // decided there was nothing to wait for, and the process would exit mid-merge.
            _shuttingDown = true;
            // Also released here as a backstop: DoReset's finally is the normal owner, but if
            // ResetIndex's Task.Run were never scheduled (a saturated pool at shutdown) the
            // flag would stay true with no DoReset to clear it, and every later reset in this
            // process would be silently dropped as a duplicate.
            _resetInFlight = false;
            _buildCts?.Cancel();
            build = _buildTask;
        }
        // stop live searches too; already-disposed means already finished (see ResetIndex)
        foreach (var kv in _sessions)
        {
            try { kv.Value.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        try { build?.Wait(TimeSpan.FromSeconds(25)); }
        catch { /* OperationCanceledException / AggregateException on cancel */ }
        _log(FtsIndexLogLevel.Information, "FTS build stopped for graceful shutdown", null);
    }

    public FtsIndexStatus Status()
    {
        EnsureIndexing();
        return Snapshot();
    }

    /// <summary>The one-shot search (ftsSearch). Ready = false while still building (or no
    /// DB). Full-text search results are NEVER capped: <paramref name="cap"/> &lt;= 0 means
    /// unlimited; a positive cap is only honoured if a caller ever explicitly asks for one.
    /// Words marked '+' (inflections) or '=' (synonyms) are expanded first; '^' (spelling variants) is the engine's.</summary>
    public FtsSearchResult Search(
        string query, int cap, int maxWordDistance, bool requireOrdered, int contextWords)
    {
        EnsureIndexing();
        if (SeforimDbFtsRelatedFormExpander.HasRelatedMarker(query)) query = RewriteRelated(query);
        var result = new FtsSearchResult();
        if (!HasDb || !_isReady)
        {
            result.Ready = false;      // still building (or no DB) — frontend shows the indexing overlay
            return result;
        }
        result.Ready = true;
        if (string.IsNullOrWhiteSpace(query)) return result;

        lock (_lock) _activeOneShotSearches++;
        try
        {
            // Core's searcher yields finished batches; the one-shot path just collects them, so
            // it gets exactly the same parallel snippeting, short-snippet flagging and
            // enrichment as the streaming path.
            var terms = new FtsTermTable();
            foreach (var batch in Searcher.SearchInBatches(
                         GetEngine(), query, maxWordDistance, requireOrdered, contextWords, terms))
            {
                result.Results.AddRange(batch.Hits);
                result.SnippetTexts.AddRange(batch.SnippetTexts);
                if (cap > 0 && result.Results.Count >= cap) break;
            }
            if (cap > 0 && result.Results.Count > cap)
                result.Results.RemoveRange(cap, result.Results.Count - cap);
            result.Terms = terms.All();
        }
        catch (Exception ex)
        {
            _log(FtsIndexLogLevel.Error, "FTS search failed", ex);
            result.Error = ex.Message;
        }
        finally
        {
            lock (_lock) _activeOneShotSearches--;
        }
        return result;
    }

    /// <summary>
    /// Scoped substring candidates (SeforimDbLineSubstringSearch narrows with it): the lines inside <paramref name="ranges"/>
    /// that may hold <paramref name="query"/> as a substring, from the index — the book
    /// view's in-book search (one range: the book) and the commentary panel's (one range
    /// per commentator group). Not answered while the index is building or cannot answer
    /// exactly — the caller then reads the lines itself.
    /// </summary>
    public FtsLinesSearchResult LinesSearch(string query, IReadOnlyList<FtsLineRange> ranges)
    {
        EnsureIndexing();
        var result = new FtsLinesSearchResult();
        if (!HasDb || !_isReady || string.IsNullOrWhiteSpace(query)) return result;
        lock (_lock) _activeOneShotSearches++;
        try
        {
            var lineRanges = new List<(int, int, int)>(ranges.Count);
            foreach (var range in ranges) lineRanges.Add((range.BookId, range.FirstLineIndex, range.LastLineIndex));
            var candidates = GetEngine().SubstringCandidates(query, lineRanges);
            if (candidates == null) return result;
            result.Answered = true;
            result.BookIds = new int[candidates.Count];
            result.LineIndexes = new int[candidates.Count];
            result.LineIds = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
                (result.BookIds[i], result.LineIndexes[i], result.LineIds[i]) = candidates[i];
        }
        catch (Exception ex)
        {
            _log(FtsIndexLogLevel.Error, "FTS scoped search failed", ex);
        }
        finally
        {
            lock (_lock) _activeOneShotSearches--;
        }
        return result;
    }

    /// <summary>
    /// Snippet texts for the rows a page shows (ftsSnippetTexts): each line's own text, and
    /// for the lines the hit flagged short also their neighbour context — one statement
    /// (<see cref="SeforimDbFtsCorpus.FetchSnippetTexts"/>). No index, no query, no locate:
    /// the descriptor already rode on the hit. Works while the index is still building.
    /// </summary>
    public List<FtsSnippetText> SnippetTexts(IReadOnlyList<int> lineIds, IReadOnlyList<int> shortLineIds)
    {
        if (!HasDb || (lineIds.Count == 0 && shortLineIds.Count == 0)) return new List<FtsSnippetText>();
        try
        {
            using var corpus = new SeforimDbFtsCorpus(_dbPath!);
            return corpus.FetchSnippetTexts(lineIds, shortLineIds);
        }
        catch (Exception ex)
        {
            _log(FtsIndexLogLevel.Error, "FTS snippet texts failed", ex);
            return new List<FtsSnippetText>();
        }
    }

    private string RewriteRelated(string query) =>
        _expander is null ? query : _expander.RewriteQuery(query, SeforimDbFtsRelatedFormExpander.PerTermLimit);

    // ── Streaming search (ftsSearchStream) ───────────────────────────────────────
    // The service PUSHES result frames continuously over the caller's single pipe
    // connection until the search finishes — no polling, no offsets, no buffering.
    // Backpressure is inherent: each batch awaits the pipe write before the next one
    // is built. Client disconnect (broken pipe) cancels the search. Results are NEVER
    // capped — streaming is exactly what makes an uncapped search feel instant.

    /// <summary>An in-flight streaming search — tracked only for cancellation
    /// (supersession, reset, shutdown). Results are pushed, never buffered.</summary>
    private sealed class SearchSession
    {
        public readonly CancellationTokenSource Cts = new();
    }

    private readonly ConcurrentDictionary<string, SearchSession> _sessions = new();
    private int _searchCounter;
    private string? _currentSearchId;   // most-recent search; the next one supersedes it

    /// <summary>Run a search and push each built batch through <paramref name="emit"/>,
    /// ending with a <c>Done</c> frame. The connection is the stream: the caller passes
    /// an emit that writes one frame per chunk on the client's connection.</summary>
    /// <param name="resumeAfterLineId">Resume: stream only the hits after this line — the
    /// last one the caller already holds from an earlier, unfinished run of the same search.
    /// 0 = from the start. A line that cannot be placed any more restarts the search
    /// (<see cref="FtsStreamChunk.Restarted"/> on the first frame).</param>
    public async Task StreamSearch(
        string query, int maxWordDistance, bool requireOrdered, int contextWords,
        Func<FtsStreamChunk, Task> emit, CancellationToken clientCt, int resumeAfterLineId = 0)
    {
        EnsureIndexing();
        if (SeforimDbFtsRelatedFormExpander.HasRelatedMarker(query))
        {
            try { await emit(new FtsStreamChunk { Stage = "rewrite" }); } catch { /* client gone */ }
            query = RewriteRelated(query);
        }
        if (!HasDb || !_isReady) { await emit(new FtsStreamChunk { Ready = false, Done = true }); return; }
        if (string.IsNullOrWhiteSpace(query)) { await emit(new FtsStreamChunk { Done = true }); return; }

        var session = new SearchSession();
        string id = "s" + Interlocked.Increment(ref _searchCounter);

        // Everything from the registration on is inside the try, so no path can leave a
        // session sitting in _sessions with an undisposed CTS. Cancel() can surface callback
        // exceptions from the superseded search, so "nothing throws between here and the
        // try" is not a safe assumption to build on.
        try
        {
            _sessions[id] = session;

            // A new search SUPERSEDES the previous in-flight one — cancel it so the service
            // never keeps burning cores generating snippets for results the caller has already
            // moved on from. "Latest search wins" is a SERVICE guarantee (mirroring the hosted
            // FtsSearchExecutor): a client only ever starts a new stream, nothing else. The
            // atomic swap makes rapid back-to-back searches race-safe.
            var prevId = Interlocked.Exchange(ref _currentSearchId, id);
            if (prevId != null && _sessions.TryRemove(prevId, out var prev))
            {
                // Cancel, but do NOT dispose: the superseded search is still inside its own
                // StreamSearch call with a linked source built off this token. It disposes its
                // CTS in its own finally once it unwinds — which it may already have done
                // between our TryRemove and this call, hence the guard.
                try { prev.Cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(clientCt, session.Cts.Token);
            try
            {
                long afterPosition = 0;
                bool restarted = false;
                if (resumeAfterLineId > 0)
                {
                    long? position = GetEngine().PositionOfLine(resumeAfterLineId);
                    if (position is long found) afterPosition = found;
                    else restarted = true;
                }
                await emit(new FtsStreamChunk { Stage = "validate", Restarted = restarted });
                // Stage changes ride alone only until the first results frame: before it they
                // are all the client has to show. After it every batch would otherwise cost
                // three stage-only frames (fetch, snippets, enrich) on top of its results
                // frame, so from then on the latest stage is folded into the next results
                // frame instead. Stage hooks fire synchronously on the enumerating thread
                // (the batch loop already blocks it per MoveNext); blocking on emit keeps
                // frame order intact. The service host has no SynchronizationContext, so
                // this cannot deadlock.
                bool resultsSent = false;
                string? stageSinceLastResults = null;
                await RunSearchCoreAsync(query, maxWordDistance, requireOrdered, contextWords, afterPosition,
                    linked.Token,
                    (built, newTerms, snippetTexts) =>
                    {
                        resultsSent = true;
                        string? stage = stageSinceLastResults;
                        stageSinceLastResults = null;
                        return emit(new FtsStreamChunk { Results = built, NewTerms = newTerms, SnippetTexts = snippetTexts, Stage = stage });
                    },
                    stage =>
                    {
                        if (resultsSent) stageSinceLastResults = stage;
                        else emit(new FtsStreamChunk { Stage = stage }).GetAwaiter().GetResult();
                    });
                await emit(new FtsStreamChunk { Done = true });
            }
            catch (OperationCanceledException) { /* superseded, reset, or client left */ }
            catch (IOException) { /* client disconnected mid-stream — that IS the cancel signal */ }
            catch (Exception ex)
            {
                _log(FtsIndexLogLevel.Error, "FTS streaming search failed", ex);
                try { await emit(new FtsStreamChunk { Done = true, Error = ex.Message }); } catch { /* client gone */ }
            }
        }
        finally
        {
            // Unregister here rather than in an inner finally so it covers every path in,
            // including a throw out of the supersede block above — a session left in
            // _sessions would make ResetIndex wait out its full 5s unwind timeout for a
            // search that is already gone.
            _sessions.TryRemove(id, out _);
            Interlocked.CompareExchange(ref _currentSearchId, null, id);

            // Last, so it runs AFTER the inner `using linked` has been disposed: a linked
            // source registers a callback on session.Cts, and disposing the parent while that
            // registration is live is exactly the kind of teardown order that bites. By now
            // the session is out of _sessions, so no superseding search can reach it.
            session.Cts.Dispose();
        }
    }

    /// <summary>The search pipeline shared by streaming and one-shot paths: content fetch
    /// STREAMS from one SQLite reader (sequential I/O, overlaps with snippeting); snippet
    /// generation — the dominant CPU cost — runs per-batch across all cores; each finished,
    /// enriched batch is handed to <paramref name="onBatch"/> in order.</summary>
    private async Task RunSearchCoreAsync(
        string query, int maxWordDistance, bool requireOrdered, int contextWords, long afterPosition,
        CancellationToken ct, Func<List<FtsHit>, List<string>, List<FtsSnippetText>, Task> onBatch,
        Action<string>? onStage = null)
    {
        // Backpressure is why this awaits per batch rather than collecting: each batch is
        // written to the caller's pipe before the next one is built. Each batch goes out with
        // the matched-term strings it introduced (hits carry ids into the search's table), and
        // the leading SeedSnippetTextHitCount hits of the search with their snippet texts,
        // so the first screen paints without a fetch. Frames past that carry no texts.
        var terms = new FtsTermTable();
        foreach (var batch in Searcher.SearchInBatches(
                     GetEngine(), query, maxWordDistance, requireOrdered, contextWords, terms, ct, onStage, afterPosition))
        {
            await onBatch(batch.Hits, terms.TakeNewTerms(), batch.SnippetTexts);
        }
    }

    // ── Indexing-progress stream (ftsIndexProgressStream) ────────────────────────
    // Event-driven push: the build's progress callback signals every subscriber, and
    // each subscriber's stream emits a fresh snapshot per signal — no polling on the
    // wire and none inside the service. The stream ends (connection closes) when the
    // build reaches a terminal state (ready, or no DB to index), so an idle service
    // carries no open progress streams.

    private readonly List<System.Threading.Channels.Channel<bool>> _progressSubs = new();

    /// <summary>Wake every progress subscriber. Cheap (TryWrite on a capacity-1 channel
    /// that drops when full — a pending signal already guarantees a fresh snapshot).</summary>
    private void NotifyProgress()
    {
        lock (_progressSubs)
            foreach (var ch in _progressSubs) ch.Writer.TryWrite(true);
    }

    /// <summary>Why the index last started building, and when; null until a build starts.</summary>
    public IndexBuildReason? LastBuildReason => _lastBuildReason;
    private volatile IndexBuildReason? _lastBuildReason;

    /// <summary>A reset's reason, carried to the build EnsureIndexing starts after the wipe
    /// (whose own plan would only say "no index yet").</summary>
    private string? _pendingBuildReason;

    private void NoteBuildReason(string reason)
    {
        _lastBuildReason = IndexBuildReason.Now(reason);
        _log(FtsIndexLogLevel.Information, "FTS index building: " + reason, null);
    }

    private FtsIndexStatus Snapshot() => new()
    {
        BuildReason = _lastBuildReason,
        IsReady = _isReady,
        IsIndexing = _isIndexing,
        Percentage = Math.Round(_pct, 1),
        ProcessedChunks = _processed,
        TotalChunks = _total,
        DbMissing = !HasDb,
        Failed = _buildFailed,
        IsMerging = _isMerging,
        MergePassesDone = _mergePassesDone,
        MergePassesExpected = _mergePassesExpected,
    };

    /// <summary>Push the current status immediately, then a fresh snapshot on every
    /// progress signal, until the build reaches a terminal state.</summary>
    public async Task StreamIndexingProgress(Func<FtsIndexStatus, Task> emit, CancellationToken ct)
    {
        EnsureIndexing();

        var ch = System.Threading.Channels.Channel.CreateBounded<bool>(
            new System.Threading.Channels.BoundedChannelOptions(1)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
            });
        lock (_progressSubs) _progressSubs.Add(ch);
        try
        {
            var last = Snapshot();
            await emit(last);
            // Terminal: nothing left to report — ready and not building, or no DB at all.
            // Failed is terminal too — see FtsIndexStatus.Failed. Leaving it out is what let
            // a thrown build hold every subscriber open on a state nothing would ever change.
            while (!((last.IsReady && !last.IsIndexing) || last.DbMissing || last.Failed))
            {
                await ch.Reader.ReadAsync(ct);
                last = Snapshot();
                await emit(last);
            }
        }
        catch (OperationCanceledException) { /* client left */ }
        catch (IOException) { /* client disconnected mid-stream */ }
        catch (Exception ex)
        {
            // A frame write can also fail as ObjectDisposedException (connection torn down
            // under us) or from serialization. Those are this subscriber's problem, not the
            // dispatcher's: letting them escape pushes an exception into the connection
            // handler for what is only a dead progress stream.
            _log(FtsIndexLogLevel.Debug, "FTS progress stream ended on an unexpected error", ex);
        }
        finally
        {
            lock (_progressSubs) _progressSubs.Remove(ch);
        }
    }
}

/// <summary>How serious a coordinator message is. Core does not know what a logger looks like,
/// so it says this much and lets the host map it.</summary>
public enum FtsIndexLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}
