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
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            string? cacheKey = BuildCacheKey(databasePath);

            // No usable key - an in-memory database, or a caller that did not supply a path.
            // Probe every time rather than risk handing one database's shape to another.
            if (cacheKey == null) return Probe(connection);

            return SchemasByDatabasePath.GetOrAdd(cacheKey, _ => Probe(connection));
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

        /// <summary>Reads the shape straight from the connection, with no caching.</summary>
        public static SeforimDbSchema Probe(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            bool hasLineContentTable = TableExists(connection, "line_content");
            bool lineStillHasContentColumn = ColumnExists(connection, "line", "content");

            return new SeforimDbSchema(
                hasSplitLineContent: hasLineContentTable && !lineStillHasContentColumn,
                hasCompressedContent: TableExists(connection, "zstd_dict"),
                hasVersionLines: TableExists(connection, "version_line"),
                declaredBaseColumn: ColumnExists(connection, "link", DeclaredBaseColumnOtzaria)
                    ? DeclaredBaseColumnOtzaria
                    : DeclaredBaseColumnZayit);
        }

        /// <summary>Forgets every cached probe. For tests, and for a database swapped at runtime.</summary>
        public static void ClearCache() => SchemasByDatabasePath.Clear();

        /// <summary>True if the database has a table with this name.</summary>
        public static bool TableExists(DbConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$name";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                return command.ExecuteScalar() != null;
            }
        }

        /// <summary>True if the named table has a column with this name.</summary>
        public static bool ColumnExists(DbConnection connection, string tableName, string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                // PRAGMA does not take parameters, and the table name here is always one of
                // our own literals rather than anything a caller supplies.
                command.CommandText = "PRAGMA table_info(" + tableName + ")";
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
