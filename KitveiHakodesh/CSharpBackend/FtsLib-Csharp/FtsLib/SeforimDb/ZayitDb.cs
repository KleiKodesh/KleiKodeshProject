using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.SeforimDb
{
    internal sealed class ZayitDb : IDisposable, IFtsCorpus
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

        // ── Neighbor fetching (snippet embellishment) ─────────────────

        /// <summary>
        /// For each line id in <paramref name="ids"/>, fetches up to <paramref name="radius"/>
        /// content lines immediately BEFORE and AFTER it WITHIN THE SAME BOOK (ordered by
        /// lineIndex), returned as two space-joined strings (prev, next) in document order.
        ///
        /// One self-join query per chunk (≤ SQLite's 999-var limit) — the neighbors of a
        /// whole batch of short lines cost a single round-trip, not one per line. The join
        /// is bounded by bookId so it never crosses a book boundary, and by a lineIndex
        /// window of ±radius so it reads at most radius rows per side per matched line.
        ///
        /// Missing entries (line at book edge, or id not found) simply yield empty strings.
        /// </summary>
        public Dictionary<int, (string Prev, string Next)> FetchNeighborContext(
            IReadOnlyList<int> ids, int radius)
        {
            var result = new Dictionary<int, (string Prev, string Next)>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0 || radius <= 0) return result;
            EnsureOpen();

            // Accumulate neighbor lines per matched id, keyed by their lineIndex delta so
            // we can join them back in document order (prev = negative deltas ascending,
            // next = positive deltas ascending).
            var prevParts = new Dictionary<int, SortedList<int, string>>();
            var nextParts = new Dictionary<int, SortedList<int, string>>();

            const int ChunkSize = 999;
            using (var cmd = _connection.CreateCommand())
            {
                var paramNames = new string[ChunkSize];
                for (int i = 0; i < ChunkSize; i++)
                {
                    paramNames[i] = $"@p{i}";
                    cmd.Parameters.Add(paramNames[i], SqliteType.Integer);
                }
                var pRadius = cmd.Parameters.Add("@radius", SqliteType.Integer);
                pRadius.Value = radius;

                for (int start = 0; start < ids.Count; start += ChunkSize)
                {
                    int end   = Math.Min(start + ChunkSize, ids.Count);
                    int count = end - start;

                    var sb = new System.Text.StringBuilder(
                        // m = the matched line; n = a neighbor in the same book within ±radius
                        // rows by lineIndex (excluding the matched line itself). Returns the
                        // matched id, the delta, and the neighbor content.
                        "SELECT m.id, n.lineIndex - m.lineIndex AS delta, n.content" +
                        " FROM line m JOIN line n" +
                        " ON n.bookId = m.bookId" +
                        " AND n.lineIndex BETWEEN m.lineIndex - @radius AND m.lineIndex + @radius" +
                        " AND n.lineIndex <> m.lineIndex" +
                        " WHERE m.id IN (");
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(paramNames[i]);
                        cmd.Parameters[paramNames[i]].Value = ids[start + i];
                    }
                    sb.Append(")");
                    cmd.CommandText = sb.ToString();

                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            int matchedId = r.GetInt32(0);
                            int delta     = r.GetInt32(1);
                            string text   = r.IsDBNull(2) ? string.Empty : r.GetString(2);
                            if (string.IsNullOrEmpty(text)) continue;

                            var bucket = delta < 0 ? prevParts : nextParts;
                            if (!bucket.TryGetValue(matchedId, out var list))
                                bucket[matchedId] = list = new SortedList<int, string>();
                            list[delta] = text;
                        }
                }
            }

            foreach (int id in ids)
            {
                string prev = prevParts.TryGetValue(id, out var pl) ? string.Join(" ", pl.Values) : string.Empty;
                string next = nextParts.TryGetValue(id, out var nl) ? string.Join(" ", nl.Values) : string.Empty;
                if (prev.Length > 0 || next.Length > 0) result[id] = (prev, next);
            }
            return result;
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

        // ── IFtsCorpus ────────────────────────────────────────────────────────────────
        //
        // The engine's caller-supplied corpus contract, implemented EXPLICITLY so the generic
        // names do not appear on this class's own surface — there is one operation per concept
        // here, not two names for it. Everything below forwards; nothing new happens.
        //
        // This is what lets the seforim reader and a caller's own data access serve the engine
        // interchangeably while both routes are live (see IFtsCorpus).

        long IFtsCorpus.CountDocuments() => CountLines();

        long IFtsCorpus.CountDocumentsUpTo(int upToId) => CountLinesUpTo(upToId);

        string IFtsCorpus.GetDocumentText(int id) => GetLineContent(id);

        IEnumerable<(int Id, string Text)> IFtsCorpus.ReadDocuments(int limit, CancellationToken ct) =>
            ReadLines(limit, ct);

        IEnumerable<(int Id, string Text)> IFtsCorpus.ReadDocumentsAfter(int afterId, int limit, CancellationToken ct) =>
            ReadLinesFrom(afterId, limit, ct);

        IEnumerable<(int Id, string Text, string Title)> IFtsCorpus.FetchDocuments(IEnumerable<int> ids) =>
            FetchSearchResultsStreaming(ids);

        IDictionary<int, (string Previous, string Next)> IFtsCorpus.FetchNeighbourText(
            IReadOnlyList<int> ids, int radius) => FetchNeighborContext(ids, radius);

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
