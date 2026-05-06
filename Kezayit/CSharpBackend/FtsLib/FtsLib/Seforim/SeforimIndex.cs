using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.Seforim
{
    /// <summary>
    /// Public API for full-text search over the seforim database.
    ///
    /// Usage:
    /// <code>
    ///   var index = new SeforimIndex(indexPath, dbPath);
    ///
    ///   // Build (one-time, or to rebuild):
    ///   index.BuildIndex(onProgress: n => Console.WriteLine($"{n} lines indexed"));
    ///
    ///   // Search:
    ///   foreach (var result in index.Search("שלום~ תורה"))
    ///       Console.WriteLine(result.BookTitle);
    ///
    ///   // Snippet for a result:
    ///   var snippet = index.GenerateSnippet(result);   // uses pre-computed matched terms
    ///   if (snippet.IsMatch) Console.WriteLine(snippet.Html);
    /// </code>
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
        private readonly string _indexPath;
        private readonly string _dbPath;

        /// <summary>Default visible-character budget for the snippet window.</summary>
        public const int DefaultSnippetLength = SnippetPipeline.DefaultSnippetLength;

        /// <summary>Default number of words of context shown on each side of the match.</summary>
        public const int DefaultContextWords = SnippetPipeline.DefaultContextWords;

        /// <param name="indexPath">
        /// Directory where the FTS index segment files are stored.
        /// Will be created on first <see cref="BuildIndex"/> call if it does not exist.
        /// </param>
        /// <param name="dbPath">
        /// Full path to the seforim SQLite database file.
        /// </param>
        public SeforimIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath must not be empty.", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath must not be empty.", nameof(dbPath));

            _indexPath = indexPath;
            _dbPath    = dbPath;
        }

        // ── Build ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the last line ID that was flushed to disk in a previous interrupted
        /// build, or 0 if no interrupted build exists. Use this to show the user that
        /// a resume is in progress rather than a fresh build.
        /// </summary>
        public int GetResumeLineId() => IndexingPipeline.ReadResumeLineId(_indexPath);

        /// <summary>
        /// Deletes the build progress file. Call this after the build completes and
        /// the version stamp is written, so a subsequent startup does not mistake a
        /// finished index for an interrupted one.
        /// </summary>
        public void DeleteBuildProgressFile() => IndexingPipeline.DeleteProgressFile(_indexPath);



        /// <summary>
        /// Returns the total number of lines in the seforim database.
        /// Useful for computing build progress percentage.
        /// </summary>
        public long CountLines()
        {
            using (var db = new Misc.ZayitDb(_dbPath))
                return db.CountLines();
        }

        /// <summary>
        /// Returns the number of lines with id &lt;= <paramref name="upToId"/>.
        /// Used to compute the correct starting offset for progress percentage
        /// when resuming an interrupted build.
        /// </summary>
        public long CountLinesUpTo(int upToId)
        {
            using (var db = new Misc.ZayitDb(_dbPath))
                return db.CountLinesUpTo(upToId);
        }

        /// <summary>
        /// Builds (or rebuilds) the full-text index from the seforim database.
        /// This is a blocking, long-running operation.
        ///
        /// The index is searchable as soon as this method returns. Call
        /// <see cref="Optimize"/> afterwards (e.g. on a background thread) to
        /// merge all segments into one for the fastest possible search performance.
        ///
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/>
        /// is cancelled — the partial index on disk is valid and can be resumed on
        /// the next call.
        ///
        /// Returns true if indexing ran to completion (all lines processed).
        /// Returns false if no lines were available to index (e.g. only WAL recovery
        /// ran, or the DB is empty) — the caller should not treat this as a completed
        /// build and must not write the version stamp.
        /// </summary>
        public bool BuildIndex(int limit = 0, Action<long> onProgress = null,
                               System.Threading.CancellationToken ct = default)
            => IndexingPipeline.Build(_indexPath, _dbPath, limit, onProgress, ct);

        /// <summary>
        /// Force-merges all index segments into one for the fastest possible search.
        /// Optional — search works correctly across any number of segments.
        /// Call this after <see cref="BuildIndex"/> returns, on a background thread,
        /// so the app can start serving searches immediately while the merge runs.
        /// </summary>
        public void Optimize()
            => IndexingPipeline.Optimize(_indexPath);

        // ── Search ────────────────────────────────────────────────────

        /// <summary>
        /// Searches the index for lines matching <paramref name="query"/> and
        /// streams matching rows from the database.
        ///
        /// Results are returned lazily — iteration drives both the index scan and
        /// the database fetch. By default all matching results are returned.
        /// </summary>
        /// <param name="query">
        /// Raw query string. Supports literal terms, wildcards (*), fuzzy (~), and OR (|).
        /// </param>
        /// <param name="cap">
        /// Maximum number of results to return. 0 (default) = no cap.
        /// </param>
        /// <returns>
        /// Lazy <see cref="IEnumerable{SearchResult}"/> of matching lines.
        /// Each item contains the line ID, book title, and raw HTML content.
        /// </returns>
        public IEnumerable<SearchResult> Search(string query, int cap = 0, CancellationToken ct = default)
            => SearchPipeline.Search(query, _indexPath, _dbPath, cap, ct);

        /// <summary>
        /// Searches the index and returns only the matching line IDs — no database
        /// fetch. Use when you only need IDs (e.g. counting results, or when you will
        /// fetch content yourself on demand).
        ///
        /// Significantly faster than <see cref="Search"/> for large result sets because
        /// it skips the SQLite content fetch entirely.
        /// </summary>
        public IEnumerable<int> SearchIds(string query, CancellationToken ct = default)
            => SearchPipeline.SearchIds(query, _indexPath, ct);

        /// <summary>
        /// Generates a highlighted HTML snippet for a single search result line.
        ///
        /// Fetches the line content from the database, finds the tightest window
        /// covering all query terms, and returns highlighted HTML with a proximity
        /// score. Results with <see cref="SnippetResult.IsMatch"/> = false are index
        /// false positives and should be filtered out.
        /// </summary>
        /// <param name="lineId">The line ID from a <see cref="SearchResult"/>.</param>
        /// <param name="query">
        /// The same query string passed to <see cref="Search"/>. Fuzzy/wildcard
        /// markers are stripped automatically — only the base word forms are used
        /// for highlighting.
        /// </param>
        // ── Snippet ───────────────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted HTML snippet for a single search result line.
        ///
        /// Fetches the line content from the database, finds the tightest window
        /// covering all query terms, and returns highlighted HTML with a proximity
        /// score. Results with <see cref="SnippetResult.IsMatch"/> = false are index
        /// false positives and should be filtered out.
        /// </summary>
        /// <param name="lineId">The line ID from a <see cref="SearchResult"/>.</param>
        /// <param name="query">
        /// The same query string passed to <see cref="Search"/>. Fuzzy/wildcard
        /// markers are stripped automatically — only the base word forms are used
        /// for highlighting.
        /// </param>
        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            var terms = SearchPipeline.ExtractTerms(query);
            return SnippetPipeline.GenerateFromDb(lineId, terms, _dbPath);
        }

        /// <summary>
        /// Generates a highlighted HTML snippet using the content and matched groups
        /// already present on the <see cref="SearchResult"/> — no second DB fetch.
        ///
        /// Preferred over <see cref="GenerateSnippet(int,string)"/> for all results
        /// from <see cref="Search"/>: content is already in memory, and the matched
        /// groups include all expanded forms (e.g. ביצחק when the query was יצחק~)
        /// so the highlighter marks the actual word that appeared in the line.
        /// </summary>
        /// <param name="result">A result returned by <see cref="Search"/>.</param>
        /// <param name="requireOrdered">
        /// When true, the snippet is only considered a match when the query terms
        /// appear in the same left-to-right order as the query groups.
        /// False (default) = unordered, any arrangement satisfies the match.
        /// </param>
        /// <param name="contextWords">
        /// Number of words of context shown on each side of the match window.
        /// Defaults to <see cref="DefaultContextWords"/> (8).
        /// </param>
        public SnippetResult GenerateSnippet(SearchResult result, bool requireOrdered = false,
            int snippetLength = DefaultSnippetLength,
            int contextWords = DefaultContextWords)
        {
            if (result == null) return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count > 0)
                return SnippetPipeline.Generate(
                    result.Content,
                    result.MatchedGroups,
                    requireOrdered,
                    result.OriginalGroupCount,
                    snippetLength,
                    contextWords);
            return SnippetResult.NoMatch;
        }
    }
}
