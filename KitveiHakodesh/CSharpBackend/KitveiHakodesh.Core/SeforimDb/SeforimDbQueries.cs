using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Idle memory release: drops the cached catalog (every book + category row). The next
        /// <see cref="GetAllBooks"/>/<see cref="GetAllCategories"/> reloads it from the database
        /// once. Thread-safe: callers already holding a list keep using it unchanged; a load
        /// racing this simply re-caches correct data.
        /// </summary>
        public void ClearCatalogCache()
        {
            lock (_catalogCacheLock)
            {
                _categoriesCache = null;
                _booksCache = null;
            }
        }

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
            r.Close();

            // The precedence every consumer ranks by, computed ONCE here (not in the frontend,
            // not per index) and shipped on the row; rows go out in that order.
            var treeOrder = SeforimDbBookTreeOrder.Load(conn);
            foreach (var row in list)
                row.TreeOrder = treeOrder.TryGetValue(row.Id, out int o) ? o : int.MaxValue;
            list = list.OrderBy(b => b.TreeOrder).ToList();

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

        private bool? _lineHasCharCount;

        /// <summary>
        /// Every line's character count for one book, indexed by lineIndex, or null when
        /// this library predates the <c>line.charCount</c> column. Feeds the reader's
        /// scrollbar with a static per-line height estimate; no text is read.
        /// </summary>
        public int[]? GetLineCharCounts(int bookId)
        {
            using var conn = Open();
            _lineHasCharCount ??= ColumnExists(conn, "line", "charCount");
            if (!_lineHasCharCount.Value) return null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLineCharCounts;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            var counts = new List<int>();
            while (r.Read())
            {
                // Placed by lineIndex, so a gap in the numbering leaves zero-weight rows
                // rather than shifting every count after it onto the wrong line.
                int lineIndex = r.IsDBNull(0) ? counts.Count : r.GetInt32(0);
                while (counts.Count < lineIndex) counts.Add(0);
                counts.Add(r.IsDBNull(1) ? 0 : r.GetInt32(1));
            }
            return counts.ToArray();
        }

        /// <summary>
        /// The ids of the lines in [fromIndex, toIndex), placed by lineIndex (0 where a
        /// lineIndex has no row), with no text. A TOC section's commentary needs its line
        /// ids only; reading the section's text for them cost a whole section per click.
        /// </summary>
        public int[] GetLineIds(int bookId, int fromIndex, int toIndex)
        {
            if (toIndex <= fromIndex) return Array.Empty<int>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLineIds;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@fromIndex", fromIndex);
            cmd.Parameters.AddWithValue("@toIndex", toIndex);
            using var r = cmd.ExecuteReader();
            var ids = new int[toIndex - fromIndex];
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                int slot = r.GetInt32(0) - fromIndex;
                if (slot >= 0 && slot < ids.Length) ids[slot] = r.GetInt32(1);
            }
            return ids;
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
        /// Hands each line of [firstLineIndex, lastLineIndex] (through a version's overlay when
        /// versionId > 0) to <paramref name="onLine"/> as it is read — nothing is collected, so
        /// a scan over a whole book holds one line at a time.
        /// </summary>
        public void ForEachLineInIndexRange(int bookId, int firstLineIndex, int lastLineIndex, int versionId, Action<int, int, string> onLine)
        {
            using var conn = Open();
            using var codec = CodecFor(conn);
            using var cmd = conn.CreateCommand();
            var schema = SchemaOf(conn);
            bool isVersionRead = versionId > 0;
            cmd.CommandText = isVersionRead
                ? SeforimDbSqlStrings.GetVersionLinesInIndexRange(schema)
                : SeforimDbSqlStrings.GetLinesInIndexRange(schema);
            if (isVersionRead) cmd.Parameters.AddWithValue("@versionId", versionId);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@from", firstLineIndex);
            cmd.Parameters.AddWithValue("@to", lastLineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string content = isVersionRead ? ReadVersionLineContent(r, codec) : codec.DecodeColumnOrEmpty(r, 2);
                onLine(r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1), content);
            }
        }

        /// <summary>
        /// Like <see cref="ForEachLineInIndexRange"/> for a sparse set of line indexes — read in
        /// batches of at most 900 (SQLite's bound-parameter limit), each in reading order.
        /// </summary>
        public void ForEachLineByIndexes(int bookId, IReadOnlyList<int> lineIndexes, int versionId, Action<int, int, string> onLine)
        {
            const int Batch = 900;
            using var conn = Open();
            using var codec = CodecFor(conn);
            var schema = SchemaOf(conn);
            bool isVersionRead = versionId > 0;
            for (int at = 0; at < lineIndexes.Count; at += Batch)
            {
                int n = Math.Min(Batch, lineIndexes.Count - at);
                var names = new string[n];
                for (int i = 0; i < n; i++) names[i] = "@i" + i;
                string list = string.Join(",", names);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = isVersionRead
                    ? SeforimDbSqlStrings.GetVersionLinesByIndexList(schema, list)
                    : SeforimDbSqlStrings.GetLinesByIndexList(schema, list);
                if (isVersionRead) cmd.Parameters.AddWithValue("@versionId", versionId);
                cmd.Parameters.AddWithValue("@bookId", bookId);
                for (int i = 0; i < n; i++) cmd.Parameters.AddWithValue(names[i], lineIndexes[at + i]);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string content = isVersionRead ? ReadVersionLineContent(r, codec) : codec.DecodeColumnOrEmpty(r, 2);
                    onLine(r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1), content);
                }
            }
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

        /// <summary>The same links as GetCommentaryLinksForSourceLineRange, de-duplicated and
        /// grouped per (target book, connection type) — see CommentaryLinkGroupRow.</summary>
        public List<CommentaryLinkGroupRow> GetCommentaryLinkGroupsForSourceLineRange(List<int> lineIds)
        {
            var list = new List<CommentaryLinkGroupRow>();
            if (lineIds is null || lineIds.Count == 0) return list;
            using var conn = Open();
            _linkHasTargetLineIndex ??= ColumnExists(conn, "link", "targetLineIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetCommentaryLinkGroupsForSourceLineRange(lineIds.Count, _linkHasTargetLineIndex.Value);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            int curBook = int.MinValue, curType = int.MinValue;
            var ids = new List<int>();
            var indexes = new List<int>();
            void Flush()
            {
                if (ids.Count == 0) return;
                list.Add(new CommentaryLinkGroupRow
                {
                    TargetBookId = curBook,
                    ConnectionTypeId = curType,
                    LineIds = ids.ToArray(),
                    LineIndexes = indexes.ToArray(),
                });
                ids.Clear();
                indexes.Clear();
            }
            while (r.Read())
            {
                int book = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                int type = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                if (book != curBook || type != curType) { Flush(); curBook = book; curType = type; }
                ids.Add(r.IsDBNull(2) ? 0 : r.GetInt32(2));
                indexes.Add(r.IsDBNull(3) ? 0 : r.GetInt32(3));
            }
            Flush();
            return list;
        }

        // Whether the link_anchor table exists (SeforimLibrary schema v2+; neither current
        // Zayit nor Otzaria v1 DB has it). Detected once — the seforim DB is static for the
        // life of the process (see _linkHasTargetLineIndex).
        private bool? _hasLinkAnchorTable;

        /// <summary>Word-level link anchors for a batch of source lines. Returns Supported=false
        /// (and no rows) on DBs whose schema predates link_anchor, so callers can stop asking.</summary>
        public WordLinkAnchorsResult GetWordLinkAnchorsForLines(List<int> lineIds)
        {
            var result = new WordLinkAnchorsResult();
            if (lineIds is null || lineIds.Count == 0)
            {
                result.Supported = _hasLinkAnchorTable ?? true; // unknown yet — don't tell callers to stop
                return result;
            }
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            result.Supported = _hasLinkAnchorTable.Value;
            if (!result.Supported) return result;
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
        /// book. Returns Supported=false (and no rows) on DBs whose schema predates link_anchor.</summary>
        public WordLinkTargetsResult GetWordLinkAnchorTargetsForBook(int bookId)
        {
            var result = new WordLinkTargetsResult();
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            result.Supported = _hasLinkAnchorTable.Value;
            if (!result.Supported) return result;
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

        /// <summary>Lines → their TOC entry, plus each distinct entry's path once. Lines of one
        /// section share an entry, so the path walk (and the wire) is per entry, not per line.</summary>
        public TocPathsForLinesResult GetTocPathsForLines(List<int> lineIds)
        {
            var result = new TocPathsForLinesResult();
            if (lineIds is null || lineIds.Count == 0) return result;
            using var conn = Open();
            var entryIds = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetTocEntryForLines(lineIds.Count);
                BindList(cmd, "p", lineIds);
                using var r = cmd.ExecuteReader();
                var seen = new HashSet<int>();
                while (r.Read())
                {
                    int entryId = r.GetInt32(2);
                    result.Lines.Add(new LineTocEntryRow
                    {
                        LineId = r.GetInt32(0),
                        BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                        TocEntryId = entryId,
                    });
                    if (seen.Add(entryId)) entryIds.Add(entryId);
                }
            }
            if (entryIds.Count == 0) return result;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetTocPathsForTocEntries(entryIds.Count);
                BindList(cmd, "e", entryIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Paths.Add(new TocEntryPathRow
                    {
                        TocEntryId = r.GetInt32(0),
                        Path = r.IsDBNull(1) ? "" : r.GetString(1),
                    });
                }
            }
            return result;
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
        /// Everything the library records about a book, for the book view's info popover.
        /// Null when the book does not exist. Both library schemas are probed per call rather
        /// than cached: it runs once per click, and the user can swap databases under us.
        /// </summary>
        public BookMetadataRow? GetBookMetadata(int bookId)
        {
            using var conn = Open();

            string descriptionColumn = ColumnExists(conn, "book", "heDesc") ? "b.heDesc" : "NULL";
            string collectiveTitleColumn = ColumnExists(conn, "book", "collectiveTitleHe") ? "b.collectiveTitleHe" : "NULL";

            BookMetadataRow? metadata = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.GetBookMetadata(descriptionColumn, collectiveTitleColumn);
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    metadata = new BookMetadataRow
                    {
                        Title = r.IsDBNull(0) ? "" : r.GetString(0),
                        HeRef = ReadTextOrNull(r, 1),
                        ShortDescription = ReadTextOrNull(r, 2),
                        Description = ReadTextOrNull(r, 3),
                        CollectiveTitle = ReadTextOrNull(r, 4),
                        CategoryTitle = ReadTextOrNull(r, 5),
                        SourceName = ReadTextOrNull(r, 6),
                    };
                }
            }
            if (metadata == null) return null;

            if (TableExists(conn, "book_author") && TableExists(conn, "author"))
                metadata.Authors = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataAuthors, bookId);
            if (TableExists(conn, "book_pub_place") && TableExists(conn, "pub_place"))
                metadata.PublicationPlaces = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataPublicationPlaces, bookId);
            if (TableExists(conn, "book_pub_date") && TableExists(conn, "pub_date"))
                metadata.PublicationDates = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataPublicationDates, bookId);
            if (TableExists(conn, "book_topic") && TableExists(conn, "topic"))
                metadata.Topics = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataTopics, bookId);

            // The licence lives in a different place in each library schema.
            if (TableExists(conn, "book_version") && ColumnExists(conn, "book_version", "license"))
            {
                metadata.Licenses = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataLicensesFromVersions, bookId);
                metadata.EditionSources = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataEditionSourcesFromVersions, bookId);
            }
            else if (TableExists(conn, "book_edition") && TableExists(conn, "license") && TableExists(conn, "edition"))
            {
                metadata.Licenses = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataLicensesFromEditions, bookId);
                metadata.EditionSources = ReadBookTextList(conn, SeforimDbSqlStrings.GetBookMetadataEditionSourcesFromEditions, bookId);
            }

            return metadata;
        }

        /// <summary>A trimmed text column, or null when it is NULL or blank.</summary>
        private static string? ReadTextOrNull(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            var text = Convert.ToString(reader.GetValue(ordinal))?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Runs a one-column, @bookId-parameterised query and returns its non-blank values,
        /// de-duplicated in reading order.
        /// </summary>
        private static List<string> ReadBookTextList(SqliteConnection conn, string sql, int bookId)
        {
            var list = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = ReadTextOrNull(r, 0);
                if (text != null && !list.Contains(text)) list.Add(text);
            }
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
