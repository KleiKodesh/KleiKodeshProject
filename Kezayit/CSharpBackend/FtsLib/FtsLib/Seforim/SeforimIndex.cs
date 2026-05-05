using System;
using System.Collections.Generic;

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
    ///
    /// Multiple tokens are AND-ed together.
    /// Wildcard and fuzzy tokens are OR-expanded internally before the intersection.
    /// </summary>
    public sealed class SeforimIndex
    {
        private readonly string _indexPath;
        private readonly string _dbPath;

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
        /// Returns the total number of lines in the seforim database.
        /// Useful for computing build progress percentage.
        /// </summary>
        public long CountLines()
        {
            using (var db = new Misc.ZayitDb(_dbPath))
                return db.CountLines();
        }

        /// <summary>
        /// Builds (or rebuilds) the full-text index from the seforim database.
        /// This is a blocking, long-running operation (~17 min for the full DB).
        /// </summary>
        /// <param name="limit">
        /// Maximum number of lines to index. 0 (default) = all lines.
        /// Pass a smaller value (e.g. 500_000) for faster partial builds during testing.
        /// </param>
        /// <param name="onProgress">
        /// Optional callback invoked after each line is processed.
        /// Receives the running count of lines indexed so far.
        /// Useful for driving a progress bar or console output.
        /// </param>
        public void BuildIndex(int limit = 0, Action<long> onProgress = null)
            => IndexingPipeline.Build(_indexPath, _dbPath, limit, onProgress);

        // ── Search ────────────────────────────────────────────────────

        /// <summary>
        /// Searches the index for lines matching <paramref name="query"/> and
        /// streams matching rows from the database.
        ///
        /// Results are returned lazily — iteration drives both the index scan and
        /// the database fetch. By default all matching results are returned.
        /// </summary>
        /// <param name="query">
        /// Raw query string. Supports literal terms, wildcards (*), and fuzzy (~).
        /// </param>
        /// <param name="cap">
        /// Maximum number of results to return. 0 (default) = no cap.
        /// </param>
        /// <returns>
        /// Lazy <see cref="IEnumerable{SearchResult}"/> of matching lines.
        /// Each item contains the line ID, book title, and raw HTML content.
        /// </returns>
        public IEnumerable<SearchResult> Search(string query, int cap = 0)
            => SearchPipeline.Search(query, _indexPath, _dbPath, cap);

        /// <summary>
        /// Searches the index and returns only the matching line IDs — no database
        /// fetch. Use when you only need IDs (e.g. counting results, or when you will
        /// fetch content yourself on demand).
        ///
        /// Significantly faster than <see cref="Search"/> for large result sets because
        /// it skips the SQLite content fetch entirely.
        /// </summary>
        public IEnumerable<int> SearchIds(string query)
            => SearchPipeline.SearchIds(query, _indexPath);

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
        public SnippetResult GenerateSnippet(SearchResult result)
        {
            if (result == null) return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count > 0)
                return SnippetPipeline.Generate(result.Content, result.MatchedGroups);
            return SnippetResult.NoMatch;
        }
    }
}
