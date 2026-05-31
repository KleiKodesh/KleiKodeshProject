// Copied from FtsLib — not referenced. Only the row-reading surface is kept;
// all search/snippet helpers from the original are omitted.
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace LuceneLib.SeforimDb
{
    /// <summary>
    /// Wraps the SQLite database used by the Zayit / Otzaria Torah study apps.
    /// Provides ordered row reading so that Lucene doc IDs match the row IDs exactly.
    /// </summary>
    public sealed class ZayitDb : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly string _dbPath;
        private bool _disposed;

        public bool IsOpen  => _connection != null;
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
                    "PRAGMA journal_mode=WAL;"  +
                    "PRAGMA cache_size=-65536;" +   // 64 MB page cache
                    "PRAGMA temp_store=MEMORY;" +
                    "PRAGMA mmap_size=268435456;";  // 256 MB memory-mapped I/O
                cmd.ExecuteNonQuery();
            }
        }

        // ── Row reading ───────────────────────────────────────────────

        public long CountLines()
        {
            EnsureOpen();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (long)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// Streams all rows ordered by id. When <paramref name="limit"/> is 0
        /// every row is returned; pass a positive value to cap during development.
        /// </summary>
        public IEnumerable<(int Id, string Content)> ReadLines(
            int limit = 0,
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
