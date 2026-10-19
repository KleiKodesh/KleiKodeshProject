using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Makes the additional libraries configured in
    /// <see cref="KitveiHakodesh.Core.Settings.SeforimDbAdditionalLibraries"/> show up, via
    /// SQLite's own ATTACH DATABASE, as extra schemas on the SAME connection the primary
    /// seforim.db queries already use — rather than opening a second connection or merging files
    /// on disk.
    ///
    /// WHY ATTACH, NOT A MERGED FILE: the primary seforim.db is replaced wholesale by
    /// Otzaria/Zayit on every update; a merged copy would need rebuilding on every one of those
    /// updates for no benefit. ATTACH costs nothing until a query actually touches the attached
    /// schema, and the primary file is opened read-only exactly as it always was — nothing here
    /// can corrupt it or the additional libraries, which are also opened read-only.
    ///
    /// Every row read from table `t` of an attached library is reached as `lib3.t` in the SQL —
    /// see <see cref="SeforimDbSqlStrings"/>. A path's alias and index are its POSITION in the
    /// resolved list, not persisted anywhere: nothing stores "lib3", only a global row id (see
    /// <see cref="SeforimDbLibraryId"/>), which embeds the index, and the index is recomputed
    /// from the same ordered settings list every time.
    /// </summary>
    /// <summary>Public because <see cref="SeforimDbQueries.AttachedLibraries"/> exposes
    /// <see cref="Library"/> for diagnostics and settings UIs in other assemblies (the service),
    /// even though attaching itself is an internal detail of how queries reach those libraries.</summary>
    public static class SeforimDbAttachedLibraries
    {
        /// <summary>The tables a library must have to answer the catalog queries this feature
        /// runs against it (books, categories, lines, author credits). A file missing one of
        /// these is not attached at all, rather than attached and failing on the first query
        /// that touches it.</summary>
        private static readonly string[] RequiredTables = { "book", "category", "line", "author", "book_author" };

        /// <summary>Whether a path looked schema-compatible last time it was probed, keyed by
        /// full path and the file's last-write time — so a rebuilt file is re-probed instead of
        /// stuck on a stale verdict for the life of the process, but an unchanged file is only
        /// probed once no matter how many connections get opened.</summary>
        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, bool Compatible)> CompatibilityCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One additional library, resolved to the alias it is ATTACHed under, its index (which
        /// row ids in its band decode back to — see <see cref="SeforimDbLibraryId"/>), and the
        /// label shown for rows that came from it.
        /// </summary>
        public readonly struct Library
        {
            public Library(string path, string alias, int index, string label)
            {
                Path = path;
                Alias = alias;
                Index = index;
                Label = label;
            }

            public string Path { get; }
            public string Alias { get; }
            public int Index { get; }
            public string Label { get; }
        }

        /// <summary>
        /// The configured additional paths that exist, look schema-compatible, and fit within
        /// <see cref="SeforimDbLibraryId.MaxLibraryIndex"/>, in stable order. Pure and cheap
        /// after the first call per path (see <see cref="CompatibilityCache"/>) — does not open
        /// a connection to the primary database and does not ATTACH anything; that is
        /// <see cref="EnsureAttached"/>'s job, run per connection.
        /// </summary>
        public static IReadOnlyList<Library> Resolve(IReadOnlyList<string> configuredPaths)
        {
            var result = new List<Library>(configuredPaths.Count);
            int index = 1; // 0 is reserved for the primary database (schema "main")
            foreach (var path in configuredPaths)
            {
                if (index > SeforimDbLibraryId.MaxLibraryIndex) break;
                if (!IsCompatible(path)) continue;

                result.Add(new Library(path, "lib" + index, index, LabelFor(path)));
                index++;
            }
            return result;
        }

        /// <summary>
        /// ATTACHes every library in <paramref name="libraries"/> to <paramref name="connection"/>
        /// that is not already attached to it. Checked via PRAGMA database_list rather than
        /// attached unconditionally, because Microsoft.Data.Sqlite pools physical connections:
        /// the connection handed to a logical Open() may be one this code already attached
        /// everything to for an earlier caller, and ATTACHing the same alias twice is an error.
        /// </summary>
        public static void EnsureAttached(SqliteConnection connection, IReadOnlyList<Library> libraries)
        {
            if (libraries.Count == 0) return;

            var already = AttachedAliases(connection);
            foreach (var library in libraries)
            {
                if (already.Contains(library.Alias)) continue;

                using var attach = connection.CreateCommand();
                // ATTACH does not accept a bound parameter in place of the path in every SQLite
                // build, but Microsoft.Data.Sqlite's provider does — kept as a parameter rather
                // than string-formatted so a path with a quote in it cannot break the statement.
                attach.CommandText = "ATTACH DATABASE $path AS " + library.Alias;
                attach.Parameters.AddWithValue("$path", library.Path);
                try
                {
                    attach.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // The file moved, was deleted, or is mid-rebuild between Resolve() seeing it
                    // and this connection opening. Skip it for THIS connection rather than fail
                    // every query on the primary database too; the next Resolve() call drops it
                    // for good once CompatibilityCache notices it is gone.
                }
            }
        }

        private static HashSet<string> AttachedAliases(SqliteConnection connection)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA database_list";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                aliases.Add(reader.GetString(1));
            return aliases;
        }

        private static bool IsCompatible(string path)
        {
            DateTime writeTimeUtc;
            try { writeTimeUtc = File.GetLastWriteTimeUtc(path); }
            catch { return false; }

            if (CompatibilityCache.TryGetValue(path, out var cached) && cached.WriteTimeUtc == writeTimeUtc)
                return cached.Compatible;

            bool compatible = Probe(path);
            CompatibilityCache[path] = (writeTimeUtc, compatible);
            return compatible;
        }

        private static bool Probe(string path)
        {
            try
            {
                using var connection = Common.SqliteConnectionFactory.OpenCorpusProbe(path);
                return RequiredTables.All(table => SeforimDbSchema.TableExists(connection, table));
            }
            catch
            {
                // Not a SQLite file, or one SQLite refuses to open read-only (mid-write, wrong
                // permissions). Not compatible; not fatal to the primary database.
                return false;
            }
        }

        private static string LabelFor(string path)
        {
            try { return Path.GetFileNameWithoutExtension(path); }
            catch { return path; }
        }
    }
}
