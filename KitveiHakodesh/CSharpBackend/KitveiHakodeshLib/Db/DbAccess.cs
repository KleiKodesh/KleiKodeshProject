using FtsLib.SeforimDb;
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

        public DbAccess(string path)
        {
            _connectionString = "Data Source=" + path + ";Version=3;Read Only=True;";
            _EnsureIndexes(path);
            _pool = new SQLiteConnection[POOL_SIZE];
            for (int i = 0; i < POOL_SIZE; i++)
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

        /// <summary>
        /// How this database stores line text, probed lazily on the first query that returns
        /// rows. Null until then; see <see cref="DecodeLineContent"/>.
        /// </summary>
        private volatile SeforimLineContent _lineContent;

        /// <summary>
        /// Guards the one-time line-content probe. Deliberately NOT the pool's lock: probing
        /// reads a PRAGMA and loads a multi-megabyte dictionary, and holding the pool lock for
        /// that would serialise all eight connections behind the first query that returns rows.
        /// </summary>
        private readonly object _lineContentLock = new object();

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

        /// <summary>
        /// Turns any compressed `content` value in a result set back into text.
        ///
        /// This is the one place in the app where rows are read with NO knowledge of their
        /// columns: the SQL arrives from the frontend and Dapper hands back a bag of objects
        /// that go straight out to the webview as JSON. On a SeforimLibrary schema-6 database
        /// line content is a zstd frame, which would serialise as a base64 blob and reach
        /// every consumer as garbage - with nothing failing to compile and no error logged.
        ///
        /// Only a column actually named `content` is touched, and only when its value is a
        /// byte array, so a legacy database and every non-line query pass through untouched.
        /// </summary>
        private void DecodeLineContent(SQLiteConnection connection, List<IDictionary<string, object>> rows)
        {
            if (rows.Count == 0) return;

            // Probe once per DbAccess, not per query: the file cannot change shape underneath
            // an open pool.
            if (_lineContent == null)
            {
                lock (_lineContentLock)
                {
                    if (_lineContent == null)
                    {
                        try
                        {
                            _lineContent = SeforimLineContent.ForConnection(connection);
                        }
                        catch
                        {
                            // A database whose dictionaries cannot be loaded is broken, but
                            // this runs on the generic query path: letting the probe throw
                            // would fail EVERY frontend query rather than the content reads
                            // that actually need it. Leave the rows untouched - a compressed
                            // value then reaches the webview as a blob, which is visible and
                            // reportable, instead of the whole app going dark.
                            return;
                        }
                    }
                }
            }

            if (!_lineContent.IsCompressed) return;

            foreach (var row in rows)
            {
                object value;
                if (!row.TryGetValue("content", out value)) continue;
                if (!(value is byte[])) continue;
                try
                {
                    row["content"] = _lineContent.Decode(value);
                }
                catch
                {
                    // Not one of our frames after all - some other table's `content` column
                    // holding an unrelated blob. Leave it exactly as it came back rather than
                    // failing the whole query on a column we only guessed was ours.
                }
            }
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

            var rows = conn.Query(namedSql, dp)
                           .Cast<IDictionary<string, object>>()
                           .ToList();

            DecodeLineContent(conn, rows);
            return rows;
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
