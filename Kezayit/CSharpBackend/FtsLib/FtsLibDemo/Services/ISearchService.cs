using FtsLibDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Executes a search against the open index and fetches display rows from the DB.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Tokenizes the query, searches the index, fetches matching lines from the DB,
        /// and streams batches of results via <paramref name="onBatch"/> as they arrive.
        /// Calls <paramref name="onBatch"/> on the thread-pool; callers must marshal to the UI thread.
        /// Returns the final status message.
        /// </summary>
        Task<string> SearchStreamingAsync(
            string query,
            string dbPath,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken ct,
            FtsLib.Core.IndexReader reader = null);
    }
}
