using System;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Manages the FTS index lifecycle: building and opening.
    /// Consumers never touch FtsLib internals — all access goes through this interface.
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

        /// <summary>Returns true if a valid index already exists for the given DB file.</summary>
        bool IndexExists(string dbPath);

        /// <summary>
        /// Builds the index from the given SQLite DB file.
        /// Reports (percentComplete 0–100, detailText) progress.
        /// </summary>
        Task BuildAsync(
            string dbPath,
            IProgress<(double pct, string detail)> progress,
            CancellationToken ct);

        /// <summary>Opens an existing index so searches can be performed.</summary>
        void Open(string dbPath);

        /// <summary>Closes the open index, if any.</summary>
        void Close();

        /// <summary>
        /// The active <see cref="FtsLib.SeforimDb.SeforimIndex"/> instance.
        /// Null when no index is open. Used by ISearchService.
        /// </summary>
        FtsLib.SeforimDb.SeforimIndex Index { get; }

        /// <summary>
        /// Opens a temporary index for the partially-built index path during a live build.
        /// Returns null if no segments exist yet.
        /// </summary>
        FtsLib.SeforimDb.SeforimIndex GetLiveIndex(string indexPath, string dbPath);
    }
}
