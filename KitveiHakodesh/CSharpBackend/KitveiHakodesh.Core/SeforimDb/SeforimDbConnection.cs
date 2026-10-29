using System;
using System.Collections.Generic;
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
        private readonly IReadOnlyList<SeforimDbAttachedLibraries.Library> _attachedLibraries;

        /// <summary>Finds the user's database the way the app does — the saved setting first,
        /// then the known install locations — and attaches every additional library configured
        /// in <see cref="SeforimDbAdditionalLibraries"/> alongside it, each in its PERSISTED
        /// id-space slot (<see cref="SeforimDbAdditionalLibraries.Entry.Index"/>).</summary>
        public SeforimDbQueries()
            : this(SeforimDbPathResolver.Resolve().Path, SeforimDbAdditionalLibraries.GetAllEntries())
        {
        }

        /// <param name="databasePath">An explicit path, or null for "no database configured".</param>
        public SeforimDbQueries(string? databasePath)
            : this(databasePath, Array.Empty<SeforimDbAdditionalLibraries.Entry>())
        {
        }

        /// <param name="databasePath">An explicit path, or null for "no database configured".</param>
        /// <param name="additionalLibraryPaths">
        /// Convenience overload for callers that do not need a PERSISTED id-space slot per path —
        /// tests, or ad-hoc embedding. Each path gets a POSITIONAL index (1, 2, ... in the order
        /// given), which is only as stable as the caller's own list: passing a different list (or
        /// the same paths in a different order) on the next call changes every id built from it.
        /// Production code goes through the parameterless constructor instead, which reads
        /// <see cref="SeforimDbAdditionalLibraries.GetAllEntries"/> — see that class's remarks for
        /// why a recomputed position is a correctness bug once a library can be removed, not just
        /// a style choice.
        /// </param>
        public SeforimDbQueries(string? databasePath, IReadOnlyList<string> additionalLibraryPaths)
            : this(databasePath, ToPositionalEntries(additionalLibraryPaths))
        {
        }

        /// <param name="databasePath">An explicit path, or null for "no database configured".</param>
        /// <param name="additionalLibraries">Extra seforim-shaped databases to ATTACH alongside
        /// it, each already carrying the id-space slot it should use — see
        /// <see cref="SeforimDbAttachedLibraries"/>. An entry whose path does not exist or does
        /// not look schema-compatible is silently skipped, exactly like a missing primary path.</param>
        public SeforimDbQueries(string? databasePath, IReadOnlyList<SeforimDbAdditionalLibraries.Entry> additionalLibraries)
        {
            _databasePath = databasePath;
            _attachedLibraries = SeforimDbAttachedLibraries.Resolve(
                additionalLibraries ?? Array.Empty<SeforimDbAdditionalLibraries.Entry>());
        }

        private static IReadOnlyList<SeforimDbAdditionalLibraries.Entry> ToPositionalEntries(IReadOnlyList<string> paths)
        {
            var entries = new SeforimDbAdditionalLibraries.Entry[paths.Count];
            for (int i = 0; i < paths.Count; i++)
                entries[i] = new SeforimDbAdditionalLibraries.Entry(i + 1, paths[i]);
            return entries;
        }

        /// <summary>The additional libraries actually attached (existing, schema-compatible, and
        /// within the id-space limit) — for diagnostics and for a settings UI to show which
        /// configured paths are live right now.</summary>
        public IReadOnlyList<SeforimDbAttachedLibraries.Library> AttachedLibraries => _attachedLibraries;

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

        /// <summary>Opens a read-only connection to the primary database with every configured
        /// additional library ATTACHed alongside it, or throws if there is no primary database
        /// to open. An additional library that failed to attach on THIS connection (see
        /// <see cref="SeforimDbAttachedLibraries.EnsureAttached"/>) does not fail the primary
        /// open — only queries that reach into its schema are affected.</summary>
        private SqliteConnection Open()
        {
            if (string.IsNullOrWhiteSpace(_databasePath))
                throw SeforimDbUnavailableException.NotConfigured();

            if (!File.Exists(_databasePath))
                throw SeforimDbUnavailableException.NotOnDisk(_databasePath!);

            var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath!);
            SeforimDbAttachedLibraries.EnsureAttached(connection, _attachedLibraries);
            return connection;
        }

        /// <summary>
        /// The shape of the database behind an open connection: whether line text sits in the
        /// `line_content` sidecar, whether it is compressed, whether alternate versions exist.
        /// Probed once per database file and cached, so asking per query is free.
        ///
        /// Every query that reads line text needs this, because the SQL itself differs - on a
        /// schema-6 database `line` has no `content` column at all, so the old statements do
        /// not merely return compressed bytes, they fail outright.
        /// </summary>
        private SeforimDbSchema SchemaOf(SqliteConnection connection)
            => SeforimDbSchema.ForDatabase(connection, _databasePath!);

        /// <summary>
        /// A codec for decoding line content read over this connection.
        ///
        /// Built per connection rather than cached on the instance: it holds a zstd
        /// decompressor with a reusable buffer and is NOT safe to share across threads, and
        /// these query methods are called concurrently. A legacy database gets the shared
        /// no-op codec, so the only real cost is on compressed databases, where it is loading
        /// the dictionary once per connection.
        /// </summary>
        private static SeforimDbLineContentCodec CodecFor(SqliteConnection connection)
            => SeforimDbLineContentCodec.Create(connection);

        /// <summary>True if <paramref name="table"/> has a column named <paramref name="column"/>.</summary>
        private static bool ColumnExists(SqliteConnection connection, string table, string column)
            => ColumnExists(connection, "main", table, column);

        /// <summary>Same check against an ATTACHed schema (<paramref name="schema"/> is a SQLite
        /// schema name — "main" or one of the aliases <see cref="SeforimDbAttachedLibraries"/>
        /// assigns), via the schema-qualified pragma form <c>PRAGMA schema.table_info(table)</c>.</summary>
        private static bool ColumnExists(SqliteConnection connection, string schema, string table, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {schema}.table_info({table})";
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
