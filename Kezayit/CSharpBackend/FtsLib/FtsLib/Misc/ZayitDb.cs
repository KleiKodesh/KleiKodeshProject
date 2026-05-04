using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace FtsLib.Misc
{
    public sealed class ZayitDb : IDisposable
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
                    "PRAGMA temp_store=MEMORY;";
                cmd.ExecuteNonQuery();
            }
        }

        public long CountLines()
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
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

        public IEnumerable<(int Id, string Content)> ReadLines(int limit)
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
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
            }
        }

        public List<(int Id, int LineIndex, string HeRef, string Content, string BookTitle)> 
            FetchSearchResults(List<int> ids)
        {
            EnsureOpen();
            var rows = new List<(int, int, string, string, string)>(ids.Count);

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    $@"SELECT l.id, l.lineIndex, l.heRef, l.content, b.title
                         FROM line l JOIN book b ON b.id = l.bookId
                        WHERE l.id IN ({string.Join(",", ids)})
                        ORDER BY l.bookId, l.lineIndex";

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int    lineId    = r.GetInt32(0);
                        int    lineIndex = r.GetInt32(1);
                        string heRef     = r.IsDBNull(2) ? null : r.GetString(2);
                        string content   = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                        string bookTitle = r.IsDBNull(4) ? string.Empty : r.GetString(4);

                        rows.Add((lineId, lineIndex, heRef, content, bookTitle));
                    }
                }
            }

            return rows;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection?.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────

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
