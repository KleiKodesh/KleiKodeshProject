using System;
using System.Collections.Generic;
using System.IO;
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
    /// Queries a Lucene index and returns matching doc IDs and snippets.
    /// Supports: literals, wildcards (*, ?), and OR (|).
    /// </summary>
    public sealed class LuceneSearcher : IDisposable
    {
        private readonly FSDirectory    _directory;
        private readonly IndexReader    _reader;
        private readonly IndexSearcher  _searcher;
        private readonly HebrewAnalyzer _analyzer;
        private bool _disposed;

        public LuceneSearcher(string indexPath)
        {
            _directory = FSDirectory.Open(indexPath);
            _reader    = DirectoryReader.Open(_directory);
            _searcher  = new IndexSearcher(_reader);
            _analyzer  = new HebrewAnalyzer();
        }

        /// <summary>Executes query and yields matching row IDs.</summary>
        public IEnumerable<int> Search(string queryText)
        {
            Query query = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (query == null) yield break;

            var counter = new TotalHitCountCollector();
            _searcher.Search(query, counter);
            if (counter.TotalHits == 0) yield break;

            TopDocs hits = _searcher.Search(query, counter.TotalHits);
            foreach (ScoreDoc sd in hits.ScoreDocs)
            {
                var doc = _searcher.Doc(sd.Doc);
                var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                if (field != null)
                    yield return field.GetInt32Value().Value;
            }
        }

        /// <summary>
        /// Executes query and yields (rowId, snippet) pairs with highlighting.
        ///
        /// When <paramref name="slop"/> is <see cref="int.MaxValue"/> (default) the search
        /// behaves exactly as before — AND semantics, no proximity constraint.
        ///
        /// When <paramref name="slop"/> is set, a <see cref="SpanNearQuery"/> is built from
        /// the same query text and passed to <see cref="QueryScorer"/> instead of the plain
        /// <see cref="BooleanQuery"/>.  The <see cref="SpanNearQuery"/> runs against a
        /// per-document <see cref="Lucene.Net.Index.Memory.MemoryIndex"/> inside
        /// <see cref="Lucene.Net.Search.Highlight.WeightedSpanTermExtractor"/>, so positions
        /// are always available even though the main index stores DOCS_ONLY.
        /// Fragments where the terms are not within <paramref name="slop"/> tokens of each
        /// other receive score 0 and are discarded by the highlighter automatically.
        ///
        /// <paramref name="inOrder"/> controls whether the terms must appear in the same
        /// left-to-right order as typed (true) or in any order (false).
        /// </summary>
        public IEnumerable<(int RowId, SnippetResult Snippet)> SearchWithSnippets(
            string queryText,
            Func<int, string> textProvider,
            string preTag      = "<mark>",
            string postTag     = "</mark>",
            int    batchSize   = 100,
            int    slop        = int.MaxValue,
            bool   inOrder     = false,
            int    fragmentSize = 2000)
        {
            // BooleanQuery — used for the actual index search (DOCS_ONLY index).
            Query boolQuery = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (boolQuery == null) yield break;

            // Scorer query — either the plain BooleanQuery (no proximity) or a
            // SpanNearQuery that enforces slop inside the highlighter's MemoryIndex.
            Query scorerQuery;
            if (slop == int.MaxValue)
            {
                scorerQuery = boolQuery;
            }
            else
            {
                SpanQuery spanQuery = HebrewQueryBuilder.BuildSpan(queryText, _analyzer, slop, inOrder);
                scorerQuery = spanQuery ?? boolQuery; // fall back if single-term or parse failure
            }

            var formatter   = new SimpleHTMLFormatter(preTag, postTag);
            var scorer      = new QueryScorer(scorerQuery);
            var highlighter = new Highlighter(formatter, scorer);
            highlighter.TextFragmenter      = new SimpleSpanFragmenter(scorer, fragmentSize);
            highlighter.MaxDocCharsToAnalyze = 1024 * 1024;

            var counter = new TotalHitCountCollector();
            _searcher.Search(boolQuery, counter);
            if (counter.TotalHits == 0) yield break;

            int offset = 0;
            while (offset < counter.TotalHits)
            {
                int fetchSize = Math.Min(batchSize, counter.TotalHits - offset);
                TopDocs hits  = _searcher.Search(boolQuery, offset + fetchSize);

                for (int i = offset; i < hits.ScoreDocs.Length; i++)
                {
                    var doc   = _searcher.Doc(hits.ScoreDocs[i].Doc);
                    var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                    if (field == null) continue;

                    int    rowId = field.GetInt32Value().Value;
                    string text  = textProvider(rowId);
                    if (text == null) continue;

                    string plain = StripHtml(text);
                    string fragment;
                    using (var ts = _analyzer.GetTokenStream(LuceneIndexWriter.FieldText,
                                                             new StringReader(plain)))
                        fragment = highlighter.GetBestFragment(ts, plain);

                    if (fragment == null) continue;

                    yield return (rowId, new SnippetResult(fragment, 1, 0, true));
                }

                offset += fetchSize;
            }
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new System.Text.StringBuilder(html.Length);
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _analyzer?.Dispose();
            _reader?.Dispose();
            _directory?.Dispose();
        }
    }
}
