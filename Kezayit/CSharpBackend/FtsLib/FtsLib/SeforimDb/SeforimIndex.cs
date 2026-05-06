using FtsLib.Indexing;
using FtsLib.Tokenization;
using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Public API for full-text search over the seforim database.
    ///
    /// Owns a long-lived <see cref="SegmentStore"/> so that live segment state is
    /// always consistent between build sessions and concurrent searches. The store
    /// is initialised once (with crash recovery) in the constructor and reused for
    /// every subsequent <see cref="BuildIndex"/> and <see cref="Search"/> call.
    /// This eliminates the race where a search opens segments by scanning the
    /// directory while a concurrent merge is deleting source files.
    ///
    /// Query syntax:
    ///   word        — literal AND term
    ///   word*       — wildcard (prefix / infix / suffix)
    ///   wor?d       — optional char: the char before '?' is optional (matches "word" and "wrd")
    ///   word~       — fuzzy match, edit distance 1
    ///   word~2      — fuzzy match, edit distance 2
    ///   word~3      — fuzzy match, edit distance 3 (maximum)
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///
    /// Multiple tokens are AND-ed together.
    /// '|'-separated tokens are OR-ed within one AND slot.
    /// Wildcard and fuzzy tokens are OR-expanded internally before the intersection;
    /// OR groups merge all their expansions.
    /// </summary>
    public sealed class SeforimIndex
    {
        private readonly string       _indexPath;
        private readonly string       _dbPath;
        private          SegmentStore _store;

        /// <summary>Default visible-character budget for the snippet window.</summary>
        public const int DefaultSnippetLength = SnippetPipeline.DefaultSnippetLength;

        /// <summary>Default number of words of context shown on each side of the match.</summary>
        public const int DefaultContextWords = SnippetPipeline.DefaultContextWords;

        public SeforimIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath must not be empty.", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath must not be empty.", nameof(dbPath));

            _indexPath = indexPath;
            _dbPath    = dbPath;

            // Initialise the store eagerly so crash recovery runs once at startup
            // and the live segment state is ready before the first search.
            EnsureStore();
        }

        // ── Store lifecycle ───────────────────────────────────────────────────────

        private void EnsureStore()
        {
            if (!System.IO.Directory.Exists(_indexPath))
                System.IO.Directory.CreateDirectory(_indexPath);

            _store = new SegmentStore(_indexPath);

            bool needsRecovery =
                System.IO.Directory.GetFiles(_indexPath, "seg_*.dat").Length > 0 ||
                System.IO.File.Exists(System.IO.Path.Combine(_indexPath, "wal.log"));

            if (!needsRecovery) return;

            Console.WriteLine("[SeforimIndex] Segments found — running crash recovery...");
            try
            {
                _store.Recover();
                Console.WriteLine("[SeforimIndex] Recovery complete.");
            }
            catch (CorruptIndexException)
            {
                // Recovery wiped the directory — start with a clean store.
                _store = new SegmentStore(_indexPath);
            }
        }

        private void ResetStore()
        {
            _store = new SegmentStore(_indexPath);
        }

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths under the store lock.
        /// Passed to SearchPipeline so IndexReader never races with a concurrent merge.
        /// </summary>
        internal List<(string dat, string db)> GetLiveSegmentPaths()
            => _store != null ? _store.GetLiveSegmentPaths() : new List<(string, string)>();

        // ── Build ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the last line ID flushed in a previous interrupted build, or 0.
        /// </summary>
        public int GetResumeLineId() => IndexingPipeline.ReadResumeLineId(_indexPath);

        /// <summary>
        /// Deletes the build progress file after a completed build.
        /// </summary>
        public void DeleteBuildProgressFile() => IndexingPipeline.DeleteProgressFile(_indexPath);

        /// <summary>Returns the total number of lines in the seforim database.</summary>
        public long CountLines()
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLines();
        }

        /// <summary>Returns the number of lines with id &lt;= <paramref name="upToId"/>.</summary>
        public long CountLinesUpTo(int upToId)
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLinesUpTo(upToId);
        }

        /// <summary>
        /// Builds (or resumes) the full-text index. Blocking, long-running.
        /// Returns true when all lines were processed; false when only WAL recovery ran.
        /// Throws <see cref="OperationCanceledException"/> on cancellation — the partial
        /// index is valid and will be resumed on the next call.
        /// </summary>
        public bool BuildIndex(int limit = 0, Action<long> onProgress = null,
                               Action onFlush = null,
                               CancellationToken ct = default)
        {
            bool result = IndexingPipeline.Build(_indexPath, _dbPath, _store, limit, onProgress, onFlush, ct);
            if (_store.IsWiped) ResetStore();
            return result;
        }

        /// <summary>
        /// Force-merges all segments into one for fastest search.
        /// Optional — search works across any number of segments.
        /// Run on a background thread after BuildIndex returns.
        /// </summary>
        public void Optimize() => IndexingPipeline.Optimize(_indexPath, _store);

        // ── Search ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Searches the index and streams matching rows from the database.
        /// Results are lazy — iteration drives both the index scan and the DB fetch.
        /// </summary>
        public IEnumerable<SearchResult> Search(string query, int cap = 0, CancellationToken ct = default)
            => SearchPipeline.Search(query, _indexPath, _dbPath, GetLiveSegmentPaths(), cap, ct);

        /// <summary>
        /// Returns only matching line IDs — no database fetch.
        /// Faster than Search for large result sets when content is not needed.
        /// </summary>
        public IEnumerable<int> SearchIds(string query, CancellationToken ct = default)
            => SearchPipeline.SearchIds(query, _indexPath, GetLiveSegmentPaths(), ct);

        // ── Snippets ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted snippet by fetching line content from the database.
        /// Prefer the SearchResult overload when content is already in memory.
        /// </summary>
        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            var terms = SearchPipeline.ExtractTerms(query);
            return SnippetPipeline.GenerateFromDb(lineId, terms, _dbPath);
        }

        /// <summary>
        /// Generates a highlighted snippet from a SearchResult already in memory.
        /// Preferred over the lineId overload — no second DB fetch, and matched groups
        /// include all expanded forms (e.g. ביצחק when the query was יצחק~).
        /// </summary>
        public SnippetResult GenerateSnippet(SearchResult result, bool requireOrdered = false,
            int snippetLength = DefaultSnippetLength,
            int contextWords  = DefaultContextWords)
        {
            if (result == null) return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0) return SnippetResult.NoMatch;
            return SnippetPipeline.Generate(
                result.Content,
                result.MatchedGroups,
                requireOrdered,
                result.OriginalGroupCount,
                snippetLength,
                contextWords);
        }
    }
}
