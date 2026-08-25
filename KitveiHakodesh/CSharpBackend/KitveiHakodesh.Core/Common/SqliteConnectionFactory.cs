using System;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Opens SQLite connections with the right policy for what the caller is doing.
    ///
    /// There is no single correct connection string here, and picking the wrong one is
    /// silent rather than loud — hence one place that knows all three cases:
    ///
    ///   CorpusRead    the shipped, read-only databases (seforim, dictionary, catalog).
    ///                 MUST be Mode=ReadOnly. Pooled, with a page cache and mmap, because
    ///                 these are large and read constantly.
    ///   CorpusProbe   the same databases, when the point is to touch them as little as
    ///                 possible: read-only, unpooled, NO pragmas. Used by provenance
    ///                 fingerprinting, which exists to survive incidental churn and must not
    ///                 cause any.
    ///   BundledWrite  a shipped database being updated in place (the HebrewBooks catalog).
    ///                 Read-write and unpooled, and deliberately does NOT set WAL — see the
    ///                 warning on that method.
    ///   SegmentWrite  FtsLib's index segments. Pooling MUST be off — the writer does
    ///                 File.Move on a just-written file and a pooled handle blocks it.
    ///   UserData      user_settings.db (highlights and notes). WAL, no pooling, and a busy
    ///                 timeout, because another process (Zayit, a second instance) may be
    ///                 writing at the same time.
    ///
    /// PROVIDER-SWAP TRAP (MIGRATION-PLAN gotcha 5): System.Data.SQLite spelled read-only as
    /// "Read Only=True" / ReadOnly=true. Microsoft.Data.Sqlite does NOT recognise either — it
    /// silently ignores them and opens read-write. A corpus opened read-write grows -wal/-shm
    /// sidecars beside the user's database and fails outright on read-only media. The only
    /// spelling that works is Mode = SqliteOpenMode.ReadOnly, below.
    /// Likewise "Version=3" has no meaning here, and PageSize/CacheSize are PRAGMAs rather
    /// than connection-string options.
    /// </summary>
    public static class SqliteConnectionFactory
    {
        /// <summary>Page cache per connection, in KiB (negative = KiB, SQLite convention).
        /// 8 MB matches the pool the hosted app has been running with.</summary>
        private const int CorpusCacheSizeKib = -8192;

        /// <summary>Memory-mapped I/O window: 256 MB. The OS shares the mapping between
        /// connections, so this is cheap to repeat per connection.</summary>
        private const long CorpusMmapSizeBytes = 268435456;

        /// <summary>How long a writer waits for a competing writer before giving up.
        /// WAL allows many readers and ONE writer; without this, a second writer fails
        /// instantly with SQLITE_BUSY instead of waiting the moment out.</summary>
        private const int UserDataBusyTimeoutSeconds = 5;

        /// <summary>
        /// A shipped, read-only database. Opens read-only and applies the read pragmas.
        /// </summary>
        public static SqliteConnection OpenCorpusRead(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required", nameof(dbPath));

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,   // see PROVIDER-SWAP TRAP above
                Pooling = true,
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();

            // Per-connection, and only meaningful once open.
            Execute(connection,
                "PRAGMA cache_size=" + CorpusCacheSizeKib + ";" +
                "PRAGMA mmap_size=" + CorpusMmapSizeBytes + ";");

            return connection;
        }

        /// <summary>
        /// A shipped database, opened to be looked at rather than read from: read-only,
        /// unpooled, and no pragmas at all.
        ///
        /// The pragmas the read policy applies are per-connection and harmless, but the
        /// caller here is provenance fingerprinting — code whose whole purpose is to NOT
        /// react to incidental churn in the file. Opening it with the same policy as a
        /// hot read path invites exactly the sort of header or sidecar activity it is
        /// trying to see through. Unpooled so no handle outlives the check.
        /// </summary>
        public static SqliteConnection OpenCorpusProbe(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required", nameof(dbPath));

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,   // see PROVIDER-SWAP TRAP above
                Pooling = false,
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// A database that SHIPS with the app, being updated in place — today only the
        /// HebrewBooks catalog.
        ///
        /// ⚠ DO NOT ADD journal_mode=WAL HERE. journal_mode is a persistent property of the
        /// FILE, so setting it once converts the shipped database for good, leaving -wal and
        /// -shm sidecars beside it. Every later reader then needs write access to the -shm
        /// file: on read-only media, or a per-machine install a user cannot write, the
        /// database stops being readable at all. WAL is right for user data, which is why it
        /// lives on that method and not this one.
        /// </summary>
        public static SqliteConnection OpenBundledWrite(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required", nameof(dbPath));

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite,  // never Create: a missing catalog is a bug, not a blank one
                Pooling = false,
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// An FtsLib index segment being written. Pooling off — see the class remarks.
        /// </summary>
        public static SqliteConnection OpenSegmentWrite(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required", nameof(dbPath));

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,                  // File.Move follows; a pooled handle blocks it
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// The user annotations database. WAL so other processes can read and write
        /// concurrently, no pooling so no handle is held between calls, and a busy timeout
        /// so a competing writer waits rather than failing instantly.
        /// </summary>
        public static SqliteConnection OpenUserData(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required", nameof(dbPath));

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = UserDataBusyTimeoutSeconds,
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();

            // journal_mode is a persistent property of the FILE, so setting it repeatedly is
            // harmless; busy_timeout is per-connection and must be set every time.
            Execute(connection,
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA busy_timeout=" + (UserDataBusyTimeoutSeconds * 1000) + ";");

            return connection;
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
