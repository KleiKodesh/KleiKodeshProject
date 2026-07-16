using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.SeforimDb
{
    internal sealed class ZayitDb : IDisposable
    {
        private readonly SqliteConnection _connection;
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

            // seforim.db is a pre-built CONTENT database this app only ever READS (indexing
            // reads lines; search reads content). Open it READ-ONLY so no write lock is ever
            // taken — that is what lets the background build and any number of concurrent
            // searches all read at once. Pooling is left ON (default), so a `new ZayitDb` per
            // search is a cheap pooled-handle checkout, not a real file open. No journal_mode
            // is set: read-only cannot change it, and it is not this app's DB mode to dictate.
            // (Matches SeforimDbService.Open() and the segment readers.)
            _connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = resolved,
                    Mode = SqliteOpenMode.ReadOnly,
                }.ConnectionString);
            _connection.Open();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
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
                    cmd.Parameters.Add(paramNames[i], SqliteType.Integer);
                }

                for (int start = 0; start < ids.Count; start += ChunkSize)
                {
                    int end   = Math.Min(start + ChunkSize, ids.Count);
                    int count = end - start;

                    var sb = new System.Text.StringBuilder(
                        "SELECT l.id, l.content, b.title" +
                        " FROM line l LEFT JOIN book b ON b.id = l.bookId" +
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
                    cmd.Parameters.Add(paramNames[i], SqliteType.Integer);
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
            SqliteCommand cmd,
            string[]      paramNames,
            List<int>     ids)
        {
            var sb = new System.Text.StringBuilder(
                "SELECT l.id, l.content, b.title" +
                " FROM line l LEFT JOIN book b ON b.id = l.bookId" +
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

        // ── Parallel search-result fetching (net10) ───────────────────
        //
        // The dominant cost of a broad query is Phase 2: reading (content, bookTitle)
        // for every matched line from the 7 GB seforim DB. Done over one connection it
        // is strictly serial. Because the matched IDs are fully materialized (ascending,
        // unique) BEFORE the fetch, and WAL mode lets any number of readers run at once,
        // we can split the ID list into contiguous ranges and read each range on its own
        // connection across cores. Results are placed back into an ordered array (same
        // order as `ids`), so the parallel fetch is result-identical to the serial one.

        // Below this many rows a single connection is faster than paying N connection-open
        // costs; above it the parallel read dominates.
        private const int MinRowsPerFetchWorker = 1500;

        /// <summary>
        /// Reads (id, content, bookTitle) for the pre-materialized ascending <paramref name="ids"/>
        /// list using up to <paramref name="maxDop"/> connections concurrently, returning
        /// <see cref="SearchResult"/>s in the SAME order as <paramref name="ids"/>.
        /// SqliteConnection is not thread-safe, so each worker owns its own connection.
        /// </summary>
        public static SearchResult[] FetchSearchResultsParallel(
            string dbPath,
            List<int> ids,
            System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyCollection<string>> matchedGroups,
            int originalGroupCount,
            int maxDop,
            CancellationToken ct = default)
        {
            var results = new SearchResult[ids.Count];
            if (ids.Count == 0) return results;

            // id -> slot in the ordered output. Built once; a Dictionary supports any
            // number of concurrent readers as long as it is not being mutated.
            var idToIndex = new Dictionary<int, int>(ids.Count);
            for (int i = 0; i < ids.Count; i++) idToIndex[ids[i]] = i;

            int workers = Math.Max(1,
                Math.Min(maxDop, (ids.Count + MinRowsPerFetchWorker - 1) / MinRowsPerFetchWorker));

            if (workers == 1)
            {
                FetchRangeInto(dbPath, ids, 0, ids.Count, idToIndex, matchedGroups, originalGroupCount, results, ct);
                return results;
            }

            int per = (ids.Count + workers - 1) / workers;
            Parallel.For(0, workers,
                new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
                w =>
                {
                    int start = w * per;
                    if (start >= ids.Count) return;
                    int end = Math.Min(start + per, ids.Count);
                    FetchRangeInto(dbPath, ids, start, end, idToIndex, matchedGroups, originalGroupCount, results, ct);
                });
            return results;
        }

        /// <summary>Read ids[start..end) on a dedicated connection, writing each row into
        /// <paramref name="results"/> at its ordered slot (via <paramref name="idToIndex"/>).</summary>
        private static void FetchRangeInto(
            string dbPath,
            List<int> ids, int start, int end,
            Dictionary<int, int> idToIndex,
            System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyCollection<string>> matchedGroups,
            int originalGroupCount,
            SearchResult[] results,
            CancellationToken ct)
        {
            const int ChunkSize = 500;
            using (var conn = OpenReadConnection(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                var paramNames = new string[ChunkSize];
                for (int i = 0; i < ChunkSize; i++)
                {
                    paramNames[i] = "@p" + i;
                    cmd.Parameters.Add(paramNames[i], SqliteType.Integer);
                }

                for (int s = start; s < end; s += ChunkSize)
                {
                    ct.ThrowIfCancellationRequested();
                    int e = Math.Min(s + ChunkSize, end);
                    int count = e - s;

                    var sb = new System.Text.StringBuilder(
                        "SELECT l.id, l.content, b.title" +
                        " FROM line l LEFT JOIN book b ON b.id = l.bookId" +
                        " WHERE l.id IN (");
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(paramNames[i]);
                        cmd.Parameters[paramNames[i]].Value = ids[s + i];
                    }
                    sb.Append(')');
                    cmd.CommandText = sb.ToString();

                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            int id = r.GetInt32(0);
                            results[idToIndex[id]] = new SearchResult(
                                id,
                                r.IsDBNull(2) ? string.Empty : r.GetString(2),
                                r.IsDBNull(1) ? string.Empty : r.GetString(1),
                                matchedGroups,
                                originalGroupCount);
                        }
                }
            }
        }

        /// <summary>Opens a fresh connection tuned for parallel content reads. The DB is
        /// already in WAL mode (set by the primary connection), so this only sets the
        /// read-side pragmas — it never touches journal_mode, avoiding write-lock
        /// contention when several workers open at once. Cache is deliberately modest
        /// (16 MB) since N of these run concurrently; mmap is OS-shared.</summary>
        private static SqliteConnection OpenReadConnection(string dbPath)
        {
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA cache_size=-16384;" +   // 16 MB page cache per worker
                    "PRAGMA temp_store=MEMORY;" +
                    "PRAGMA mmap_size=268435456;";  // 256 MB memory-mapped I/O (OS-shared)
                cmd.ExecuteNonQuery();
            }
            return conn;
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
                    cmd.Parameters.Add(paramNames[i], SqliteType.Integer);
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
