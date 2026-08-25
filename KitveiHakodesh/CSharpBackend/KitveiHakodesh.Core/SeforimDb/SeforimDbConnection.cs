using System;
using System.IO;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Finding, opening and schema-probing the seforim database — the plumbing every query in
    /// <see cref="SeforimDbQueries"/> sits on. Same class, split across two files: this one is
    /// how the queries reach the database, the other is what they ask it.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        private readonly string? _databasePath;

        /// <summary>Finds the user's database the way the app does — the saved setting first,
        /// then the known install locations.</summary>
        public SeforimDbQueries()
            : this(SeforimDbPathResolver.Resolve().Path)
        {
        }

        /// <param name="databasePath">An explicit path, or null for "no database configured".</param>
        public SeforimDbQueries(string? databasePath)
        {
            _databasePath = databasePath;
        }

        /// <summary>The resolved path, or null when none was found. A non-null value is not a
        /// promise the file is still there — see <see cref="IsAvailable"/>.</summary>
        public string? DatabasePath => _databasePath;

        /// <summary>Whether there is a database to read. False means the user has not chosen
        /// one, or the one they chose has moved.</summary>
        public bool IsAvailable => !string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath);

        /// <summary>
        /// Warms the cold paths so the user's first real query does not pay for them: loading
        /// the SQLite native library, opening the first pooled connection, filling the catalog
        /// cache, and running the hot read paths once.
        ///
        /// Runs SYNCHRONOUSLY and throws what it hits. Deciding to do this on a background
        /// thread, and what to do when it fails, is the host's call — the version this replaces
        /// made both decisions itself with a fire-and-forget Task.Run and an empty catch.
        /// </summary>
        public void Warmup()
        {
            if (!IsAvailable) return;

            GetAllCategories();
            GetAllBooks();
            GetBookById(1);
            GetLinesPaged(1, 1, 0);
        }

        /// <summary>Opens a read-only connection, or throws if there is nothing to open.</summary>
        private SqliteConnection Open()
        {
            if (string.IsNullOrWhiteSpace(_databasePath))
                throw SeforimDbUnavailableException.NotConfigured();

            if (!File.Exists(_databasePath))
                throw SeforimDbUnavailableException.NotOnDisk(_databasePath!);

            return SqliteConnectionFactory.OpenCorpusRead(_databasePath!);
        }

        /// <summary>True if <paramref name="table"/> has a column named <paramref name="column"/>.</summary>
        private static bool ColumnExists(SqliteConnection connection, string table, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>True if the database has a table named <paramref name="table"/>. Whole
        /// tables, not just columns, differ across seforim-DB schema versions — link_anchor
        /// only exists from SeforimLibrary schema v2 on.</summary>
        private static bool TableExists(SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @t LIMIT 1";
            command.Parameters.AddWithValue("@t", table);
            return command.ExecuteScalar() != null;
        }
    }
}
