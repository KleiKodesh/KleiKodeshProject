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
public sealed class FullTextSearchService(ILogger<FullTextSearchService> logger)
{
    private readonly string? _dbPath = Environment.GetEnvironmentVariable("DB_PATH");
    private readonly string _indexPath = ResolveIndexPath();
    // True when FTS_INDEX_PATH was supplied — use that index as-is, never build into it.
    private readonly bool _external = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FTS_INDEX_PATH"));

    private readonly object _lock = new();
    private SeforimIndex? _index;
    private Task? _buildTask;

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

            // A completed service-owned build → ready without rebuilding.
            if (File.Exists(Path.Combine(_indexPath, "fts.ver")) && SegmentsExist())
            {
                _isReady = true;
                _buildStarted = true;
                return;
            }

            _buildStarted = true;
            _isIndexing = true;
            _buildTask = Task.Run(RunBuild);
        }
    }

    private void RunBuild()
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
                ct: CancellationToken.None);

            if (ok)
            {
                File.WriteAllText(Path.Combine(_indexPath, "fts.ver"), "service");
                try { index.DeleteBuildProgressFile(); } catch { }
                _pct = 100.0;
                _isReady = true;
                logger.LogInformation("FTS build complete — {Index}", _indexPath);
            }
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
        if (cap <= 0) cap = 200;

        try
        {
            var index = GetIndex();
            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv))
            {
                var snippet = index.GenerateSnippet(hit, requireOrdered, contextWords);
                if (!snippet.IsMatch) continue;
                if (snippet.WordDistance > maxWordDistance) continue;

                var matchedTerms = new List<string>();
                foreach (var group in hit.MatchedGroups)
                    foreach (var term in group)
                        if (!matchedTerms.Contains(term)) matchedTerms.Add(term);

                result.Results.Add(new FtsHit
                {
                    LineId = hit.LineId,
                    BookId = 0,
                    BookTitle = hit.BookTitle ?? "",
                    TocText = "",
                    Score = snippet.Score,
                    Snippet = snippet.Html ?? "",
                    MatchedTerms = matchedTerms,
                });

                if (result.Results.Count >= cap) break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTS search failed");
            result.Error = ex.Message;
        }
        return result;
    }
}
