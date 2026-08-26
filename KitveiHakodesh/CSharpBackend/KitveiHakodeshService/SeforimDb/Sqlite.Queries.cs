using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Query logic for the seforim DB. SQL strings live in Sqlite.Strings.cs
/// (SeforimSql); this file only maps parameters and reads rows.
/// </summary>
public sealed partial class SeforimDbService
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
        Run(() =>
        {
            using var conn = Open();
            // Detect the optional orderIndex column at query time (mirrors the
            // frontend's ensureCategorySchema()).
            bool hasOrder = ColumnExists(conn, "category", "orderIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllCategories(hasOrder);
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
        }, "getAllCategories");

        if (list.Count > 0)
            lock (_catalogCacheLock) _categoriesCache ??= list;
        return list;
    }

    public List<BookRow> GetAllBooks()
    {
        lock (_catalogCacheLock)
            if (_booksCache is { } cached) return cached;

        var list = new List<BookRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllBooks;
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
        }, "getAllBooks");

        if (list.Count > 0)
            lock (_catalogCacheLock) _booksCache ??= list;
        return list;
    }

    // ── Book + lines ──────────────────────────────────────────────────────────

    public BookInfo? GetBookById(int id)
    {
        BookInfo? book = null;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetBookById;
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
        }, "getBookById");
        return book;
    }

    /// <summary>
    /// Whether this DB carries alternate versions at all. Cached like
    /// <see cref="_hasLinkAnchorTable"/>: the tables arrived in a later seforim-DB
    /// schema, and an older library simply has no versions to offer.
    /// </summary>
    private bool? _hasBookVersionTables;

    /// <summary>
    /// The alternate versions of a book that carry text, best edition first.
    /// Empty when the DB predates versions or the book has none — the caller shows
    /// no version control at all rather than an empty menu.
    /// </summary>
    public List<BookVersionRow> GetBookVersions(int bookId)
    {
        var list = new List<BookVersionRow>();
        Run(() =>
        {
            using var conn = Open();
            _hasBookVersionTables ??= TableExists(conn, "book_version") && TableExists(conn, "version_line");
            if (!_hasBookVersionTables.Value) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetBookVersions;
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
        }, "getBookVersions");
        return list;
    }

    /// <summary>
    /// A page of lines. With <paramref name="versionId"/> set, the text is read through
    /// that version's overlay; otherwise it is the book's merged text.
    /// </summary>
    public List<LineRow> GetLinesPaged(int bookId, int limit, int offset, int versionId = 0)
    {
        var list = new List<LineRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = versionId > 0 ? SeforimSql.GetVersionLinesPaged : SeforimSql.GetLinesPaged;
            if (versionId > 0) cmd.Parameters.AddWithValue("@versionId", versionId);
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
                    Content = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
        }, "getLinesPaged");
        return list;
    }

    // ── TOC ──────────────────────────────────────────────────────────────────

    public List<TocEntryRow> GetAllTocEntries(int bookId) =>
        ReadTocEntries(SeforimSql.GetAllTocEntries, ("@bookId", bookId));

    public List<TocEntryRow> GetAllAltTocEntries(int structureId) =>
        ReadTocEntries(SeforimSql.GetAllAltTocEntries, ("@structureId", structureId));

    private List<TocEntryRow> ReadTocEntries(string sql, params (string, object)[] ps)
    {
        var list = new List<TocEntryRow>();
        Run(() =>
        {
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
        }, "getTocEntries");
        return list;
    }

    public List<AltTocStructureRow> GetAltTocStructures(int bookId)
    {
        var list = new List<AltTocStructureRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAltTocStructures;
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
        }, "getAltTocStructures");
        return list;
    }

    public List<TocPrefixRow> GetTocEntryByTextPrefix(int bookId, string pattern)
    {
        var list = new List<TocPrefixRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetTocEntryByTextPrefix;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@pattern", pattern);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new TocPrefixRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? null : r.GetInt32(1) });
        }, "getTocEntryByTextPrefix");
        return list;
    }

    public List<TocTitleRow> GetTocTitlesForBooks(List<int> bookIds, string? filterWord)
    {
        var list = new List<TocTitleRow>();
        if (bookIds is null || bookIds.Count == 0) return list;

        string word = (filterWord ?? "").ToLowerInvariant();
        bool usePrefilter = word.Length > 0 && IsLikeSafe(word);

        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = usePrefilter
                ? SeforimSql.GetTocTitlesMatchingForBooks(bookIds.Count)
                : SeforimSql.GetTocTitlesForBooks(bookIds.Count);
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
        }, "getTocTitlesForBooks");
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
        Run(() =>
        {
            using var conn = Open();
            _linkHasTargetLineIndex ??= ColumnExists(conn, "link", "targetLineIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetCommentaryLinksForSourceLineRange(lineIds.Count, _linkHasTargetLineIndex.Value);
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
        }, "getCommentaryLinksForSourceLineRange");
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
        Run(() =>
        {
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            result.Supported = _hasLinkAnchorTable.Value;
            if (!result.Supported) return;
            _linkHasTargetLineIndex ??= ColumnExists(conn, "link", "targetLineIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetWordLinkAnchorsForLines(lineIds.Count, _linkHasTargetLineIndex.Value);
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
        }, "getWordLinkAnchorsForLines");
        return result;
    }

    /// <summary>Distinct word-link targets (commentary book id + anchor label) for one source
    /// book. Returns Supported=false (and no rows) on DBs whose schema predates link_anchor.</summary>
    public WordLinkTargetsResult GetWordLinkAnchorTargetsForBook(int bookId)
    {
        var result = new WordLinkTargetsResult();
        Run(() =>
        {
            using var conn = Open();
            _hasLinkAnchorTable ??= TableExists(conn, "link_anchor");
            result.Supported = _hasLinkAnchorTable.Value;
            if (!result.Supported) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetWordLinkAnchorTargetsForBook;
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
        }, "getWordLinkAnchorTargetsForBook");
        return result;
    }

    public List<LineContentRow> GetLineContents(List<int> lineIds)
    {
        var list = new List<LineContentRow>();
        if (lineIds is null || lineIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLineContents(lineIds.Count);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineContentRow { Id = r.GetInt32(0), Content = r.IsDBNull(1) ? "" : r.GetString(1) });
        }, "getLineContents");
        return list;
    }

    public List<ConnectionTypeRow> GetAllConnectionTypes()
    {
        var list = new List<ConnectionTypeRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllConnectionTypes;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ConnectionTypeRow { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? "" : r.GetString(1) });
        }, "getAllConnectionTypes");
        return list;
    }

    public List<DefaultCommentatorRow> GetDefaultCommentators(int bookId)
    {
        var list = new List<DefaultCommentatorRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetDefaultCommentators;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new DefaultCommentatorRow { CommentatorBookId = r.GetInt32(0) });
        }, "getDefaultCommentators");
        return list;
    }

    // ── Reverse lookups ─────────────────────────────────────────────────────────

    public List<ReverseLineRow> GetReverseLineData(List<int> lineIds, List<int> typeIds)
    {
        var list = new List<ReverseLineRow>();
        if (lineIds is null || lineIds.Count == 0 || typeIds is null || typeIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetReverseLineData(lineIds.Count, typeIds.Count);
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
                    Content = r.IsDBNull(3) ? "" : r.GetString(3),
                });
            }
        }, "getReverseLineData");
        return list;
    }

    public List<ReverseBookRow> GetReverseBooks(int bookId, List<int> typeIds)
    {
        var list = new List<ReverseBookRow>();
        if (typeIds is null || typeIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetReverseBooks(typeIds.Count);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            BindList(cmd, "c", typeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ReverseBookRow { SourceBookId = r.GetInt32(0) });
        }, "getReverseBooks");
        return list;
    }

    public List<StaticFilterRow> GetStaticFilterBooks(int sourceBookId, List<int> typeIds)
    {
        var list = new List<StaticFilterRow>();
        if (typeIds is null || typeIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetStaticFilterBooks(typeIds.Count);
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
        }, "getStaticFilterBooks");
        return list;
    }

    // ── Commentary navigation ────────────────────────────────────────────────────

    public List<SectionNavRow> GetSectionWithCommentary(int mainBookId, int commentaryBookId, int lineIndex, bool next)
    {
        var list = new List<SectionNavRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetSectionWithCommentary(next);
            cmd.Parameters.AddWithValue("@mainBookId", mainBookId);
            cmd.Parameters.AddWithValue("@commentaryBookId", commentaryBookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SectionNavRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
        }, "getSectionWithCommentary");
        return list;
    }

    public List<TocSectionRow> GetTocSectionWithCommentary(int mainBookId, int commentaryBookId, List<int> rangePairs, bool next)
    {
        var list = new List<TocSectionRow>();
        if (rangePairs is null || rangePairs.Count < 2) return list;
        int count = rangePairs.Count / 2;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetTocSectionWithCommentary(count, next);
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
        }, "getTocSectionWithCommentary");
        return list;
    }

    public List<LinkTargetRow> GetLinkTargetForSourceLineAndBook(int sourceLineId, int targetBookId)
    {
        var list = new List<LinkTargetRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinkTargetForSourceLineAndBook;
            cmd.Parameters.AddWithValue("@sourceLineId", sourceLineId);
            cmd.Parameters.AddWithValue("@targetBookId", targetBookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LinkTargetRow { TargetLineId = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
        }, "getLinkTargetForSourceLineAndBook");
        return list;
    }

    // ── TOC paths & line→book/index helpers ──────────────────────────────────────

    public List<TocPathRow> GetTocPathsForLines(List<int> lineIds)
    {
        var list = new List<TocPathRow>();
        if (lineIds is null || lineIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetTocPathsForLines(lineIds.Count);
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
        }, "getTocPathsForLines");
        return list;
    }

    /// <summary>triples = flat [groupKey, firstLineId, lastLineId, …].</summary>
    public List<EnclosingTocPathRow> GetEnclosingTocPathForLineRanges(List<int> triples)
    {
        var list = new List<EnclosingTocPathRow>();
        if (triples is null || triples.Count < 3) return list;
        int groupCount = triples.Count / 3;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetEnclosingTocPathForLineRanges(groupCount);
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
        }, "getEnclosingTocPathForLineRanges");
        return list;
    }

    public List<LineBookRow> GetBookIdsForLines(List<int> lineIds)
    {
        var list = new List<LineBookRow>();
        if (lineIds is null || lineIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetBookIdsForLines(lineIds.Count);
            BindList(cmd, "p", lineIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineBookRow { LineId = r.GetInt32(0), BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
        }, "getBookIdsForLines");
        return list;
    }

    public List<LineIndexRow> GetLineIndexFromLineId(int lineId)
    {
        var list = new List<LineIndexRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLineIndexFromLineId;
            cmd.Parameters.AddWithValue("@id", lineId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LineIndexRow { LineIndex = r.IsDBNull(0) ? 0 : r.GetInt32(0), BookId = r.IsDBNull(1) ? 0 : r.GetInt32(1) });
        }, "getLineIndexFromLineId");
        return list;
    }

    // ── Dictionary sources in the seforim DB ─────────────────────────────────────

    public List<BookIdRow> GetBookIdsByTitlePattern(string pattern) =>
        ReadBookIds(SeforimSql.GetBookIdsByTitlePattern, ("@pattern", pattern));

    public List<BookIdRow> GetBookIdByExactTitle(string title) =>
        ReadBookIds(SeforimSql.GetBookIdByExactTitle, ("@title", title));

    private List<BookIdRow> ReadBookIds(string sql, params (string, object)[] ps)
    {
        var list = new List<BookIdRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new BookIdRow { Id = r.GetInt32(0) });
        }, "readBookIds");
        return list;
    }

    public List<BoldLineRow> GetLinesWithContentPatternForBooks(List<int> bookIds, string pattern)
    {
        var list = new List<BoldLineRow>();
        if (bookIds is null || bookIds.Count == 0) return list;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinesWithContentPatternForBooks(bookIds.Count);
            BindList(cmd, "b", bookIds);
            cmd.Parameters.AddWithValue("@pattern", pattern);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BoldLineRow
                {
                    Content = r.IsDBNull(0) ? "" : r.GetString(0),
                    Title = r.IsDBNull(1) ? "" : r.GetString(1),
                    BookId = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    LineId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    LineIndex = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                });
            }
        }, "getLinesWithContentPatternForBooks");
        return list;
    }

    public List<RawLineRow> GetLinesWithEitherContentPattern(int bookId, string p1, string p2)
    {
        var list = new List<RawLineRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinesWithEitherContentPattern;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@p1", p1);
            cmd.Parameters.AddWithValue("@p2", p2);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RawLineRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1), Content = r.IsDBNull(2) ? "" : r.GetString(2) });
        }, "getLinesWithEitherContentPattern");
        return list;
    }

    public List<RawLineRow> GetLineByBookAndLineIndex(int bookId, int lineIndex)
    {
        var list = new List<RawLineRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLineByBookAndLineIndex;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RawLineRow { Id = r.GetInt32(0), LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1), Content = r.IsDBNull(2) ? "" : r.GetString(2) });
        }, "getLineByBookAndLineIndex");
        return list;
    }

    /// <summary>Binds a list of ints to @{prefix}0..@{prefix}N-1 (for dynamic IN clauses).</summary>
    private static void BindList(SqliteCommand cmd, string prefix, List<int> values)
    {
        for (int i = 0; i < values.Count; i++) cmd.Parameters.AddWithValue("@" + prefix + i, values[i]);
    }
}
