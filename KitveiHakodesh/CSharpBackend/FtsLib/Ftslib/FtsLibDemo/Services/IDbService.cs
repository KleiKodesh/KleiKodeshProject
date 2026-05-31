using System;
using System.Collections.Generic;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Provides access to the seforim SQLite database.
    /// Wraps ZayitDb for centralized database operations.
    /// ZayitDb refers to the external Zayit/Otzaria app's database — not this app's old name.
    /// </summary>
    public interface IDbService : IDisposable
    {
        /// <summary>
        /// Opens the database at the given path.
        /// </summary>
        void Open(string dbPath);

        /// <summary>
        /// Gets the total number of lines in the database.
        /// </summary>
        long CountLines();

        /// <summary>
        /// Gets the content of a single line by ID.
        /// </summary>
        string GetLineContent(int id);

        /// <summary>
        /// Streams all lines from the database in ID order.
        /// </summary>
        IEnumerable<(int Id, string Content)> ReadLines(int limit = 0);

        /// <summary>
        /// Fetches search result rows by line IDs.
        /// </summary>
        List<SearchResultItem> FetchSearchResults(List<int> ids);
    }
}
