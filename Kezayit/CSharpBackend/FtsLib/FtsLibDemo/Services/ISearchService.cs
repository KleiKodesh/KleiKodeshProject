using FtsLibDemo.ViewModels;
using System.Collections.Generic;
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
        /// and returns them as display-ready items.
        /// Returns an empty list (never null) when there are no results.
        /// If reader is provided, uses it instead of the service's default reader.
        /// </summary>
        Task<(List<SearchResultItem> rows, string statusMessage)> SearchAsync(
            string query,
            string dbPath,
            FtsLib.Core.IndexReader reader = null);
    }
}
