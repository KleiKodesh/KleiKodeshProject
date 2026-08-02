using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib.Db
{
    /// <summary>
    /// Thin wrapper around SQLite. Converts positional ? params to named @p0, @p1, ...
    /// because Dapper requires named parameters.
    ///
    /// Uses a pool of read-only connections so concurrent JS queries (prefetch,
    /// TOC, lines chunk, metadata) execute truly in parallel on the thread pool
    /// rather than serializing on a single shared connection.
    ///
    /// The pool size matches the maximum number of queries the frontend fires
    /// concurrently: CONCURRENT_CHUNKS (3) + 1 prefetch + 1 TOC + 1 metadata = 6.
    /// A small headroom above that is fine — connections are cheap for a read-only DB.
    /// </summary>
    public class DbAccess : IDisposable
    {
        private const int POOL_SIZE = 8;
        private readonly string _connectionString;
        private readonly SQLiteConnection[] _pool;
        private readonly object _lock = new object();
        private int _nextSlot = 0;
        private bool _disposed = false;

        /// <param name="poolSize">Concurrent-connection count; the default matches the
        /// frontend's max concurrent queries against the main DB (see class doc).</param>
        /// <param name="ensureIndexes">Pass false for databases we do not own (Otzaria's
        /// user_books.db) — index creation opens a WRITE connection to a file another
        /// app may hold open, and we must never write to it.</param>
        public DbAccess(string path, int poolSize = POOL_SIZE, bool ensureIndexes = true)
        {
            _connectionString = "Data Source=" + path + ";Version=3;Read Only=True;";
            if (ensureIndexes) _EnsureIndexes(path);
            _pool = new SQLiteConnection[poolSize];
            for (int i = 0; i < poolSize; i++)
                _pool[i] = _OpenConnection();
        }

        /// <summary>
        /// Creates any missing performance indexes on the database.
        /// Uses a separate read-write connection (the pool connections are read-only).
        /// Runs synchronously before the read-only pool is opened so the indexes are
        /// guaranteed to exist before any query executes.
        /// </summary>
        private static void _EnsureIndexes(string path)
        {
            string readWriteConnectionString = "Data Source=" + path + ";Version=3;";
            try
            {
                using (var conn = new SQLiteConnection(readWriteConnectionString))
                {
                    conn.Open();
                    conn.Execute(
                        "CREATE INDEX IF NOT EXISTS idx_link_type_target_line " +
                        "ON link(connectionTypeId, targetLineId)"
                    );
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — the index is a performance optimization, not a correctness requirement.
                // If the DB is read-only or the index already exists under a different name,
                // queries will still work, just potentially slower.
                System.Diagnostics.Debug.WriteLine("[DbAccess] EnsureIndexes failed: " + ex.Message);
            }
        }

        private SQLiteConnection _OpenConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            // 64 MB page cache per connection — reduces cold-read latency for large
            // text content in the line table. Each connection gets its own cache.
            try { conn.Execute("PRAGMA cache_size = -8192"); }  // 8 MB per connection × 8 = 64 MB total
            catch { /* non-fatal */ }
            // Memory-mapped I/O — shared across all connections to the same file.
            try { conn.Execute("PRAGMA mmap_size = 268435456"); } // 256 MB
            catch { /* non-fatal */ }
            return conn;
        }

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            // Round-robin across the pool so concurrent Task.Run calls each get
            // their own connection and run truly in parallel.
            SQLiteConnection conn;
            lock (_lock)
            {
                conn = _pool[_nextSlot];
                _nextSlot = (_nextSlot + 1) % _pool.Length;
            }

            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            return conn.Query(namedSql, dp)
                       .Cast<IDictionary<string, object>>()
                       .ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var conn in _pool)
            {
                try { conn.Close(); conn.Dispose(); }
                catch { /* best effort */ }
            }
        }
    }
}
