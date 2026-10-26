using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KitveiHakodesh.Core.Common;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Applies one unpacked update patch to seforim.db, in place, in ONE transaction, and
    /// verifies the result against the manifest before committing — so the database is either
    /// the old release or exactly the new one, never in between. Readers keep the old release
    /// until the commit.
    ///
    /// A patch is itself a SQLite file: patch_meta, migrations(version, sql), upsert_&lt;table&gt;
    /// (rows to insert or replace), delete_&lt;table&gt; (primary keys to drop), stat1_snapshot.
    /// Port of otzaria_library_updater services/patch_applier.dart, with Otzaria's settings:
    /// no "from" hash, no foreign-key count, and only the tables the patch can have changed
    /// re-hashed (plus schema_meta).
    /// </summary>
    public static class SeforimLibraryPatchApplier
    {
        /// <summary>
        /// Applies <paramref name="patchPath"/> to <paramref name="dbPath"/>.
        /// </summary>
        /// <exception cref="InvalidDataException">The database is not the patch's starting
        /// release, the patch is malformed, or the result does not hash to the manifest —
        /// nothing was changed. The caller falls back to a full download.</exception>
        public static void Apply(string dbPath, string patchPath, SeforimLibraryPatchManifest manifest)
        {
            using var db = SqliteConnectionFactory.OpenBundledWrite(dbPath);
            Execute(db, "PRAGMA foreign_keys = ON");
            Execute(db, "PRAGMA cache_size = -262144");
            Execute(db, "PRAGMA temp_store = FILE");

            int? localVersion = SeforimLibraryLocalVersion.ReadInt(db, "db_version");
            int? localSchema = SeforimLibraryLocalVersion.ReadInt(db, "db_schema_version");
            if (localVersion != manifest.FromVersion)
                throw new InvalidDataException($"library is v{localVersion}, the patch starts at v{manifest.FromVersion}");
            if (localSchema != null && localSchema != manifest.FromSchemaVersion)
                throw new InvalidDataException($"library schema {localSchema}, the patch expects {manifest.FromSchemaVersion}");

            using (var attach = db.CreateCommand())
            {
                // Attached by plain path: nothing here writes to it, and the provider is not
                // guaranteed to open with URI filenames enabled.
                attach.CommandText = "ATTACH DATABASE $path AS patch";
                attach.Parameters.AddWithValue("$path", patchPath);
                attach.ExecuteNonQuery();
            }

            try
            {
                AssertPatchMatchesManifest(db, manifest);

                // Raw BEGIN/COMMIT, not BeginTransaction(): the provider would then demand every
                // command here carry the transaction object.
                Execute(db, "BEGIN IMMEDIATE");
                try
                {
                    Execute(db, "PRAGMA defer_foreign_keys = ON");
                    RunMigrations(db);
                    RunUpserts(db);
                    RunDeletes(db);
                    ApplyStat1Snapshot(db);
                    VerifyChangedTables(db, manifest);
                    Execute(db, "COMMIT");
                }
                catch
                {
                    try { Execute(db, "ROLLBACK"); } catch (SqliteException) { /* already rolled back */ }
                    throw;
                }
            }
            finally
            {
                Execute(db, "DETACH DATABASE patch");
            }
        }

        private static void AssertPatchMatchesManifest(SqliteConnection db, SeforimLibraryPatchManifest manifest)
        {
            int? format = ReadPatchMetaInt(db, "schema_version");
            if (format is null or < 1 or > SeforimLibraryPatchManifest.SupportedPatchFormatVersion)
                throw new InvalidDataException($"patch format {format} is not supported");
            if (manifest.PatchFormatVersion != null && format != manifest.PatchFormatVersion)
                throw new InvalidDataException($"patch format {format}, manifest says {manifest.PatchFormatVersion}");
            int? from = ReadPatchMetaInt(db, "from_version");
            int? to = ReadPatchMetaInt(db, "to_version");
            if (from != manifest.FromVersion || to != manifest.ToVersion)
                throw new InvalidDataException($"patch is v{from}->v{to}, manifest says v{manifest.FromVersion}->v{manifest.ToVersion}");
        }

        private static void RunMigrations(SqliteConnection db)
        {
            if (!HasTable(db, "patch", "migrations")) return;
            var statements = new List<string>();
            using (var command = db.CreateCommand())
            {
                command.CommandText = "SELECT sql FROM patch.migrations ORDER BY version ASC";
                using var reader = command.ExecuteReader();
                while (reader.Read()) statements.Add(reader.GetString(0));
            }
            foreach (string sql in statements) Execute(db, sql);
        }

        /// <summary>Insert-or-update, parents before children. An update only touches rows that
        /// actually differ, so unchanged rows are not rewritten.</summary>
        private static void RunUpserts(SqliteConnection db)
        {
            foreach (var table in SeforimLibraryPatchTables.InForeignKeyOrder)
            {
                string patchTable = "upsert_" + table.Name;
                if (!HasTable(db, "patch", patchTable)) continue;
                List<string> columns = PatchTableColumns(db, patchTable);
                if (columns.Count == 0) continue;

                string columnList = string.Join(",", columns.Select(Quote));
                string keyList = string.Join(",", table.PrimaryKey.Select(Quote));
                var nonKey = columns.Where(c => !table.PrimaryKey.Contains(c, StringComparer.Ordinal)).ToList();
                string onConflict;
                if (!table.Updatable || nonKey.Count == 0)
                {
                    onConflict = $"ON CONFLICT({keyList}) DO NOTHING";
                }
                else
                {
                    string target = Quote(table.Name);
                    string set = string.Join(",", nonKey.Select(c => $"{Quote(c)} = excluded.{Quote(c)}"));
                    string changed = string.Join(" OR ", nonKey.Select(c =>
                        $"{target}.{Quote(c)} IS NOT excluded.{Quote(c)} COLLATE BINARY OR typeof({target}.{Quote(c)}) IS NOT typeof(excluded.{Quote(c)})"));
                    onConflict = $"ON CONFLICT({keyList}) DO UPDATE SET {set} WHERE {changed}";
                }

                try
                {
                    // "WHERE true" lets SQLite parse ON CONFLICT after a SELECT (the documented
                    // upsert-from-select ambiguity).
                    Execute(db, $"INSERT INTO {Quote(table.Name)} ({columnList}) SELECT {columnList} FROM patch.{Quote(patchTable)} WHERE true {onConflict}");
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    throw new InvalidDataException($"patch conflicts with table {table.Name}: {ex.Message}", ex);
                }
            }
        }

        /// <summary>Deletes, children before parents.</summary>
        private static void RunDeletes(SqliteConnection db)
        {
            foreach (var table in SeforimLibraryPatchTables.InForeignKeyOrder.Reverse())
            {
                string patchTable = "delete_" + table.Name;
                if (!HasTable(db, "patch", patchTable)) continue;
                string keys = string.Join(",", table.PrimaryKey.Select(Quote));
                string target = table.PrimaryKey.Length == 1 ? keys : "(" + keys + ")";
                Execute(db, $"DELETE FROM {Quote(table.Name)} WHERE {target} IN (SELECT {keys} FROM patch.{Quote(patchTable)})");
            }
        }

        /// <summary>The producer's optimizer statistics replace ours, so query plans match the new data.</summary>
        private static void ApplyStat1Snapshot(SqliteConnection db)
        {
            string snapshot = SeforimLibraryPatchTables.Stat1SnapshotTable;
            if (!HasTable(db, "patch", snapshot)) return;
            if (!HasTable(db, "main", "sqlite_stat1")) Execute(db, "ANALYZE main.sqlite_schema");
            Execute(db, "DELETE FROM main.sqlite_stat1");
            Execute(db, $"INSERT INTO main.sqlite_stat1 (tbl, idx, stat) SELECT tbl, idx, stat FROM patch.{Quote(snapshot)}");
            if (HasTable(db, "main", "sqlite_stat4")) Execute(db, "DELETE FROM main.sqlite_stat4");
        }

        /// <summary>
        /// Re-hashes every table the patch could have changed and compares with the manifest.
        /// A mismatch throws, which rolls the whole transaction back. A manifest without
        /// per-table hashes cannot be verified this cheaply and is refused.
        /// </summary>
        private static void VerifyChangedTables(SqliteConnection db, SeforimLibraryPatchManifest manifest)
        {
            var from = manifest.FromTableContentHashes;
            var to = manifest.ToTableContentHashes;
            var order = SeforimLibraryPatchTables.HashOrderSchema6;
            if (from == null || to == null || !SameKeys(to.Keys, order))
                throw new InvalidDataException("the manifest has no per-table hashes for this schema");

            var tables = order.Where(t =>
                    t == "schema_meta"
                    || !string.Equals(from.GetValueOrDefault(t), to[t], StringComparison.Ordinal)
                    || PatchHasRows(db, "upsert_" + t)
                    || PatchHasRows(db, "delete_" + t))
                .ToList();

            var actual = SeforimLibraryContentHasher.HashTables(db, tables);
            var mismatched = tables.Where(t => !string.Equals(actual[t], to[t], StringComparison.Ordinal)).ToList();
            if (mismatched.Count > 0)
                throw new InvalidDataException("tables differ from the release after patching: " + string.Join(", ", mismatched));
        }

        private static bool SameKeys(IEnumerable<string> keys, IReadOnlyList<string> order)
        {
            var set = new HashSet<string>(keys, StringComparer.Ordinal);
            return set.Count == order.Count && order.All(set.Contains);
        }

        private static bool PatchHasRows(SqliteConnection db, string table)
        {
            if (!HasTable(db, "patch", table)) return false;
            using var command = db.CreateCommand();
            command.CommandText = $"SELECT 1 FROM patch.{Quote(table)} LIMIT 1";
            return command.ExecuteScalar() != null;
        }

        private static bool HasTable(SqliteConnection db, string schema, string name)
        {
            using var command = db.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {schema}.sqlite_master WHERE type='table' AND name=$name LIMIT 1";
            command.Parameters.AddWithValue("$name", name);
            return command.ExecuteScalar() != null;
        }

        private static List<string> PatchTableColumns(SqliteConnection db, string table)
        {
            using var command = db.CreateCommand();
            command.CommandText = $"PRAGMA patch.table_info({Quote(table)})";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(1));
            return names;
        }

        private static int? ReadPatchMetaInt(SqliteConnection db, string key)
        {
            if (!HasTable(db, "patch", "patch_meta")) return null;
            using var command = db.CreateCommand();
            command.CommandText = "SELECT value FROM patch.patch_meta WHERE key = $key LIMIT 1";
            command.Parameters.AddWithValue("$key", key);
            return int.TryParse(Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        }

        private static void Execute(SqliteConnection db, string sql)
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 0;
            command.ExecuteNonQuery();
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
