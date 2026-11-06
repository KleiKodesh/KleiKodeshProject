using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The links of an additional library that was built to be merged into the official database.
    ///
    /// Such a library has no <c>link</c> table of line ids: it cannot know the ids of the official
    /// lines it cites. It carries <c>external_link</c> instead - for a source line (book, lineIndex)
    /// the TARGET by title and lineIndex, either an official book ("official") or another book of the
    /// library itself - and leaves it to whoever merges the library to resolve titles to ids. Without
    /// that translation a personal book's links (sources, footnotes, references) never show up.
    ///
    /// This does the translation at read time, per request and only for the lines asked about: the
    /// source line's stored id is joined to its (book, lineIndex), the target book is found by exact
    /// title (the primary database, or the library), and the target line by (book, lineIndex). The
    /// results use GLOBAL ids (<see cref="SeforimDbLibraryId"/>) and the primary database's own
    /// connection-type ids, so everything downstream treats them like any <c>link</c> row.
    /// Nothing is written, and the library file is only read.
    /// </summary>
    internal static class SeforimDbExternalLinks
    {
        /// <summary>One resolved link: a target line (global ids) of a connection type.</summary>
        internal readonly struct Link
        {
            public Link(int targetBookId, int connectionTypeId, int targetLineId, int targetLineIndex)
            {
                TargetBookId = targetBookId;
                ConnectionTypeId = connectionTypeId;
                TargetLineId = targetLineId;
                TargetLineIndex = targetLineIndex;
            }

            public int TargetBookId { get; }
            public int ConnectionTypeId { get; }
            public int TargetLineId { get; }
            public int TargetLineIndex { get; }
        }

        private const string OfficialSource = "official";

        /// <summary>Whether the library carries the table at all (a plain library does not).</summary>
        internal static bool HasTable(SqliteConnection conn, SeforimDbAttachedLibraries.Library library) =>
            SeforimDbSchema.TableExists(conn, "external_link", library.Alias);

        /// <summary>The primary database's connection types by name.</summary>
        internal static Dictionary<string, int> LoadConnectionTypeIds(SqliteConnection conn)
        {
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetAllConnectionTypes;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(1)) ids[r.GetString(1)] = r.GetInt32(0);
            return ids;
        }

        /// <summary>The primary database's id for a connection type named in <c>external_link</c>.
        /// A type the primary database does not know is shown as the closest one it does: footnotes are
        /// notes on a text, like commentary; anything else lands in "other".</summary>
        internal static int ConnectionTypeId(Dictionary<string, int> primaryTypes, string? name)
        {
            string wanted = string.IsNullOrWhiteSpace(name) ? "SOURCE" : name!.Trim();
            if (primaryTypes.TryGetValue(wanted, out int id)) return id;
            string fallback = string.Equals(wanted, "FOOTNOTES", StringComparison.OrdinalIgnoreCase) ? "COMMENTARY" : "OTHER";
            return primaryTypes.TryGetValue(fallback, out id) ? id : 0;
        }

        /// <summary>
        /// The links FROM the given lines of <paramref name="library"/> (<paramref name="localLineIds"/>
        /// are the library's LOCAL ids), each target resolved to global ids. A target that cannot be
        /// resolved (no such title, no such line) is left out - the same as a link nothing can open.
        /// </summary>
        internal static List<Link> ForLines(SqliteConnection conn, SeforimDbAttachedLibraries.Library library,
            IReadOnlyList<int> localLineIds, Dictionary<string, int> primaryTypes)
        {
            var result = new List<Link>();
            if (localLineIds.Count == 0 || !HasTable(conn, library)) return result;

            var raw = new List<(string? Source, string Title, int LineIndex, string? Type)>();
            const int Chunk = 400;
            for (int at = 0; at < localLineIds.Count; at += Chunk)
            {
                int n = Math.Min(Chunk, localLineIds.Count - at);
                var names = new string[n];
                for (int i = 0; i < n; i++) names[i] = "@p" + i;
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    $"SELECT e.targetSource, e.targetTitle, e.targetLineIndex, e.connectionType " +
                    $"FROM {library.Alias}.line l JOIN {library.Alias}.external_link e " +
                    $"ON e.sourceBookId = l.bookId AND e.sourceLineIndex = l.lineIndex " +
                    $"WHERE l.id IN ({string.Join(",", names)})";
                for (int i = 0; i < n; i++)
                    cmd.Parameters.AddWithValue(names[i], library.ToStoredLineId(localLineIds[at + i]));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(1) || r.IsDBNull(2)) continue;
                    raw.Add((r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3)));
                }
            }
            if (raw.Count == 0) return result;
            return Resolve(conn, library, raw, primaryTypes);
        }

        /// <summary>Distinct (target book, connection type) pairs of a whole book: what a book "links to",
        /// without reading its lines. <paramref name="localBookId"/> is the library's local book id.</summary>
        internal static List<(int TargetBookId, int ConnectionTypeId)> ForBook(SqliteConnection conn,
            SeforimDbAttachedLibraries.Library library, int localBookId, Dictionary<string, int> primaryTypes)
        {
            var result = new List<(int, int)>();
            if (!HasTable(conn, library)) return result;
            var raw = new List<(string? Source, string Title, string? Type)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $"SELECT DISTINCT targetSource, targetTitle, connectionType FROM {library.Alias}.external_link WHERE sourceBookId = @b";
                cmd.Parameters.AddWithValue("@b", localBookId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(1))
                        raw.Add((r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
            }
            var books = new Dictionary<(bool Official, string Title), int?>();
            var seen = new HashSet<(int, int)>();
            foreach (var (source, title, type) in raw)
            {
                bool official = IsOfficial(source);
                int? book = BookByTitle(conn, library, official, title, books);
                if (book is not { } b) continue;
                int typeId = ConnectionTypeId(primaryTypes, type);
                int global = official ? b : SeforimDbLibraryId.ToGlobal(library.Index, b);
                if (seen.Add((global, typeId))) result.Add((global, typeId));
            }
            return result;
        }

        /// <summary>Which connection kinds a book of the library has (by the names in <c>external_link</c>),
        /// so the book's flags can say so even when the library's own book row does not.</summary>
        internal static HashSet<string> KindsOfBook(SqliteConnection conn, SeforimDbAttachedLibraries.Library library, int localBookId)
        {
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!HasTable(conn, library)) return kinds;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT DISTINCT connectionType FROM {library.Alias}.external_link WHERE sourceBookId = @b";
            cmd.Parameters.AddWithValue("@b", localBookId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) kinds.Add(r.IsDBNull(0) ? "SOURCE" : r.GetString(0));
            return kinds;
        }

        private static bool IsOfficial(string? source) =>
            string.Equals(source?.Trim(), OfficialSource, StringComparison.OrdinalIgnoreCase);

        private static List<Link> Resolve(SqliteConnection conn, SeforimDbAttachedLibraries.Library library,
            List<(string? Source, string Title, int LineIndex, string? Type)> raw, Dictionary<string, int> primaryTypes)
        {
            var result = new List<Link>(raw.Count);
            var books = new Dictionary<(bool Official, string Title), int?>();
            using var primaryLine = conn.CreateCommand();
            primaryLine.CommandText = "SELECT id FROM line WHERE bookId = @b AND lineIndex = @i LIMIT 1";
            var pb = primaryLine.Parameters.Add("@b", SqliteType.Integer);
            var pi = primaryLine.Parameters.Add("@i", SqliteType.Integer);
            using var ownLine = conn.CreateCommand();
            ownLine.CommandText = $"SELECT id FROM {library.Alias}.line WHERE bookId = @b AND lineIndex = @i LIMIT 1";
            var ob = ownLine.Parameters.Add("@b", SqliteType.Integer);
            var oi = ownLine.Parameters.Add("@i", SqliteType.Integer);

            foreach (var (source, title, lineIndex, type) in raw)
            {
                bool official = IsOfficial(source);
                int? book = BookByTitle(conn, library, official, title, books);
                if (book is not { } bookId) continue;

                int targetLine;
                if (official)
                {
                    pb.Value = bookId; pi.Value = lineIndex;
                    object? found = primaryLine.ExecuteScalar();
                    if (found is null || found is DBNull) continue;
                    targetLine = Convert.ToInt32(found);
                    result.Add(new Link(bookId, ConnectionTypeId(primaryTypes, type), targetLine, lineIndex));
                }
                else
                {
                    ob.Value = bookId; oi.Value = lineIndex;
                    object? found = ownLine.ExecuteScalar();
                    if (found is null || found is DBNull) continue;
                    long stored = Convert.ToInt64(found);
                    int local = library.ToLocalLineId(stored);
                    result.Add(new Link(
                        SeforimDbLibraryId.ToGlobal(library.Index, bookId),
                        ConnectionTypeId(primaryTypes, type),
                        SeforimDbLibraryId.ToGlobal(library.Index, local),
                        lineIndex));
                }
            }
            return result;
        }

        /// <summary>The (local) book id with this exact title: in the primary database for an "official"
        /// target, else in the library itself. Null when there is none.</summary>
        private static int? BookByTitle(SqliteConnection conn, SeforimDbAttachedLibraries.Library library, bool official,
            string title, Dictionary<(bool Official, string Title), int?> cache)
        {
            var key = (official, title);
            if (cache.TryGetValue(key, out int? known)) return known;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = official
                ? "SELECT id FROM book WHERE title = @t ORDER BY id LIMIT 1"
                : $"SELECT id FROM {library.Alias}.book WHERE title = @t ORDER BY id LIMIT 1";
            cmd.Parameters.AddWithValue("@t", title);
            object? found = cmd.ExecuteScalar();
            int? id = found is null || found is DBNull ? null : Convert.ToInt32(found);
            cache[key] = id;
            return id;
        }
    }
}
