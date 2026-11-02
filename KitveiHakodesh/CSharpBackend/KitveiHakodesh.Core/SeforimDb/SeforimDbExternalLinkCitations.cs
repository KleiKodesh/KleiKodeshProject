using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The other direction of <see cref="SeforimDbExternalLinks"/>: which lines of an additional library CITE a
    /// given line of the official library.
    ///
    /// A personal book that cites an official book ("this line refers to Kiddushin, line 4") is, seen from the
    /// official book, a commentator on that line. The official book's reader asks for exactly that - the links
    /// OUT of its lines, and the books it is linked to - so without this a personal commentary never shows up
    /// beside the book it is about.
    ///
    /// <c>external_link</c> is indexed by the SOURCE line only, so this direction would be a scan of millions of
    /// rows per request. Instead it is read once into compact arrays - per official book, its cited line indexes
    /// sorted, with the citing (book, lineIndex) next to each - and cached per library file version. A request
    /// then costs a binary search per line. The library file is only read.
    /// </summary>
    internal sealed class SeforimDbExternalLinkCitations
    {
        private sealed class Block
        {
            public int[] TargetLine = Array.Empty<int>();   // ascending
            public int[] SourceBook = Array.Empty<int>();
            public int[] SourceLine = Array.Empty<int>();
        }

        /// <summary>One citation: a line of the library (local book id, lineIndex) citing an official line.</summary>
        internal readonly struct Citation
        {
            public Citation(int sourceBook, int sourceLineIndex) { SourceBook = sourceBook; SourceLineIndex = sourceLineIndex; }
            public int SourceBook { get; }
            public int SourceLineIndex { get; }
        }

        private readonly Dictionary<int, Block> _byOfficialBook;

        private SeforimDbExternalLinkCitations(Dictionary<int, Block> byOfficialBook) => _byOfficialBook = byOfficialBook;

        /// <summary>The books of the library that cite this official book (local book ids).</summary>
        internal IReadOnlyCollection<int> CitingBooks(int officialBookId) =>
            _byOfficialBook.TryGetValue(officialBookId, out var block) ? block.SourceBook.Distinct().ToArray() : Array.Empty<int>();

        /// <summary>The citations of one official line (book id, lineIndex).</summary>
        internal IEnumerable<Citation> CitingLines(int officialBookId, int lineIndex)
        {
            if (!_byOfficialBook.TryGetValue(officialBookId, out var block)) yield break;
            int lo = LowerBound(block.TargetLine, lineIndex);
            for (int i = lo; i < block.TargetLine.Length && block.TargetLine[i] == lineIndex; i++)
                yield return new Citation(block.SourceBook[i], block.SourceLine[i]);
        }

        private static int LowerBound(int[] sorted, int value)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (sorted[mid] < value) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // ── one copy per library file version ────────────────────────────────────────────────────────

        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, Lazy<SeforimDbExternalLinkCitations> Index)> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The citation index of the library, read on first use and kept until the file changes. Null
        /// when the library carries no <c>external_link</c> table.</summary>
        internal static SeforimDbExternalLinkCitations? For(SqliteConnection conn, SeforimDbAttachedLibraries.Library library)
        {
            if (!SeforimDbExternalLinks.HasTable(conn, library)) return null;
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(library.Path); } catch { written = DateTime.MinValue; }
            var entry = Cache.AddOrUpdate(
                library.Path,
                _ => (written, new Lazy<SeforimDbExternalLinkCitations>(() => Build(conn, library))),
                (_, old) => old.WriteTimeUtc == written
                    ? old
                    : (written, new Lazy<SeforimDbExternalLinkCitations>(() => Build(conn, library))));
            return entry.Index.Value;
        }

        /// <summary>Starts reading the library's citations in the background, so the first official book opened
        /// after the service starts does not wait for it.</summary>
        internal static void Prewarm(Func<SqliteConnection> open, SeforimDbAttachedLibraries.Library library)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try { using var conn = open(); For(conn, library); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("SeforimDb: external_link citations of '" + library.Label + "' not read: " + ex.Message); }
            });
        }

        private static SeforimDbExternalLinkCitations Build(SqliteConnection conn, SeforimDbAttachedLibraries.Library library)
        {
            // official titles -> the official book id (the first book of that exact title)
            var bookByTitle = new Dictionary<string, int>(StringComparer.Ordinal);
            var titles = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT DISTINCT targetTitle FROM {library.Alias}.external_link WHERE lower(targetSource) = 'official'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) if (!r.IsDBNull(0)) titles.Add(r.GetString(0));
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM book WHERE title = @t ORDER BY id LIMIT 1";
                var p = cmd.Parameters.Add("@t", SqliteType.Text);
                foreach (var title in titles)
                {
                    p.Value = title;
                    object? found = cmd.ExecuteScalar();
                    if (found is not null && found is not DBNull) bookByTitle[title] = Convert.ToInt32(found);
                }
            }

            // every citation, grouped per official book
            var lists = new Dictionary<int, (List<int> Target, List<int> SrcBook, List<int> SrcLine)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $"SELECT targetTitle, targetLineIndex, sourceBookId, sourceLineIndex FROM {library.Alias}.external_link " +
                    "WHERE lower(targetSource) = 'official' AND targetLineIndex IS NOT NULL";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0) || !bookByTitle.TryGetValue(r.GetString(0), out int bookId)) continue;
                    if (!lists.TryGetValue(bookId, out var l)) lists[bookId] = l = (new List<int>(), new List<int>(), new List<int>());
                    l.Target.Add(r.GetInt32(1)); l.SrcBook.Add(r.GetInt32(2)); l.SrcLine.Add(r.GetInt32(3));
                }
            }

            var blocks = new Dictionary<int, Block>(lists.Count);
            foreach (var (bookId, l) in lists)
            {
                int n = l.Target.Count;
                var order = Enumerable.Range(0, n).ToArray();
                Array.Sort(order, (a, b) =>
                {
                    int c = l.Target[a].CompareTo(l.Target[b]);
                    if (c != 0) return c;
                    c = l.SrcBook[a].CompareTo(l.SrcBook[b]);
                    return c != 0 ? c : l.SrcLine[a].CompareTo(l.SrcLine[b]);
                });
                var block = new Block { TargetLine = new int[n], SourceBook = new int[n], SourceLine = new int[n] };
                for (int i = 0; i < n; i++)
                {
                    block.TargetLine[i] = l.Target[order[i]];
                    block.SourceBook[i] = l.SrcBook[order[i]];
                    block.SourceLine[i] = l.SrcLine[order[i]];
                }
                blocks[bookId] = block;
            }
            return new SeforimDbExternalLinkCitations(blocks);
        }
    }
}
