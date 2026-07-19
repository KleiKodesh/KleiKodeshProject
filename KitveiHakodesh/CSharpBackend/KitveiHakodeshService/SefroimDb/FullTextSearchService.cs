using System.Collections.Concurrent;
using FtsLib.SeforimDb;

namespace KitveiHakodeshService.SefroimDb;

/// <summary>
/// Full-text search over the custom FtsLib index. The service OWNS the index and
/// builds it in the background from the seforim DB — the whole point of a service:
/// indexing runs while the user works, and the index becomes searchable segment by
/// segment as it's written (partial results before the build finishes).
///
/// Index location: FTS_INDEX_PATH if set (used as-is), else "FtsIndex" next to the
/// service binary (AppContext.BaseDirectory) — so deleting the service folder deletes
/// the index too. A completed build writes fts.ver so later runs skip rebuilding.
/// Search returns one capped batch (the hosted C# path streams; dev doesn't need to).
/// </summary>
public sealed class FullTextSearchService(ILogger<FullTextSearchService> logger, SeforimDbService seforim)
{
    /// <summary>Prefix for the fts.ver stamp. Bump when the FtsLib on-disk segment
    /// format or the indexing pipeline changes, so existing indexes rebuild even when
    /// the source DB is unchanged.</summary>
    private const string FtsVersion = "fts1";

    private readonly string? _dbPath = SeforimDbLocator.Resolve();
    private readonly string _indexPath = ResolveIndexPath();
    // True when FTS_INDEX_PATH was supplied — use that index as-is, never build into it.
    private readonly bool _external = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FTS_INDEX_PATH"));

    private readonly object _lock = new();
    private SeforimIndex? _index;
    private Task? _buildTask;
    private CancellationTokenSource? _buildCts;   // cancels the background build (for reset)

    private volatile bool _isReady;
    private volatile bool _isIndexing;
    private volatile bool _buildStarted;   // latch: build at most once per process (no colliding retries)
    private double _pct;   // progress % — plain double (atomic enough for status reads)
    private volatile int _processed;
    private volatile int _total;

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
        if (!HasDb || _isReady) return;
        lock (_lock)
        {
            if (_isReady) return;

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

            // fts.ver records the change-STAMP of the seforim DB the index was built
            // from (Common.DbChangeStamp — path + content-free file metadata). If the
            // user switched databases OR the DB file's content changed (same path,
            // edited/replaced/updated), the stored stamp no longer matches the current
            // one and the old index answers for the wrong content — wipe and rebuild.
            string verFile = Path.Combine(_indexPath, "fts.ver");
            string currentStamp = Common.DbChangeStamp.Compute(_dbPath!, FtsVersion);
            if (File.Exists(verFile))
            {
                string builtFrom = "";
                try { builtFrom = File.ReadAllText(verFile).Trim(); } catch { }
                if (!string.Equals(builtFrom, currentStamp, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("FTS index is stale (seforim DB changed) — wiping for rebuild");
                    try { Directory.Delete(_indexPath, recursive: true); }
                    catch (Exception ex) { logger.LogError(ex, "FTS stale-index wipe failed"); }
                }
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
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;
            _buildTask = Task.Run(() => RunBuild(token));
        }
    }

    private void RunBuild(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_indexPath);
            var index = GetIndex();

            // Resume from the progress file if a prior build was interrupted (no DB scan on resume).
            index.GetResumeState(out _, out long cachedTotal, out long cachedOffset);
            long total = cachedTotal > 0 ? cachedTotal : SafeCountLines(index);
            long resumeOffset = cachedOffset;
            _total = (int)Math.Min(total, int.MaxValue);

            // Existing segments (resume) are already searchable.
            if (SegmentsExist()) _isReady = true;

            logger.LogInformation("FTS build starting — index={Index} total≈{Total}", _indexPath, total);

            bool ok = index.BuildIndex(
                limit: 0,
                onProgress: sessionCount =>
                {
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
                    if (!_isReady && SegmentsExist()) _isReady = true;
                    NotifyProgress();
                },
                totalLines: total,
                resumeOffset: resumeOffset,
                forceMergeOnComplete: true,
                ct: ct);

            if (ok)
            {
                // Record the source-DB change stamp so any later DB change (switch,
                // edit, or replacement) invalidates this index on the next start.
                File.WriteAllText(Path.Combine(_indexPath, "fts.ver"),
                    Common.DbChangeStamp.Compute(_dbPath!, FtsVersion));
                try { index.DeleteBuildProgressFile(); } catch { }
                _pct = 100.0;
                _isReady = true;
                logger.LogInformation("FTS build complete — {Index}", _indexPath);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("FTS build cancelled (reset)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTS index build failed");
        }
        finally
        {
            _isIndexing = false;
            NotifyProgress();   // terminal (or failed) state — wake subscribers so streams close
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

        string verFile = Path.Combine(_indexPath, "fts.ver");
        if (!File.Exists(verFile)) return false; // no completed build to invalidate yet

        string builtFrom = "";
        try { builtFrom = File.ReadAllText(verFile).Trim(); } catch { return false; }
        string current;
        try { current = Common.DbChangeStamp.Compute(_dbPath!, FtsVersion); } catch { return false; }
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
        _ = Task.Run(DoReset);
    }

    private void DoReset()
    {
        // 1) Stop the running build (its `using IndexWriteLock` releases on return) and wait.
        Task? build;
        lock (_lock)
        {
            _buildCts?.Cancel();
            build = _buildTask;
        }
        try { build?.Wait(TimeSpan.FromSeconds(30)); } catch { /* cancellation / aggregate */ }

        // 2) Cancel in-flight search sessions so nothing keeps reading the old segments.
        foreach (var kv in _sessions) kv.Value.Cts.Cancel();
        _sessions.Clear();

        // 3) Delete the whole index directory, then reset state for a fresh build.
        lock (_lock)
        {
            _index = null; // drop the SegmentStore so it holds no file references
            try
            {
                if (Directory.Exists(_indexPath))
                    Directory.Delete(_indexPath, recursive: true);
            }
            catch (Exception ex) { logger.LogError(ex, "FTS reset: could not delete {Index}", _indexPath); }

            _isReady = false;
            _isIndexing = false;
            _buildStarted = false;
            _pct = 0; _processed = 0; _total = 0;
            _buildTask = null;
            _buildCts = null;
        }

        logger.LogInformation("FTS index reset — rebuilding from scratch");
        EnsureIndexing();
        NotifyProgress();
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
            _buildCts?.Cancel();
            build = _buildTask;
        }
        foreach (var kv in _sessions) kv.Value.Cts.Cancel();   // stop live searches too
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
        string query, int cap, int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv)
    {
        EnsureIndexing();
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
            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv))
            {
                if (TryBuildHit(index, hit, requireOrdered, contextWords, maxWordDistance, out var built))
                    result.Results.Add(built!);
                if (cap > 0 && result.Results.Count >= cap) break;
            }
            EnrichHits(result.Results);   // fill bookId + toc path server-side (self-contained results)
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
        Func<FtsStreamChunk, Task> emit, CancellationToken clientCt)
    {
        EnsureIndexing();
        if (!HasDb || !_isReady) { await emit(new FtsStreamChunk { Ready = false, Done = true }); return; }
        if (string.IsNullOrWhiteSpace(query)) { await emit(new FtsStreamChunk { Done = true }); return; }

        var session = new SearchSession();
        string id = "s" + Interlocked.Increment(ref _searchCounter);
        _sessions[id] = session;

        // A new search SUPERSEDES the previous in-flight one — cancel it so the service
        // never keeps burning cores generating snippets for results the caller has already
        // moved on from. "Latest search wins" is a SERVICE guarantee (mirroring the hosted
        // FtsSearchExecutor): a client only ever starts a new stream, nothing else. The
        // atomic swap makes rapid back-to-back searches race-safe.
        var prevId = Interlocked.Exchange(ref _currentSearchId, id);
        if (prevId != null && _sessions.TryRemove(prevId, out var prev)) prev.Cts.Cancel();

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
        finally
        {
            _sessions.TryRemove(id, out _);
            Interlocked.CompareExchange(ref _currentSearchId, null, id);
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
    /// passing hits (bookId + toc path) server-side.</summary>
    private List<FtsHit> BuildBatch(SeforimIndex index, IReadOnlyList<SearchResult> results,
        bool requireOrdered, int contextWords, int maxWordDistance, CancellationToken ct)
    {
        var built = new FtsHit?[results.Count];
        Parallel.For(0, results.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            if (TryBuildHit(index, results[i], requireOrdered, contextWords, maxWordDistance, out var h))
                built[i] = h;
        });

        var passing = new List<FtsHit>(results.Count);
        for (int i = 0; i < built.Length; i++)
            if (built[i] is { } hit) passing.Add(hit);
        EnrichHits(passing);
        return passing;
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
            while (!((last.IsReady && !last.IsIndexing) || last.DbMissing))
            {
                await ch.Reader.ReadAsync(ct);
                last = Snapshot();
                await emit(last);
            }
        }
        catch (OperationCanceledException) { /* client left */ }
        catch (IOException) { /* client disconnected mid-stream */ }
        finally
        {
            lock (_progressSubs) _progressSubs.Remove(ch);
        }
    }

    /// <summary>Generate the snippet, apply the match / word-distance filter, and build the
    /// frontend hit. Returns false for hits that don't pass. Shared by one-shot + streaming.</summary>
    private static bool TryBuildHit(SeforimIndex index, SearchResult hit,
        bool requireOrdered, int contextWords, int maxWordDistance, out FtsHit? built)
    {
        built = null;
        var snippet = index.GenerateSnippet(hit, requireOrdered, contextWords);
        if (!snippet.IsMatch) return false;
        if (snippet.WordDistance > maxWordDistance) return false;

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
