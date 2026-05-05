using FtsLib.Core;
using FtsLib.Misc;
using FtsLibDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Searches the open index and streams result rows from the source SQLite DB in batches.
    /// </summary>
    public sealed class SearchService : ISearchService
    {
        private const int BatchSize = 200;

        private readonly IIndexService _indexService;

        public SearchService(IIndexService indexService)
        {
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
        }

        public Task<string> SearchStreamingAsync(
            string query,
            string dbPath,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken ct,
            IndexReader reader = null)
        {
            var indexReader = reader ?? _indexService.Reader;
            if (indexReader == null)
                return Task.FromResult("אין אינדקס פתוח");

            return Task.Run(() => RunSearch(query, dbPath, indexReader, onBatch, ct), ct);
        }

        // ── Core search ───────────────────────────────────────────────

        private static string RunSearch(
            string query,
            string dbPath,
            IndexReader reader,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken ct)
        {
            var tokenizer = new Tokenizer();
            var terms     = new List<string>(tokenizer.Extract(query));

            if (terms.Count == 0)
                return "אין מילות חיפוש תקינות";

            var ids = new List<int>(reader.Search(terms));

            if (ids.Count == 0)
                return "לא נמצאו תוצאות";

            int total = ids.Count;
            StreamRows(dbPath, ids, onBatch, ct);

            return ct.IsCancellationRequested
                ? "החיפוש בוטל"
                : $"נמצאו {total:N0} תוצאות";
        }

        // ── Streaming DB fetch ────────────────────────────────────────

        private static void StreamRows(
            string dbPath,
            List<int> ids,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken ct)
        {
            using (var db = new ZayitDb(dbPath))
            {
                var batch = new List<SearchResultItem>(BatchSize);

                foreach (var (lineId, content, bookTitle) in db.FetchSearchResults(ids))
                {
                    if (ct.IsCancellationRequested) break;

                    string snippet = StripHtml(content);
                    batch.Add(new SearchResultItem(lineId, bookTitle, snippet));

                    if (batch.Count >= BatchSize)
                    {
                        onBatch(batch);
                        batch = new List<SearchResultItem>(BatchSize);
                    }
                }

                if (batch.Count > 0)
                    onBatch(batch);
            }
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
