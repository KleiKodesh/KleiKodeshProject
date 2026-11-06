using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Makes a library usable whose LINE ids and/or TOC-ENTRY ids are too large for the global id
    /// scheme (<see cref="SeforimDbLibraryId"/> gives every library a band of
    /// <see cref="SeforimDbLibraryId.OffsetUnit"/> ids inside int32), by translating them.
    ///
    /// Libraries built to be merged into the official database often reserve a private, very
    /// high range of ids so they never collide: one published database stores
    /// <c>line.id = bookId * 1,048,576 + lineIndex + 1</c> and the same for <c>tocEntry.id</c>, i.e.
    /// ids up to ten BILLION for a few million rows. Such ids cannot be offset into int32 at all. These
    /// ids are only opaque handles, though, and in such a library each book's rows are consecutive
    /// ids - so a few numbers per book (where its ids start, how many rows it has) are enough to
    /// translate between the id stored in the file (<see cref="ToStored"/>) and a dense LOCAL id
    /// 1..Total (<see cref="ToLocal"/>), which then goes through the normal band scheme. The file is
    /// never modified, and nothing is scanned row by row: blocks are read with one grouped lookup.
    ///
    /// One map per translated id kind (lines, TOC entries). Book and category ids are small in such
    /// libraries and keep using the plain band scheme; a library whose OTHER ids do not fit is still
    /// rejected.
    /// </summary>
    public sealed class SeforimDbIdMap
    {
        private readonly long[] _storedStart;   // ascending: the stored id of each block's first row
        private readonly int[] _count;          // rows in the block
        private readonly int[] _localStart;     // ascending: the dense local id of each block's first row (1-based)

        private SeforimDbIdMap(long[] storedStart, int[] count, int[] localStart, int total)
        {
            _storedStart = storedStart;
            _count = count;
            _localStart = localStart;
            Total = total;
        }

        /// <summary>Number of rows the map covers; the dense local ids are 1..Total.</summary>
        public int Total { get; }

        /// <summary>Number of books (blocks) with at least one row.</summary>
        public int BlockCount => _storedStart.Length;

        /// <summary>The dense local id of an id as stored in the file, or 0 when the stored id is
        /// not a row of this library.</summary>
        public int ToLocal(long stored)
        {
            int i = LastAtOrBefore(_storedStart, stored);
            if (i < 0) return 0;
            long offset = stored - _storedStart[i];
            return offset < _count[i] ? _localStart[i] + (int)offset : 0;
        }

        /// <summary>The id stored in the file for a dense local id, or -1 when the local id is outside
        /// 1..<see cref="Total"/> (binds to nothing, so a stale id finds no row).</summary>
        public long ToStored(int local)
        {
            if (local < 1 || local > Total) return -1;
            int i = LastAtOrBefore(_localStart, local);
            return i < 0 ? -1 : _storedStart[i] + (local - _localStart[i]);
        }

        /// <summary>Index of the last element that is &lt;= <paramref name="value"/>, or -1.</summary>
        private static int LastAtOrBefore(long[] sorted, long value)
        {
            int lo = 0, hi = sorted.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (sorted[mid] <= value) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        private static int LastAtOrBefore(int[] sorted, int value)
        {
            int lo = 0, hi = sorted.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (sorted[mid] <= value) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        /// <summary>
        /// The LINE-id map, from a connection whose tables are reachable as
        /// <paramref name="schemaPrefix"/> + "line" / "book" ("" for a connection opened on the file itself,
        /// "lib3." for an ATTACHed library). Returns null, with the reason in <paramref name="failure"/>,
        /// when the library does not have the layout this needs: every book's lines must be consecutive
        /// ids that follow lineIndex, books must not share ids, and the dense ids must fit the band.
        /// </summary>
        public static SeforimDbIdMap? TryBuildForLines(SqliteConnection connection, string schemaPrefix, out string? failure)
        {
            failure = null;
            var blocks = new List<(long First, int Count, int BookId)>();

            using (var command = connection.CreateCommand())
            {
                // One row per book, every value an index lookup on (bookId, lineIndex): no scan of
                // the line table, so this is milliseconds even for a multi-gigabyte library.
                command.CommandText =
                    $"SELECT b.id," +
                    $" (SELECT l.id FROM {schemaPrefix}line l WHERE l.bookId = b.id AND l.lineIndex = 0)," +
                    $" (SELECT l.lineIndex FROM {schemaPrefix}line l WHERE l.bookId = b.id ORDER BY l.lineIndex DESC LIMIT 1)," +
                    $" (SELECT l.id FROM {schemaPrefix}line l WHERE l.bookId = b.id ORDER BY l.lineIndex DESC LIMIT 1)," +
                    $" (SELECT COUNT(*) FROM {schemaPrefix}line l WHERE l.bookId = b.id)" +
                    $" FROM {schemaPrefix}book b";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int bookId = reader.GetInt32(0);
                    long count = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                    if (count == 0) continue;           // a book without lines owns no ids
                    if (reader.IsDBNull(1))
                    {
                        // Lines exist but none is lineIndex 0: the layout this map relies on is not there,
                        // and silently leaving the book out would make its real ids translate to 0.
                        failure = $"book {bookId} has lines but no line with lineIndex 0";
                        return null;
                    }
                    long firstId = reader.GetInt64(1);
                    long lastIndex = reader.GetInt64(2);
                    long lastId = reader.GetInt64(3);
                    if (lastId - firstId != lastIndex || count != lastIndex + 1)
                    {
                        failure = $"line ids of book {bookId} are not consecutive";
                        return null;
                    }
                    if (count > int.MaxValue) { failure = $"book {bookId} has too many lines"; return null; }
                    blocks.Add((firstId, (int)count, bookId));
                }
            }
            return Assemble(blocks, "lines", out failure);
        }

        /// <summary>
        /// The TOC-ENTRY id map (<c>tocEntry.id</c>), same contract as <see cref="TryBuildForLines"/>:
        /// each book's entries must be consecutive ids and books must not share ids. A TOC entry's
        /// parent, its line_toc rows and line.tocEntryId all hold these same stored ids.
        /// </summary>
        public static SeforimDbIdMap? TryBuildForTocEntries(SqliteConnection connection, string schemaPrefix, out string? failure)
        {
            failure = null;
            var blocks = new List<(long First, int Count, int BookId)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    $"SELECT bookId, MIN(id), MAX(id), COUNT(*) FROM {schemaPrefix}tocEntry GROUP BY bookId";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0)) { failure = "a TOC entry has no book"; return null; }
                    int bookId = reader.GetInt32(0);
                    long first = reader.GetInt64(1), last = reader.GetInt64(2), count = reader.GetInt64(3);
                    if (last - first + 1 != count)
                    {
                        failure = $"TOC entry ids of book {bookId} are not consecutive";
                        return null;
                    }
                    if (count > int.MaxValue) { failure = $"book {bookId} has too many TOC entries"; return null; }
                    blocks.Add((first, (int)count, bookId));
                }
            }
            return Assemble(blocks, "TOC entries", out failure);
        }

        private static SeforimDbIdMap? Assemble(List<(long First, int Count, int BookId)> blocks, string what, out string? failure)
        {
            failure = null;
            blocks.Sort((a, b) => a.First.CompareTo(b.First));
            var storedStart = new long[blocks.Count];
            var counts = new int[blocks.Count];
            var localStart = new int[blocks.Count];
            long running = 1;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0 && blocks[i].First <= blocks[i - 1].First + blocks[i - 1].Count - 1)
                {
                    failure = $"books {blocks[i - 1].BookId} and {blocks[i].BookId} share {what} ids";
                    return null;
                }
                storedStart[i] = blocks[i].First;
                counts[i] = blocks[i].Count;
                localStart[i] = (int)running;
                running += blocks[i].Count;
                if (running - 1 >= SeforimDbLibraryId.OffsetUnit)
                {
                    failure = $"the library has more {what} than an additional library may hold";
                    return null;
                }
            }
            return new SeforimDbIdMap(storedStart, counts, localStart, (int)(running - 1));
        }

        /// <summary>A short stable digest of the whole map (every block's start and size). Two libraries with the
        /// same min/max/count can still lay their blocks out differently; an index built over one must not be
        /// trusted for the other, because local ids are positions in this layout.</summary>
        public string Signature()
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;           // FNV-1a over (start, count) of every block
                for (int i = 0; i < _storedStart.Length; i++)
                {
                    h = (h ^ (ulong)_storedStart[i]) * 1099511628211UL;
                    h = (h ^ (ulong)(uint)_count[i]) * 1099511628211UL;
                }
                return Total.ToString(CultureInfo.InvariantCulture) + "/" + BlockCount.ToString(CultureInfo.InvariantCulture) +
                       "/" + h.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "SeforimDbIdMap({0:N0} rows in {1:N0} books)", Total, BlockCount);
    }
}
