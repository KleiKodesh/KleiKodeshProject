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

        // ── Store lifecycle ───────────────────────────────────────────

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

        // ── Build ─────────────────────────────────────────────────────

        public int GetResumeLineId() => IndexingPipeline.ReadResumeLineId(_indexPath);

        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => IndexingPipeline.ReadProgressFile(_indexPath, out lineId, out totalLines, out resumeOffset);

        public void DeleteBuildProgressFile() => IndexingPipeline.DeleteProgressFile(_indexPath);

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

        public bool BuildIndex(int limit = 0, Action<long> onProgress = null,
                               Action onFlush = null,
                               long totalLines = 0,
                               long resumeOffset = 0,
                               CancellationToken ct = default)
        {
            using (new IndexWriteLock(_indexPath))
            {
                bool result = IndexingPipeline.Build(_indexPath, _dbPath, _store, limit, totalLines, resumeOffset, onProgress, onFlush, ct);
                if (_store.IsWiped) ResetStore();
                return result;
            }
        }

        public void Optimize() => IndexingPipeline.Optimize(_indexPath, _store);

        // ── Search ────────────────────────────────────────────────────

        public IEnumerable<SearchResult> Search(string query, int cap = 0, bool expandKetiv = false, CancellationToken ct = default)
            => SearchPipeline.Search(query, _indexPath, _dbPath, GetLiveSegmentPaths(), cap, expandKetiv, ct);

        public IEnumerable<int> SearchIds(string query, bool expandKetiv = false, CancellationToken ct = default)
            => SearchPipeline.SearchIds(query, _indexPath, GetLiveSegmentPaths(), expandKetiv, ct);

        // ── Snippets ──────────────────────────────────────────────────

        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            var terms = SearchPipeline.ExtractTerms(query);
            return SnippetPipeline.GenerateFromDb(lineId, terms, _dbPath);
        }

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
