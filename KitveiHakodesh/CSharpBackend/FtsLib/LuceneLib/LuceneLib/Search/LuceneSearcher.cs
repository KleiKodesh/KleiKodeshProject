using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Search.Spans;
using Lucene.Net.Store;
using LuceneLib.Indexing;
using LuceneLib.Snippets;
using LuceneLib.Tokenization;

namespace LuceneLib.Search
{
    /// <summary>
    /// Long-lived searcher over a Lucene index directory.
    ///
    /// Uses <see cref="SearcherManager"/> for safe concurrent access and
    /// near-real-time refresh.  SearcherManager handles reference counting
    /// internally: a searcher (and its underlying reader) is only closed after
    /// every in-flight search that holds it has finished.
    ///
    /// Result ordering
    /// ---------------
    /// Both <see cref="Search"/> and <see cref="SearchWithSnippets"/> sort results
    /// by the <c>rowId</c> field ascending, matching the database row order.
    /// This gives deterministic, consistent results regardless of segment layout
    /// and matches the behaviour of FtsLib (which intersected posting lists in
    /// doc-ID order).
    ///
    /// Thread safety: all public methods are safe to call concurrently.
    /// </summary>
    public sealed class LuceneSearcher : IDisposable
    {
        private readonly FSDirectory     _directory;   // null when using external manager
        private readonly SearcherManager _manager;
        private readonly HebrewAnalyzer  _analyzer;

        // Sort by rowId ascending — created once, shared across all searches.
        // SortField.Type.INT32 matches the Int32Field used in LuceneIndexWriter.
        private static readonly Sort RowIdSort =
            new Sort(new SortField(LuceneIndexWriter.FieldRowId, SortFieldType.INT32));

        private bool _disposed;

        /// <summary>
        /// Opens the index at <paramref name="indexPath"/> and prepares for searching.
        /// Uses a disk-based <see cref="SearcherManager"/> — suitable for post-build
        /// searches where the writer is closed.
        /// Throws if no committed index exists yet.
        /// </summary>
        public LuceneSearcher(string indexPath)
        {
            _directory = FSDirectory.Open(indexPath);
            _analyzer  = new HebrewAnalyzer();
            _manager   = new SearcherManager(_directory, null);
        }

        /// <summary>
        /// Wraps an externally-created <see cref="SearcherManager"/> — typically an
        /// NRT manager obtained from <see cref="LuceneIndexWriter.GetNrtSearcherManager"/>.
        /// The caller retains ownership of <paramref name="manager"/> and must dispose
        /// it separately; this searcher does NOT dispose it.
        /// </summary>
        public LuceneSearcher(SearcherManager manager)
        {
            _manager  = manager ?? throw new ArgumentNullException(nameof(manager));
            _analyzer = new HebrewAnalyzer();
            // _directory is null — we don't own it
        }

        /// <summary>
        /// Returns true when a committed Lucene index exists at <paramref name="indexPath"/>
        /// (i.e. it is safe to construct a <see cref="LuceneSearcher"/>).
        /// </summary>
        public static bool IndexExists(string indexPath)
            => LuceneIndexWriter.IndexExists(indexPath);

        // ── Search ────────────────────────────────────────────────────

        /// <summary>
        /// Executes the query and returns all matching row IDs sorted by rowId ascending.
        /// Acquires a searcher from the manager for the duration of the search,
        /// then releases it so the manager can safely close old reader generations.
        /// </summary>
        public IEnumerable<int> Search(string queryText,
                                       CancellationToken ct = default)
        {
            Query query = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (query == null) yield break;

            IndexSearcher searcher = _manager.Acquire();
            try
            {
                // Single sorted search — no double-query needed.
                // doDocScores=false, doMaxScore=false: we don't use relevance scores,
                // only the sort key, so skip the score computation entirely.
                var counter = new TotalHitCountCollector();
                searcher.Search(query, counter);
                if (counter.TotalHits == 0) yield break;

                TopFieldDocs hits = searcher.Search(query, counter.TotalHits, RowIdSort);

                foreach (ScoreDoc sd in hits.ScoreDocs)
                {
                    ct.ThrowIfCancellationRequested();
                    var doc   = searcher.Doc(sd.Doc);
                    var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                    if (field != null)
                        yield return field.GetInt32Value().Value;
                }
            }
            finally
            {
                _manager.Release(searcher);
            }
        }

        /// <summary>
        /// Executes the query and yields (rowId, snippet) pairs with highlighting,
        /// sorted by rowId ascending.
        ///
        /// When <paramref name="slop"/> is <see cref="int.MaxValue"/> (default) the
        /// search uses plain AND semantics with no proximity constraint.
        ///
        /// When <paramref name="slop"/> is set, a <see cref="SpanNearQuery"/> is built
        /// and passed to <see cref="QueryScorer"/> so the highlighter enforces proximity
        /// inside its per-document <see cref="Lucene.Net.Index.Memory.MemoryIndex"/>.
        ///
        /// <paramref name="minMarks"/> — minimum number of &lt;mark&gt; tags the
        /// returned fragment must contain.  When the highlighter's best window is too
        /// narrow to include all query terms, the fragment will have fewer marks than
        /// the query has AND-slots; passing the slot count here filters those bogus
        /// partial-match snippets out.  0 (default) disables the check.
        ///
        /// <paramref name="ct"/> is checked between results — cancellation stops
        /// streaming immediately.
        /// </summary>
        public IEnumerable<(int RowId, SnippetResult Snippet)> SearchWithSnippets(
            string            queryText,
            Func<int, string> textProvider,
            string            preTag       = "<mark>",
            string            postTag      = "</mark>",
            int               batchSize    = 100,
            int               slop         = int.MaxValue,
            bool              inOrder      = false,
            int               fragmentSize = 2000,
            int               minMarks     = 0,
            CancellationToken ct           = default)
        {
            Query boolQuery = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (boolQuery == null) yield break;

            Query scorerQuery;
            if (slop == int.MaxValue)
            {
                scorerQuery = boolQuery;
            }
            else
            {
                SpanQuery spanQuery = HebrewQueryBuilder.BuildSpan(
                    queryText, _analyzer, slop, inOrder);
                scorerQuery = spanQuery ?? boolQuery;
            }

            var formatter   = new SimpleHTMLFormatter(preTag, postTag);
            var scorer      = new QueryScorer(scorerQuery);
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter       = new SimpleSpanFragmenter(scorer, fragmentSize),
                MaxDocCharsToAnalyze = 1024 * 1024,
            };

            IndexSearcher searcher = _manager.Acquire();
            try
            {
                var counter = new TotalHitCountCollector();
                searcher.Search(boolQuery, counter);
                if (counter.TotalHits == 0) yield break;

                // Collect all hits sorted by rowId in one shot.
                // Paging through TopDocs re-executes the query each time — instead
                // we collect everything once and stream through the sorted array.
                TopFieldDocs hits = searcher.Search(boolQuery, counter.TotalHits, RowIdSort);

                foreach (ScoreDoc sd in hits.ScoreDocs)
                {
                    ct.ThrowIfCancellationRequested();

                    var doc   = searcher.Doc(sd.Doc);
                    var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                    if (field == null) continue;

                    int    rowId = field.GetInt32Value().Value;
                    string text  = textProvider(rowId);
                    if (text == null) continue;

                    string plain = StripHtml(text);
                    string fragment;
                    using (var ts = _analyzer.GetTokenStream(
                               LuceneIndexWriter.FieldText, new StringReader(plain)))
                        fragment = highlighter.GetBestFragment(ts, plain);

                    if (fragment == null) continue;

                    // If the caller requires a minimum number of highlighted terms,
                    // count <mark> tags in the fragment and skip it when the window
                    // was too narrow to include all query terms.  This filters out
                    // bogus results where a short snippet only shows part of the match.
                    if (minMarks > 0 && CountMarks(fragment, preTag) < minMarks)
                        continue;

                    yield return (rowId, new SnippetResult(fragment, 1, 0, true));
                }
            }
            finally
            {
                _manager.Release(searcher);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Counts the number of opening highlight tags in <paramref name="fragment"/>.
        /// Used to verify that the highlighter's best window contains all query terms.
        /// </summary>
        private static int CountMarks(string fragment, string openTag)
        {
            if (string.IsNullOrEmpty(fragment)) return 0;
            int count = 0;
            int pos   = 0;
            while ((pos = fragment.IndexOf(openTag, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                pos += openTag.Length;
            }
            return count;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new System.Text.StringBuilder(html.Length);
            bool inTag = false;
            foreach (char c in html)
            {
                if      (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag)   sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Only dispose the manager when we created it (disk-based constructor).
            // When constructed from an external NRT manager, the caller owns it.
            if (_directory != null)
                _manager?.Dispose();
            _analyzer?.Dispose();
            _directory?.Dispose();
        }
    }
}
