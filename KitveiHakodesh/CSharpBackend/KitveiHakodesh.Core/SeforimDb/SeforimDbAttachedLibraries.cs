using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Makes the additional libraries configured in
    /// <see cref="SeforimDbAdditionalLibraries"/> show up, via SQLite's own ATTACH DATABASE, as
    /// extra schemas on the SAME connection the primary seforim.db queries already use — rather
    /// than opening a second connection or merging files on disk.
    ///
    /// WHY ATTACH, NOT A MERGED FILE: the primary seforim.db is replaced wholesale by
    /// Otzaria/Zayit on every update; a merged copy would need rebuilding on every one of those
    /// updates for no benefit. ATTACH costs nothing until a query actually touches the attached
    /// schema, and the primary file is opened read-only exactly as it always was — nothing here
    /// can corrupt it or the additional libraries, which are also opened read-only.
    ///
    /// Every row read from table `t` of an attached library is reached as `lib3.t` in the SQL —
    /// see <see cref="SeforimDbSqlStrings"/>. A library's alias and index come straight from its
    /// <see cref="SeforimDbAdditionalLibraries.Entry.Index"/> — the PERSISTED slot
    /// <see cref="SeforimDbAdditionalLibraries"/> assigned it when it was added, never the
    /// library's position in whatever subset of configured libraries happens to be attachable
    /// right now. See that class's remarks for why position-based indexing is a correctness bug,
    /// not a style choice: it lets an unrelated library's removal (or a disconnected network
    /// drive) silently reassign another library's ids out from under a user's existing
    /// highlights and bookmarks.
    ///
    /// Public because <see cref="SeforimDbQueries.AttachedLibraries"/> exposes
    /// <see cref="Library"/> for diagnostics and settings UIs in other assemblies (the service),
    /// even though attaching itself is an internal detail of how queries reach those libraries.
    /// </summary>
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
        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, string? Rejection)> CompatibilityCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The line-id translation of a library whose stored line ids do not fit the band, keyed
        /// like <see cref="CompatibilityCache"/> (same probe, same file version). Absent/null for an
        /// ordinary library.</summary>
        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, SeforimDbIdMap? Map)> LineMapCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Same as <see cref="LineMapCache"/>, for the TOC-entry ids.</summary>
        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, SeforimDbIdMap? Map)> TocMapCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The line-id map of <paramref name="path"/> as of its last probe, or null when its
        /// line ids are used as stored. Call after <see cref="Rejection"/> has accepted the path.</summary>
        internal static SeforimDbIdMap? LineMapFor(string path) =>
            LineMapCache.TryGetValue(path, out var entry) ? entry.Map : null;

        /// <summary>The TOC-entry id map of <paramref name="path"/> as of its last probe, or null.</summary>
        internal static SeforimDbIdMap? TocMapFor(string path) =>
            TocMapCache.TryGetValue(path, out var entry) ? entry.Map : null;

        /// <summary>
        /// Why a configured path is not (or is) attached, for a settings page and for logs:
        /// <see cref="Rejection"/> is null exactly when the library is attachable. The reasons
        /// are short English machine-readable strings the UI maps to its own wording. Probing
        /// never throws: a probe failure IS the reason, because an exception swallowed here
        /// (the file checks run as LocalSystem, where a per-user network path or a permission
        /// problem is the usual cause) left the user with a silent "not attached".
        /// </summary>
        public readonly struct Status
        {
            public Status(SeforimDbAdditionalLibraries.Entry entry, Library? library, string? rejection)
            {
                Entry = entry;
                Library = library;
                Rejection = rejection;
            }

            public SeforimDbAdditionalLibraries.Entry Entry { get; }
            public Library? Library { get; }
            public string? Rejection { get; }
        }

        /// <summary>Same selection as <see cref="Resolve"/>, but reports EVERY configured entry
        /// with the reason it was left out, instead of dropping it silently.</summary>
        public static IReadOnlyList<Status> ResolveWithStatus(IReadOnlyList<SeforimDbAdditionalLibraries.Entry> configuredLibraries)
        {
            var result = new List<Status>(configuredLibraries.Count);
            var usedIndices = new HashSet<int>();
            foreach (var entry in configuredLibraries)
            {
                if (entry.Index < 1 || entry.Index > SeforimDbLibraryId.MaxLibraryIndex)
                { result.Add(new Status(entry, null, "invalid library index")); continue; }
                if (!usedIndices.Add(entry.Index))
                { result.Add(new Status(entry, null, "duplicate library index")); continue; }

                string? rejection = Rejection(entry.Path);
                result.Add(new Status(entry,
                    rejection is null ? new Library(entry.Path, "lib" + entry.Index, entry.Index, LabelFor(entry.Path), LineMapFor(entry.Path), TocMapFor(entry.Path)) : null,
                    rejection));
            }
            return result;
        }

        /// <summary>
        /// Opens the library read-only and decodes ONE line of text through the same codec path
        /// the reader uses. Returns null when the text is readable, else the reason. This is the
        /// check that makes "attached" mean "its text can actually be read" - a library can have
        /// every catalog table and still have no usable line text (missing sidecar, empty
        /// zstd_dict, corrupt frames).
        /// </summary>
        public static string? CheckTextReadable(string path)
        {
            try
            {
                using var connection = Common.SqliteConnectionFactory.OpenCorpusProbe(path);
                var schema = SeforimDbSchema.Probe(connection);
                bool hasText = schema.HasSplitLineContent
                    ? SeforimDbSchema.TableExists(connection, "line_content")
                    : SeforimDbSchema.ColumnExists(connection, "line", "content");
                if (!hasText) return "no line text (neither line.content nor line_content)";

                using var codec = SeforimDbLineContentCodec.Create(connection);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT " + SeforimDbLineContentSql.ContentColumn(schema, "l") +
                    " FROM line l" + SeforimDbLineContentSql.ContentJoin(schema, "l") + " LIMIT 1";
                using var reader = command.ExecuteReader();
                if (reader.Read()) codec.DecodeColumnOrEmpty(reader, 0);
                return null;
            }
            catch (Exception ex)
            {
                return "line text unreadable: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// One additional library, resolved to the alias it is ATTACHed under, its index (which
        /// row ids in its band decode back to — see <see cref="SeforimDbLibraryId"/>), and the
        /// label shown for rows that came from it.
        /// </summary>
        public readonly struct Library
        {
            public Library(string path, string alias, int index, string label, SeforimDbIdMap? lineIds = null, SeforimDbIdMap? tocIds = null)
            {
                Path = path;
                Alias = alias;
                Index = index;
                Label = label;
                LineIds = lineIds;
                TocIds = tocIds;
            }

            public string Path { get; }
            public string Alias { get; }
            public int Index { get; }
            public string Label { get; }

            /// <summary>Non-null only for a library whose line ids are too large for the id band and are
            /// translated (see <see cref="SeforimDbIdMap"/>): the ids stored in the file are NOT the
            /// ids this feature hands out, so every query/index that binds or reads a line id of this
            /// library goes through <see cref="ToStoredLineId"/> / <see cref="ToLocalLineId"/>.</summary>
            internal SeforimDbIdMap? LineIds { get; }

            /// <summary>Same for TOC-entry ids (tocEntry.id, its parentId, line_toc.tocEntryId): non-null only
            /// when the stored ids do not fit the band.</summary>
            internal SeforimDbIdMap? TocIds { get; }

            /// <summary>The TOC-entry id to BIND into this library's own SQL for a LOCAL TOC-entry id.</summary>
            internal long ToStoredTocId(int localId) => TocIds is null ? localId : TocIds.ToStored(localId);

            /// <summary>The LOCAL TOC-entry id for an id READ from this library's own SQL.</summary>
            internal int ToLocalTocId(long storedId)
            {
                // 0 / NULL is "no TOC entry" in the library's own data and stays 0.
                if (TocIds is null || storedId == 0) return storedId == 0 ? 0 : checked((int)storedId);
                int local = TocIds.ToLocal(storedId);
                if (local == 0) throw new InvalidDataException($"TOC entry id {storedId} of '{Label}' is not in its id map");
                return local;
            }

            /// <summary>The line id to BIND into this library's own SQL for a LOCAL line id.</summary>
            internal long ToStoredLineId(int localId) => LineIds is null ? localId : LineIds.ToStored(localId);

            /// <summary>The LOCAL line id for a line id READ from this library's own SQL.</summary>
            internal int ToLocalLineId(long storedId)
            {
                if (LineIds is null) return checked((int)storedId);
                int local = LineIds.ToLocal(storedId);
                if (local == 0) throw new InvalidDataException($"line id {storedId} of '{Label}' is not in its id map");
                return local;
            }
        }

        /// <summary>
        /// The configured entries that exist and look schema-compatible (required tables present,
        /// and every id in them fits inside its own band — see <see cref="MaxIdFits"/>), resolved
        /// to the schema alias each one's PERSISTED index names it. Pure and cheap after the
        /// first call per path (see <see cref="CompatibilityCache"/>) — does not open a
        /// connection to the primary database and does not ATTACH anything; that is
        /// <see cref="EnsureAttached"/>'s job, run per connection.
        ///
        /// An entry whose index is out of range (0, negative, or beyond
        /// <see cref="SeforimDbLibraryId.MaxLibraryIndex"/> — a hand-edited or corrupted registry
        /// value) or that DUPLICATES an index already used by an earlier entry in the same list
        /// is skipped: either would mean two libraries fighting over the same ATTACH alias and
        /// the same id band, which is exactly the collision this whole scheme exists to prevent.
        /// </summary>
        public static IReadOnlyList<Library> Resolve(IReadOnlyList<SeforimDbAdditionalLibraries.Entry> configuredLibraries)
        {
            var result = new List<Library>(configuredLibraries.Count);
            var usedIndices = new HashSet<int>();
            foreach (var entry in configuredLibraries)
            {
                if (entry.Index < 1 || entry.Index > SeforimDbLibraryId.MaxLibraryIndex) continue;
                if (!usedIndices.Add(entry.Index)) continue;
                if (Rejection(entry.Path) is not null) continue;

                result.Add(new Library(entry.Path, "lib" + entry.Index, entry.Index, LabelFor(entry.Path), LineMapFor(entry.Path), TocMapFor(entry.Path)));
            }
            return result;
        }

        /// <summary>
        /// ATTACHes every library in <paramref name="libraries"/> to <paramref name="connection"/>
        /// under its alias, (re)attaching only when needed. Checked via PRAGMA database_list
        /// rather than attached unconditionally, because Microsoft.Data.Sqlite POOLS physical
        /// connections by connection string: the connection handed to a logical Open() may be
        /// one this code already attached everything to for an EARLIER caller, and ATTACHing the
        /// same alias twice is an error.
        ///
        /// Checking that the alias is attached is not enough on its own, though — the pool is
        /// keyed on the PRIMARY database's path alone, so two SeforimDbQueries instances pointed
        /// at the same primary path but DIFFERENT additional libraries can be handed the exact
        /// same physical connection. Without comparing the attached file too, the second
        /// instance would see "lib1 is already attached" and silently keep querying the FIRST
        /// instance's library under that alias. So every alias's CURRENT file is compared against
        /// the one it should hold, and a mismatch is corrected with a DETACH + re-ATTACH.
        /// </summary>
        public static void EnsureAttached(SqliteConnection connection, IReadOnlyList<Library> libraries)
        {
            if (libraries.Count == 0) return;

            var attachedFileByAlias = AttachedFiles(connection);
            foreach (var library in libraries)
            {
                if (attachedFileByAlias.TryGetValue(library.Alias, out var attachedFile))
                {
                    if (SamePath(attachedFile, library.Path)) continue; // already correct

                    try
                    {
                        using var detach = connection.CreateCommand();
                        detach.CommandText = "DETACH DATABASE " + library.Alias;
                        detach.ExecuteNonQuery();
                    }
                    catch (SqliteException)
                    {
                        // A detach can fail if a statement elsewhere on this connection still has
                        // the schema open; the ATTACH right below will then fail too and is
                        // caught the same way as any other attach failure.
                    }
                }

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

        /// <summary>Every schema currently attached to <paramref name="connection"/>, alias to
        /// backing file path (empty for a schema with no file, e.g. an in-memory "main").</summary>
        private static Dictionary<string, string> AttachedFiles(SqliteConnection connection)
        {
            var byAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA database_list";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                byAlias[reader.GetString(1)] = reader.IsDBNull(2) ? "" : reader.GetString(2);
            return byAlias;
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Null when the library at <paramref name="path"/> is attachable, else why not.</summary>
        internal static string? Rejection(string path)
        {
            DateTime writeTimeUtc;
            try
            {
                if (!File.Exists(path)) return "file not found (or not visible to the service account)";
                writeTimeUtc = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) { return "file not accessible: " + ex.GetType().Name + ": " + ex.Message; }

            if (CompatibilityCache.TryGetValue(path, out var cached) && cached.WriteTimeUtc == writeTimeUtc)
                return cached.Rejection;

            string? rejection = Probe(path, out var lineMap, out var tocMap);
            LineMapCache[path] = (writeTimeUtc, lineMap);
            TocMapCache[path] = (writeTimeUtc, tocMap);
            CompatibilityCache[path] = (writeTimeUtc, rejection);
            return rejection;
        }

        private static string? Probe(string path, out SeforimDbIdMap? lineMap, out SeforimDbIdMap? tocMap)
        {
            lineMap = null;
            tocMap = null;
            try
            {
                using var connection = Common.SqliteConnectionFactory.OpenCorpusProbe(path);
                var missing = RequiredTables.Where(table => !SeforimDbSchema.TableExists(connection, table)).ToArray();
                if (missing.Length > 0)
                    return "missing required table(s): " + string.Join(", ", missing);

                // The required tables exist, but do their OWN ids actually fit the band this
                // library's index will offset them into? A library built independently of
                // seforim.db has no reason to respect SeforimDbLibraryId.OffsetUnit, and a local
                // id that reaches or exceeds it would land inside the NEXT library's band (or
                // overflow int32 outright) once ToGlobal adds the offset — silently returning
                // another library's book, or none, for some ids. Checked on every id-bearing
                // table this feature actually namespaces, not just `line` (the biggest one):
                // `book`/`category` are namespaced too and a library could plausibly have more
                // categories than lines in a degenerate case.
                // Column-level check of exactly what the catalog queries and the full-text corpus
                // SELECT: a file with the right table NAMES but other columns would otherwise attach
                // fine and then throw in the middle of an index build, after the primary was
                // already indexed. Rejected here, before any enumeration begins.
                if (!HasColumns(connection)) return "required tables have unexpected columns";
                foreach (string table in new[] { "book", "category" })
                    if (!MaxIdFits(connection, table)) return $"ids in table '{table}' exceed the per-library id range";
                if (!MaxIdFits(connection, "line"))
                {
                    // Line ids are opaque handles, so a library that reserved a huge private range for
                    // them (to merge cleanly into another database) is still usable when each book's
                    // lines are consecutive: they are translated to dense local ids instead of rejected.
                    lineMap = SeforimDbIdMap.TryBuildForLines(connection, "", out string? mapFailure);
                    if (lineMap is null)
                        return "ids in table 'line' exceed the per-library id range" +
                               (mapFailure is null ? "" : " (" + mapFailure + ")");
                }
                // The same library built for merging reserves a high range for its TOC entries too
                // (tocEntry.id / parentId / line_toc.tocEntryId): translated the same way.
                if (SeforimDbSchema.TableExists(connection, "tocEntry") && !MaxIdFits(connection, "tocEntry"))
                {
                    tocMap = SeforimDbIdMap.TryBuildForTocEntries(connection, "", out string? tocFailure);
                    if (tocMap is null)
                        return "ids in table 'tocEntry' exceed the per-library id range" +
                               (tocFailure is null ? "" : " (" + tocFailure + ")");
                }
                return null;
            }
            catch (Exception ex)
            {
                // Not a SQLite file, or one SQLite refuses to open read-only (mid-write, wrong
                // permissions). Not compatible; not fatal to the primary database. The reason is
                // returned, not swallowed.
                return "cannot be opened as a SQLite database: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>Selecting each column the catalog/full-text readers use, with LIMIT 0, proves
        /// the columns exist without reading a row. Only columns the code cannot work without are
        /// listed: optional ones (book.hasTeamim and the other has* flags, line.heRef, ...) are
        /// absent from libraries built by other tools and are defaulted at query time instead. Line TEXT is deliberately not checked here:
        /// it lives in line.content or line_content depending on schema version, and the full-text
        /// corpus validates that itself (see CompositeFtsCorpus).</summary>
        private static bool HasColumns(SqliteConnection connection)
        {
            string[] probes =
            {
                "SELECT id, parentId, title, level FROM category LIMIT 0",
                "SELECT id, categoryId, title, orderIndex, totalLines FROM book LIMIT 0",
                "SELECT id, name FROM author LIMIT 0",
                "SELECT bookId, authorId FROM book_author LIMIT 0",
                "SELECT id, bookId, lineIndex FROM line LIMIT 0",
            };
            foreach (string sql in probes)
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    using var reader = command.ExecuteReader();
                }
                catch (SqliteException) { return false; }
            }
            return true;
        }

        /// <summary>True when every id in <paramref name="table"/> is small enough to sit inside
        /// one library's private id-space band without reaching the next one — see
        /// <see cref="SeforimDbLibraryId"/>.</summary>
        private static bool MaxIdFits(SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT MAX(id) FROM {table}";
            object? result = command.ExecuteScalar();
            if (result is null || result is DBNull) return true; // an empty table has nothing to collide

            long maxId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
            return maxId >= 0 && maxId < SeforimDbLibraryId.OffsetUnit;
        }

        private static string LabelFor(string path)
        {
            try { return Path.GetFileNameWithoutExtension(path); }
            catch { return path; }
        }
    }
}
