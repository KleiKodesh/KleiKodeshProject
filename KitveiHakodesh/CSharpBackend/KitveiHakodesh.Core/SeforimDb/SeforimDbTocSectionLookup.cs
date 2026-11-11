using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The few TOC questions the related-books resolver asks, answered by indexed queries
    /// instead of loading whole TOCs: the heading a line sits under, an entry and its parent,
    /// the entries of a book whose label equals a given one, and an entry's line range.
    ///
    /// A "TOC" is either a book's main TOC as the reader sees it - GetAllTocEntries, with a
    /// mergeable Seifim structure merged in as the simanim's children (SeifimTocMerge) - or one
    /// alt structure. Answers match what the frontend computed over the full entry list
    /// (findHeadingAt / tocEntryRange / tocLabelPair in BookViewRelatedBooksDropdown.vue).
    /// One instance per request: it remembers what it read for that request only.
    /// </summary>
    internal sealed class TocSectionLookup
    {
        /// <summary>A TOC entry as the matcher sees it. Text is trimmed (JS trim).</summary>
        public sealed class Entry
        {
            public int Id;
            public int? ParentId;
            public int Level;
            public int? LineIndex;
            public string Text = "";
            /// <summary>Position in the full list: main rows by id, a merged seif right after
            /// its siman (then by its own id). Breaks findHeadingAt's ties.</summary>
            public (long, int, long) Order;
        }

        /// <summary>Which TOC: a book's main one (merged seifim included) or an alt structure.</summary>
        public readonly struct Toc : IEquatable<Toc>
        {
            public readonly int BookId;
            public readonly int? AltStructureId;
            public Toc(int bookId, int? altStructureId) { BookId = bookId; AltStructureId = altStructureId; }
            public bool Equals(Toc other) => BookId == other.BookId && AltStructureId == other.AltStructureId;
            public override bool Equals(object? obj) => obj is Toc t && Equals(t);
            public override int GetHashCode() => HashCode.Combine(BookId, AltStructureId);
        }

        private readonly SqliteConnection _conn;
        private readonly Dictionary<int, MergedSeifim?> _merged = new();
        private readonly Dictionary<(Toc, int), Entry?> _headingAt = new();
        private readonly Dictionary<(Toc, int), Entry?> _byId = new();
        private readonly Dictionary<(Toc, int), (int Start, int? End)?> _range = new();
        private readonly Dictionary<string, List<int>> _textIds = new();
        private List<(int Id, string Text)>? _leadingWhitespaceTexts;

        public TocSectionLookup(SqliteConnection conn) { _conn = conn; }

        // -- Merged seifim ---------------------------------------------------------

        /// <summary>A book's merged Seifim structure: its id, and each siman's main-TOC twin
        /// (the LAST main leaf on the siman's line - SeifimTocMerge's map), which becomes the
        /// parent of the siman's seifim, one level below it.</summary>
        internal sealed class MergedSeifim
        {
            public int StructureId;
            public Dictionary<int, (int TwinId, int TwinLevel)> TwinOfSiman = new();
            public int MinSeifLevel = int.MaxValue;
        }

        private MergedSeifim? MergedOf(int bookId)
        {
            if (_merged.TryGetValue(bookId, out var known)) return known;
            var mm = LoadMergedSeifim(_conn, bookId);
            return _merged[bookId] = mm;
        }

        private sealed class LeanRow
        {
            public int Id, TextId, HasChildren, Level;
            public int? ParentId, LineId, LineIndex;
        }

        private static List<LeanRow> ReadLean(SqliteConnection conn, string sql, int key)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@k", key);
            using var r = cmd.ExecuteReader();
            var list = new List<LeanRow>();
            while (r.Read())
                list.Add(new LeanRow
                {
                    Id = r.GetInt32(0),
                    ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    LineId = r.IsDBNull(2) ? null : r.GetInt32(2),
                    TextId = r.GetInt32(3),
                    HasChildren = r.GetInt32(4),
                    Level = r.GetInt32(5),
                    LineIndex = r.IsDBNull(6) ? null : r.GetInt32(6),
                });
            return list;
        }

        /// <summary>
        /// GetAllTocEntries' merge decision, made in memory from the two lists' ids and line
        /// numbers (no text): GetMergeableSeifimStructure's rules, then SeifimTocMerge's own
        /// bail-out (a siman whose last-leaf twin carries another text). The SQL form of the
        /// check walks the main TOC once per siman - 200 ms on Mishna Berura.
        /// </summary>
        internal static MergedSeifim? LoadMergedSeifim(SqliteConnection conn, int bookId)
        {
            int structureId;
            using (var probe = conn.CreateCommand())
            {
                probe.CommandText = "SELECT id FROM alt_toc_structure WHERE bookId = @b AND key = 'Seifim' LIMIT 1";
                probe.Parameters.AddWithValue("@b", bookId);
                if (probe.ExecuteScalar() is not long id) return null;
                structureId = (int)id;
            }
            var main = ReadLean(conn, @"SELECT e.id, e.parentId, e.lineId, e.textId, e.hasChildren, e.level, l.lineIndex
                FROM tocEntry e LEFT JOIN line l ON l.id = e.lineId WHERE e.bookId = @k", bookId);
            var alt = ReadLean(conn, @"SELECT a.id, a.parentId, a.lineId, a.textId, a.hasChildren, a.level, l.lineIndex
                FROM alt_toc_entry a LEFT JOIN line l ON l.id = a.lineId WHERE a.structureId = @k", structureId);

            // The main TOC by (lineIndex, id), NULLs first: each row's LEAD lineIndex.
            var byLine = main.OrderBy(m => m.LineIndex.HasValue ? 1 : 0).ThenBy(m => m.LineIndex ?? 0).ThenBy(m => m.Id).ToList();
            var next = new Dictionary<int, int?>();
            for (int i = 0; i < byLine.Count; i++) next[byLine[i].Id] = i + 1 < byLine.Count ? byLine[i + 1].LineIndex : null;
            var mainByLine = main.Where(m => m.LineId != null).ToLookup(m => m.LineId!.Value);
            var kidsOf = alt.Where(a => a.ParentId != null).ToLookup(a => a.ParentId!.Value);

            var parents = alt.Where(a => a.HasChildren == 1).ToList();
            if (parents.Count == 0) return null;
            foreach (var p in parents)
            {
                bool ok = p.LineId is int pl && mainByLine[pl].Any(m =>
                    m.TextId == p.TextId && m.HasChildren == 0
                    && !kidsOf[p.Id].Any(k => k.HasChildren == 1 || k.LineIndex == null
                        || (m.LineIndex != null && k.LineIndex < m.LineIndex)
                        || (next[m.Id] is int n && k.LineIndex >= n)));
                if (!ok) return null;
            }

            // SeifimTocMerge: the twin is the last main leaf on the siman's line, with the same text.
            var lastLeafByLine = new Dictionary<int, LeanRow>();
            foreach (var m in main.OrderBy(m => m.Id))
                if (m.HasChildren == 0 && m.LineId is int l) lastLeafByLine[l] = m;
            var merged = new MergedSeifim { StructureId = structureId };
            foreach (var siman in alt)
            {
                if (!kidsOf[siman.Id].Any()) continue;
                if (siman.LineId is not int line || !lastLeafByLine.TryGetValue(line, out var twin) || twin.TextId != siman.TextId)
                    return null;
                merged.TwinOfSiman[siman.Id] = (twin.Id, twin.Level);
                merged.MinSeifLevel = Math.Min(merged.MinSeifLevel, twin.Level + 1);
            }
            return merged;
        }

        // Seif rows of a merged structure: (seif id, siman id, seif lineIndex, text).
        private const string SeifSelect = @"
            SELECT k.id, k.parentId, kl.lineIndex, kt.text
            FROM alt_toc_entry k
            JOIN tocText kt ON kt.id = k.textId
            LEFT JOIN line kl ON kl.id = k.lineId
            WHERE k.structureId = @s AND k.parentId IS NOT NULL";

        private static Func<SqliteDataReader, Entry> SeifEntry(MergedSeifim merged) => r =>
        {
            var (twinId, twinLevel) = merged.TwinOfSiman[r.GetInt32(1)];
            return new Entry
            {
                Id = SeifimTocMerge.MergedAltEntryIdOffset + r.GetInt32(0),
                ParentId = twinId,
                Level = twinLevel + 1,
                LineIndex = r.IsDBNull(2) ? null : r.GetInt32(2),
                Text = JsTrim(r.GetString(3)),
                Order = (twinId, 1, r.GetInt32(0)),
            };
        };

        // -- Plain entry rows ------------------------------------------------------

        private static string EntrySelect(Toc toc) => toc.AltStructureId == null
            ? @"SELECT te.id, te.parentId, te.level, ln.lineIndex, tt.text
                FROM tocEntry te JOIN tocText tt ON tt.id = te.textId LEFT JOIN line ln ON ln.id = te.lineId
                WHERE te.bookId = @b"
            : @"SELECT te.id, te.parentId, te.level, ln.lineIndex, tt.text
                FROM alt_toc_entry te JOIN tocText tt ON tt.id = te.textId LEFT JOIN line ln ON ln.id = te.lineId
                WHERE te.structureId = @s";

        private static Entry PlainEntry(SqliteDataReader r) => new()
        {
            Id = r.GetInt32(0),
            ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
            Level = r.GetInt32(2),
            LineIndex = r.IsDBNull(3) ? null : r.GetInt32(3),
            Text = JsTrim(r.GetString(4)),
            Order = (r.GetInt32(0), 0, 0),
        };

        private List<Entry> Read(Toc toc, string sql, Func<SqliteDataReader, Entry> map, Action<SqliteCommand>? bind = null)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@b", toc.BookId);
            cmd.Parameters.AddWithValue("@s", toc.AltStructureId ?? MergedOf(toc.BookId)?.StructureId ?? 0);
            bind?.Invoke(cmd);
            using var r = cmd.ExecuteReader();
            var list = new List<Entry>();
            while (r.Read()) list.Add(map(r));
            return list;
        }

        // -- Questions -------------------------------------------------------------

        /// <summary>findHeadingAt: the last-starting positioned entry at or before the line,
        /// deepest on ties, earliest in list order after that.</summary>
        public Entry? HeadingAt(Toc toc, int lineIndex)
        {
            if (_headingAt.TryGetValue((toc, lineIndex), out var known)) return known;
            var candidates = new List<Entry>();
            // The last line at or before lineIndex that opens an entry, then every entry there.
            string positioned = toc.AltStructureId == null
                ? "FROM line ln JOIN tocEntry te ON te.lineId = ln.id WHERE ln.bookId = @b AND te.bookId = @b AND ln.lineIndex <= @L"
                : "FROM line ln JOIN alt_toc_entry te ON te.lineId = ln.id WHERE ln.bookId = @b AND te.structureId = @s AND ln.lineIndex <= @L";
            candidates.AddRange(Read(toc,
                EntrySelect(toc) + $" AND ln.lineIndex = (SELECT ln.lineIndex {positioned} ORDER BY ln.lineIndex DESC LIMIT 1)",
                PlainEntry, c => c.Parameters.AddWithValue("@L", lineIndex)));
            if (toc.AltStructureId == null && MergedOf(toc.BookId) is { } mergedHere)
                candidates.AddRange(Read(toc,
                    SeifSelect + @" AND kl.lineIndex = (SELECT kl2.lineIndex FROM line kl2
                        JOIN alt_toc_entry k2 ON k2.lineId = kl2.id
                        WHERE kl2.bookId = @b AND k2.structureId = @s AND k2.parentId IS NOT NULL AND kl2.lineIndex <= @L
                        ORDER BY kl2.lineIndex DESC LIMIT 1)",
                    SeifEntry(mergedHere), c => c.Parameters.AddWithValue("@L", lineIndex)));

            Entry? found = null;
            foreach (var e in candidates)
                if (found == null || e.LineIndex > found.LineIndex
                    || (e.LineIndex == found.LineIndex && (e.Level > found.Level
                        || (e.Level == found.Level && e.Order.CompareTo(found.Order) < 0))))
                    found = e;
            foreach (var e in candidates) _byId[(toc, e.Id)] = e;
            return _headingAt[(toc, lineIndex)] = found;
        }

        /// <summary>An entry by id (a merged seif's id included), or null.</summary>
        public Entry? ById(Toc toc, int id)
        {
            if (_byId.TryGetValue((toc, id), out var known)) return known;
            Entry? entry;
            if (toc.AltStructureId == null && id >= SeifimTocMerge.MergedAltEntryIdOffset)
                entry = MergedOf(toc.BookId) is not { } m ? null
                    : Read(toc, SeifSelect + " AND k.id = @id", SeifEntry(m),
                        c => c.Parameters.AddWithValue("@id", id - SeifimTocMerge.MergedAltEntryIdOffset)).FirstOrDefault();
            else
                entry = Read(toc, EntrySelect(toc) + " AND te.id = @id", PlainEntry,
                    c => c.Parameters.AddWithValue("@id", id)).FirstOrDefault();
            return _byId[(toc, id)] = entry;
        }

        /// <summary>The entries whose trimmed text is <paramref name="label"/>, in list order.</summary>
        public List<Entry> WithLabel(Toc toc, string label)
        {
            var textIds = TextIdsFor(label);
            if (textIds.Count == 0) return new List<Entry>();
            string inList = string.Join(",", textIds);
            var list = Read(toc, EntrySelect(toc) + $" AND te.textId IN ({inList})", PlainEntry);
            if (toc.AltStructureId == null && MergedOf(toc.BookId) is { } m)
                list.AddRange(Read(toc, SeifSelect + $" AND k.textId IN ({inList})", SeifEntry(m)));
            foreach (var e in list) _byId[(toc, e.Id)] = e;
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        /// <summary>tocLabelPair: "parent label / label" - the same label can repeat across chapters.</summary>
        public string LabelPair(Toc toc, Entry entry)
        {
            var parent = entry.ParentId is int p ? ById(toc, p) : null;
            return (parent?.Text ?? "") + " / " + entry.Text;
        }

        /// <summary>tocEntryRange: from the entry's line (an unpositioned heading: its earliest
        /// positioned descendant) to the next entry at its level or shallower; null end = book end.</summary>
        public (int Start, int? End)? Range(Toc toc, Entry entry)
        {
            if (_range.TryGetValue((toc, entry.Id), out var known)) return known;
            int? start = entry.LineIndex;
            if (start == null && entry.Id < SeifimTocMerge.MergedAltEntryIdOffset)
            {
                // A merged seif never starts before its siman (the merge check proves it), so
                // the main rows alone give the earliest descendant.
                string table = toc.AltStructureId == null ? "tocEntry" : "alt_toc_entry";
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $@"
                    WITH RECURSIVE d(id) AS (
                      SELECT id FROM {table} WHERE parentId = @id
                      UNION ALL SELECT c.id FROM {table} c JOIN d ON c.parentId = d.id)
                    SELECT MIN(ln.lineIndex) FROM d JOIN {table} e ON e.id = d.id JOIN line ln ON ln.id = e.lineId";
                cmd.Parameters.AddWithValue("@id", entry.Id);
                if (cmd.ExecuteScalar() is long min) start = (int)min;
            }
            if (start == null) return _range[(toc, entry.Id)] = null;

            int? end = null;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = toc.AltStructureId == null
                    ? @"SELECT ln.lineIndex FROM line ln JOIN tocEntry te ON te.lineId = ln.id
                        WHERE ln.bookId = @b AND te.bookId = @b AND ln.lineIndex > @start AND te.level <= @level
                        ORDER BY ln.lineIndex LIMIT 1"
                    : @"SELECT ln.lineIndex FROM line ln JOIN alt_toc_entry te ON te.lineId = ln.id
                        WHERE ln.bookId = @b AND te.structureId = @s AND ln.lineIndex > @start AND te.level <= @level
                        ORDER BY ln.lineIndex LIMIT 1";
                cmd.Parameters.AddWithValue("@b", toc.BookId);
                cmd.Parameters.AddWithValue("@s", toc.AltStructureId ?? 0);
                cmd.Parameters.AddWithValue("@start", start.Value);
                cmd.Parameters.AddWithValue("@level", entry.Level);
                if (cmd.ExecuteScalar() is long next) end = (int)next;
            }
            if (toc.AltStructureId == null && MergedOf(toc.BookId) is { } merged && entry.Level >= merged.MinSeifLevel)
            {
                // Seifs after the start, in line order, until one sits at the entry's level or above.
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT k.parentId, kl.lineIndex FROM line kl JOIN alt_toc_entry k ON k.lineId = kl.id
                    WHERE kl.bookId = @b AND k.structureId = @s AND k.parentId IS NOT NULL
                      AND kl.lineIndex > @start" + (end != null ? " AND kl.lineIndex < @end" : "") + @"
                    ORDER BY kl.lineIndex";
                cmd.Parameters.AddWithValue("@b", toc.BookId);
                cmd.Parameters.AddWithValue("@s", merged.StructureId);
                cmd.Parameters.AddWithValue("@start", start.Value);
                if (end != null) cmd.Parameters.AddWithValue("@end", end.Value);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (merged.TwinOfSiman.TryGetValue(r.GetInt32(0), out var twin) && twin.TwinLevel + 1 <= entry.Level)
                    {
                        end = r.GetInt32(1);
                        break;
                    }
            }
            return _range[(toc, entry.Id)] = (start.Value, end);
        }

        /// <summary>The alt structures the reader's TOC panel offers (a merged Seifim one is part
        /// of the main TOC, not an alt one), by id: (id, key).</summary>
        public List<(int Id, string Key)> AltStructures(int bookId)
        {
            var list = new List<(int, string)>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetAltTocStructures;
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            }
            if (list.Exists(s => s.Item2 == "Seifim") && MergedOf(bookId) is { } merged)
                list.RemoveAll(s => s.Item1 == merged.StructureId);
            return list;
        }

        // -- Label -> text ids -----------------------------------------------------

        /// <summary>JS String.prototype.trim's whitespace (not .NET's: no U+0085, plus U+FEFF).</summary>
        private static bool IsJsWhitespace(char c) =>
            c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680'
              or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';

        internal static string JsTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsJsWhitespace(s[a])) a++;
            while (b > a && IsJsWhitespace(s[b - 1])) b--;
            return a == 0 && b == s.Length ? s : s.Substring(a, b - a);
        }

        // First characters of JS whitespace as [from, to) code point ranges.
        private static readonly (int From, int To)[] WhitespaceRanges =
        {
            (0x09, 0x0E), (0x20, 0x21), (0xA0, 0xA1), (0x1680, 0x1681), (0x2000, 0x200B),
            (0x2028, 0x202A), (0x202F, 0x2030), (0x205F, 0x2060), (0x3000, 0x3001), (0xFEFF, 0xFF00),
        };

        /// <summary>
        /// The tocText ids whose trimmed text is <paramref name="label"/>: the exact text, the
        /// text followed by whitespace (index ranges right after the label), and any text that
        /// STARTS with whitespace (a small fixed set, read once per request).
        /// </summary>
        private List<int> TextIdsFor(string label)
        {
            if (_textIds.TryGetValue(label, out var known)) return known;
            var ids = new List<int>();
            using (var cmd = _conn.CreateCommand())
            {
                var sql = new System.Text.StringBuilder("SELECT id, text FROM tocText WHERE text = @label");
                cmd.Parameters.AddWithValue("@label", label);
                for (int i = 0; i < WhitespaceRanges.Length; i++)
                {
                    sql.Append($" OR (text >= @lo{i} AND text < @hi{i})");
                    cmd.Parameters.AddWithValue("@lo" + i, label + char.ConvertFromUtf32(WhitespaceRanges[i].From));
                    cmd.Parameters.AddWithValue("@hi" + i, label + char.ConvertFromUtf32(WhitespaceRanges[i].To));
                }
                cmd.CommandText = sql.ToString();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (JsTrim(r.GetString(1)) == label) ids.Add(r.GetInt32(0));
            }
            foreach (var (id, text) in LeadingWhitespaceTexts())
                if (JsTrim(text) == label && !ids.Contains(id)) ids.Add(id);
            return _textIds[label] = ids;
        }

        private List<(int, string)> LeadingWhitespaceTexts()
        {
            if (_leadingWhitespaceTexts != null) return _leadingWhitespaceTexts;
            var list = new List<(int, string)>();
            using var cmd = _conn.CreateCommand();
            var sql = new System.Text.StringBuilder("SELECT id, text FROM tocText WHERE text = ''");
            for (int i = 0; i < WhitespaceRanges.Length; i++)
            {
                sql.Append($" OR (text >= @lo{i} AND text < @hi{i})");
                cmd.Parameters.AddWithValue("@lo" + i, char.ConvertFromUtf32(WhitespaceRanges[i].From));
                cmd.Parameters.AddWithValue("@hi" + i, char.ConvertFromUtf32(WhitespaceRanges[i].To));
            }
            cmd.CommandText = sql.ToString();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1)));
            return _leadingWhitespaceTexts = list;
        }
    }
}
