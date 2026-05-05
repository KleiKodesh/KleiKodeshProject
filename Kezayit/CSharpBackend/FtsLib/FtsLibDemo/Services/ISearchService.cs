using FtsLib.Seforim;
using FtsLibDemo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Executes a search against the open index and streams display rows from the DB.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Parses the query, searches the index, fetches matching lines from the DB,
        /// and streams batches of results via <paramref name="onBatch"/> as they arrive.
        /// Calls <paramref name="onBatch"/> on the thread-pool; callers must marshal to the UI thread.
        /// Returns the final status message.
        /// </summary>
        /// <param name="index">
        /// The <see cref="SeforimIndex"/> to search against.
        /// Pass the live index during a build, or the open index for normal searches.
        /// </param>
        Task<string> SearchStreamingAsync(
            string                                  query,
            Action<IReadOnlyList<SearchResultItem>> onBatch,
            CancellationToken                       ct,
            SeforimIndex                            index,
            int                                     maxWordDistance = 10,
            bool                                    requireOrdered  = false);
    }
}
