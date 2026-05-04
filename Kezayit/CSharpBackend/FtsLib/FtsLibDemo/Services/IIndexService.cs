using System;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Manages the FTS index lifecycle: building, opening, and closing.
    /// </summary>
    public interface IIndexService : IDisposable
    {
        /// <summary>True when an index is open and ready to search.</summary>
        bool IsReady { get; }

        /// <summary>
        /// Derives the index directory path for a given DB file.
        /// Stable — same DB always maps to the same index path.
        /// </summary>
        string GetIndexPath(string dbPath);

        /// <summary>
        /// Returns true if a valid index already exists for the given DB file.
        /// </summary>
        bool IndexExists(string dbPath);

        /// <summary>
        /// Builds the index from the given SQLite DB file.
        /// Reports (percentComplete 0–100, detailText) progress.
        /// </summary>
        Task BuildAsync(
            string dbPath,
            IProgress<(double pct, string detail)> progress,
            CancellationToken ct);

        /// <summary>
        /// Opens an existing index so searches can be performed.
        /// Throws if the index does not exist.
        /// </summary>
        void Open(string dbPath);

        /// <summary>Closes the open index reader, if any.</summary>
        void Close();

        /// <summary>
        /// Returns the open IndexReader for use by ISearchService.
        /// Null if not open.
        /// </summary>
        FtsLib.Core.IndexReader Reader { get; }

        /// <summary>
        /// Returns a reader for the live index being built, or null if no segments exist yet.
        /// Used to enable search during indexing.
        /// </summary>
        FtsLib.Core.IndexReader GetLiveReader(string indexPath);
    }
}
