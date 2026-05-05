using FtsLib.Seforim;
using FtsLibDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Searches the open index and streams result rows in batches.
    /// All query parsing, wildcard/fuzzy expansion, and DB access are handled
    /// internally by <see cref="SeforimIndex"/> — this service only drives batching.
    /// </summary>
    public sealed class SearchService : ISearchService
    {
        private const int BatchSize = 200;

        public Task<string> SearchStreamingAsync(
            string                                  query,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken                       ct,
            SeforimIndex                            index,
            int                                     maxWordDistance = 10,
            bool                                    requireOrdered  = false)
        {
            if (index == null)
                return Task.FromResult("אין אינדקס פתוח");

            return Task.Run(() => RunSearch(query, index, onBatch, ct, maxWordDistance, requireOrdered), ct);
        }

        private static string RunSearch(
            string                                  query,
            SeforimIndex                            index,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken                       ct,
            int                                     maxWordDistance,
            bool                                    requireOrdered)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "אין מילות חיפוש תקינות";

            var batch = new List<SearchResultItem>(BatchSize);
            int total = 0;

            foreach (var result in index.Search(query, ct: ct))
            {
                if (ct.IsCancellationRequested) break;

                var snippet = index.GenerateSnippet(result, requireOrdered);

                // Filter: drop results where the query terms are too far apart,
                // or (in ordered mode) where terms don't appear in query order.
                if (!snippet.IsMatch || snippet.WordDistance > maxWordDistance)
                    continue;

                batch.Add(new SearchResultItem(result.LineId, result.BookTitle, snippet.Html));
                total++;

                if (batch.Count >= BatchSize)
                {
                    onBatch(batch);
                    batch = new List<SearchResultItem>(BatchSize);
                }
            }

            if (batch.Count > 0 && !ct.IsCancellationRequested)
                onBatch(batch);

            if (total == 0)
                return "לא נמצאו תוצאות";

            return ct.IsCancellationRequested
                ? "החיפוש בוטל"
                : $"נמצאו {total:N0} תוצאות";
        }
    }
}
