using System.Collections.Concurrent;
using FtsLib.Indexing;
using FtsLib.SeforimDb;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Full-text search over the custom FtsLib index. The service OWNS the index and
/// builds it in the background from the seforim DB — the whole point of a service:
/// indexing runs while the user works, and the index becomes searchable segment by
/// segment as it's written (partial results before the build finishes).
///
/// Index location: FTS_INDEX_PATH if set (used as-is), else "FtsIndex" next to the
/// service binary (AppContext.BaseDirectory) — so deleting the service folder deletes
/// the index too. A completed build writes fts.ver so later runs skip rebuilding;
/// every build session writes fts.src at start so even an INTERRUPTED build records
/// which DB it came from (resume is refused when that no longer matches).
/// Search returns one capped batch (the hosted C# path streams; dev doesn't need to).
/// </summary>
public sealed class FullTextSearchService(ILogger<FullTextSearchService> logger, SeforimDbService seforim, SearchExpansionService expansion)
{
    /// <summary>Prefix for the fts.ver stamp. Bump when the FtsLib on-disk segment
    /// format or the indexing pipeline changes, so existing indexes rebuild even when
    /// the source DB is unchanged.</summary>
    // fts2 (2026-07-22): tokenizer change — inline tags (<b>, <small>, …) are now
    // word-transparent and HTML entities are word separators, so indexed terms
    // differ from fts1 indexes; the stamp mismatch forces a clean rebuild.
    private const string FtsVersion = "fts2";

    private readonly string? _dbPath = SeforimDbLocator.Resolve();
    private readonly string _indexPath = ResolveIndexPath();
    // True when FTS_INDEX_PATH was supplied — use that index as-is, never build into it.
    private readonly bool _external = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FTS_INDEX_PATH"));

    private readonly object _lock = new();
    private SeforimIndex? _index;
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

    /// <summary>True from the moment a reset is accepted until its wipe-and-rebuild has been
    /// handed off. Guards against a second reset stacking on the first — see ResetIndex.</summary>
    private bool _resetInFlight;

    private static string ResolveIndexPath()
    {
        string? env = Environment.GetEnvironmentVariable("FTS_INDEX_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        // Next to the service binary (same convention as the hosted app) so deleting
        // the service folder removes the index along with it.
        return Path.Combine(AppContext.BaseDirectory, "FtsIndex");
    }

    private bool HasDb => !string.IsNullOrWhiteSpace(_dbPath) && File.Exists(_dbPath);

    /// <summary>True while background work is running (index build or live search
    /// sessions) — the idle memory trimmer must not run then.</summary>
    public bool IsBusy => _isIndexing || !_sessions.IsEmpty;

    private bool SegmentsExist()
    {
        try { return Directory.Exists(_indexPath) && Directory.GetFiles(_indexPath, "seg_*.dat").Length > 0; }
        catch { return false; }
    }

    private SeforimIndex GetIndex()
    {
        lock (_lock) { return _index ??= new SeforimIndex(_indexPath, _dbPath!); }
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

            // Provenance check. fts.ver records the CONTENT stamp of the seforim DB the
            // index was built from (Common.DbContentStamp — the rows, not the file),
            // written on build COMPLETION. fts.src records the same stamp at build START,
            // so an interrupted build still carries provenance. If any index state exists
            // (segments or a build.progress watermark) and its recorded stamp doesn't
            // match the current DB — or there is no stamp at all (legacy interrupted
            // build) — the state belongs to a different/unknown database and resuming it
            // would permanently skip every line below the old watermark (2026-07-28
            // incident: a DB swap under an incomplete build left the whole
            // chumash-parshanut corpus unsearchable). Wipe and rebuild.
            //
            // This used to use DbChangeStamp, which answers "did anything touch the file?"
            // — including a rewrite that changed no row. That turned every launch into a
            // rebuild for users whose DB gets touched on open, and its USN/file-id fields
            // made it permanent (they never return to a previous value).
            string verFile = Path.Combine(_indexPath, "fts.ver");
            string currentStamp = Common.DbContentStamp.Compute(_dbPath!, FtsVersion);
            string? recordedStamp = ReadStamp(verFile) ?? ReadStamp(Path.Combine(_indexPath, "fts.src"));
            bool hasIndexState = SegmentsExist() || File.Exists(Path.Combine(_indexPath, "build.progress"));

            // A stamp in the older STAMP FORMAT cannot be compared against a current one;
            // it would always look like a different DB and wipe a good index once per
            // format change. A COMPLETED build (fts.ver + segments, no watermark to resume
            // from) is kept and re-stamped on its next build. Anything resumable is still
            // wiped: resuming needs positive proof the watermark belongs to this DB.
            //
            // Note this covers only an unreadable stamp FORMAT, never a changed FtsVersion
            // — a bumped FtsVersion means the segment format changed and MUST rebuild, so
            // it falls through to the mismatch check below. No stamp at all (null) is also
            // not "legacy": it stays null and the mismatch check wipes, as before.
            bool completedBuild = File.Exists(verFile) && SegmentsExist()
                               && !File.Exists(Path.Combine(_indexPath, "build.progress"));
            if (Common.DbContentStamp.IsLegacy(recordedStamp) && completedBuild)
            {
                logger.LogInformation(
                    "FTS provenance stamp is an older format (recorded={Recorded}) — keeping the completed index",
                    recordedStamp);
                recordedStamp = currentStamp; // unverifiable, but not evidence of a different DB
            }

            if (hasIndexState && !string.Equals(recordedStamp, currentStamp, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "FTS index is stale (seforim DB changed or provenance unknown; recorded={Recorded}) — wiping for rebuild",
                    recordedStamp ?? "(none)");
                try { Directory.Delete(_indexPath, recursive: true); }
                catch (Exception ex) { logger.LogError(ex, "FTS stale-index wipe failed"); }
                _index = null; // never let an already-open store answer over the wiped directory
            }

            // A completed service-owned build against the CURRENT DB → ready, no rebuild.
            if (File.Exists(verFile) && SegmentsExist())
            {
                _isReady = true;
                _buildStarted = true;
                return;
            }

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

    /// <summary>Reads a stamp file; null when absent or unreadable.</summary>
    private static string? ReadStamp(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    private void RunBuild(CancellationToken ct, int generation)
    {
        try
        {
            Directory.CreateDirectory(_indexPath);
            // Record the source-DB stamp BEFORE any segment or progress state exists, so
            // an interrupted build is attributable to its DB on the next start (see the
            // provenance check in EnsureIndexing). A resume session rewrites the same
            // stamp — EnsureIndexing already validated it matches the current DB.
            try
            {
                File.WriteAllText(Path.Combine(_indexPath, "fts.src"),
                    Common.DbContentStamp.Compute(_dbPath!, FtsVersion));
            }
            catch (Exception ex) { logger.LogError(ex, "FTS fts.src write failed"); }
            var index = GetIndex();

            // Resume from the progress file if a prior build was interrupted (no DB scan on resume).
            index.GetResumeState(out _, out long cachedTotal, out long cachedOffset);
            long total = cachedTotal > 0 ? cachedTotal : SafeCountLines(index);
            long resumeOffset = cachedOffset;
            if (IsCurrentBuild(generation)) _total = (int)Math.Min(total, int.MaxValue);

            // Existing segments (resume) are already searchable.
            if (IsCurrentBuild(generation) && SegmentsExist()) _isReady = true;

            logger.LogInformation("FTS build starting — index={Index} total≈{Total}", _indexPath, total);

            // A fast service restart (dev respawn) can begin the new build before the
            // previous process has fully exited and released the OS write.lock — the
            // acquisition then throws IndexWriteLockException. The stale lock clears the
            // moment the old process dies (the OS releases it), so wait-and-retry a few
            // times instead of abandoning the build. This is the ONLY place we can guard
            // it without changing shared FtsLib (used by the hosted net48 path too).
            bool ok = false;
            const int lockRetries = 8;
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    ok = index.BuildIndex(
                        limit: 0,
                        onProgress: sessionCount =>
                        {
                            // A reset that gave up waiting for this build leaves it running;
                            // its progress must not be written over the new build's state.
                            if (!IsCurrentBuild(generation)) return;
                            if (total > 0 && sessionCount % 5000 == 0)
                            {
                                long indexed = resumeOffset + sessionCount;
                                _pct = Math.Min(99.9, indexed * 100.0 / total);
                                _processed = (int)Math.Min(indexed, int.MaxValue);
                                if (!_isReady && SegmentsExist()) _isReady = true; // first segment → searchable
                                NotifyProgress();
                            }
                        },
                        onFlush: () =>
                        {
                            if (!IsCurrentBuild(generation)) return; // see onProgress
                            if (!_isReady && SegmentsExist()) _isReady = true;
                            NotifyProgress();
                        },
                        totalLines: total,
                        resumeOffset: resumeOffset,
                        forceMergeOnComplete: true,
                        ct: ct,
                        // The FTS build is the long, heavy one; in background processing
                        // mode (lowest CPU + very-low I/O priority) it stops contending
                        // with searches and with the catalog/file-search rebuilds that an
                        // app reset starts alongside it — they all run in parallel and the
                        // FTS build takes whatever the machine has left over.
                        backgroundPriority: true);
                    break;
                }
                catch (IndexWriteLockException) when (attempt < lockRetries)
                {
                    logger.LogInformation(
                        "FTS index write-lock held (attempt {Attempt}/{Max}) — a prior instance is still exiting; retrying",
                        attempt + 1, lockRetries);
                    Task.Delay(500, ct).GetAwaiter().GetResult();
                }
            }

            if (ok)
            {
                // A superseded build must not claim completion: writing fts.ver here would
                // stamp the NEW build's directory as a finished index of the old build's
                // content, and EnsureIndexing would then skip the rebuild entirely on the
                // next start. Its segments are already gone (the reset deleted them), so
                // there is nothing of this build left to record.
                if (!IsCurrentBuild(generation))
                {
                    logger.LogInformation(
                        "FTS build finished after a reset superseded it — discarding its result");
                    return;
                }

                // Record the source-DB change stamp so any later DB change (switch,
                // edit, or replacement) invalidates this index on the next start.
                File.WriteAllText(Path.Combine(_indexPath, "fts.ver"),
                    Common.DbContentStamp.Compute(_dbPath!, FtsVersion));
                try { index.DeleteBuildProgressFile(); } catch { }
                _pct = 100.0;
                _isReady = true;
                logger.LogInformation("FTS build complete — {Index}", _indexPath);
            }
            else if (IsCurrentBuild(generation) && !_isReady)
            {
                // BuildIndex returned false with nothing searchable: an empty corpus, or a
                // session that only replayed WAL recovery. No stamp is written and the latch
                // stays armed, so nothing here will retry — terminal, not "still working".
                logger.LogInformation(
                    "FTS build processed no lines and produced no searchable index — reporting terminal");
                _buildFailed = true;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("FTS build cancelled (reset)");
        }
        catch (IndexWriteLockException ex)
        {
            // Retries exhausted — the write lock is still held (unusually long-lived prior
            // instance, or a genuinely concurrent writer). Re-arm the build latch so a later
            // EnsureIndexing() (search / status poll / next start) can resume, rather than
            // abandoning the index permanently and leaving every restart to re-resume a
            // never-finishing build.
            logger.LogWarning(ex, "FTS index write-lock still held after retries — will resume later");
            // Only if this build still owns the state: re-arming a latch that a concurrent
            // reset is holding on purpose would let a search's EnsureIndexing start a build
            // into the directory that reset is still deleting.
            lock (_lock) { if (IsCurrentBuild(generation)) _buildStarted = false; }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTS index build failed");
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

        // fts.ver = completed build; fts.src = in-progress build (written at build
        // start). Either one is a valid provenance record to invalidate — a DB swapped
        // MID-BUILD must cancel and restart, not keep appending the new DB's lines onto
        // the old DB's segments. Neither present → nothing built yet to invalidate.
        string? builtFrom = ReadStamp(Path.Combine(_indexPath, "fts.ver"))
                         ?? ReadStamp(Path.Combine(_indexPath, "fts.src"));
        if (builtFrom == null) return false;
        // An older-format stamp is not comparable — reading it as "changed" here would
        // wipe and rebuild a live index. EnsureIndexing already re-stamps on the next build.
        if (Common.DbContentStamp.IsLegacy(builtFrom)) return false;

        string current;
        try { current = Common.DbContentStamp.Compute(_dbPath!, FtsVersion); } catch { return false; }
        if (string.Equals(builtFrom, current, StringComparison.OrdinalIgnoreCase))
            return false; // unchanged

        logger.LogInformation("FTS: seforim DB changed while running — wiping and rebuilding");
        ResetIndex(); // cancels the in-flight build, wipes, re-arms, and rebuilds
        return true;
    }

    /// <summary>Wipe the FTS index and rebuild it from scratch (dev "reset search index").
    /// Runs on a background thread and returns immediately — progress is streamed.</summary>
    public void ResetIndex()
    {
        if (_external) return; // an externally-supplied index (FTS_INDEX_PATH) isn't ours to wipe

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
                logger.LogInformation("FTS reset already in progress — ignoring the duplicate request");
                return;
            }
            _resetInFlight = true;
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
        }
        NotifyProgress();

        _ = Task.Run(() =>
        {
            // Fire-and-forget: without this catch a failure is an unobserved task exception and
            // the reset half-completes (index dropped, nothing rebuilding) with nothing logged.
            try { DoReset(); }
            catch (Exception ex) { logger.LogError(ex, "FTS index reset failed"); }
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
                    logger.LogWarning(
                        "FTS reset: the cancelled build has not unwound after 30s (likely a long merge) — "
                        + "still waiting; the index directory cannot be deleted while it writes");
                // Unconditional: returns at once when the wait above already completed, and
                // surfaces a build that ended in a fault rather than by cancellation.
                try { build.Wait(); }
                catch (Exception ex) { logger.LogInformation(ex, "FTS reset: prior build ended with an exception"); }
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
                logger.LogWarning("FTS reset: {Count} search session(s) still unwinding after 5s — deleting anyway",
                    _sessions.Count);
            _sessions.Clear();

            // 3) Detach the in-memory state, then delete the directory OUTSIDE the lock. The
            //    recursive delete of a multi-gigabyte index takes seconds, and holding _lock
            //    through it (as this used to) blocked every status stream, Status() call, and
            //    search for the whole delete — they all enter EnsureIndexing, which takes the
            //    lock. Nothing can repopulate _index while the delete runs: searches bail on
            //    !_isReady before ever calling GetIndex, and EnsureIndexing is latched out by
            //    the _buildStarted ResetIndex set.
            lock (_lock)
            {
                _index = null; // drop the SegmentStore so it holds no file references
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
            catch (Exception ex) { logger.LogError(ex, "FTS reset: could not delete {Index}", _indexPath); }
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
            logger.LogInformation("FTS index reset — rebuilding from scratch");
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
        logger.LogInformation("FTS build stopped for graceful shutdown");
    }

    private static long SafeCountLines(SeforimIndex idx)
    {
        try { return idx.CountLines(); } catch { return 0; }
    }

    public FtsIndexStatus Status()
    {
        EnsureIndexing();
        return Snapshot();
    }

    public FtsSearchResult Search(
        string query, int cap, int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv,
        bool expandRelated = false)
    {
        EnsureIndexing();
        if (expandRelated) query = expansion.RewriteQuery(query);
        var result = new FtsSearchResult();
        if (!HasDb || !_isReady)
        {
            result.Ready = false;      // still building (or no DB) — frontend shows the indexing overlay
            return result;
        }
        result.Ready = true;
        if (string.IsNullOrWhiteSpace(query)) return result;
        // Full-text search results are NEVER capped. cap <= 0 means unlimited; a
        // positive cap is only honoured if a caller ever explicitly asks for one.

        try
        {
            var index = GetIndex();
            // Buffer in batches and reuse BuildBatch so the one-shot path gets the same
            // parallel snippeting, short-snippet embellishment, and enrichment as streaming.
            const int SnippetBatch = 256;
            var batch = new List<SearchResult>(SnippetBatch);
            bool capped = false;
            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv))
            {
                batch.Add(hit);
                if (batch.Count >= SnippetBatch)
                {
                    result.Results.AddRange(BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, default));
                    batch.Clear();
                    if (cap > 0 && result.Results.Count >= cap) { capped = true; break; }
                }
            }
            if (!capped && batch.Count > 0)
                result.Results.AddRange(BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, default));
            if (cap > 0 && result.Results.Count > cap)
                result.Results.RemoveRange(cap, result.Results.Count - cap);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTS search failed");
            result.Error = ex.Message;
        }
        return result;
    }

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
    /// an emit that writes one frame per chunk on the client's pipe connection.</summary>
    public async Task StreamSearch(
        string query, int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv,
        Func<FtsStreamChunk, Task> emit, CancellationToken clientCt, bool expandRelated = false)
    {
        EnsureIndexing();
        if (expandRelated) query = expansion.RewriteQuery(query);
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
                await RunSearchCoreAsync(query, maxWordDistance, requireOrdered, contextWords, expandKetiv,
                    linked.Token, built => emit(new FtsStreamChunk { Results = built }));
                await emit(new FtsStreamChunk { Done = true });
            }
            catch (OperationCanceledException) { /* superseded, reset, or client left */ }
            catch (IOException) { /* client disconnected mid-stream — that IS the cancel signal */ }
            catch (Exception ex)
            {
                logger.LogError(ex, "FTS streaming search failed");
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
    /// enriched batch is handed to <paramref name="onBatch"/> in order.
    ///
    /// NOTE: a whole-set parallel fetch (SeforimIndex.SearchParallel) is ~2x faster at the
    /// fetch step in isolation (see FetchBenchTest), but its up-front barrier delays the
    /// first paint — streaming + parallel snippet wins on first-result latency, which is
    /// what the search feels like. SearchParallel stays available as a bulk-fetch API.</summary>
    private async Task RunSearchCoreAsync(
        string query, int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv,
        CancellationToken ct, Func<List<FtsHit>, Task> onBatch)
    {
        var index = GetIndex();
        const int SnippetBatch = 256;
        var batch = new List<SearchResult>(SnippetBatch);
        foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: ct))
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(hit);
            if (batch.Count >= SnippetBatch)
            {
                var built = BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, ct);
                if (built.Count > 0) await onBatch(built);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            var built = BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, ct);
            if (built.Count > 0) await onBatch(built);
        }
    }

    /// <summary>Snippet-generate a batch across all cores (thread-safe: FtsLib's
    /// GenerateSnippet uses a [ThreadStatic] builder), preserving order, and enrich the
    /// passing hits (bookId + toc path) server-side.
    ///
    /// Short snippets — fewer visible words than the requested context (contextWords per
    /// side) because the matched line is itself short — are then EMBELLISHED with their
    /// surrounding lines: the whole batch's short lines get their neighbors in one batched
    /// query, and only those hits are re-rendered. Lines that already fill the context are
    /// untouched, so a batch with no short lines pays nothing extra.</summary>
    private List<FtsHit> BuildBatch(SeforimIndex index, IReadOnlyList<SearchResult> results,
        bool requireOrdered, int contextWords, int maxWordDistance, CancellationToken ct)
    {
        var built     = new FtsHit?[results.Count];
        var wordCount = new int[results.Count];   // window word count of each passing hit (0 = didn't pass)
        Parallel.For(0, results.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            if (TryBuildHit(index, results[i], requireOrdered, contextWords, maxWordDistance,
                            out var h, out int words))
            {
                built[i]     = h;
                wordCount[i] = words;
            }
        });

        EmbellishShortSnippets(index, results, built, wordCount, requireOrdered, contextWords, ct);

        var passing = new List<FtsHit>(results.Count);
        for (int i = 0; i < built.Length; i++)
            if (built[i] is { } hit) passing.Add(hit);
        EnrichHits(passing);
        return passing;
    }

    // How many lines of context to pull on each side when embellishing (same book). Two
    // lines is enough to fill the snippet's visual space (~4 clamped lines) and reach about
    // the requested per-side word context for prose; benchmarking showed radius 2 costs
    // roughly half of radius 3 (it re-tokenizes fewer neighbor lines). See EmbellishBenchTest.
    private const int NeighborLineRadius = 2;

    /// <summary>Re-render the batch's short snippets — those whose window spans fewer words
    /// than the requested context (<paramref name="contextWords"/>), i.e. the matched line
    /// was too short to fill it — over their surrounding lines. One batched neighbor fetch
    /// for the whole batch; re-render runs across cores. No-op — and no DB hit — when nothing
    /// is short (a broad query's typical batch has only ~1 in 6 short lines).</summary>
    private static void EmbellishShortSnippets(
        SeforimIndex index, IReadOnlyList<SearchResult> results,
        FtsHit?[] built, int[] wordCount, bool requireOrdered, int contextWords,
        CancellationToken ct)
    {
        // "Smaller than the setting": a snippet whose window holds fewer words than the
        // context the user asked for on one side didn't have room to show full context, so
        // its matched line is short enough to enrich with neighbors.
        int target = contextWords;

        List<int>? shortIdx = null;
        List<int>? shortIds = null;
        for (int i = 0; i < built.Length; i++)
        {
            if (built[i] == null || wordCount[i] >= target) continue;
            (shortIdx ??= new List<int>()).Add(i);
            (shortIds ??= new List<int>()).Add(results[i].LineId);
        }
        if (shortIds == null) return; // nothing short — zero extra cost

        var neighbors = index.FetchNeighborContext(shortIds, NeighborLineRadius);
        if (neighbors.Count == 0) return;

        Parallel.ForEach(shortIdx!, new ParallelOptions { CancellationToken = ct }, i =>
        {
            if (!neighbors.TryGetValue(results[i].LineId, out var ctx)) return;
            var re = index.GenerateSnippetWithNeighbors(
                results[i], ctx.Prev, ctx.Next, requireOrdered, contextWords);
            // Keep the original word-distance/score (relevance keys computed on the line
            // itself); only swap in the richer snippet HTML. Guard against a failed
            // re-render (shouldn't happen — same terms) by keeping the original.
            if (re.IsMatch && !string.IsNullOrEmpty(re.Html) && built[i] is { } h)
                h.Snippet = re.Html;
        });
    }

    /// <summary>
    /// Fill each hit's <c>BookId</c> and <c>TocText</c> from the seforim DB so results come
    /// out of the service COMPLETE — no consumer has to make a second round-trip to resolve
    /// them. This is the same lookup the frontend used to do per batch (getTocPathsForLines +
    /// a getBookIdsForLines fallback for lines with no toc entry, e.g. custom books), now
    /// owned by the service. Batched (one query per flush), so it adds negligible cost and
    /// removes an entire client-side enrichment lane. SeforimDbService opens a fresh
    /// read-only connection per call, so this is safe to call from the search task.
    /// </summary>
    private void EnrichHits(List<FtsHit> hits)
    {
        if (hits.Count == 0) return;

        var lineIds = new List<int>(hits.Count);
        foreach (var h in hits) lineIds.Add(h.LineId);

        var toc = seforim.GetTocPathsForLines(lineIds);
        if (toc.Count > 0)
        {
            var map = new Dictionary<int, TocPathRow>(toc.Count);
            foreach (var t in toc) map[t.LineId] = t;
            foreach (var h in hits)
                if (map.TryGetValue(h.LineId, out var t)) { h.BookId = t.BookId; h.TocText = t.TocPath; }
        }

        // Fallback: lines with no line_toc entry (custom books / negative IDs) get no toc row,
        // so resolve their bookId directly from the line table.
        List<int>? missing = null;
        foreach (var h in hits)
            if (h.BookId == 0) (missing ??= new List<int>()).Add(h.LineId);
        if (missing != null)
        {
            var books = seforim.GetBookIdsForLines(missing);
            if (books.Count > 0)
            {
                var bmap = new Dictionary<int, int>(books.Count);
                foreach (var b in books) bmap[b.LineId] = b.BookId;
                foreach (var h in hits)
                    if (h.BookId == 0 && bmap.TryGetValue(h.LineId, out var bid)) h.BookId = bid;
            }
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

    private FtsIndexStatus Snapshot() => new()
    {
        IsReady = _isReady,
        IsIndexing = _isIndexing,
        Percentage = Math.Round(_pct, 1),
        ProcessedChunks = _processed,
        TotalChunks = _total,
        DbMissing = !HasDb,
        Failed = _buildFailed,
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
            logger.LogDebug(ex, "FTS progress stream ended on an unexpected error");
        }
        finally
        {
            lock (_progressSubs) _progressSubs.Remove(ch);
        }
    }

    /// <summary>Generate the snippet, apply the match / word-distance filter, and build the
    /// frontend hit. Returns false for hits that don't pass. Shared by one-shot + streaming.</summary>
    private static bool TryBuildHit(SeforimIndex index, SearchResult hit,
        bool requireOrdered, int contextWords, int maxWordDistance, out FtsHit? built,
        out int windowWordCount)
    {
        built = null;
        windowWordCount = 0;
        var snippet = index.GenerateSnippet(hit, requireOrdered, contextWords);
        if (!snippet.IsMatch) return false;
        if (snippet.WordDistance > maxWordDistance) return false;
        windowWordCount = snippet.WindowWordCount;

        var matchedTerms = new List<string>();
        foreach (var group in hit.MatchedGroups)
            foreach (var term in group)
                if (!matchedTerms.Contains(term)) matchedTerms.Add(term);

        built = new FtsHit
        {
            LineId = hit.LineId,
            BookId = 0,
            BookTitle = hit.BookTitle ?? "",
            TocText = "",
            Score = snippet.Score,
            WordDistance = snippet.WordDistance,
            Snippet = snippet.Html ?? "",
            MatchedTerms = matchedTerms,
        };
        return true;
    }
}
