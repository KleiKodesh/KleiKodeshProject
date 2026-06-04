using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace SearchEngine.SeforimDb
{
    /// <summary>
    /// Wraps the SQLite database used by the Zayit / Otzaria Torah study apps.
    /// </summary>
    public sealed class ZayitDb : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly string _dbPath;
        private bool _disposed;

        public bool   IsOpen => _connection != null;
        public string DbPath => _dbPath;

        public ZayitDb(string dbPath = null)
        {
            string resolved = ResolveDbPath(dbPath);
            _dbPath = resolved;
            if (!File.Exists(resolved))
                return;
            _connection = new SQLiteConnection($"Data Source={resolved};Version=3;Page Size=4096;");
            _connection.Open();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA journal_mode=WAL;"  +
                    "PRAGMA cache_size=-65536;" +   // 64 MB page cache
                    "PRAGMA temp_store=MEMORY;" +
                    "PRAGMA mmap_size=268435456;";  // 256 MB memory-mapped I/O
                cmd.ExecuteNonQuery();
            }
        }

        // ── Counting ──────────────────────────────────────────────────

        public long CountLines()
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (long)cmd.ExecuteScalar();
            }
        }

        public long CountLinesUpTo(int upToId)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line WHERE id <= @id";
                cmd.Parameters.AddWithValue("@id", upToId);
                return (long)cmd.ExecuteScalar();
            }
        }

        // ── Batch metadata fetch (minimal-index search path) ─────────

        /// <summary>
        /// Returns a map of lineId → bookId for the given line IDs in one query.
        /// Used by the minimal-index search path to enrich snippet results with
        /// book identity before fetching book titles.
        /// </summary>
        public Dictionary<int, int> GetBookIdsByLineIds(IList<int> lineIds)
        {
            EnsureOpen();
            var result = new Dictionary<int, int>(lineIds.Count);
            if (lineIds.Count == 0) return result;

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, bookId FROM line WHERE id IN (" +
                    BuildIntegerList(lineIds) + ")";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        result[r.GetInt32(0)] = r.GetInt32(1);
            }
            return result;
        }

        /// <summary>
        /// Returns a map of bookId → bookTitle for the given book IDs in one query.
        /// Used by the minimal-index search path after <see cref="GetBookIdsByLineIds"/>
        /// to resolve titles for a batch of results.
        /// </summary>
        public Dictionary<int, string> GetBookTitlesByBookIds(IList<int> bookIds)
        {
            EnsureOpen();
            var result = new Dictionary<int, string>(bookIds.Count);
            if (bookIds.Count == 0) return result;

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, title FROM book WHERE id IN (" +
                    BuildIntegerList(bookIds) + ")";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        result[r.GetInt32(0)] = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            }
            return result;
        }

        /// <summary>
        /// Builds a comma-separated list of integers for an IN clause.
        /// Only safe for integer values — never use with string input.
        /// </summary>
        private static string BuildIntegerList(IList<int> ids)
        {
            var sb = new StringBuilder(ids.Count * 6);
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            return sb.ToString();
        }

        // ── Single-line fetch (search-time content for snippets) ──────

        /// <summary>
        /// Fetches the raw content of a single line by id.
        /// Called per-hit during snippet generation — the only remaining DB access at search time.
        /// </summary>
        public string GetLineById(int id)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT content FROM line WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : (string)result;
            }
        }

        // ── Book-by-book indexing ─────────────────────────────────────

        /// <summary>
        /// Returns all (bookId, bookTitle) pairs ordered by id.
        /// Used as the outer loop for book-by-book index building.
        /// </summary>
        public IEnumerable<(int BookId, string BookTitle)> ReadAllBooks()
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, title FROM book ORDER BY id";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
            }
        }

        /// <summary>
        /// Streams all lines for a single book ordered by lineIndex.
        /// </summary>
        public IEnumerable<(int Id, string Content)> ReadLinesForBook(int bookId)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, content FROM line WHERE bookId = @bookId ORDER BY lineIndex";
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
            }
        }

        /// <summary>
        /// Builds a map of lineId → tocPath for a single book.
        ///
        /// Strategy:
        ///   1. Load all tocEntry rows for the book in one query (id, parentId, lineId, text).
        ///   2. Build an entryId → (parentId, text) lookup.
        ///   3. For each entry that has a lineId, resolve its full ancestor path via
        ///      <see cref="ResolveTocPath"/>. Intermediate ancestor paths are memoised
        ///      in a shared cache so sibling subtrees never re-walk shared ancestors.
        ///
        /// The path is assembled root→leaf, segments joined by " › ",
        /// with the root segment stripped when it duplicates the book title.
        /// </summary>
        public Dictionary<int, string> BuildTocPathMap(int bookId, string bookTitle)
        {
            EnsureOpen();

            // Load all tocEntry rows for this book in one query.
            var entryById  = new Dictionary<int, (int? ParentId, string Text)>();
            var lineToEntry = new Dictionary<int, int>(); // lineId → entryId

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT te.id, te.parentId, te.lineId, tt.text " +
                    "FROM tocEntry te " +
                    "JOIN tocText tt ON tt.id = te.textId " +
                    "WHERE te.bookId = @bookId " +
                    "ORDER BY te.id";
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        int  id       = r.GetInt32(0);
                        int? parentId = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                        int? lineId   = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                        string text   = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                        entryById[id] = (parentId, text);
                        if (lineId.HasValue)
                            lineToEntry[lineId.Value] = id;
                    }
            }

            if (entryById.Count == 0)
                return new Dictionary<int, string>();

            // Resolve paths for all anchored lines, sharing a memoisation cache and
            // a reusable chain buffer across all calls.
            var pathCache  = new Dictionary<int, string>(entryById.Count);
            var chainBuffer = new List<int>(16); // reused across ResolveTocPath calls
            var lineToPath = new Dictionary<int, string>(lineToEntry.Count);

            foreach (var kv in lineToEntry)
            {
                string path = ResolveTocPath(kv.Value, entryById, pathCache, chainBuffer, bookTitle);
                lineToPath[kv.Key] = path;
            }

            return lineToPath;
        }

        /// <summary>
        /// Resolves the full root→leaf path for <paramref name="entryId"/>,
        /// memoising every node along the chain so sibling subtrees cost O(1).
        /// <paramref name="chainBuffer"/> is a caller-supplied list that is cleared
        /// and reused on each call to avoid per-call allocation.
        /// </summary>
        private static string ResolveTocPath(
            int                                            entryId,
            Dictionary<int, (int? ParentId, string Text)> entryById,
            Dictionary<int, string>                        cache,
            List<int>                                      chainBuffer,
            string                                         bookTitle)
        {
            if (cache.TryGetValue(entryId, out string cached))
                return cached;

            // Walk up to root, collecting entry ids bottom→top into the reused buffer.
            chainBuffer.Clear();
            int current = entryId;
            while (true)
            {
                chainBuffer.Add(current);
                if (!entryById.TryGetValue(current, out var entry)) break;
                if (entry.ParentId == null) break;
                current = entry.ParentId.Value;
            }

            // Reverse to root→leaf order.
            chainBuffer.Reverse();

            // Find the deepest already-cached ancestor — we can start building from there.
            int    startIndex    = 0;
            string prefixPath    = string.Empty;
            for (int i = 0; i < chainBuffer.Count - 1; i++)
            {
                if (cache.TryGetValue(chainBuffer[i], out string existing))
                {
                    prefixPath = existing;
                    startIndex = i + 1;
                    // Don't break — keep scanning for a deeper cached ancestor.
                }
            }

            // Build path segments from startIndex to leaf, caching each node.
            var sb = new StringBuilder(prefixPath.Length + 64);
            sb.Append(prefixPath);

            for (int i = startIndex; i < chainBuffer.Count; i++)
            {
                if (!entryById.TryGetValue(chainBuffer[i], out var entry)) break;

                // Strip root segment when it duplicates the book title.
                bool isRoot = i == 0;
                if (isRoot && string.Equals(entry.Text, bookTitle, StringComparison.OrdinalIgnoreCase))
                {
                    cache[chainBuffer[i]] = string.Empty;
                    continue;
                }

                if (sb.Length > 0) sb.Append(" \u203a "); // " › "
                sb.Append(entry.Text);

                cache[chainBuffer[i]] = sb.ToString();
            }

            string path = sb.ToString();
            cache[entryId] = path;
            return path;
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection?.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string ResolveDbPath(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath)) return dbPath;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string def     = Path.Combine(appData, "io.github.kdroidfilter.seforimapp",
                                          "databases", "seforim.db");
            return Interaction.GetSetting("KitveiHakodesh", "Database", "Path", def);
        }

        private void EnsureOpen()
        {
            if (_connection == null)
                throw new InvalidOperationException("ZayitDb: database file was not found at open time.");
        }
    }
}
