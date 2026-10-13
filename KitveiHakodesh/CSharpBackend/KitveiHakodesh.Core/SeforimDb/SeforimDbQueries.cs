using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Every read of the user's seforim.db — the large Torah-library content database.
    ///
    /// The database is NOT bundled: it is the user's own file, found by
    /// <see cref="SeforimDbPathResolver"/>. When there is none, these methods throw
    /// <see cref="SeforimDbUnavailableException"/> rather than returning empty. An empty
    /// result and a missing library are different answers, and the caller shows different
    /// things for them — the version this replaced logged a warning and returned empty for
    /// both, which is how a missing database came to look like an empty one.
    ///
    /// SQL errors are not caught either. A malformed query or a corrupt file is a fault, and
    /// swallowing it here would leave the caller with an empty list and no way to know.
    ///
    /// SQL strings live in <see cref="SeforimDbSqlStrings"/>; this file binds parameters and
    /// reads rows.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        // ── Catalog ─────────────────────────────────────────────────────────────────
        // The catalog (categories + books) is STATIC for the life of the process — the
        // seforim DB is read-only and only ever replaced while the service is down. Both
        // queries are also the heaviest catalog cost (getAllBooks is a 3-table GROUP BY +
        // group_concat over every book, ~70-90ms), so serve them from an in-memory cache
        // after the first load: every consumer (dev reloads, future hosted path) gets an
        // instant catalog without re-running the join.

        private List<CategoryRow>? _categoriesCache;
        private List<BookRow>? _booksCache;
        private readonly object _catalogCacheLock = new();

        public List<CategoryRow> GetAllCategories()
        {
            lock (_catalogCacheLock)
                if (_categoriesCache is { } cached) return cached;

            var list = new List<CategoryRow>();
            using var conn = Open();
            // Detect the optional orderIndex column at query time (mirrors the
            // frontend's ensureCategorySchema()).
            bool hasOrder = ColumnExists(conn, "category", "orderIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetAllCategories(hasOrder);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CategoryRow
                {
                    Id = r.GetInt32(0),
                    ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    Title = r.IsDBNull(2) ? "" : r.GetString(2),
                    Level = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                });
            }

            if (list.Count > 0)
                lock (_catalogCacheLock) _categoriesCache ??= list;
            return list;
        }

        public List<BookRow> GetAllBooks()
        {
            lock (_catalogCacheLock)
                if (_booksCache is { } cached) return cached;

            var list = new List<BookRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetAllBooks;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookRow
                {
                    Id = r.GetInt32(0),
                    CategoryId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Title = r.IsDBNull(2) ? "" : r.GetString(2),
                    HasTeamim = r.IsDBNull(3) ? null : r.GetInt32(3),
                    Authors = r.IsDBNull(4) ? null : r.GetString(4),
                });
            }

            if (list.Count > 0)
                lock (_catalogCacheLock) _booksCache ??= list;
            return list;
        }

        // ── Book + lines ──────────────────────────────────────────────────────────

        public BookInfo? GetBookById(int id)
        {
            BookInfo? book = null;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetBookById;
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                book = new BookInfo
                {
                    TotalLines = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    HasTeamim = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    HasTargumConnection = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    HasReferenceConnection = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    HasSourceConnection = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    HasCommentaryConnection = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    HasOtherConnection = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                };
            }
            return book;
        }

        /// <param name="versionId">0 for the book's own text; a book_version id to read the
        /// page through that version's overlay instead. See
        /// <see cref="SeforimDbSqlStrings.GetVersionLinesPaged"/> for what an overlay does and
        /// does not replace.</param>
        public List<LineRow> GetLinesPaged(int bookId, int limit, int offset, int versionId = 0)
        {
            var list = new List<LineRow>();
            using var conn = Open();
            using var codec = CodecFor(conn);
            using var cmd = conn.CreateCommand();
            var schema = SchemaOf(conn);
            bool isVersionRead = versionId > 0;
            cmd.CommandText = isVersionRead
                ? SeforimDbSqlStrings.GetVersionLinesPaged(schema)
                : SeforimDbSqlStrings.GetLinesPaged(schema);
            if (isVersionRead) cmd.Parameters.AddWithValue("@versionId", versionId);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LineRow
                {
                    Id = r.GetInt32(0),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Content = isVersionRead
                        ? ReadVersionLineContent(r, codec)
                        : codec.DecodeColumnOrEmpty(r, 2),
                });
            }
            return list;
        }

        /// <summary>
        /// The text of one row of <see cref="SeforimDbSqlStrings.GetVersionLinesPaged"/>.
        ///
        /// That statement hands back BOTH candidate columns plus a discriminator rather than
        /// choosing in SQL, because a CASE that mixes a BLOB arm with a text arm makes SQLite
        /// coerce the blob to TEXT and the compressed frame arrives as unreadable bytes. The
        /// choice is made here instead, where each column keeps its own type and goes through
        /// the codec intact.
        /// </summary>
        private static string ReadVersionLineContent(SqliteDataReader r, SeforimDbLineContentCodec codec)
        {
            const int BaseContentOrdinal = 2;
            const int VersionContentOrdinal = 3;
            const int ContentSourceOrdinal = 4;

            int contentSource = r.IsDBNull(ContentSourceOrdinal)
                ? SeforimDbSqlStrings.VersionLineContentSource.Uncovered
                : r.GetInt32(ContentSourceOrdinal);

            return contentSource switch
            {
                SeforimDbSqlStrings.VersionLineContentSource.Base
                    => codec.DecodeColumnOrEmpty(r, BaseContentOrdinal),
                SeforimDbSqlStrings.VersionLineContentSource.Version
                    => codec.DecodeColumnOrEmpty(r, VersionContentOrdinal),
                _ => string.Empty,
            };
        }

        // ── TOC ──────────────────────────────────────────────────────────────────

        public List<TocEntryRow> GetAllTocEntries(int bookId) =>
            ReadTocEntries(SeforimDbSqlStrings.GetAllTocEntries, ("@bookId", bookId));

        public List<TocEntryRow> GetAllAltTocEntries(int structureId) =>
            ReadTocEntries(SeforimDbSqlStrings.GetAllAltTocEntries, ("@structureId", structureId));

        private List<TocEntryRow> ReadTocEntries(string sql, params (string, object)[] ps)
        {
            var list = new List<TocEntryRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TocEntryRow
                {
                    Id = r.GetInt32(0),
                    ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    Level = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    LineId = r.IsDBNull(3) ? null : r.GetInt32(3),
                    HasChildren = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    Text = r.IsDBNull(5) ? "" : r.GetString(5),
                    LineIndex = r.IsDBNull(6) ? null : r.GetInt32(6),
                });
            }
            return list;
        }

        public List<AltTocStructureRow> GetAltTocStructures(int bookId)
        {
            var list = new List<AltTocStructureRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetAltTocStructures;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AltTocStructureRow
                {
                    Id = r.GetInt32(0),
                    Key = r.IsDBNull(1) ? "" : r.GetString(1),
                    Title = r.IsDBNull(2) ? null : r.GetString(2),
                    HeTitle = r.IsDBNull(3) ? null : r.GetString(3),
                });
            }
            return list;
        }

        public List<TocPrefixRow> GetTocEntryByTextPrefix(int bookId, string pattern)
        {
            var list = new List<TocPrefixRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetTocEntryByTextPrefix;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@pattern", pattern);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new TocPrefixRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? null : r.GetInt32(1) });
            return list;
        }

        public List<TocTitleRow> GetTocTitlesForBooks(List<int> bookIds, string? filterWord)
        {
            var list = new List<TocTitleRow>();
            if (bookIds is null || bookIds.Count == 0) return list;

            string word = (filterWord ?? "").ToLowerInvariant();
            bool usePrefilter = word.Length > 0 && IsLikeSafe(word);

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = usePrefilter
                ? SeforimDbSqlStrings.GetTocTitlesMatchingForBooks(bookIds.Count)
                : SeforimDbSqlStrings.GetTocTitlesForBooks(bookIds.Count);
            for (int i = 0; i < bookIds.Count; i++) cmd.Parameters.AddWithValue("@b" + i, bookIds[i]);
            if (usePrefilter) cmd.Parameters.AddWithValue("@word", EscapeLikeWord(word));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TocTitleRow
                {
                    Id = r.GetInt32(0),
                    ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    BookId = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    Text = r.IsDBNull(3) ? "" : r.GetString(3),
                    LineIndex = r.IsDBNull(4) ? null : r.GetInt32(4),
                });
            }
            return list;
        }

        // Mirrors the frontend LIKE_SAFE_WORD_RE = /^[ -~֐-׿]+$/ and escapeLikeWord().
        private static bool IsLikeSafe(string word)
        {
            if (word.Length == 0) return false;
            foreach (char c in word)
            {
                bool ascii = c is >= ' ' and <= '~';
                bool hebrew = c is >= '֐' and <= '׿'; // Hebrew block (regex ֐-׿)
                if (!ascii && !hebrew) return false;
            }
            return true;
        }

        private static string EscapeLikeWord(string word)
        {
            var sb = new System.Text.StringBuilder(word.Length);
            foreach (char c in word)
            {
                if (c is '\\' or '%' or '_') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Commentary / links ──────────────────────────────────────────────────────

        // Whether link.targetLineIndex exists (Zayit: yes, Otzaria: no). Detected once —
        // the seforim DB is static for the life of the process (see the catalog caches).
        private bool? _linkHasTargetLineIndex;

        public List<CommentaryLinkRow> GetCommentaryLinksForSourceLineRange(List<int> lineIds)
        {
            var list = new List<CommentaryLinkRow>();
            if (lineIds is null || lineIds.Count == 0) return list;
            using var conn = Open();
            _linkHasTargetLineIndex ??= ColumnExists(conn, "link", "targetLineIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetCommentaryLinksForSourceLineRange(lineIds.Count, _linkHasTargetLineIndex.Value);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CommentaryLinkRow
                {
                    TargetBookId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    TargetLineId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    ConnectionTypeId = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    LineIndex = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                });
            }
            return list;
        }

        // Whether the link_anchor table exists (Otzaria has it; Zayit does not, and its word
        // links come from the inline commentator markers instead). Detected once — the seforim
        // DB is static for the life of the process (see _linkHasTargetLineIndex).
        private bool? _hasLinkAnchorTable;

        /// <summary>Word-level link anchors for a batch of source lines — from link_anchor, or on
        /// a database without it, from the inline commentator markers. Always Supported.</summary>
        public WordLinkAnchorsResult GetWordLinkAnchorsForLines(List<int> lineIds)
        {
            var result = new WordLinkAnchorsResult { Supported = true };
            if (lineIds is null || lineIds.Count == 0) return result;
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            if (!_hasLinkAnchorTable.Value)
            {
                result.Rows = GetInlineMarkerAnchorsForLines(conn, lineIds);
                return result;
            }
            _linkHasTargetLineIndex ??= ColumnExists(conn, "link", "targetLineIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetWordLinkAnchorsForLines(lineIds.Count, _linkHasTargetLineIndex.Value);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Rows.Add(new WordLinkAnchorRow
                {
                    LineId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    CharStart = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    CharEnd = r.IsDBNull(2) ? null : r.GetInt32(2),
                    Label = r.IsDBNull(3) ? null : r.GetString(3),
                    TargetBookId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    TargetLineId = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    TargetLineIndex = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    SourceBookId = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                });
            }
            return result;
        }

        /// <summary>Distinct word-link targets (commentary book id + anchor label) for one source
        /// book — from link_anchor, or from the inline commentator markers. Always Supported.</summary>
        public WordLinkTargetsResult GetWordLinkAnchorTargetsForBook(int bookId)
        {
            var result = new WordLinkTargetsResult();
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            result.Supported = true;
            if (!_hasLinkAnchorTable.Value)
            {
                result.Rows = InlineMarkerResolutionFor(conn, bookId).Targets;
                return result;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetWordLinkAnchorTargetsForBook;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Rows.Add(new WordLinkTargetRow
                {
                    TargetBookId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    Label = r.IsDBNull(1) ? null : r.GetString(1),
                });
            }
            return result;
        }

        // Per source book: what its inline commentator-marker names resolve to. Static for the
        // life of the process, like the rest of this database.
        private readonly ConcurrentDictionary<int, SeforimDbInlineCommentatorMarkers.BookResolution> _inlineMarkerBooks = new();

        /// <summary>Word-link anchors built from the inline commentator markers in the line text —
        /// the stand-in for link_anchor on databases that lack it. See
        /// <see cref="SeforimDbInlineCommentatorMarkers"/>.</summary>
        private List<WordLinkAnchorRow> GetInlineMarkerAnchorsForLines(SqliteConnection conn, List<int> lineIds)
        {
            var rows = new List<WordLinkAnchorRow>();

            var markerLines = new List<(int LineId, int BookId, List<SeforimDbInlineCommentatorMarkers.Marker> Markers)>();
            using (var codec = CodecFor(conn))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetLineBookIdsAndContents(SchemaOf(conn), lineIds.Count);
                BindList(cmd, "p", lineIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var markers = SeforimDbInlineCommentatorMarkers.Parse(codec.DecodeColumnOrEmpty(r, 2));
                    if (markers.Count > 0) markerLines.Add((r.GetInt32(0), r.GetInt32(1), markers));
                }
            }
            if (markerLines.Count == 0) return rows;

            var withMarkers = markerLines.Select(l => l.LineId).ToList();
            var linkedLines = new Dictionary<int, List<SeforimDbInlineCommentatorMarkers.LinkedLine>>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetLinkedLinesForLines(withMarkers.Count);
                BindList(cmd, "p", withMarkers);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int lineId = r.GetInt32(0);
                    if (!linkedLines.TryGetValue(lineId, out var list)) linkedLines[lineId] = list = new();
                    list.Add(new SeforimDbInlineCommentatorMarkers.LinkedLine(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
                }
            }

            foreach (var line in markerLines)
            {
                if (!linkedLines.TryGetValue(line.LineId, out var linked)) continue;
                var resolution = InlineMarkerResolutionFor(conn, line.BookId);
                rows.AddRange(SeforimDbInlineCommentatorMarkers.AnchorsForLine(
                    line.LineId, line.BookId, line.Markers, linked, resolution));
            }
            rows.Sort((a, b) => a.LineId != b.LineId ? a.LineId.CompareTo(b.LineId) : a.CharStart.CompareTo(b.CharStart));
            return rows;
        }

        /// <summary>Resolves one source book's marker names, once: reads every line of the book
        /// and the books each line is linked with.</summary>
        private SeforimDbInlineCommentatorMarkers.BookResolution InlineMarkerResolutionFor(SqliteConnection conn, int bookId)
        {
            if (_inlineMarkerBooks.TryGetValue(bookId, out var cached)) return cached;

            var markersByLine = new Dictionary<int, List<SeforimDbInlineCommentatorMarkers.Marker>>();
            using (var codec = CodecFor(conn))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetBookLineContents(SchemaOf(conn));
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var markers = SeforimDbInlineCommentatorMarkers.Parse(codec.DecodeColumnOrEmpty(r, 1));
                    if (markers.Count > 0) markersByLine[r.GetInt32(0)] = markers;
                }
            }

            var linkedBooks = new Dictionary<int, HashSet<int>>();
            if (markersByLine.Count > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimDbSqlStrings.GetLinkedBooksForBookLines;
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int lineId = r.GetInt32(0);
                    if (!markersByLine.ContainsKey(lineId)) continue;
                    if (!linkedBooks.TryGetValue(lineId, out var set)) linkedBooks[lineId] = set = new();
                    set.Add(r.GetInt32(1));
                }
            }

            var resolution = SeforimDbInlineCommentatorMarkers.ResolveBook(
                markersByLine.Select(kv => (kv.Value, linkedBooks.TryGetValue(kv.Key, out var set) ? set : new HashSet<int>())));
            return _inlineMarkerBooks.GetOrAdd(bookId, resolution);
        }

        public List<LineContentRow> GetLineContents(List<int> lineIds)
        {
            var list = new List<LineContentRow>();
            if (lineIds is null || lineIds.Count == 0) return list;
            using var conn = Open();
            using var codec = CodecFor(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLineContents(SchemaOf(conn), lineIds.Count);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineContentRow { Id = r.GetInt32(0), Content = codec.DecodeColumnOrEmpty(r, 1) });
            return list;
        }

        public List<ConnectionTypeRow> GetAllConnectionTypes()
        {
            var list = new List<ConnectionTypeRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetAllConnectionTypes;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ConnectionTypeRow { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? "" : r.GetString(1) });
            return list;
        }

        public List<DefaultCommentatorRow> GetDefaultCommentators(int bookId)
        {
            var list = new List<DefaultCommentatorRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetDefaultCommentators;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new DefaultCommentatorRow { CommentatorBookId = r.GetInt32(0) });
            return list;
        }

        // ── Reverse lookups ─────────────────────────────────────────────────────────

        public List<ReverseLineRow> GetReverseLineData(List<int> lineIds, List<int> typeIds)
        {
            var list = new List<ReverseLineRow>();
            if (lineIds is null || lineIds.Count == 0 || typeIds is null || typeIds.Count == 0) return list;
            using var conn = Open();
            using var codec = CodecFor(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetReverseLineData(SchemaOf(conn), lineIds.Count, typeIds.Count);
            BindList(cmd, "t", lineIds);
            BindList(cmd, "c", typeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ReverseLineRow
                {
                    SourceBookId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    SourceLineId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    LineIndex = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    Content = codec.DecodeColumnOrEmpty(r, 3),
                });
            }
            return list;
        }

        public List<ReverseBookRow> GetReverseBooks(int bookId, List<int> typeIds)
        {
            var list = new List<ReverseBookRow>();
            if (typeIds is null || typeIds.Count == 0) return list;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetReverseBooks(SchemaOf(conn), typeIds.Count);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            BindList(cmd, "c", typeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ReverseBookRow { SourceBookId = r.GetInt32(0) });
            return list;
        }

        public List<StaticFilterRow> GetStaticFilterBooks(int sourceBookId, List<int> typeIds)
        {
            var list = new List<StaticFilterRow>();
            if (typeIds is null || typeIds.Count == 0) return list;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetStaticFilterBooks(typeIds.Count);
            cmd.Parameters.AddWithValue("@bookId", sourceBookId);
            BindList(cmd, "c", typeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StaticFilterRow
                {
                    TargetBookId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    ConnectionTypeId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                });
            }
            return list;
        }

        // ── Commentary navigation ────────────────────────────────────────────────────

        public List<SectionNavRow> GetSectionWithCommentary(int mainBookId, int commentaryBookId, int lineIndex, bool next)
        {
            var list = new List<SectionNavRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetSectionWithCommentary(next);
            cmd.Parameters.AddWithValue("@mainBookId", mainBookId);
            cmd.Parameters.AddWithValue("@commentaryBookId", commentaryBookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SectionNavRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
            return list;
        }

        public List<TocSectionRow> GetTocSectionWithCommentary(int mainBookId, int commentaryBookId, List<int> rangePairs, bool next)
        {
            var list = new List<TocSectionRow>();
            if (rangePairs is null || rangePairs.Count < 2) return list;
            int count = rangePairs.Count / 2;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetTocSectionWithCommentary(count, next);
            for (int i = 0; i < count; i++)
            {
                cmd.Parameters.AddWithValue("@s" + i, rangePairs[i * 2]);
                cmd.Parameters.AddWithValue("@e" + i, rangePairs[i * 2 + 1]);
            }
            cmd.Parameters.AddWithValue("@mainBookId", mainBookId);
            cmd.Parameters.AddWithValue("@commentaryBookId", commentaryBookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new TocSectionRow { SectionStart = r.GetInt32(0) });
            return list;
        }

        public List<LinkTargetRow> GetLinkTargetForSourceLineAndBook(int sourceLineId, int targetBookId)
        {
            var list = new List<LinkTargetRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLinkTargetForSourceLineAndBook;
            cmd.Parameters.AddWithValue("@sourceLineId", sourceLineId);
            cmd.Parameters.AddWithValue("@targetBookId", targetBookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LinkTargetRow { TargetLineId = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
            return list;
        }

        // ── TOC paths & line→book/index helpers ──────────────────────────────────────

        public List<TocPathRow> GetTocPathsForLines(List<int> lineIds)
        {
            var list = new List<TocPathRow>();
            if (lineIds is null || lineIds.Count == 0) return list;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetTocPathsForLines(lineIds.Count);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TocPathRow
                {
                    LineId = r.GetInt32(0),
                    BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    TocPath = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
            return list;
        }

        /// <summary>triples = flat [groupKey, firstLineId, lastLineId, …].</summary>
        public List<EnclosingTocPathRow> GetEnclosingTocPathForLineRanges(List<int> triples)
        {
            var list = new List<EnclosingTocPathRow>();
            if (triples is null || triples.Count < 3) return list;
            int groupCount = triples.Count / 3;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetEnclosingTocPathForLineRanges(groupCount);
            for (int i = 0; i < groupCount; i++)
            {
                cmd.Parameters.AddWithValue("@g" + i, triples[i * 3]);
                cmd.Parameters.AddWithValue("@f" + i, triples[i * 3 + 1]);
                cmd.Parameters.AddWithValue("@l" + i, triples[i * 3 + 2]);
            }
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new EnclosingTocPathRow
                {
                    GroupKey = r.GetInt32(0),
                    BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    TocPath = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
            return list;
        }

        public List<LineBookRow> GetBookIdsForLines(List<int> lineIds)
        {
            var list = new List<LineBookRow>();
            if (lineIds is null || lineIds.Count == 0) return list;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetBookIdsForLines(lineIds.Count);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineBookRow { LineId = r.GetInt32(0), BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
            return list;
        }

        public List<LineIndexRow> GetLineIndexFromLineId(int lineId)
        {
            var list = new List<LineIndexRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLineIndexFromLineId;
            cmd.Parameters.AddWithValue("@id", lineId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineIndexRow { LineIndex = r.IsDBNull(0) ? 0 : r.GetInt32(0), BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
            return list;
        }

        // ── Dictionary sources in the seforim DB ─────────────────────────────────────

        public List<BookIdRow> GetBookIdsByTitlePattern(string pattern) =>
            ReadBookIds(SeforimDbSqlStrings.GetBookIdsByTitlePattern, ("@pattern", pattern));

        public List<BookIdRow> GetBookIdByExactTitle(string title) =>
            ReadBookIds(SeforimDbSqlStrings.GetBookIdByExactTitle, ("@title", title));

        private List<BookIdRow> ReadBookIds(string sql, params (string, object)[] ps)
        {
            var list = new List<BookIdRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new BookIdRow { Id = r.GetInt32(0) });
            return list;
        }

        public List<RawLineRow> GetLineByBookAndLineIndex(int bookId, int lineIndex)
        {
            var list = new List<RawLineRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            using var codec = CodecFor(conn);
            cmd.CommandText = SeforimDbSqlStrings.GetLineByBookAndLineIndex(SchemaOf(conn));
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RawLineRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1), Content = codec.DecodeColumnOrEmpty(r, 2) });
            return list;
        }

        // ── Book versions, provenance and schema stamp ───────────────────────────────

        /// <summary>Whether this database has the version-overlay tables at all. Probed once:
        /// they arrive in a later library schema and a book view asks on every open.</summary>
        private bool? _hasBookVersionTables;


        /// <summary>
        /// The alternate text versions of a book, best first. Empty on a library whose schema
        /// predates the version tables — that is a shape, not a failure, so the caller simply
        /// shows no version picker.
        /// </summary>
        public List<BookVersionRow> GetBookVersions(int bookId)
        {
            var list = new List<BookVersionRow>();
            using var conn = Open();

            _hasBookVersionTables ??= TableExists(conn, "book_version") && TableExists(conn, "version_line");
            if (!_hasBookVersionTables.Value) return list;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetBookVersions;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookVersionRow
                {
                    Id = r.GetInt32(0),
                    VersionTitle = r.IsDBNull(1) ? "" : r.GetString(1),
                    HeVersionTitle = r.IsDBNull(2) ? null : r.GetString(2),
                    VersionSource = r.IsDBNull(3) ? null : r.GetString(3),
                    VersionNotes = r.IsDBNull(4) ? null : r.GetString(4),
                    HeVersionNotes = r.IsDBNull(5) ? null : r.GetString(5),
                });
            }
            return list;
        }

        /// <summary>The upstream source a book came from, for crediting a report.</summary>
        public List<BookSourceNameRow> GetBookSourceName(int bookId)
        {
            var list = new List<BookSourceNameRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetBookSourceName;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new BookSourceNameRow { SourceName = r.IsDBNull(0) ? "" : r.GetString(0) });
            return list;
        }

        /// <summary>
        /// The library build number, for stamping a report. Empty on a library with no
        /// schema_meta table — nothing depends on the number, so there is nothing to fail.
        /// </summary>
        public List<SchemaMetaValueRow> GetLibraryDbVersion()
        {
            var list = new List<SchemaMetaValueRow>();
            using var conn = Open();

            if (!TableExists(conn, "schema_meta")) return list;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLibraryDbVersion;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SchemaMetaValueRow { Value = r.IsDBNull(0) ? "" : r.GetString(0) });
            return list;
        }

        /// <summary>One line's canonical reference and stored text, so a reported mistake can be
        /// checked against what the library actually holds.</summary>
        public List<LineRefRow> GetLineRefAndContent(int bookId, int lineIndex)
        {
            var list = new List<LineRefRow>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            using var codec = CodecFor(conn);
            cmd.CommandText = SeforimDbSqlStrings.GetLineRefAndContent(SchemaOf(conn));
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LineRefRow
                {
                    Id = r.GetInt32(0),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    HeRef = r.IsDBNull(2) ? null : r.GetString(2),
                    Content = codec.DecodeColumnOrEmpty(r, 3),
                });
            }
            return list;
        }

        /// <summary>Binds a list of ints to @{prefix}0..@{prefix}N-1 (for dynamic IN clauses).</summary>
        private static void BindList(SqliteCommand cmd, string prefix, List<int> values)
        {
            for (int i = 0; i < values.Count; i++) cmd.Parameters.AddWithValue("@" + prefix + i, values[i]);
        }
    }
}
