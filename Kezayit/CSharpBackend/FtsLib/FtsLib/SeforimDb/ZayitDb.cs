using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace FtsLib.SeforimDb
{
    internal sealed class ZayitDb : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly string _dbPath;
        private bool _disposed;

        public bool IsOpen => _connection != null;
        public string DbPath => _dbPath;

        public ZayitDb(string dbPath = null)
        {
            string resolved = ResolveDbPath(dbPath);
            _dbPath = resolved;
            if (!File.Exists(resolved))
            {
                Console.WriteLine($"[ZayitDb] Database not found: {resolved}");
                return;
            }

            Console.WriteLine($"[ZayitDb] Opening: {resolved}");
            _connection = new SQLiteConnection($"Data Source={resolved};Version=3;Page Size=4096;");
            _connection.Open();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA journal_mode=WAL;" +
                    "PRAGMA cache_size=-65536;" +  // up to 64 MB page cache
                    "PRAGMA temp_store=MEMORY;" +
                    "PRAGMA mmap_size=268435456;"; // 256 MB memory-mapped I/O
                cmd.ExecuteNonQuery();
            }
        }

        // ── Indexing helpers ──────────────────────────────────────────

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

        public string GetLineContent(int id)
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

        public IEnumerable<(int Id, string Content)> ReadLines(int limit,
            System.Threading.CancellationToken ct = default)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = limit > 0
                    ? "SELECT id, content FROM line ORDER BY id LIMIT @lim"
                    : "SELECT id, content FROM line ORDER BY id";
                if (limit > 0) cmd.Parameters.AddWithValue("@lim", limit);

                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
                    }
            }
        }

        public IEnumerable<(int Id, string Content)> ReadLinesFrom(int afterId, int limit = 0,
            System.Threading.CancellationToken ct = default)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = limit > 0
                    ? "SELECT id, content FROM line WHERE id > @after ORDER BY id LIMIT @lim"
                    : "SELECT id, content FROM line WHERE id > @after ORDER BY id";
                cmd.Parameters.AddWithValue("@after", afterId);
                if (limit > 0) cmd.Parameters.AddWithValue("@lim", limit);

                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
                    }
            }
        }

        // ── Search result fetching ────────────────────────────────────

        /// <summary>
        /// Fetches all results for a pre-materialized ID list.
        /// Book title is resolved via JOIN — no separate title load needed.
        /// Chunks to stay within SQLite's variable limit (999).
        /// </summary>
        public IEnumerable<(int Id, string Content, string BookTitle)>
            FetchSearchResults(List<int> ids)
        {
            EnsureOpen();
            if (ids.Count == 0) yield break;

            const int ChunkSize = 999;
            using (var cmd = _connection.CreateCommand())
            {
                var paramNames = new string[ChunkSize];
                for (int i = 0; i < ChunkSize; i++)
                {
                    paramNames[i] = $"@p{i}";
                    cmd.Parameters.Add(paramNames[i], System.Data.DbType.Int32);
                }

                for (int start = 0; start < ids.Count; start += ChunkSize)
                {
                    int end   = Math.Min(start + ChunkSize, ids.Count);
                    int count = end - start;

                    var sb = new System.Text.StringBuilder(
                        "SELECT l.id, l.content, b.title" +
                        " FROM line l JOIN book b ON b.id = l.bookId" +
                        " WHERE l.id IN (");
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(paramNames[i]);
                        cmd.Parameters[paramNames[i]].Value = ids[start + i];
                    }
                    sb.Append(") ORDER BY l.id");
                    cmd.CommandText = sb.ToString();

                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            yield return (
                                r.GetInt32(0),
                                r.IsDBNull(1) ? string.Empty : r.GetString(1),
                                r.IsDBNull(2) ? string.Empty : r.GetString(2));
                }
            }
        }

        /// <summary>
        /// Streaming overload — accepts a lazy ID sequence and fetches rows in batches
        /// of 200, yielding results as each batch completes.
        /// IDs are assumed to arrive in ascending order (as produced by the index
        /// intersection) — no ORDER BY needed.
        /// Book title is resolved via JOIN.
        /// </summary>
        public IEnumerable<(int Id, string Content, string BookTitle)>
            FetchSearchResultsStreaming(IEnumerable<int> ids)
        {
            EnsureOpen();

            const int ChunkSize = 200;
            var chunk = new List<int>(ChunkSize);

            using (var cmd = _connection.CreateCommand())
            {
                var paramNames = new string[ChunkSize];
                for (int i = 0; i < ChunkSize; i++)
                {
                    paramNames[i] = $"@p{i}";
                    cmd.Parameters.Add(paramNames[i], System.Data.DbType.Int32);
                }

                foreach (int id in ids)
                {
                    chunk.Add(id);
                    if (chunk.Count == ChunkSize)
                    {
                        foreach (var row in FetchChunk(cmd, paramNames, chunk))
                            yield return row;
                        chunk.Clear();
                    }
                }

                if (chunk.Count > 0)
                {
                    foreach (var row in FetchChunk(cmd, paramNames, chunk))
                        yield return row;
                }
            }
        }

        private static IEnumerable<(int Id, string Content, string BookTitle)> FetchChunk(
            SQLiteCommand cmd,
            string[]      paramNames,
            List<int>     ids)
        {
            var sb = new System.Text.StringBuilder(
                "SELECT l.id, l.content, b.title" +
                " FROM line l JOIN book b ON b.id = l.bookId" +
                " WHERE l.id IN (");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(paramNames[i]);
                cmd.Parameters[paramNames[i]].Value = ids[i];
            }
            sb.Append(")");
            cmd.CommandText = sb.ToString();

            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    yield return (
                        r.GetInt32(0),
                        r.IsDBNull(1) ? string.Empty : r.GetString(1),
                        r.IsDBNull(2) ? string.Empty : r.GetString(2));
        }

        /// <summary>
        /// Fetches only id + bookTitle — no content column.
        /// Use when content is not needed (counting, ID-only pipelines).
        /// </summary>
        public IEnumerable<(int Id, string BookTitle)>
            FetchSearchResultsNoContent(List<int> ids)
        {
            EnsureOpen();
            if (ids.Count == 0) yield break;

            const int ChunkSize = 999;
            using (var cmd = _connection.CreateCommand())
            {
                var paramNames = new string[ChunkSize];
                for (int i = 0; i < ChunkSize; i++)
                {
                    paramNames[i] = $"@p{i}";
                    cmd.Parameters.Add(paramNames[i], System.Data.DbType.Int32);
                }

                for (int start = 0; start < ids.Count; start += ChunkSize)
                {
                    int end   = Math.Min(start + ChunkSize, ids.Count);
                    int count = end - start;

                    var sb = new System.Text.StringBuilder(
                        "SELECT l.id, b.title" +
                        " FROM line l JOIN book b ON b.id = l.bookId" +
                        " WHERE l.id IN (");
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(paramNames[i]);
                        cmd.Parameters[paramNames[i]].Value = ids[start + i];
                    }
                    sb.Append(") ORDER BY l.bookId, l.lineIndex");
                    cmd.CommandText = sb.ToString();

                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            yield return (
                                r.GetInt32(0),
                                r.IsDBNull(1) ? string.Empty : r.GetString(1));
                }
            }
        }

        // ── Diagnostic / test helpers ─────────────────────────────────

        public List<(long Id, string Content)> FindByPhrase(string phrase, int limit = 20)
        {
            EnsureOpen();
            var results = new List<(long, string)>();
            using (var cmd = _connection.CreateCommand())
            {
                string escaped = phrase.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.CommandText =
                    "SELECT id, content FROM line WHERE content LIKE @p ESCAPE '\\' LIMIT @lim";
                cmd.Parameters.AddWithValue("@p",   "%" + escaped + "%");
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        results.Add((r.GetInt64(0), r.IsDBNull(1) ? string.Empty : r.GetString(1)));
            }
            return results;
        }

        public List<(long Id, string BookTitle, string HeRef, string Content)> FindByBookAndPhrase(
            string bookTitleFragment, string phrase, int limit = 20)
        {
            EnsureOpen();
            var results = new List<(long, string, string, string)>();
            using (var cmd = _connection.CreateCommand())
            {
                string escapedPhrase = phrase.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                string escapedBook   = bookTitleFragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.CommandText = @"
                    SELECT l.id, b.title, l.heRef, l.content
                    FROM line l JOIN book b ON b.id = l.bookId
                    WHERE b.title LIKE @book ESCAPE '\'
                      AND l.content LIKE @phrase ESCAPE '\'
                    LIMIT @lim";
                cmd.Parameters.AddWithValue("@book",   "%" + escapedBook   + "%");
                cmd.Parameters.AddWithValue("@phrase", "%" + escapedPhrase + "%");
                cmd.Parameters.AddWithValue("@lim",    limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        results.Add((
                            r.GetInt64(0),
                            r.IsDBNull(1) ? string.Empty : r.GetString(1),
                            r.IsDBNull(2) ? string.Empty : r.GetString(2),
                            r.IsDBNull(3) ? string.Empty : r.GetString(3)));
            }
            return results;
        }

        public (long Count, long MinId, long MaxId,
                long FirstId, string FirstBook,
                long Id500k, string Book500k,
                long Id500k1, string Book500k1) GetIdStats()
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*), MIN(id), MAX(id) FROM line";
                long count = 0, minId = 0, maxId = 0;
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { count = r.GetInt64(0); minId = r.GetInt64(1); maxId = r.GetInt64(2); }

                cmd.CommandText = "SELECT l.id, b.title FROM line l JOIN book b ON b.id=l.bookId ORDER BY l.id LIMIT 1";
                long firstId = 0; string firstBook = "";
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { firstId = r.GetInt64(0); firstBook = r.GetString(1); }

                cmd.CommandText = "SELECT l.id, b.title FROM line l JOIN book b ON b.id=l.bookId ORDER BY l.id LIMIT 1 OFFSET 499999";
                long id500k = 0; string book500k = "";
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { id500k = r.GetInt64(0); book500k = r.GetString(1); }

                cmd.CommandText = "SELECT l.id, b.title FROM line l JOIN book b ON b.id=l.bookId ORDER BY l.id LIMIT 1 OFFSET 500000";
                long id500k1 = 0; string book500k1 = "";
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { id500k1 = r.GetInt64(0); book500k1 = r.GetString(1); }

                return (count, minId, maxId, firstId, firstBook, id500k, book500k, id500k1, book500k1);
            }
        }

        public List<(int Id, string Title)> FindBooks(string titleFragment, int limit = 50)
        {
            EnsureOpen();
            var results = new List<(int, string)>();
            using (var cmd = _connection.CreateCommand())
            {
                string escaped = titleFragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.CommandText =
                    "SELECT id, title FROM book WHERE title LIKE @p ESCAPE '\\' ORDER BY id LIMIT @lim";
                cmd.Parameters.AddWithValue("@p",   "%" + escaped + "%");
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        results.Add((r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1)));
            }
            return results;
        }

        public (string BookTitle, string HeRef, string Content)? GetLineInfo(int id)
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT b.title, l.heRef, l.content
                    FROM line l JOIN book b ON b.id = l.bookId
                    WHERE l.id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return (
                        r.IsDBNull(0) ? string.Empty : r.GetString(0),
                        r.IsDBNull(1) ? string.Empty : r.GetString(1),
                        r.IsDBNull(2) ? string.Empty : r.GetString(2));
                }
            }
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
            return Interaction.GetSetting("ZayitApp", "Database", "Path", def);
        }

        private void EnsureOpen()
        {
            if (_connection == null)
                throw new InvalidOperationException("ZayitDb: database file was not found at open time.");
        }
    }
}
