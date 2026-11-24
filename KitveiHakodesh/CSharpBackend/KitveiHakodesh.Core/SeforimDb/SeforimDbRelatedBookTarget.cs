using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    public sealed partial class SeforimDbQueries
    {
        /// <summary>
        /// Where a book picked in the related-books list opens: the line in
        /// <paramref name="target"/> that corresponds to <paramref name="topLineIndex"/> in
        /// <paramref name="fromBookId"/>, or null for the book's start. See
        /// <see cref="RelatedBookTargetResolver"/> for the rules.
        /// </summary>
        public int? ResolveRelatedTarget(int fromBookId, int topLineIndex, RelatedBook target)
        {
            using var conn = Open();
            return new RelatedBookTargetResolver(conn, SchemaOf(conn)).Resolve(fromBookId, topLineIndex, target);
        }
    }

    /// <summary>
    /// The related-books click: links first, kept inside the TOC section that matches the
    /// reader's place.
    ///
    ///   1. A line reached by links: a base text by the reverse (base -> this book) links near
    ///      the top line; a commentary of a base text through that base's line (and, for a
    ///      commentary on one of the base's commentaries, through that one's line too); any
    ///      other book by this line's own link, else the nearest linked line ahead, then behind.
    ///   2. The TOC section of the target whose heading matches the reader's (deepest heading
    ///      first, then its parents; main TOC, then the alt structures). A linked line inside it
    ///      wins; else its start - but only when the section's division lines up with the
    ///      reader's: the Rif labels its own folios "daf" too, so a section is trusted only when
    ///      one of its first lines links back into the section the reader sits in.
    ///
    /// The same answers as the dropdown's former frontend code (resolveTargetLineIndex and
    /// what it called), from indexed queries rather than one RPC per probe line.
    /// </summary>
    internal sealed class RelatedBookTargetResolver
    {
        private const int BaseLineProbeRadius = 30;
        private const int DivisionProbeLines = 3;

        private readonly SqliteConnection _conn;
        private readonly SeforimDbSchema _schema;
        private readonly List<int> _dependantTypeIds;
        private readonly HashSet<int> _commentaryShapedTypeIds;
        private readonly BaseTextLookup _bases;
        private readonly TocSectionLookup _toc;

        private readonly struct Position
        {
            public readonly int BookId;
            public readonly int LineIndex;
            public Position(int bookId, int lineIndex) { BookId = bookId; LineIndex = lineIndex; }
        }

        private readonly struct LinkedLine
        {
            public readonly int LineId;
            public readonly int LineIndex;
            public LinkedLine(int lineId, int lineIndex) { LineId = lineId; LineIndex = lineIndex; }
        }

        private readonly struct Section
        {
            public readonly int Start;
            public readonly int? End;
            public Section(int start, int? end) { Start = start; End = end; }
            public bool Contains(int lineIndex) => lineIndex >= Start && (End == null || lineIndex < End);
        }

        public RelatedBookTargetResolver(SqliteConnection conn, SeforimDbSchema schema)
        {
            _conn = conn;
            _schema = schema;
            var typeNames = SeforimDbQueries.ReadConnectionTypeNames(conn);
            _dependantTypeIds = SeforimDbQueries.DependantTypeIds(typeNames);
            _commentaryShapedTypeIds = new HashSet<int>(typeNames
                .Where(kv => kv.Value is "COMMENTARY" or "SUPER_COMMENTARY" or "TARGUM").Select(kv => kv.Key));
            _bases = new BaseTextLookup(conn, schema, _dependantTypeIds);
            _toc = new TocSectionLookup(conn);
        }

        // -- Target line -----------------------------------------------------------

        public int? Resolve(int fromBookId, int topLineIndex, RelatedBook book)
        {
            var (linked, positions) = ResolveLinked(fromBookId, topLineIndex, book);
            foreach (var position in positions)
            {
                var section = FindMatchingSection(position, book.BookId);
                if (section == null) continue;
                if (linked is int l && section.Value.Contains(l)) return linked;
                if (SectionMatchesReaderDivision(book.BookId, section.Value, position)) return section.Value.Start;
            }
            return linked;
        }

        private (int? Linked, List<Position> Positions) ResolveLinked(int fromBookId, int topLineIndex, RelatedBook book)
        {
            var top = new Position(fromBookId, topLineIndex);
            int? topLineId = LineIdAt(fromBookId, topLineIndex);
            if (topLineId == null) return (null, new List<Position> { top });

            // A commentary of a base text has no link of its own: hop through the base's line,
            // and for a commentary on one of the base's commentaries through that one's too.
            // A commentary on the open book itself (Rif -> Shiltei) links directly.
            if (book.ViaSourceBookId is int via && book.ChainBaseBookId != fromBookId)
            {
                var sourceLine = BaseLineNear(fromBookId, topLineIndex, via);
                if (sourceLine == null) return (null, new List<Position> { top });
                if (book.ChainBaseBookId is int chain)
                {
                    var chainLine = LinkedLineFrom(via, sourceLine.Value, chain);
                    if (chainLine == null) return (null, new List<Position>());
                    var chained = LinkedLineFrom(chain, chainLine.Value, book.BookId);
                    return (chained?.LineIndex, new List<Position> { new(chain, chainLine.Value.LineIndex) });
                }
                var target = LinkedLineFrom(via, sourceLine.Value, book.BookId);
                return (target?.LineIndex, new List<Position> { top, new(via, sourceLine.Value.LineIndex) });
            }

            if (book.IsBaseText)
            {
                var baseLine = BaseLineNear(fromBookId, topLineIndex, book.BookId);
                return (baseLine?.LineIndex, baseLine is { } b
                    ? new List<Position> { top, new(book.BookId, b.LineIndex) }
                    : new List<Position> { top });
            }

            var direct = LinkedLineFrom(fromBookId, new LinkedLine(topLineId.Value, topLineIndex), book.BookId);
            return (direct?.LineIndex, new List<Position> { top });
        }

        private int? LineIdAt(int bookId, int lineIndex)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM line WHERE bookId = @b AND lineIndex = @i LIMIT 1";
            cmd.Parameters.AddWithValue("@b", bookId);
            cmd.Parameters.AddWithValue("@i", lineIndex);
            return cmd.ExecuteScalar() is long id ? (int)id : null;
        }

        // -- Base line: reverse links near the top line ------------------------------

        /// <summary>
        /// The base text's line linked to the nearest line around the top one (top, then +1, -1,
        /// +2, -2 ... up to the radius), in ONE query over the whole window. Base-text links are
        /// stored base -> this book, so this is a reverse lookup; a link counts when the data
        /// declares the base, or the pair passes GetReverseLineData's corpus test. Of several
        /// base lines on one probe line, the one GetReverseLineData listed first (lowest type
        /// id, then link id - its index order).
        /// </summary>
        private LinkedLine? BaseLineNear(int fromBookId, int topLineIndex, int baseBookId)
        {
            if (baseBookId == fromBookId || _dependantTypeIds.Count == 0) return null;
            bool undeclaredOk = _bases.PairAllowedUndeclared(baseBookId, fromBookId);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT ln.lineIndex, l.sourceLineId, sl.lineIndex, l.connectionTypeId, l.id
                FROM line ln
                JOIN link l ON l.targetLineId = ln.id
                JOIN line sl ON sl.id = l.sourceLineId
                WHERE ln.bookId = @from AND ln.lineIndex BETWEEN @lo AND @hi
                  AND l.connectionTypeId IN ({SeforimDbSqlStrings.InPlaceholders("c", _dependantTypeIds.Count)})
                  AND l.sourceBookId = @base
                  {(undeclaredOk ? "" : $"AND {_schema.DeclaredBaseExpr("l")} > 0")}";
            cmd.Parameters.AddWithValue("@from", fromBookId);
            cmd.Parameters.AddWithValue("@lo", topLineIndex - BaseLineProbeRadius);
            cmd.Parameters.AddWithValue("@hi", topLineIndex + BaseLineProbeRadius);
            cmd.Parameters.AddWithValue("@base", baseBookId);
            for (int i = 0; i < _dependantTypeIds.Count; i++) cmd.Parameters.AddWithValue("@c" + i, _dependantTypeIds[i]);

            (int Distance, int Side, int Type, long LinkId)? best = null;
            LinkedLine? found = null;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int probe = r.GetInt32(0);
                int distance = Math.Abs(probe - topLineIndex);
                var key = (distance, probe >= topLineIndex ? 0 : 1, r.GetInt32(3), r.GetInt64(4));
                if (best == null || key.CompareTo(best.Value) < 0)
                {
                    best = key;
                    found = new LinkedLine(r.GetInt32(1), r.GetInt32(2));
                }
            }
            return found;
        }

        // -- Linked line: this line's link, else the nearest linked line ahead / behind --

        private LinkedLine? LinkedLineFrom(int fromBookId, LinkedLine from, int targetBookId)
        {
            if (LinkTarget(from.LineId, targetBookId) is { } direct) return direct;
            foreach (bool ahead in new[] { true, false })
                if (NearestLinkedLine(fromBookId, targetBookId, from.LineIndex, ahead) is int lineId
                    && LinkTarget(lineId, targetBookId) is { } near)
                    return near;
            return null;
        }

        /// <summary>GetLinkTargetForSourceLineAndBook: commentary-shaped links first, then the
        /// earliest target line.</summary>
        private LinkedLine? LinkTarget(int sourceLineId, int targetBookId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLinkTargetForSourceLineAndBook;
            cmd.Parameters.AddWithValue("@sourceLineId", sourceLineId);
            cmd.Parameters.AddWithValue("@targetBookId", targetBookId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? new LinkedLine(r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1)) : null;
        }

        /// <summary>
        /// The nearest line of <paramref name="fromBookId"/> ahead of (or behind) the line that
        /// has any link to <paramref name="targetBookId"/>. Walks the lines, probing each one's
        /// links by (sourceLineId, targetBookOrderIndex) - an index seek to the target's links
        /// only, instead of reading every link of every line passed (GetSectionWithCommentary).
        /// Timed against that walk and against scanning the two books' link set: ~2x faster than
        /// the first, 3-7x than the second.
        /// </summary>
        private int? NearestLinkedLine(int fromBookId, int targetBookId, int lineIndex, bool ahead)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT ln.id FROM line ln
                WHERE ln.bookId = @fromBookId AND ln.lineIndex {(ahead ? ">" : "<")} @lineIndex
                  AND EXISTS (SELECT 1 FROM link lk
                              WHERE lk.sourceLineId = ln.id
                                AND lk.targetBookOrderIndex = (SELECT orderIndex FROM book WHERE id = @targetBookId)
                                AND lk.targetBookId = @targetBookId)
                ORDER BY ln.lineIndex {(ahead ? "ASC" : "DESC")} LIMIT 1";
            cmd.Parameters.AddWithValue("@fromBookId", fromBookId);
            cmd.Parameters.AddWithValue("@targetBookId", targetBookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            return cmd.ExecuteScalar() is long id ? (int)id : null;
        }

        // -- TOC section match -----------------------------------------------------

        /// <summary>findMatchingTocSection: the main TOC first, then each alt structure the two
        /// books share by key.</summary>
        private Section? FindMatchingSection(Position from, int targetBookId)
        {
            var main = MatchCascade(new TocSectionLookup.Toc(from.BookId, null), new TocSectionLookup.Toc(targetBookId, null), from.LineIndex);
            if (main != null) return main;

            var fromAlt = _toc.AltStructures(from.BookId);
            if (fromAlt.Count == 0) return null;
            var targetAlt = _toc.AltStructures(targetBookId);
            foreach (var (id, key) in fromAlt)
            {
                int at = targetAlt.FindIndex(t => t.Key == key);
                if (at < 0) continue;
                var alt = MatchCascade(new TocSectionLookup.Toc(from.BookId, id),
                    new TocSectionLookup.Toc(targetBookId, targetAlt[at].Id), from.LineIndex);
                if (alt != null) return alt;
            }
            return null;
        }

        /// <summary>matchTocCascade: the reader's heading looked up by label in the target (a
        /// repeated label disambiguated by its parent's), then its parent, and up.</summary>
        private Section? MatchCascade(TocSectionLookup.Toc current, TocSectionLookup.Toc target, int lineIndex)
        {
            var heading = _toc.HeadingAt(current, lineIndex);
            while (heading != null)
            {
                var sameLabel = _toc.WithLabel(target, heading.Text);
                List<TocSectionLookup.Entry> matches = sameLabel;
                if (sameLabel.Count > 1)
                {
                    string pair = _toc.LabelPair(current, heading);
                    matches = sameLabel.Where(e => _toc.LabelPair(target, e) == pair).ToList();
                }
                if (matches.Count == 1 && _toc.Range(target, matches[0]) is { } range)
                    return new Section(range.Start, range.End);
                heading = heading.ParentId is int p ? _toc.ById(current, p) : null;
            }
            return null;
        }

        /// <summary>
        /// sectionMatchesReaderDivision: one of the matched section's first lines links (either
        /// direction) into the TOC section the reader sits in.
        /// </summary>
        private bool SectionMatchesReaderDivision(int targetBookId, Section section, Position position)
        {
            var readerToc = new TocSectionLookup.Toc(position.BookId, null);
            var heading = _toc.HeadingAt(readerToc, position.LineIndex);
            if (heading == null || _toc.Range(readerToc, heading) is not { } r) return false;
            var reader = new Section(r.Start, r.End);

            int probeCount = section.End == null ? DivisionProbeLines : Math.Min(DivisionProbeLines, section.End.Value - section.Start);
            if (probeCount <= 0) return false;
            var probeIds = new List<int>();
            using (var cmd = _conn.CreateCommand())
            {
                // getLinesPaged's positional OFFSET, as the frontend paged it.
                cmd.CommandText = "SELECT id FROM line WHERE bookId = @b ORDER BY lineIndex LIMIT @n OFFSET @o";
                cmd.Parameters.AddWithValue("@b", targetBookId);
                cmd.Parameters.AddWithValue("@n", probeCount);
                cmd.Parameters.AddWithValue("@o", section.Start);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) probeIds.Add(rd.GetInt32(0));
            }
            if (probeIds.Count == 0) return false;
            string ids = string.Join(",", probeIds);

            // Reverse: the reader's book links INTO a probe line (GetReverseLineData's rules).
            if (position.BookId != targetBookId && _dependantTypeIds.Count > 0)
            {
                bool undeclaredOk = _bases.PairAllowedUndeclared(position.BookId, targetBookId);
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT sl.lineIndex FROM link l JOIN line sl ON sl.id = l.sourceLineId
                    WHERE l.targetLineId IN ({ids})
                      AND l.connectionTypeId IN ({string.Join(",", _dependantTypeIds)})
                      AND l.sourceBookId = @reader
                      {(undeclaredOk ? "" : $"AND {_schema.DeclaredBaseExpr("l")} > 0")}";
                cmd.Parameters.AddWithValue("@reader", position.BookId);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    if (reader.Contains(rd.GetInt32(0))) return true;
            }

            // Forward: each probe line's own first link into the reader's book.
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT lk.sourceLineId, lk.connectionTypeId, ln.lineIndex
                    FROM link lk JOIN line ln ON ln.id = lk.targetLineId
                    WHERE lk.sourceLineId IN ({ids}) AND lk.targetBookId = @reader";
                cmd.Parameters.AddWithValue("@reader", position.BookId);
                var first = new Dictionary<int, (int Rank, int LineIndex)>();
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var key = (_commentaryShapedTypeIds.Contains(rd.GetInt32(1)) ? 0 : 1, rd.GetInt32(2));
                    int lineId = rd.GetInt32(0);
                    if (!first.TryGetValue(lineId, out var known) || key.CompareTo(known) < 0) first[lineId] = key;
                }
                return first.Values.Any(f => reader.Contains(f.LineIndex));
            }
        }
    }
}
