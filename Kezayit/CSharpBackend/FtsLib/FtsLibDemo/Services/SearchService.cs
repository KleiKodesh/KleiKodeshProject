using FtsLib;
using FtsLib.Core;
using FtsLib.Misc;
using FtsLibDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Searches the open index and fetches result rows from the source SQLite DB.
    /// Depends on IIndexService for the live IndexReader.
    /// </summary>
    public sealed class SearchService : ISearchService
    {
        private readonly IIndexService _indexService;

        public SearchService(IIndexService indexService)
        {
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
        }

        public Task<(List<SearchResultItem> rows, string statusMessage)> SearchAsync(
            string query,
            string dbPath,
            IndexReader reader = null)
        {
            var indexReader = reader ?? _indexService.Reader;
            if (indexReader == null)
                return Task.FromResult((new List<SearchResultItem>(), "אין אינדקס פתוח"));

            return Task.Run(() => RunSearch(query, dbPath, indexReader));
        }

        // ── Core search ───────────────────────────────────────────────

        private static (List<SearchResultItem> rows, string statusMessage) RunSearch(
            string      query,
            string      dbPath,
            IndexReader reader)
        {
            var tokenizer = new Tokenizer();
            var terms     = new List<string>(tokenizer.Extract(query));

            if (terms.Count == 0)
                return (new List<SearchResultItem>(), "אין מילות חיפוש תקינות");

            var ids = new List<int>(reader.Search(terms));

            if (ids.Count == 0)
                return (new List<SearchResultItem>(), "לא נמצאו תוצאות");

            var rows = FetchRows(dbPath, ids);
            return (rows, $"נמצאו {rows.Count:N0} תוצאות");
        }

        // ── DB fetch ──────────────────────────────────────────────────

        private static List<SearchResultItem> FetchRows(string dbPath, List<int> ids)
        {
            var rows = new List<SearchResultItem>(ids.Count);

            using (var db = new ZayitDb(dbPath))
            {
                var results = db.FetchSearchResults(ids);
                foreach (var (lineId, lineIndex, heRef, content, bookTitle) in results)
                {
                    string reference = heRef ?? $"שורה {lineIndex}";
                    string snippet   = StripHtml(content);
                    rows.Add(new SearchResultItem(lineId, bookTitle, reference, snippet));
                }
            }

            return rows;
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var  sb    = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
