using System;
using System.Collections.Generic;
using System.IO;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
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
        /// Uses Lucene's built-in Highlighter with QueryScorer — handles all query types
        /// including wildcards, OR groups, and multi-term AND queries.
        /// Results are streamed in batches for responsive UI.
        /// </summary>
        public IEnumerable<(int RowId, SnippetResult Snippet)> SearchWithSnippets(
            string queryText,
            Func<int, string> textProvider,
            string preTag = "<mark>",
            string postTag = "</mark>",
            int batchSize = 100,
            int maxDistance = int.MaxValue)
        {
            // Build the query once — same object used for both search and highlighting.
            Query query = HebrewQueryBuilder.Build(queryText, _analyzer);
            if (query == null) yield break;

            // Rewrite expands wildcards/prefix queries against the actual index terms.
            // Must be done before passing to QueryScorer so all term variants are scored.
            Query rewritten = query.Rewrite(_reader);

            var formatter   = new SimpleHTMLFormatter(preTag, postTag);
            var scorer      = new QueryScorer(rewritten);
            var highlighter = new Highlighter(formatter, scorer);
            highlighter.TextFragmenter = new SimpleSpanFragmenter(scorer, 2000);

            // By default Lucene only analyzes the first ~50 KB of a document.
            // For long rows this means terms near the end are never seen, so the
            // highlighter picks a fragment from the analyzed portion even if a better
            // one (containing all AND terms together) exists later in the text.
            // int.MaxValue overflows Lucene's internal array sizing — 1 MB is enough.
            highlighter.MaxDocCharsToAnalyze = 1024 * 1024;

            var counter = new TotalHitCountCollector();
            _searcher.Search(query, counter);
            if (counter.TotalHits == 0) yield break;

            int offset = 0;
            while (offset < counter.TotalHits)
            {
                int fetchSize = Math.Min(batchSize, counter.TotalHits - offset);
                TopDocs hits  = _searcher.Search(query, offset + fetchSize);

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
                    try
                    {
                        using (var ts = _analyzer.GetTokenStream(LuceneIndexWriter.FieldText, new StringReader(plain)))
                            fragment = highlighter.GetBestFragment(ts, plain);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Highlighter failed on rowId={rowId}, plainText.Length={plain.Length}, query=[{rewritten}]: {ex.Message}", ex);
                    }

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
