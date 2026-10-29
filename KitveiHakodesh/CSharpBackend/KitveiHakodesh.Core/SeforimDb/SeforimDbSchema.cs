using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.IO;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Which shape of seforim database we are talking to, decided by looking at the file
    /// rather than by trusting a version number.
    ///
    /// Three shapes are in the wild, and all three must keep working:
    ///
    ///   Zayit            line.content is TEXT. No version_line table at all.
    ///   Otzaria, pre-6   line.content is TEXT. version_line.content is NOT NULL.
    ///   Otzaria, 6+      line has NO content column; the text lives in line_content,
    ///                    compressed with a dictionary from zstd_dict, and
    ///                    version_line.content is NULLABLE.
    ///
    /// WHY NOT READ schema_meta: the databases do carry a `db_schema_version` row (1, 2 and
    /// 6 respectively), but it is written by the external library builder and Otzaria itself
    /// never reads it. Trusting it would make us the only consumer that breaks when a
    /// rebuild forgets to bump it, and it tells us nothing a direct look at the tables does
    /// not. The two structural questions below are independent of each other and of any
    /// version number, which is also how Otzaria asks them.
    /// </summary>
    public sealed class SeforimDbSchema
    {
        /// <summary>
        /// One schema per database file. The shape cannot change while the file is open, and
        /// every connection to the same path sees the same answer, so probing once per path
        /// keeps the cost off the hot read paths. Keyed by full path, case-insensitively.
        /// </summary>
        private static readonly ConcurrentDictionary<string, SeforimDbSchema> SchemasByDatabasePath =
            new ConcurrentDictionary<string, SeforimDbSchema>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Name of the column flagging a link as a DECLARED base-text relationship. The two
        /// seforim-DB schemas spell the same idea differently: Zayit's own database has
        /// <c>isDeclaredBase</c> (0/1), the newer otzaria build has <c>baseProvenance</c>
        /// (0 = none, 1 = inferred from the title, 2 = declared by Sefaria). Both mean
        /// "> 0 means the data asserts this relationship", so a caller only needs the name.
        /// </summary>
        public const string DeclaredBaseColumnOtzaria = "baseProvenance";
        public const string DeclaredBaseColumnZayit = "isDeclaredBase";

        private SeforimDbSchema(
            bool hasSplitLineContent,
            bool hasCompressedContent,
            bool hasVersionLines,
            string declaredBaseColumn)
        {
            HasSplitLineContent = hasSplitLineContent;
            HasCompressedContent = hasCompressedContent;
            HasVersionLines = hasVersionLines;
            DeclaredBaseColumn = declaredBaseColumn;
        }

        /// <summary>
        /// Which column this database uses for the declared-base flag — see
        /// <see cref="DeclaredBaseColumnOtzaria"/>. Hardcoding either name breaks the reverse
        /// lookups on the other schema with a "no such column" error, so every query that
        /// tests the flag interpolates this.
        /// </summary>
        public string DeclaredBaseColumn { get; }

        /// <summary>
        /// True when line text lives in the `line_content` sidecar and `line` itself has no
        /// `content` column. Decides the SHAPE OF EVERY QUERY that reads line text: those
        /// queries must join line_content instead of selecting line.content.
        ///
        /// Both halves matter. A database part-way through a rebuild could have the sidecar
        /// while `line` still carries the old column, and then the old queries are still the
        /// correct ones to run.
        /// </summary>
        public bool HasSplitLineContent { get; }

        /// <summary>
        /// True when the database ships zstd dictionaries, meaning stored content is
        /// compressed. Decides how a VALUE IS READ, and is deliberately separate from
        /// <see cref="HasSplitLineContent"/>: the two travel together in today's releases,
        /// but nothing in the format ties them, and the codec copes with either alone.
        /// </summary>
        public bool HasCompressedContent { get; }

        /// <summary>
        /// True when the database has a `version_line` table, i.e. alternate editions of a
        /// book. Zayit databases have none, so every version-aware query must be guarded by
        /// this rather than assuming the table exists.
        /// </summary>
        public bool HasVersionLines { get; }

        /// <summary>
        /// The schema of the database behind <paramref name="connection"/>, probed once per
        /// file and cached. <paramref name="databasePath"/> is only the cache key; the
        /// connection is what gets read.
        /// </summary>
        public static SeforimDbSchema ForDatabase(DbConnection connection, string databasePath)
            => ForDatabase(connection, databasePath, "main");

        /// <summary>
        /// Same as <see cref="ForDatabase(DbConnection, string)"/>, but probes an ATTACHed
        /// schema instead of the connection's own database. Used for additional libraries
        /// ATTACHed alongside the primary seforim.db (see
        /// <see cref="SeforimDbAttachedLibraries"/>) — each one can be its own shape (plain,
        /// split, compressed), independent of the primary database's shape and of every other
        /// attached library's.
        /// </summary>
        public static SeforimDbSchema ForDatabase(DbConnection connection, string databasePath, string schema)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            string? cacheKey = BuildCacheKey(databasePath);

            // No usable key - an in-memory database, or a caller that did not supply a path.
            // Probe every time rather than risk handing one database's shape to another.
            if (cacheKey == null) return Probe(connection, schema);

            return SchemasByDatabasePath.GetOrAdd(cacheKey, _ => Probe(connection, schema));
        }

        /// <summary>
        /// The cache key for a database path, or null when the path cannot identify a file.
        ///
        /// Two different spellings of one path must not probe twice, and - far more
        /// importantly - two DIFFERENT databases must never share a key. In-memory databases
        /// are the trap: every one of them is a distinct database, yet they all spell their
        /// path ":memory:", so caching on that string would hand the first one's schema to
        /// every other. Those are never cached.
        /// </summary>
        private static string? BuildCacheKey(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) return null;

            if (databasePath.IndexOf(":memory:", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            // A URI filename can ask for an in-memory database too (file:x?mode=memory), and
            // a shared-cache one is still per-name rather than per-file.
            if (databasePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (databasePath.IndexOf("mode=memory", StringComparison.OrdinalIgnoreCase) >= 0)
                    return null;
                return databasePath;
            }

            try
            {
                // Collapses "a\.", "a/b" and relative paths onto one spelling.
                return Path.GetFullPath(databasePath);
            }
            catch
            {
                // Not a path we can canonicalise (invalid characters, too long). Use it
                // verbatim rather than failing the query that asked for the schema.
                return databasePath;
            }
        }

        /// <summary>Reads the shape straight from the connection's own database ("main"), with
        /// no caching.</summary>
        public static SeforimDbSchema Probe(DbConnection connection) => Probe(connection, "main");

        /// <summary>Same as <see cref="Probe(DbConnection)"/>, but against an ATTACHed schema.</summary>
        public static SeforimDbSchema Probe(DbConnection connection, string schema)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            bool hasLineContentTable = TableExists(connection, "line_content", schema);
            bool lineStillHasContentColumn = ColumnExists(connection, "line", "content", schema);

            return new SeforimDbSchema(
                hasSplitLineContent: hasLineContentTable && !lineStillHasContentColumn,
                hasCompressedContent: TableExists(connection, "zstd_dict", schema),
                hasVersionLines: TableExists(connection, "version_line", schema),
                declaredBaseColumn: ColumnExists(connection, "link", DeclaredBaseColumnOtzaria, schema)
                    ? DeclaredBaseColumnOtzaria
                    : DeclaredBaseColumnZayit);
        }

        /// <summary>Forgets every cached probe. For tests, and for a database swapped at runtime.</summary>
        public static void ClearCache() => SchemasByDatabasePath.Clear();

        /// <summary>True if the database has a table with this name.</summary>
        public static bool TableExists(DbConnection connection, string tableName) => TableExists(connection, tableName, "main");

        /// <summary>Same check against an ATTACHed schema (<paramref name="schema"/> is a SQLite
        /// schema name — "main" or one of the aliases <see cref="SeforimDbAttachedLibraries"/>
        /// assigns).</summary>
        public static bool TableExists(DbConnection connection, string tableName, string schema)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    $"SELECT 1 FROM {schema}.sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$name";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                return command.ExecuteScalar() != null;
            }
        }

        /// <summary>True if the named table has a column with this name.</summary>
        public static bool ColumnExists(DbConnection connection, string tableName, string columnName) => ColumnExists(connection, tableName, columnName, "main");

        /// <summary>Same check against an ATTACHed schema — see <see cref="TableExists(DbConnection, string, string)"/>.</summary>
        public static bool ColumnExists(DbConnection connection, string tableName, string columnName, string schema)
        {
            using (var command = connection.CreateCommand())
            {
                // PRAGMA does not take parameters, and the table/schema names here are always
                // our own literals rather than anything a caller supplies.
                command.CommandText = $"PRAGMA {schema}.table_info({tableName})";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
