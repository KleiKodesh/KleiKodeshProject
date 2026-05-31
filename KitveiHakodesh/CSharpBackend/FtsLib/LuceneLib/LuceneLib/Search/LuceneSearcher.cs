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
    /// Owns a single <see cref="DirectoryReader"/> that is opened once and kept
    /// alive for the lifetime of this object.  Call <see cref="Refresh"/> after
    /// each index commit to make newly indexed documents visible to subsequent
    /// searches — this is a near-zero-cost operation when nothing has changed.
    ///
    /// Thread safety: <see cref="Search"/> and <see cref="SearchWithSnippets"/> may
    /// be called concurrently from multiple threads.  <see cref="Refresh"/> is also
    /// safe to call from any thread (including the indexing thread) — it acquires a
    /// brief write lock only when a new reader generation is actually available.
    /// </summary>
    public sealed class LuceneSearcher : IDisposable
    {
        private readonly FSDirectory    _directory;
        private readonly HebrewAnalyzer _analyzer;

        // Reader + searcher pair — replaced atomically on Refresh().
        // Reads are lock-free (volatile read of the reference); writes take _readerLock.
        private volatile ReaderSearcherPair _current;
        private readonly object             _readerLock = new object();
        private bool _disposed;

        // Bundles the reader and its derived searcher so they are always consistent.
        private sealed class ReaderSearcherPair
        {
            public readonly DirectoryReader  Reader;
            public readonly IndexSearcher    Searcher;
            public ReaderSearcherPair(DirectoryReader reader)
            {
                Reader   = reader;
                Searcher = new IndexSearcher(reader);
            }
        }

        /// <summary>
        /// Opens the index at <paramref name="indexPath"/> and prepares for searching.
        /// Throws if no committed index exists yet.
        /// </summary>
        public LuceneSearcher(string indexPath)
        {
            _directory = FSDirectory.Open(indexPath);
            _analyzer  = new HebrewAnalyzer();
            _current   = new ReaderSearcherPair(DirectoryReader.Open(_directory));
        }

        /// <summary>
        /// Returns true when a committed Lucene index exists at <paramref name="indexPath"/>
        /// (i.e. it is safe to construct a <see cref="LuceneSearcher"/>).
        /// </summary>
        public static bool IndexExists(string indexPath)
            => LuceneIndexWriter.IndexExists(indexPath);

        // ── Refresh ───────────────────────────────────────────────────

        /// <summary>
        /// Checks whether new documents have been committed since the last open/refresh
        /// and, if so, atomically swaps in a new <see cref="DirectoryReader"/>.
        ///
        /// Safe to call from the indexing thread after every <c>Commit()</c>.
        /// Near-zero cost when the index has not changed.
        /// </summary>
        public void Refresh()
        {
            if (_disposed) return;

            // DirectoryReader.OpenIfChanged returns null when nothing has changed —
            // avoid taking the write lock in that common case.
            var newReader = DirectoryReader.OpenIfChanged(_current.Reader);
            if (newReader == null) return;

            lock (_readerLock)
            {
                if (_disposed) { newReader.Dispose(); return; }

                var old = _current;
                _current = new ReaderSearcherPair(newReader);
                // Close the old reader outside the lock to avoid blocking searchers.
                old.Reader.Dispose();
            }
        }

        // ── Search ────────────────────────────────────────────────────

        /// <summary>
        /// Executes the query and returns all matching row IDs.
        /// Checks <paramref name="ct"/> between results — cancellation stops
        /// iteration cleanly without throwing from inside Lucene internals.
        /// </summary>
        public IEnumerable<int> Search(string queryText,
                                       CancellationToken ct = default)
        {
            // Snapshot the current pair — safe even if Refresh() runs concurrently.
            var pair = _current;

            Query query = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (query == null) yield break;

            var counter = new TotalHitCountCollector();
            pair.Searcher.Search(query, counter);
            if (counter.TotalHits == 0) yield break;

            TopDocs hits = pair.Searcher.Search(query, counter.TotalHits);
            foreach (ScoreDoc sd in hits.ScoreDocs)
            {
                ct.ThrowIfCancellationRequested();
                var doc   = pair.Searcher.Doc(sd.Doc);
                var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                if (field != null)
                    yield return field.GetInt32Value().Value;
            }
        }

        /// <summary>
        /// Executes the query and yields (rowId, snippet) pairs with highlighting.
        ///
        /// When <paramref name="slop"/> is <see cref="int.MaxValue"/> (default) the
        /// search uses plain AND semantics with no proximity constraint.
        ///
        /// When <paramref name="slop"/> is set, a <see cref="SpanNearQuery"/> is built
        /// and passed to <see cref="QueryScorer"/> so the highlighter enforces proximity
        /// inside its per-document <see cref="Lucene.Net.Index.Memory.MemoryIndex"/>.
        /// Fragments where terms are not within <paramref name="slop"/> tokens receive
        /// score 0 and are discarded automatically.
        ///
        /// <paramref name="inOrder"/> controls whether terms must appear left-to-right.
        /// <paramref name="ct"/> is checked between results — a cancelled token stops
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
            CancellationToken ct           = default)
        {
            var pair = _current;

            Query boolQuery = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (boolQuery == null) yield break;

            // Scorer query: plain BooleanQuery (no proximity) or SpanNearQuery.
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

            var counter = new TotalHitCountCollector();
            pair.Searcher.Search(boolQuery, counter);
            if (counter.TotalHits == 0) yield break;

            int offset = 0;
            while (offset < counter.TotalHits)
            {
                ct.ThrowIfCancellationRequested();

                int fetchSize = Math.Min(batchSize, counter.TotalHits - offset);
                TopDocs hits  = pair.Searcher.Search(boolQuery, offset + fetchSize);

                for (int i = offset; i < hits.ScoreDocs.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var doc   = pair.Searcher.Doc(hits.ScoreDocs[i].Doc);
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

                    yield return (rowId, new SnippetResult(fragment, 1, 0, true));
                }

                offset += fetchSize;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

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
            lock (_readerLock)
            {
                _current?.Reader.Dispose();
                _current = null;
            }
            _analyzer?.Dispose();
            _directory?.Dispose();
        }
    }
}
