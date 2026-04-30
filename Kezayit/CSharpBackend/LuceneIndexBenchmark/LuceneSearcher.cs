using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.IO;

namespace LuceneIndexBenchmark
{
    /// <summary>
    /// Runs test queries against a built Lucene index and prints timing results.
    /// Demonstrates:
    ///   - Single-term search
    ///   - Multi-term AND search
    ///   - Phrase search (the main advantage over Bloom filters)
    ///   - Per-book filter (TermQuery on bookId field)
    /// </summary>
    public sealed class LuceneSearcher : IDisposable
    {
        private readonly IndexReader _indexReader;
        private readonly IndexSearcher _indexSearcher;
        private readonly QueryParser _queryParser;

        public LuceneSearcher(string indexDirectoryPath)
        {
            var directory = FSDirectory.Open(new DirectoryInfo(indexDirectoryPath));
            _indexReader    = DirectoryReader.Open(directory);
            _indexSearcher  = new IndexSearcher(_indexReader);

            // WhitespaceAnalyzer matches what we used at index time
            var analyzer = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);
            _queryParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);

            Console.WriteLine("[LuceneSearcher] Index opened. Documents: " + _indexReader.NumDocs.ToString("N0"));
        }

        /// <summary>
        /// Runs a set of benchmark queries and prints timing for each.
        /// </summary>
        public void RunBenchmark(IEnumerable<string> queries, int maxResultsPerQuery = 100)
        {
            Console.WriteLine();
            Console.WriteLine("=== Search Benchmark ===");
            Console.WriteLine(string.Format("{0,-40} {1,8} {2,10} {3,10}",
                "Query", "Hits", "First(ms)", "Total(ms)"));
            Console.WriteLine(new string('-', 72));

            foreach (string rawQuery in queries)
            {
                RunSingleQuery(rawQuery, maxResultsPerQuery);
            }

            Console.WriteLine();
        }

        private void RunSingleQuery(string rawQuery, int maxResults)
        {
            try
            {
                // Normalize the query the same way we normalized the indexed text
                string normalizedQuery = HebrewTextNormalizer.Normalize(rawQuery);

                // Wrap multi-word queries in AND mode so all terms must appear
                _queryParser.DefaultOperator = QueryParserBase.AND_OPERATOR;

                Query query = _queryParser.Parse(normalizedQuery);

                var stopwatch = Stopwatch.StartNew();
                TopDocs topDocs = _indexSearcher.Search(query, maxResults);
                long firstHitMs = stopwatch.ElapsedMilliseconds;

                // Retrieve lineIds from stored field
                var lineIds = new List<int>(Math.Min(topDocs.ScoreDocs.Length, maxResults));
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    var document = _indexSearcher.Doc(scoreDoc.Doc);
                    string lineIdValue = document.Get("lineId");
                    if (lineIdValue != null)
                        lineIds.Add(int.Parse(lineIdValue));
                }

                stopwatch.Stop();

                Console.WriteLine(string.Format("{0,-40} {1,8:N0} {2,10} {3,10}",
                    TruncateQuery(rawQuery, 40),
                    topDocs.TotalHits,
                    firstHitMs + "ms",
                    stopwatch.ElapsedMilliseconds + "ms"));

                // Show first 3 lineIds so we can verify results are real
                if (lineIds.Count > 0)
                {
                    Console.WriteLine("  → lineIds: " + string.Join(", ", lineIds.GetRange(0, Math.Min(3, lineIds.Count)))
                        + (lineIds.Count > 3 ? " ..." : ""));
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(string.Format("{0,-40} ERROR: {1}", TruncateQuery(rawQuery, 40), exception.Message));
            }
        }

        /// <summary>
        /// Demonstrates a phrase search — impossible with Bloom filters.
        /// </summary>
        public void RunPhraseSearch(string phrase, int maxResults = 50)
        {
            Console.WriteLine();
            Console.WriteLine("=== Phrase Search: \"" + phrase + "\" ===");

            string normalizedPhrase = HebrewTextNormalizer.Normalize(phrase);
            string[] tokens = HebrewTextNormalizer.Tokenize(normalizedPhrase);

            if (tokens.Length < 2)
            {
                Console.WriteLine("  (need at least 2 tokens for a phrase search)");
                return;
            }

            var phraseQuery = new PhraseQuery();
            foreach (string token in tokens)
                phraseQuery.Add(new Term("content", token));

            var stopwatch = Stopwatch.StartNew();
            TopDocs topDocs = _indexSearcher.Search(phraseQuery, maxResults);
            stopwatch.Stop();

            Console.WriteLine(string.Format("  Hits: {0:N0}  Time: {1}ms",
                topDocs.TotalHits, stopwatch.ElapsedMilliseconds));

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var document = _indexSearcher.Doc(scoreDoc.Doc);
                Console.WriteLine("  lineId=" + document.Get("lineId"));
            }
        }

        public void Dispose()
        {
            _indexReader?.Dispose();
        }

        private static string TruncateQuery(string query, int maxLength)
        {
            if (query.Length <= maxLength)
                return query;
            return query.Substring(0, maxLength - 3) + "...";
        }
    }
}
