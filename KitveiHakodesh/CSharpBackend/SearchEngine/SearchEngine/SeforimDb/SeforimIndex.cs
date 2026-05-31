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
    /// </summary>
    public sealed class SeforimIndex : IDisposable
    {
        private readonly string _indexPath;
        private readonly string _dbPath;

        // Long-lived searcher — null until the first commit exists.
        // Guarded by _searcherLock for open/close; volatile for lock-free reads.
        private volatile LuceneSearcher _searcher;
        private readonly object         _searcherLock = new object();
        private bool _disposed;

        // Progress file written after each commit so a build can be resumed.
        private const string ProgressFileName = "build.progress";

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
            // Fast path — already open.
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

            bool deleteExisting = resumeLineId == 0;

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

                // Open an NRT SearcherManager from the writer so live searches see
                // new docs without waiting for a disk commit.
                using (var nrtManager = writer.GetNrtSearcherManager())
                {
                    // Install the NRT searcher as the live searcher for this build.
                    // Any search arriving during the build will use this NRT manager.
                    InstallNrtSearcher(nrtManager);

                    var rows = resumeLineId > 0
                        ? db.ReadLinesFrom(resumeLineId, limit, ct)
                        : db.ReadLines(limit, ct);

                    try
                    {
                        foreach (var (id, content) in rows)
                        {
                            ct.ThrowIfCancellationRequested();
                            anyRowsProcessed  = true;
                            lastWrittenLineId = id;
                            n++;

                            writer.AddDocument(id, content ?? string.Empty);
                            onProgress?.Invoke(n);

                            // NRT refresh — makes new docs visible to live searches
                            // without a disk commit.
                            if (n % NrtRefreshInterval == 0)
                                nrtManager.MaybeRefresh();

                            // Disk commit — for crash recovery and progress file.
                            if (n % CommitInterval == 0)
                            {
                                writer.Commit();
                                WriteProgressFile(lastWrittenLineId, effectiveTotal,
                                                  resumeOffset + n);
                                Console.WriteLine(
                                    $"[SeforimIndex] Committed at lineId={lastWrittenLineId} (n={n})");
                                onFlush?.Invoke();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    // Final commit and NRT refresh.
                    writer.Commit();
                    nrtManager.MaybeRefresh();

                    if (!cancelled && anyRowsProcessed)
                    {
                        Console.WriteLine("[SeforimIndex] Merging segments…");
                        writer.ForceMerge();
                        Console.WriteLine("[SeforimIndex] Merge complete.");
                    }
                } // nrtManager disposed here

                // After the NRT manager is gone, switch the live searcher back to
                // a normal disk-based one so post-build searches use the merged index.
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

        /// <summary>
        /// Installs a <see cref="LuceneSearcher"/> backed by the given NRT manager
        /// as the live searcher.  Any existing disk-based searcher is disposed.
        /// </summary>
        private void InstallNrtSearcher(SearcherManager nrtManager)
        {
            lock (_searcherLock)
            {
                if (_disposed) return;
                var old = _searcher;
                _searcher = new LuceneSearcher(nrtManager);
                // Dispose the old disk-based searcher if one was open.
                old?.Dispose();
            }
        }

        /// <summary>
        /// Replaces the NRT searcher with a fresh disk-based one pointing at the
        /// now-complete (merged) index.  Called after the writer is closed.
        /// </summary>
        private void ReplaceWithDiskSearcher()
        {
            lock (_searcherLock)
            {
                if (_disposed) return;
                var old = _searcher;
                // The NRT searcher wraps an external manager — disposing it only
                // releases the analyzer and directory reference, not the manager.
                old?.Dispose();
                _searcher = LuceneIndexWriter.IndexExists(_indexPath)
                    ? new LuceneSearcher(_indexPath)
                    : null;
            }
        }

        // ── Resume helpers ────────────────────────────────────────────

        public int GetResumeLineId() => ReadResumeLineId();

        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => ReadProgressFile(out lineId, out totalLines, out resumeOffset);

        public void DeleteBuildProgressFile()
        {
            try
            {
                string path = Path.Combine(_indexPath, ProgressFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
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

        /// <summary>
        /// Searches the index for lines matching the query and fetches their content
        /// from the database.  Results are yielded lazily.
        ///
        /// Uses the long-lived <see cref="LuceneSearcher"/> — no reader open/close
        /// overhead per call.  Safe to call while <see cref="BuildIndex"/> is running.
        ///
        /// Cancelling <paramref name="ct"/> stops result streaming immediately and
        /// throws <see cref="OperationCanceledException"/>.  The caller is responsible
        /// for cancelling any previous search before starting a new one.
        /// </summary>
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

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) yield break;

                int yielded = 0;
                foreach (int rowId in searcher.Search(effectiveQuery, ct))
                {
                    ct.ThrowIfCancellationRequested();

                    string content   = db.GetLineById(rowId);
                    string bookTitle = db.GetBookTitleByLineId(rowId);

                    yield return new SearchResult(
                        rowId,
                        bookTitle ?? string.Empty,
                        content   ?? string.Empty,
                        matchedGroups,
                        matchedGroups.Count);

                    yielded++;
                    if (cap > 0 && yielded >= cap) yield break;
                }
            }
        }

        /// <summary>
        /// Searches the index and returns only matching line IDs — no database fetch.
        /// Cancelling <paramref name="ct"/> stops iteration immediately.
        /// </summary>
        public IEnumerable<int> SearchIds(
            string            query,
            bool              expandKetiv = false,
            CancellationToken ct          = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var searcher = EnsureSearcher();
            if (searcher == null) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;

            foreach (int rowId in searcher.Search(effectiveQuery, ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return rowId;
            }
        }

        // ── Combined search + snippet (fast path) ─────────────────────

        /// <summary>
        /// Searches the index and yields (rowId, bookTitle, snippet) tuples in a
        /// single pass — one Lucene query, one DB connection, batch title lookups.
        ///
        /// This is the preferred path for the UI layer.  The old two-phase
        /// <c>Search</c> + <c>GenerateSnippet</c> loop is O(N) Lucene queries;
        /// this method is O(1) Lucene queries regardless of result count.
        ///
        /// <paramref name="maxWordDistance"/> filters out results where the matched
        /// terms are farther apart than this many tokens.
        /// <paramref name="requireOrdered"/> enforces left-to-right term order.
        /// <paramref name="contextWords"/> controls the snippet window size.
        /// <paramref name="titleBatchSize"/> controls how many book titles are fetched
        /// per batch query (default 200 — keeps IN clause manageable).
        /// </summary>
        public IEnumerable<(int RowId, string BookTitle, SnippetResult Snippet)> SearchWithSnippets(
            string            query,
            int               maxWordDistance = int.MaxValue,
            bool              requireOrdered  = false,
            int               contextWords    = DefaultContextWords,
            bool              expandKetiv     = false,
            int               titleBatchSize  = 200,
            CancellationToken ct              = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var searcher = EnsureSearcher();
            if (searcher == null) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;
            int    fragmentSize   = contextWords * 8 * 2;
            int    slop           = requireOrdered ? contextWords : int.MaxValue;

            // Require the fragment to contain a mark for every AND-slot in the query.
            // This filters out snippets where the highlighter's window was too narrow
            // to include all query terms — those are partial-match artifacts, not
            // genuine results.
            var matchedGroupsForFilter = BuildMatchedGroups(effectiveQuery);
            int minMarks = matchedGroupsForFilter.Count;

            // Title cache — populated lazily in batches as rowIds arrive.
            var titleCache = new Dictionary<int, string>();
            var pendingIds = new List<int>(titleBatchSize);

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) yield break;

                // textProvider: called by SearchWithSnippets for each hit.
                // Content is fetched from DB to get the original HTML.
                string TextProvider(int rowId)
                {
                    return db.GetLineById(rowId);
                }

                foreach (var (rowId, snippet) in searcher.SearchWithSnippets(
                    effectiveQuery,
                    TextProvider,
                    fragmentSize: fragmentSize,
                    slop:         slop,
                    inOrder:      requireOrdered,
                    minMarks:     minMarks,
                    ct:           ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!snippet.IsMatch) continue;

                    int wordDistance = ComputeWordDistance(snippet.Html);
                    if (wordDistance > maxWordDistance) continue;

                    // Ensure this rowId's title is in the cache.
                    if (!titleCache.ContainsKey(rowId))
                    {
                        pendingIds.Add(rowId);
                        if (pendingIds.Count >= titleBatchSize)
                            FlushTitleBatch(db, pendingIds, titleCache);
                    }

                    // Flush any remaining pending IDs so we can resolve the title.
                    if (pendingIds.Count > 0)
                        FlushTitleBatch(db, pendingIds, titleCache);

                    titleCache.TryGetValue(rowId, out string bookTitle);

                    int score = snippet.Html.Length;
                    yield return (rowId,
                                  bookTitle ?? string.Empty,
                                  new SnippetResult(snippet.Html, score, wordDistance, true));
                }
            }
        }

        private static void FlushTitleBatch(
            ZayitDb                db,
            List<int>              pendingIds,
            Dictionary<int, string> cache)
        {
            if (pendingIds.Count == 0) return;
            var titles = db.GetBookTitlesByLineIds(pendingIds);
            foreach (var kv in titles)
                cache[kv.Key] = kv.Value;
            pendingIds.Clear();
        }

        // ── Snippets ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted HTML snippet for a line ID and query string.
        /// Fetches the line content from the database.
        /// </summary>
        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return SnippetResult.NoMatch;

            var searcher = EnsureSearcher();
            if (searcher == null) return SnippetResult.NoMatch;

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) return SnippetResult.NoMatch;
                string content = db.GetLineById(lineId);
                if (content == null) return SnippetResult.NoMatch;
                int minMarks = BuildMatchedGroups(query).Count;
                return GenerateSnippetFromContent(searcher, content, query,
                    minMarks: minMarks);
            }
        }

        /// <summary>
        /// Generates a highlighted HTML snippet from an already-fetched
        /// <see cref="SearchResult"/>. No database round-trip.
        /// </summary>
        public SnippetResult GenerateSnippet(
            SearchResult result,
            bool         requireOrdered = false,
            int          contextWords   = DefaultContextWords)
        {
            if (result == null)                       return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0)      return SnippetResult.NoMatch;
            if (string.IsNullOrEmpty(result.Content)) return SnippetResult.NoMatch;

            var searcher = EnsureSearcher();
            if (searcher == null) return SnippetResult.NoMatch;

            string reconstructedQuery = ReconstructQuery(result.MatchedGroups);
            return GenerateSnippetFromContent(
                searcher, result.Content, reconstructedQuery,
                requireOrdered, contextWords,
                minMarks: result.MatchedGroups.Count);
        }

        // ── Private helpers ───────────────────────────────────────────

        private static SnippetResult GenerateSnippetFromContent(
            LuceneSearcher    searcher,
            string            content,
            string            query,
            bool              requireOrdered = false,
            int               contextWords   = DefaultContextWords,
            int               minMarks       = 0,
            CancellationToken ct             = default)
        {
            int fragmentSize = contextWords * 8 * 2;
            int slop         = requireOrdered ? contextWords : int.MaxValue;

            foreach (var (_, snippet) in searcher.SearchWithSnippets(
                query,
                _ => content,
                fragmentSize: fragmentSize,
                slop:         slop,
                inOrder:      requireOrdered,
                minMarks:     minMarks,
                ct:           ct))
            {
                int wordDistance = ComputeWordDistance(snippet.Html);
                int score        = snippet.Html.Length;
                return new SnippetResult(snippet.Html, score, wordDistance, snippet.IsMatch);
            }

            return SnippetResult.NoMatch;
        }

        /// <summary>
        /// Computes word distance for a query against content without generating
        /// the full HTML snippet. Used for fast filtering before snippet generation.
        /// Returns int.MaxValue if the query does not match.
        /// </summary>
        public int ComputeWordDistance(string query, string content, bool requireOrdered = false, int contextWords = DefaultContextWords)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(content))
                return int.MaxValue;

            var searcher = EnsureSearcher();
            if (searcher == null) return int.MaxValue;

            int fragmentSize = contextWords * 8 * 2;
            int slop         = requireOrdered ? contextWords : int.MaxValue;

            foreach (var (_, snippet) in searcher.SearchWithSnippets(
                query,
                _ => content,
                fragmentSize: fragmentSize,
                slop:         slop,
                inOrder:      requireOrdered))
            {
                return ComputeWordDistanceFromHtml(snippet.Html);
            }

            return int.MaxValue;
        }

        /// <summary>
        /// Counts tokens between the first &lt;mark&gt; and last &lt;/mark&gt;,
        /// minus the number of matched terms — mirrors FtsLib's WordDistance metric.
        /// </summary>
        private static int ComputeWordDistance(string html)
        {
            if (string.IsNullOrEmpty(html)) return int.MaxValue;
            return ComputeWordDistanceFromHtml(html);
        }

        private static int ComputeWordDistanceFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return int.MaxValue;

            int firstMark = html.IndexOf("<mark>",  StringComparison.OrdinalIgnoreCase);
            int lastClose = html.LastIndexOf("</mark>", StringComparison.OrdinalIgnoreCase);
            if (firstMark < 0 || lastClose < 0) return int.MaxValue;

            int windowEnd = lastClose + "</mark>".Length;
            string window = html.Substring(firstMark, windowEnd - firstMark);

            var sb = new System.Text.StringBuilder(window.Length);
            bool inTag = false;
            foreach (char c in window)
            {
                if      (c == '<') { inTag = true;  continue; }
                if      (c == '>') { inTag = false; sb.Append(' '); continue; }
                if (!inTag) sb.Append(c);
            }

            int totalTokens = 0;
            foreach (var tok in sb.ToString().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                totalTokens++;

            int markCount = 0;
            int pos = 0;
            while ((pos = html.IndexOf("<mark>", pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            { markCount++; pos += 6; }

            int dist = totalTokens - markCount;
            return dist < 0 ? 0 : dist;
        }

        private static string ReconstructQuery(
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var slots = new List<string>(groups.Count);
            foreach (var group in groups)
                slots.Add(string.Join(" | ", group));
            return string.Join(" ", slots);
        }

        private static IReadOnlyList<IReadOnlyCollection<string>> BuildMatchedGroups(
            string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<IReadOnlyCollection<string>>();

            query = query.Replace("|", " | ");
            var slots       = new List<IReadOnlyCollection<string>>();
            var pendingSlot = new List<string>();
            bool lastWasPipe = false;

            foreach (var raw in query.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool isPipe = true;
                foreach (char c in raw) if (c != '|') { isPipe = false; break; }
                if (isPipe) { lastWasPipe = true; continue; }

                string normalised = NormaliseToken(raw);
                if (normalised.Length == 0) continue;

                if (!lastWasPipe && pendingSlot.Count > 0)
                {
                    slots.Add(pendingSlot.ToArray());
                    pendingSlot = new List<string>();
                }

                pendingSlot.Add(normalised);
                lastWasPipe = false;
            }

            if (pendingSlot.Count > 0)
                slots.Add(pendingSlot.ToArray());

            return slots;
        }

        private static string NormaliseToken(string raw)
        {
            int start = 0;
            while (start < raw.Length && (raw[start] == '~' || raw[start] == '%'))
                start++;
            int end = raw.Length;
            while (end > start && raw[end - 1] == '%')
                end--;

            // Also strip trailing ~N fuzzy suffix
            for (int i = end - 1; i >= start; i--)
            {
                if (raw[i] == '~')
                {
                    string suffix = raw.Substring(i + 1, end - i - 1);
                    bool valid = suffix.Length == 0
                              || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9');
                    if (valid) { end = i; break; }
                }
            }

            var sb = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = raw[i];
                if (c >= '\u0591' && c <= '\u05C7') continue;
                if (c == '\u05F3' || c == '\u05F4' || c == '"') continue;
                if (c == '*' || c == '?') { sb.Append(c); continue; }
                if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; }
                if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }
            }
            return sb.ToString();
        }

        private static string ApplyKetivPrefix(string query)
        {
            query = query.Replace("|", " | ");
            var parts = query.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (sb.Length > 0) sb.Append(' ');
                bool isPipe   = true;
                foreach (char c in part) if (c != '|') { isPipe = false; break; }
                bool isMarked = part.Length > 0 && (part[0] == '~' || part[0] == '%');
                bool isWild   = part.IndexOf('*') >= 0 || part.IndexOf('?') >= 0;
                if (!isPipe && !isMarked && !isWild) sb.Append('~');
                sb.Append(part);
            }
            return sb.ToString();
        }

        // ── Progress file ─────────────────────────────────────────────

        private int ReadResumeLineId()
        {
            ReadProgressFile(out int lineId, out _, out _);
            return lineId;
        }

        private void ReadProgressFile(
            out int lineId, out long totalLines, out long resumeOffset)
        {
            lineId       = 0;
            totalLines   = 0;
            resumeOffset = 0;
            string path = Path.Combine(_indexPath, ProgressFileName);
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllText(path).Trim().Split('\n');
                if (lines.Length >= 1) int.TryParse(lines[0].Trim(),  out lineId);
                if (lines.Length >= 2) long.TryParse(lines[1].Trim(), out totalLines);
                if (lines.Length >= 3) long.TryParse(lines[2].Trim(), out resumeOffset);
            }
            catch { }
        }

        private void WriteProgressFile(int lineId, long totalLines, long resumeOffset)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(_indexPath, ProgressFileName),
                    lineId.ToString()       + "\n" +
                    totalLines.ToString()   + "\n" +
                    resumeOffset.ToString());
            }
            catch { }
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
