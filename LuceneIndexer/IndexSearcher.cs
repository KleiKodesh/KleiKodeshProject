using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;

namespace MinimalIndexer
{
    /// <summary>
    /// Searches indexed Hebrew text with score filtering and returns results with metadata.
    /// </summary>
    public sealed class IndexSearcher : IDisposable
    {
        const LuceneVersion VERSION = LuceneVersion.LUCENE_48;
        private readonly Lucene.Net.Search.IndexSearcher _searcher;
        private readonly StandardAnalyzer _analyzer;

        public IndexSearcher(string indexPath)
        {
            var directory = FSDirectory.Open(indexPath);
            var reader = DirectoryReader.Open(directory);
            _searcher = new Lucene.Net.Search.IndexSearcher(reader);
            _analyzer = new StandardAnalyzer(VERSION);
        }

        /// <summary>
        /// Search with score filtering - returns search results with metadata.
        /// </summary>
        /// <param name="queryText">Search query</param>
        /// <param name="maxResults">Maximum number of results to consider</param>
        /// <param name="minScoreRatio">Minimum score as ratio of top score (0.0-1.0). Default 0.1 = 10% of top score</param>
        /// <returns>List of SearchResult containing line IDs, book titles, and TOC text</returns>
        public List<SearchResult> Search(string queryText, int maxResults = 100, float minScoreRatio = 0.1f)
        {
            // Normalize query text before parsing
            string normalizedQuery = TextNormalizer.Normalize(queryText);

            var parser = new QueryParser(VERSION, "content", _analyzer);
            var query = parser.Parse(normalizedQuery);
            var hits = _searcher.Search(query, maxResults).ScoreDocs;

            if (hits.Length == 0)
                return new List<SearchResult>();

            float threshold = hits[0].Score * minScoreRatio;
            var results = new List<SearchResult>(hits.Length);

            foreach (var hit in hits)
            {
                if (hit.Score < threshold)
                    break;

                var doc = _searcher.Doc(hit.Doc);

                results.Add(new SearchResult
                {
                    LineId = doc.GetField("id").GetInt32Value().Value,
                    BookTitle = doc.Get("bookTitle") ?? "",
                    TocText = doc.Get("tocText") ?? "",
                    Score = hit.Score
                });
            }

            return results;
        }

        public void Dispose()
        {
            _searcher.IndexReader.Dispose();
            _analyzer.Dispose();
        }
    }

    /// <summary>
    /// Represents a search result with metadata.
    /// </summary>
    public class SearchResult
    {
        public int LineId { get; set; }
        public string BookTitle { get; set; }
        public string TocText { get; set; }
        public float Score { get; set; }
    }
}