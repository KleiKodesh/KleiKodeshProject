using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using KitveiHakodesh.Core.Common;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Each book's precedence (<see cref="SeforimDbBookTreeOrder"/>) SAVED, so ordering a handful of
    /// books is a primary-key lookup instead of loading every category and book to walk the tree.
    ///
    /// It lives in the COMPANION database: a sidecar SQLite file of ours, in the app's own folder
    /// (AppFileLocator, never beside the library), attached to every library connection as schema
    /// "companion" - so SQL joins it like part of the library while the library file is never
    /// written. (Writing DDL into the library file moved its schema version, which the search-index
    /// stamps read, and would have rebuilt both indexes for every user.)
    ///
    /// THINKING only (see Core/CLAUDE.md 5a): where the file is, when to rebuild, what counts as
    /// stale. The SQL is in SeforimDbSqlStrings.cs, the calls (ReadBookTreeOrderState,
    /// WriteBookTreeOrder, AttachCompanionIfPresent, ...) in SeforimDbQueries.cs.
    ///
    /// Two small tables in the companion:
    ///   book_tree_order(bookId PRIMARY KEY, treeOrder)   - the numbers
    ///   book_tree_order_state(signature)                 - what library content they were built from
    ///
    /// One companion file PER LIBRARY (a different library has other books and orders). Rebuilt
    /// whenever the library's content changes: an Otzaria or Zayit update, a patch or a whole new
    /// download of our own library. The library's file-changed stamp tells us to look again; the
    /// signature (totals over the rows the walk reads) tells us whether the numbers are still right.
    ///
    /// NO FALLBACK: when the companion cannot be written (the app folder is read-only) there is no
    /// treeOrder - the readers answer nothing, nothing computes the order in memory instead.
    /// <see cref="BookTreeOrderUnavailableReason"/> says why.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        private readonly object _treeOrderLock = new();
        /// <summary>The LIBRARY file stamp the companion was last verified (or failed) for.</summary>
        private (long Length, long Ticks) _treeOrderStamp;
        private bool _treeOrderReady;
        private string? _treeOrderUnavailableReason;

        /// <summary>Why the stored tree order is not available (null when it is, or not tried yet).</summary>
        public string? BookTreeOrderUnavailableReason
        {
            get { lock (_treeOrderLock) return _treeOrderUnavailableReason; }
        }

        /// <summary>
        /// The companion database file of THIS library: inside the app folder, one file per library
        /// path (a library switch must not read another library's orders).
        /// </summary>
        private string CompanionDatabasePath()
        {
            string library = Path.GetFullPath(_databasePath!).ToLowerInvariant();
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(library)))[..16].ToLowerInvariant();
            return AppFileLocator.ResolveWritablePath(Path.Combine("seforim-companion", "library-" + key + ".db"));
        }

        /// <summary>
        /// Makes sure the stored tree order exists and matches the library, building it when it
        /// does not. Cheap when nothing changed (a stat of the library file). A failure to write is
        /// not thrown: it returns false and keeps the reason, and is not retried until the library
        /// file changes.
        /// </summary>
        public bool EnsureBookTreeOrder()
        {
            if (!IsAvailable) return false;
            lock (_treeOrderLock)
            {
                (long Length, long Ticks) stamp = default;
                try
                {
                    stamp = DatabaseFileStamp();
                    if (stamp == _treeOrderStamp) return _treeOrderReady;
                    BuildBookTreeOrderIfStale();
                    _treeOrderReady = true;
                    _treeOrderUnavailableReason = null;
                }
                catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
                {
                    _treeOrderReady = false;
                    _treeOrderUnavailableReason = e.Message;
                }
                _treeOrderStamp = stamp;
                return _treeOrderReady;
            }
        }

        /// <summary>bookId -> tree order for the given books. Empty when the stored order is not
        /// available (see the class remarks); a book the library does not have is absent.</summary>
        public Dictionary<int, int> GetBookTreeOrders(List<int> bookIds)
        {
            if (bookIds is null || bookIds.Count == 0 || !EnsureBookTreeOrder()) return new Dictionary<int, int>();
            return ReadBookTreeOrders(bookIds);
        }

        private (long Length, long Ticks) DatabaseFileStamp()
        {
            var file = new FileInfo(_databasePath!);
            return (file.Length, file.LastWriteTimeUtc.Ticks);
        }

        /// <summary>The walk and the write, only when the companion's tables are missing or were built
        /// from other library content. The walk is the one whole-library read, once per library change.</summary>
        private void BuildBookTreeOrderIfStale()
        {
            var state = ReadBookTreeOrderState();
            string signature = LibraryOrderSignature(state);
            if (state.TablesExist && string.Equals(state.StoredSignature, signature, StringComparison.Ordinal)) return;

            string companionPath = CompanionDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(companionPath)!);
            WriteBookTreeOrder(ReadBookTreeOrderFromLibrary(), signature, companionPath);
        }

        /// <summary>
        /// The library's totals over exactly what the walk reads - which categories exist, under
        /// which parent, in which sibling order; which books, in which category, in which order,
        /// which are base texts - written as one string. Weighted by id (in the SQL), so swapping
        /// two rows' positions changes it.
        /// </summary>
        private static string LibraryOrderSignature(BookTreeOrderState state) =>
            "v1;c:" + string.Join(",", Array.ConvertAll(state.CategoryTotals, n => n.ToString(CultureInfo.InvariantCulture)))
            + ";b:" + string.Join(",", Array.ConvertAll(state.BookTotals, n => n.ToString(CultureInfo.InvariantCulture)));
    }
}
