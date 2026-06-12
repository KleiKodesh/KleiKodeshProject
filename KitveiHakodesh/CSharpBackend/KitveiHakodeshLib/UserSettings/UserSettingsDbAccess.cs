using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib.UserSettings
{
    /// <summary>
    /// Process-wide singleton access point for user_settings.db.
    ///
    /// Cross-process safety (Zayit, other app instances):
    ///   The database is opened in WAL (Write-Ahead Log) mode. WAL allows multiple
    ///   readers and one writer to operate concurrently without blocking each other,
    ///   even across separate OS processes. This means Zayit or another instance of
    ///   this app can read or write the database at the same time as us.
    ///
    ///   To avoid holding a file lock between operations, NO persistent connection is
    ///   kept open. Each Query() and Execute() call opens a fresh connection, executes,
    ///   and closes immediately. SQLite with WAL is fast enough that open/close overhead
    ///   is negligible for annotation read/write workloads.
    ///
    /// In-process safety (multiple AppViewer instances):
    ///   All AppViewer instances share this singleton so they never race each other.
    ///   The _lock serializes concurrent calls arriving on Task.Run thread-pool threads.
    ///   Because the connection is closed after every operation, LastInsertRowId is
    ///   read within the same open/close scope as the INSERT — it is always correct.
    ///
    /// Lifecycle:
    ///   - Open(seforimDbPath) sets up the singleton for a given DB path.
    ///   - The first Open call creates the schema and enables WAL mode.
    ///   - Subsequent Open calls from additional AppViewer instances at the same path
    ///     are no-ops.
    ///   - When the user switches the seforim DB path, Open() re-points the singleton.
    ///   - DisposeShared() clears the singleton at process teardown (no connection to
    ///     close since we never keep one open).
    /// </summary>
    public class UserSettingsDbAccess
    {
        // ── Shared singleton ──────────────────────────────────────────────────────

        private static readonly object _lock = new object();
        private static UserSettingsDbAccess _shared;

        /// <summary>
        /// Opens (or re-uses) the process-wide access point for the given seforim DB path.
        /// Thread-safe. Idempotent when called repeatedly with the same path.
        /// </summary>
        public static UserSettingsDbAccess Open(string seforimDbPath)
        {
            if (string.IsNullOrEmpty(seforimDbPath) || !File.Exists(seforimDbPath))
                return null;

            string desiredPath = DeriveUserSettingsDbPath(seforimDbPath);

            lock (_lock)
            {
                if (_shared != null &&
                    string.Equals(_shared.Path, desiredPath, StringComparison.OrdinalIgnoreCase))
                    return _shared;

                // Path changed or first call — replace the singleton.
                _shared = null;
                var instance = new UserSettingsDbAccess(desiredPath);
                instance._EnsureSchemaExists();
                _shared = instance;
                return _shared;
            }
        }

        /// <summary>
        /// Returns the currently active shared instance, or null if not yet opened.
        /// </summary>
        public static UserSettingsDbAccess Current
        {
            get { lock (_lock) { return _shared; } }
        }

        /// <summary>
        /// Clears the singleton at process teardown. Safe to call multiple times.
        /// No connection needs closing since connections are never held open.
        /// </summary>
        public static void DisposeShared()
        {
            lock (_lock)
            {
                _shared = null;
            }
        }

        // ── Instance ──────────────────────────────────────────────────────────────

        public string Path { get; private set; }

        /// <summary>
        /// Derives the user settings database path from the seforim database path.
        /// Example: C:\data\seforim.db  →  C:\data\Settings\user_settings.db
        /// </summary>
        public static string DeriveUserSettingsDbPath(string seforimDbPath)
        {
            string folder = System.IO.Path.GetDirectoryName(seforimDbPath);
            return System.IO.Path.Combine(folder, "Settings", "user_settings.db");
        }

        // Connection string with WAL and no persistent lock:
        //   Journal Mode=WAL  — enables Write-Ahead Logging so other processes can
        //                        read and write concurrently without blocking us
        //   Cache Size=0      — no shared page cache; each connection is fully
        //                        independent (safe for short-lived connections)
        //   Pooling=False     — disable connection pooling so every SQLiteConnection
        //                        object is a real open/close cycle and never re-used
        //                        from a pool that might hold a stale lock
        private readonly string _connectionString;

        private UserSettingsDbAccess(string userSettingsDbPath)
        {
            Path = userSettingsDbPath;
            _connectionString =
                "Data Source=" + userSettingsDbPath + ";" +
                "Version=3;" +
                "Pooling=False;";
        }

        private SQLiteConnection _OpenConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        private void _EnsureSchemaExists()
        {
            string dir = System.IO.Path.GetDirectoryName(this.Path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Open once to create the schema and set WAL mode, then close immediately.
            // WAL mode is a persistent DB property — it survives connection close and
            // stays active for all future connections from any process.
            using (var conn = _OpenConnection())
            {
                conn.Execute("PRAGMA journal_mode=WAL;");
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS user_highlights (
                        id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        bookId      INTEGER NOT NULL,
                        lineId      INTEGER NOT NULL,
                        startOffset INTEGER NOT NULL,
                        endOffset   INTEGER NOT NULL,
                        colorArgb   INTEGER NOT NULL,
                        createdAt   INTEGER NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line
                        ON user_highlights (bookId, lineId);

                    CREATE TABLE IF NOT EXISTS user_notes (
                        id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        bookId      INTEGER NOT NULL,
                        lineId      INTEGER NOT NULL,
                        startOffset INTEGER NOT NULL,
                        endOffset   INTEGER NOT NULL,
                        note        TEXT    NOT NULL,
                        quote       TEXT    NOT NULL,
                        createdAt   INTEGER NOT NULL,
                        updatedAt   INTEGER NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_user_notes_book_line
                        ON user_notes (bookId, lineId);
                ");
            }
        }

        /// <summary>
        /// Executes a SELECT query and returns rows as a list of dictionaries.
        /// Positional ? parameters are converted to named @p0, @p1, ... for Dapper.
        /// Opens a connection, executes, closes — no lock held between calls.
        /// Thread-safe via _lock.
        /// </summary>
        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            lock (_lock)
            {
                using (var conn = _OpenConnection())
                {
                    return conn.Query(namedSql, dp)
                               .Cast<IDictionary<string, object>>()
                               .ToList();
                }
            }
        }

        /// <summary>
        /// Executes an INSERT, UPDATE, or DELETE statement.
        /// Returns the last inserted row id for INSERT.
        /// Opens a connection, executes, reads LastInsertRowId, closes — all within
        /// the same lock scope so the id is always consistent.
        /// Thread-safe via _lock.
        /// </summary>
        public long Execute(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            lock (_lock)
            {
                using (var conn = _OpenConnection())
                {
                    conn.Execute(namedSql, dp);
                    return conn.LastInsertRowId;
                }
            }
        }
    }
}
