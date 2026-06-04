using SearchEngine.Indexing;
using SearchEngine.Search;
using SearchEngine.Snippets;
using Lucene.Net.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SearchEngine.SeforimDb
{
    /// <summary>
    /// Public API for full-text search over the seforim database, backed by Lucene.NET.
    ///
    /// Mirrors <c>FtsLib.SeforimDb.SeforimIndex</c> exactly — callers switch
    /// implementations by changing only the using statement and constructor name.
    ///
    /// Lifecycle
    /// ---------
    /// <see cref="SeforimIndex"/> is a long-lived object.  It owns a single
    /// <see cref="LuceneSearcher"/> that is opened once (lazily, on the first search
    /// or after the first commit) and kept alive until <see cref="Dispose"/> is called.
    /// This means:
    ///   • Repeated searches pay no reader-open overhead.
    ///   • Searches during a build see all rows committed so far — the searcher is
    ///     refreshed after every periodic commit inside <see cref="BuildIndex"/>.
    ///   • <see cref="BuildIndex"/> calls <see cref="LuceneIndexWriter.ForceMerge"/>
    ///     at the end of a completed (non-cancelled) build so the index is left in
    ///     optimal single-segment form for subsequent searches.
    ///
    /// Thread safety
    /// -------------
    /// <see cref="Search"/> and <see cref="GenerateSnippet"/> are safe to call from
    /// any thread concurrently.  <see cref="BuildIndex"/> must not be called
    /// concurrently with itself, but may run while searches are in progress.
    ///
    /// Query syntax (handled by <see cref="HebrewQueryBuilder"/>):
    ///   word        — literal AND term
    ///   word*       — prefix wildcard
    ///   *word       — suffix wildcard
    ///   *word*      — infix wildcard
    ///   wor?d       — optional char: the char before '?' is optional
    ///   word~N      — fuzzy match, edit distance N (1–2, via Lucene FuzzyQuery)
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///   %word       — grammar prefix expansion
    ///   word%       — grammar suffix expansion
    ///   %word%      — grammar prefix + suffix expansion
    ///   ~word       — כתיב חסר/מלא spelling variants
    ///
    /// Multiple tokens are AND-ed; '|'-separated tokens are OR-ed within one slot.
    ///
    /// Partial classes
    /// ---------------
    /// SeforimIndex.Snippets.cs    — snippet generation and word-distance computation
    /// SeforimIndex.QueryGroups.cs — matched-group building, token normalisation,
    ///                               query reconstruction, and progress-file I/O
    /// </summary>
    public sealed partial class SeforimIndex : IDisposable
    {
        private readonly string _indexPath;
        private readonly string _dbPath;

        // Detected once at construction time by inspecting field metadata.
        // true  → index stores bookId/bookTitle/tocPath; read them from Lucene.
        // false → minimal index; fetch bookId/bookTitle from the DB at search time.
        private readonly bool _metadataStored;

        // Long-lived searcher — null until the first commit exists.
        // Guarded by _searcherLock for open/close; volatile for lock-free reads.
        private volatile LuceneSearcher _searcher;
        private readonly object         _searcherLock = new object();
        private bool _disposed;

        /// <summary>Default number of words of context shown on each side of the match.</summary>
        public const int DefaultContextWords = 8;

        /// <summary>Kept for binary compatibility with FtsLib callers.</summary>
        public const int DefaultSnippetLength = DefaultContextWords;

        public SeforimIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath must not be empty.", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath must not be empty.", nameof(dbPath));

            _indexPath = indexPath;
            _dbPath    = dbPath;

            if (!Directory.Exists(_indexPath))
                Directory.CreateDirectory(_indexPath);

            // Detect index mode by inspecting stored field metadata — no documents read.
            // Must happen before EnsureSearcher so _metadataStored is set for all searches.
            _metadataStored = LuceneIndexWriter.DetectMetadataStored(_indexPath);
            Console.WriteLine("[SeforimIndex] Index mode: " +
                (_metadataStored ? "full (metadata stored in index)" : "minimal (metadata fetched from DB)"));

            // Open the searcher eagerly if a committed index already exists,
            // so the first search call pays no open cost.
            if (LuceneIndexWriter.IndexExists(_indexPath))
                EnsureSearcher();
        }

        // ── Searcher lifecycle ────────────────────────────────────────

        /// <summary>
        /// Returns the live searcher, opening it if this is the first call after a
        /// commit made the index available.  Returns null when no committed index
        /// exists yet (build has not started or no commit has been made).
        /// </summary>
        private LuceneSearcher EnsureSearcher()
        {
            var s = _searcher;
            if (s != null) return s;

            if (!LuceneIndexWriter.IndexExists(_indexPath)) return null;

            lock (_searcherLock)
            {
                if (_disposed) return null;
                if (_searcher != null) return _searcher;
                _searcher = new LuceneSearcher(_indexPath);
                return _searcher;
            }
        }

        private void InstallNrtSearcher(SearcherManager nrtManager)
        {
            lock (_searcherLock)
            {
                if (_disposed) return;
                var old = _searcher;
                _searcher = new LuceneSearcher(nrtManager);
                old?.Dispose();
            }
        }

        private void ReplaceWithDiskSearcher()
        {
            lock (_searcherLock)
            {
                if (_disposed) return;
                var old = _searcher;
                old?.Dispose();
                _searcher = LuceneIndexWriter.IndexExists(_indexPath)
                    ? new LuceneSearcher(_indexPath)
                    : null;
            }
        }

        // ── Build ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds (or resumes) the Lucene index from the seforim database.
        ///
        /// Resume: if a <c>build.progress</c> file exists, indexing continues from
        /// the last committed row ID.  Otherwise a fresh build is started (existing
        /// index directory is deleted).
        ///
        /// Live search: the shared <see cref="LuceneSearcher"/> is refreshed after
        /// every periodic commit so searches running concurrently see the latest rows.
        ///
        /// Optimise: on a completed (non-cancelled) build, <c>ForceMerge(1)</c> is
        /// called to collapse all segments into one for optimal search performance.
        ///
        /// Returns true when at least one row was processed.
        /// </summary>
        /// <param name="limit">Maximum rows to index. 0 = all rows.</param>
        /// <param name="onProgress">Called after each row with the running count.</param>
        /// <param name="onFlush">Called after each periodic commit.</param>
        /// <param name="totalLines">Total rows in the database (for progress display).</param>
        /// <param name="resumeOffset">Kept for API compatibility — not used internally.</param>
        /// <param name="ct">Cancellation token.</param>
        public bool BuildIndex(
            int               limit        = 0,
            Action<long>      onProgress   = null,
            Action            onFlush      = null,
            long              totalLines   = 0,
            long              resumeOffset = 0,
            CancellationToken ct           = default)
        {
            int resumeLineId = ReadResumeLineId();
            if (resumeLineId > 0)
                Console.WriteLine($"[SeforimIndex] Resuming from line id {resumeLineId}");
            else
                Console.WriteLine("[SeforimIndex] Starting fresh build");

            bool deleteExisting    = resumeLineId == 0;
            long n                 = 0;
            bool anyRowsProcessed  = false;
            bool cancelled         = false;
            int  lastWrittenLineId = resumeLineId;

            // Disk commit interval — for crash recovery / progress file.
            const long CommitInterval = 250_000;

            // NRT refresh interval — how often the live searcher sees new docs.
            // Much smaller than CommitInterval so live searches stay fresh.
            const long NrtRefreshInterval = 5_000;

            using (var db     = new ZayitDb(_dbPath))
            using (var writer = new LuceneIndexWriter(_indexPath,
                                    deleteExistingIndex: deleteExisting))
            {
                if (!db.IsOpen)
                {
                    Console.WriteLine("[SeforimIndex] Database not found — aborting.");
                    return false;
                }

                long effectiveTotal = totalLines > 0 ? totalLines : db.CountLines();

                using (var nrtManager = writer.GetNrtSearcherManager())
                {
                    InstallNrtSearcher(nrtManager);

                    try
                    {
                        long linesRemaining = limit > 0 ? limit : long.MaxValue;

                        foreach (var (bookId, bookTitle) in db.ReadAllBooks())
                        {
                            if (linesRemaining <= 0) break;

                            // Build the lineId→tocPath map for this book in one pass.
                            var tocMap = db.BuildTocPathMap(bookId, bookTitle);
                            string lastTocPath = string.Empty;

                            foreach (var (id, content) in db.ReadLinesForBook(bookId))
                            {
                                ct.ThrowIfCancellationRequested();

                                // Skip lines we already indexed in a previous (resumed) build.
                                if (id <= resumeLineId) continue;

                                // Propagate: if this line has a TOC anchor use it,
                                // otherwise inherit the last seen path.
                                if (tocMap.TryGetValue(id, out string tocPath))
                                    lastTocPath = tocPath;

                                anyRowsProcessed  = true;
                                lastWrittenLineId = id;
                                n++;
                                linesRemaining--;

                                writer.AddDocument(id, bookId, bookTitle,
                                                   lastTocPath, content ?? string.Empty);
                                onProgress?.Invoke(n);

                                if (n % NrtRefreshInterval == 0)
                                    nrtManager.MaybeRefresh();

                                if (n % CommitInterval == 0)
                                {
                                    writer.Commit();
                                    WriteProgressFile(lastWrittenLineId, effectiveTotal,
                                                      resumeOffset + n);
                                    Console.WriteLine(
                                        $"[SeforimIndex] Committed at lineId={lastWrittenLineId} (n={n})");
                                    onFlush?.Invoke();
                                }

                                if (linesRemaining <= 0) break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    writer.Commit();
                    nrtManager.MaybeRefresh();

                    if (!cancelled && anyRowsProcessed)
                    {
                        Console.WriteLine("[SeforimIndex] Merging segments…");
                        writer.ForceMerge();
                        Console.WriteLine("[SeforimIndex] Merge complete.");
                    }
                }

                if (anyRowsProcessed)
                    ReplaceWithDiskSearcher();
            }

            if (anyRowsProcessed)
            {
                WriteProgressFile(lastWrittenLineId, totalLines, resumeOffset + n);
                Console.WriteLine(
                    $"[SeforimIndex] Build {(cancelled ? "interrupted" : "complete")} " +
                    $"— final lineId={lastWrittenLineId}");
            }
            else
            {
                Console.WriteLine("[SeforimIndex] No rows processed — progress file unchanged.");
            }

            if (cancelled) throw new OperationCanceledException(ct);
            return anyRowsProcessed;
        }

        // ── Database helpers ──────────────────────────────────────────

        public long CountLines()
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLines();
        }

        public long CountLinesUpTo(int upToId)
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLinesUpTo(upToId);
        }

        // ── Search ────────────────────────────────────────────────────

        public IEnumerable<SearchResult> Search(
            string            query,
            int               cap         = 0,
            bool              expandKetiv = false,
            CancellationToken ct          = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var searcher = EnsureSearcher();
            if (searcher == null) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;
            var matchedGroups     = BuildMatchedGroups(effectiveQuery);

            int yielded = 0;
            foreach (var (rowId, bookId, bookTitle, tocPath) in searcher.Search(effectiveQuery, ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return new SearchResult(
                    rowId, bookId, bookTitle, tocPath, string.Empty,
                    matchedGroups, matchedGroups.Count);

                yielded++;
                if (cap > 0 && yielded >= cap) yield break;
            }
        }

        public IEnumerable<int> SearchIds(
            string            query,
            bool              expandKetiv = false,
            CancellationToken ct          = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var searcher = EnsureSearcher();
            if (searcher == null) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;

            foreach (var (rowId, _, __, ___) in searcher.Search(effectiveQuery, ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return rowId;
            }
        }

        public IEnumerable<(int RowId, int BookId, string BookTitle, string TocPath, SnippetResult Snippet)> SearchWithSnippets(
            string            query,
            int               maxWordDistance = int.MaxValue,
            bool              requireOrdered  = false,
            int               contextWords    = DefaultContextWords,
            bool              expandKetiv     = false,
            CancellationToken ct              = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var searcher = EnsureSearcher();
            if (searcher == null) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;
            int    fragmentSize   = contextWords * 8 * 2;
            int    slop           = requireOrdered ? contextWords : int.MaxValue;
            int    minMarks       = BuildMatchedGroups(effectiveQuery).Count;

            if (_metadataStored)
            {
                // Full index path — bookId/bookTitle/tocPath come from stored Lucene fields.
                using (var db = new ZayitDb(_dbPath))
                {
                    if (!db.IsOpen) yield break;

                    string TextProvider(int rowId) => db.GetLineById(rowId);

                    foreach (var (rowId, bookId, bookTitle, tocPath, fragment) in searcher.SearchWithSnippets(
                        effectiveQuery,
                        TextProvider,
                        fragmentSize: fragmentSize,
                        slop:         slop,
                        inOrder:      requireOrdered,
                        minMarks:     minMarks,
                        ct:           ct))
                    {
                        ct.ThrowIfCancellationRequested();

                        int wordDistance = ComputeWordDistance(fragment);
                        if (wordDistance > maxWordDistance) continue;

                        yield return (rowId, bookId, bookTitle, tocPath,
                                      new SnippetResult(fragment, fragment.Length, wordDistance, true));
                    }
                }
            }
            else
            {
                // Minimal index path — only rowIds come from Lucene.
                // Flow:
                //   1. Collect all matching rowIds from Lucene (no stored metadata).
                //   2. Batch-fetch bookIds from the DB for all rowId hits at once.
                //   3. Run snippet generation per row (requires DB content read per row).
                //   4. After each snippet result, enrich with bookTitle from the batch map.
                //   tocPath is yielded as empty string — the frontend fetches it
                //   from SQL as part of its batch-enrichment step.

                using (var db = new ZayitDb(_dbPath))
                {
                    if (!db.IsOpen) yield break;

                    // Step 1 — collect all matching rowIds in one Lucene pass.
                    // We need them all up front to batch the bookId SQL query.
                    var rowIds = new List<int>();
                    foreach (var rowId in searcher.SearchRowIds(effectiveQuery, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        rowIds.Add(rowId);
                    }

                    if (rowIds.Count == 0) yield break;

                    // Step 2 — fetch bookId for every matched line in one query.
                    Dictionary<int, int> bookIdByLineId = db.GetBookIdsByLineIds(rowIds);

                    // Collect the unique bookIds so we can fetch titles in one query.
                    var uniqueBookIds = new List<int>(bookIdByLineId.Count);
                    var seen = new HashSet<int>();
                    foreach (var bookId in bookIdByLineId.Values)
                        if (seen.Add(bookId)) uniqueBookIds.Add(bookId);

                    // Step 3 — fetch bookTitle for every unique bookId in one query.
                    Dictionary<int, string> bookTitleByBookId =
                        db.GetBookTitlesByBookIds(uniqueBookIds);

                    // Step 4 — run snippet generation, enriching each hit with metadata.
                    // Snippet generation opens a virtual document for each rowId and requires
                    // the raw line content — fetched per-row inside SearchWithSnippets via
                    // the textProvider delegate.
                    string TextProvider(int rowId) => db.GetLineById(rowId);

                    foreach (var (rowId, _, __, ___, fragment) in searcher.SearchWithSnippets(
                        effectiveQuery,
                        TextProvider,
                        fragmentSize: fragmentSize,
                        slop:         slop,
                        inOrder:      requireOrdered,
                        minMarks:     minMarks,
                        ct:           ct))
                    {
                        ct.ThrowIfCancellationRequested();

                        int wordDistance = ComputeWordDistance(fragment);
                        if (wordDistance > maxWordDistance) continue;

                        bookIdByLineId.TryGetValue(rowId, out int bookId);
                        bookTitleByBookId.TryGetValue(bookId, out string bookTitle);

                        yield return (rowId, bookId, bookTitle ?? string.Empty, string.Empty,
                                      new SnippetResult(fragment, fragment.Length, wordDistance, true));
                    }
                }
            }
        }

        // ── IDisposable ───────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_searcherLock)
            {
                _searcher?.Dispose();
                _searcher = null;
            }
        }
    }
}
