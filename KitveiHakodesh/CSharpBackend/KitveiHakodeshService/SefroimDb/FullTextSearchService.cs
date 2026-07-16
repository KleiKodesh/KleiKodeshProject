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

            // fts.ver records WHICH seforim DB the index was built from. If the user
            // switched databases (settings page / wizard → registry → service restart),
            // the old index answers for the wrong content — wipe and rebuild.
            string verFile = Path.Combine(_indexPath, "fts.ver");
            if (File.Exists(verFile))
            {
                string builtFrom = "";
                try { builtFrom = File.ReadAllText(verFile).Trim(); } catch { }
                if (!string.Equals(builtFrom, _dbPath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation(
                        "FTS index was built from a different DB ({Old}) — wiping for rebuild against {New}",
                        builtFrom, _dbPath);
                    try { Directory.Delete(_indexPath, recursive: true); }
                    catch (Exception ex) { logger.LogError(ex, "FTS stale-index wipe failed"); }
                }
            }

            // A completed service-owned build → ready without rebuilding.
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
                    }
                },
                onFlush: () => { if (!_isReady && SegmentsExist()) _isReady = true; },
                totalLines: total,
                resumeOffset: resumeOffset,
                forceMergeOnComplete: true,
                ct: ct);

            if (ok)
            {
                // Record the source DB so a later DB switch invalidates this index.
                File.WriteAllText(Path.Combine(_indexPath, "fts.ver"), _dbPath ?? "");
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
        }
    }

    /// <summary>Wipe the FTS index and rebuild it from scratch (dev "reset search index").
    /// Runs on a background thread and returns immediately — progress is via Status().</summary>
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
        return new FtsIndexStatus
        {
            IsReady = _isReady,
            IsIndexing = _isIndexing,
            Percentage = Math.Round(_pct, 1),
            ProcessedChunks = _processed,
            TotalChunks = _total,
        };
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

    // ── Streaming search (start + poll) ─────────────────────────────────────────
    // Mirrors the hosted C# FtsSearchExecutor: run the search on a background thread
    // and let the caller drain results incrementally, so the first hits are available
    // in milliseconds instead of after every snippet is generated. Results are NEVER
    // capped — streaming is exactly what makes an uncapped search feel instant.

    private sealed class SearchSession
    {
        public readonly List<FtsHit> Hits = new();
        public volatile bool Done;
        public string? Error;
        public readonly CancellationTokenSource Cts = new();
        public DateTime LastAccess = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, SearchSession> _sessions = new();
    private int _searchCounter;
    private string? _currentSearchId;   // most-recent streaming search; the next one supersedes it
    private const int SessionTtlMinutes = 5;

    public FtsSearchStartResult StartSearch(
        string query, int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv)
    {
        EnsureIndexing();
        if (!HasDb || !_isReady) return new FtsSearchStartResult { Ready = false };
        if (string.IsNullOrWhiteSpace(query)) return new FtsSearchStartResult { Ready = true, SearchId = "" };

        PruneStaleSessions();
        var session = new SearchSession();
        string id = "s" + Interlocked.Increment(ref _searchCounter);
        _sessions[id] = session;

        // A new search SUPERSEDES the previous in-flight one — cancel it so the service
        // never keeps burning cores generating snippets for results the caller has already
        // moved on from. This makes "latest search wins" a SERVICE guarantee (mirroring the
        // hosted FtsSearchExecutor), so every consumer gets it for free with no client-side
        // bookkeeping — a client only needs to StartSearch again. The atomic swap makes
        // rapid back-to-back searches race-safe (exactly one predecessor cancelled each time).
        var prevId = Interlocked.Exchange(ref _currentSearchId, id);
        if (prevId != null && _sessions.TryRemove(prevId, out var prev)) prev.Cts.Cancel();

        _ = Task.Run(() => RunSearch(session, query, maxWordDistance, requireOrdered, contextWords, expandKetiv));
        return new FtsSearchStartResult { Ready = true, SearchId = id };
    }

    private void RunSearch(SearchSession session, string query,
        int maxWordDistance, bool requireOrdered, int contextWords, bool expandKetiv)
    {
        try
        {
            var index = GetIndex();
            var ct = session.Cts.Token;
            // Content fetch STREAMS from one SQLite reader (sequential I/O, cache-friendly
            // even cold, and it overlaps naturally with the snippet stage below); snippet
            // generation is the dominant CPU cost and is done per-batch across all cores.
            // Pull hits in ordered batches and snippet each batch in parallel, appending
            // passing hits in order — this preserves result order AND streaming cadence, so
            // the first hits paint in tens of milliseconds instead of after the whole set.
            //
            // NOTE: a whole-set parallel fetch (SeforimIndex.SearchParallel) is ~2x faster
            // at the fetch step in isolation (see FetchBenchTest), but it did NOT help here:
            // for large result sets the service wall-clock is dominated by serializing and
            // transporting tens of thousands of hits over the pipe (~85%), not by fetch, and
            // its up-front barrier delayed the first paint. Streaming + parallel snippet wins
            // on first-result latency, which is what the search feels like. SearchParallel
            // stays available as a bulk-fetch API for callers that consume the whole set.
            const int SnippetBatch = 256;
            var batch = new List<SearchResult>(SnippetBatch);
            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                batch.Add(hit);
                if (batch.Count >= SnippetBatch)
                {
                    FlushSnippetRange(session, index, batch, 0, batch.Count, requireOrdered, contextWords, maxWordDistance, ct);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                FlushSnippetRange(session, index, batch, 0, batch.Count, requireOrdered, contextWords, maxWordDistance, ct);
        }
        catch (OperationCanceledException) { /* cancelled or superseded */ }
        catch (Exception ex)
        {
            session.Error = ex.Message;
            logger.LogError(ex, "FTS streaming search failed");
        }
        finally { session.Done = true; }
    }

    /// <summary>Snippet-generate results[start..end) across all cores (thread-safe: FtsLib's
    /// GenerateSnippet uses a [ThreadStatic] builder), preserving order, enrich the passing
    /// hits (bookId + toc path) server-side, then append them to the session buffer.</summary>
    private void FlushSnippetRange(SearchSession session, SeforimIndex index,
        IReadOnlyList<SearchResult> results, int start, int end,
        bool requireOrdered, int contextWords, int maxWordDistance, CancellationToken ct)
    {
        int count = end - start;
        var built = new FtsHit?[count];
        Parallel.For(0, count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            if (TryBuildHit(index, results[start + i], requireOrdered, contextWords, maxWordDistance, out var h))
                built[i] = h;
        });

        // Collect passing hits in order, enrich them server-side, then publish the batch.
        var passing = new List<FtsHit>(count);
        for (int i = 0; i < built.Length; i++)
            if (built[i] is { } hit) passing.Add(hit);
        EnrichHits(passing);

        lock (session.Hits) session.Hits.AddRange(passing);
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

    // Long-poll: the request is held open until a batch is ready, so results arrive
    // as fast as they're generated (like the hosted push) with few round-trips. The
    // pipe server handles each request on its own task, so blocking here is safe.
    private const int PollMaxWaitMs = 8000;   // cap when the search produces nothing new
    private const int PollBatchWindowMs = 120; // coalesce window once results start flowing
    private const int PollBatchTarget = 500;   // …or return early once this many are ready
    private const int PollTickMs = 15;

    public async Task<FtsSearchPollResult> PollSearch(string searchId, int offset, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(searchId) || !_sessions.TryGetValue(searchId, out var session))
            return new FtsSearchPollResult { Done = true, Error = "unknown or expired search" };

        session.LastAccess = DateTime.UtcNow;
        if (offset < 0) offset = 0;

        // 1) Hold until there's something new beyond offset (or the search finishes).
        var deadline = DateTime.UtcNow.AddMilliseconds(PollMaxWaitMs);
        while (CurrentCount(session) <= offset && !session.Done && DateTime.UtcNow < deadline)
            await Task.Delay(PollTickMs, ct);

        // 2) Coalesce a sizeable batch so we return few, large responses (low churn).
        //    Skip it for the very first batch (offset 0) so the first results paint
        //    the instant they exist — matching the hosted stream's flush-at-first-hit.
        if (offset > 0 && !session.Done && CurrentCount(session) - offset < PollBatchTarget)
        {
            var batchDeadline = DateTime.UtcNow.AddMilliseconds(PollBatchWindowMs);
            while (!session.Done
                   && CurrentCount(session) - offset < PollBatchTarget
                   && DateTime.UtcNow < batchDeadline)
                await Task.Delay(PollTickMs, ct);
        }

        var result = new FtsSearchPollResult { Error = session.Error };
        int total;
        lock (session.Hits)
        {
            total = session.Hits.Count;
            if (offset < total)
                result.Results.AddRange(session.Hits.GetRange(offset, total - offset));
        }
        result.Done = session.Done;
        // Free the session once a completed search has been fully drained.
        if (session.Done && offset + result.Results.Count >= total)
            _sessions.TryRemove(searchId, out _);
        return result;
    }

    private static int CurrentCount(SearchSession s) { lock (s.Hits) return s.Hits.Count; }

    public void CancelSearch(string searchId)
    {
        if (string.IsNullOrEmpty(searchId)) return;
        // If this was the current search, it's no longer current (clear only if unchanged).
        Interlocked.CompareExchange(ref _currentSearchId, null, searchId);
        if (_sessions.TryRemove(searchId, out var session))
            session.Cts.Cancel();
    }

    private void PruneStaleSessions()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-SessionTtlMinutes);
        foreach (var kv in _sessions)
            if (kv.Value.LastAccess < cutoff && _sessions.TryRemove(kv.Key, out var s))
                s.Cts.Cancel();
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
