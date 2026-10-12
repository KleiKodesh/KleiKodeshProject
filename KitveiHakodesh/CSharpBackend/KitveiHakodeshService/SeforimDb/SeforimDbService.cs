using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Transport wrapper over <see cref="SeforimDbQueries"/> — read-only access to the user's
/// seforim.db (the large Torah-library content database).
///
/// Core owns the path resolution, the connection policy, the schema probing, the line-content
/// codec and every one of the queries below, with their SQL in SeforimDbSqlStrings. The only
/// thing this class adds is the service's ERROR POLICY: Core returns data or throws (it never
/// swallows), and a service RPC answers an empty result with a logged error rather than taking
/// the connection down. That decision belongs to a host, which is why it lives here.
///
/// The row types are Core's and are serialized straight onto the wire; there are no mirrors.
/// </summary>
public sealed class SeforimDbService(ILogger<SeforimDbService> logger)
{
    private readonly SeforimDbQueries _queries = new();

    /// <summary>Whether there is a database to read — false when the user has chosen none, or
    /// the one they chose has moved.</summary>
    public bool HasDb => _queries.IsAvailable;

    /// <summary>
    /// Client-triggered warm-up — called by an app when IT loads (never at service boot; boot
    /// stays idle by design). Pays the one-time cold costs in the background so the user's first
    /// real query does not: loading the SQLite native library, opening the first pooled
    /// connection, filling the static catalog cache and JITting the hot read paths.
    ///
    /// Fire-and-forget is the HOST's choice: Core's Warmup runs synchronously and throws what it
    /// hits, precisely so the caller decides where it runs and what a failure means. Here it
    /// means nothing — the first real query will warm instead.
    /// </summary>
    public void Warmup()
    {
        if (!HasDb) return;
        _ = Task.Run(() =>
        {
            try
            {
                _queries.Warmup();
                logger.LogInformation("seforim DB warm-up complete (client-triggered)");
            }
            catch { /* best-effort — the first real query will warm instead */ }
        });
    }

    // ── Queries ───────────────────────────────────────────────────────────────────
    //
    // Every one of these is a straight delegation. Core holds the SQL and the row reading;
    // Run supplies the "no database / failed" answer this transport wants.

    public List<CategoryRow> GetAllCategories() =>
        Run(() => _queries.GetAllCategories(), new List<CategoryRow>(), "getAllCategories");

    public List<BookRow> GetAllBooks() =>
        Run(() => _queries.GetAllBooks(), new List<BookRow>(), "getAllBooks");

    public BookInfo? GetBookById(int id) =>
        Run(() => _queries.GetBookById(id), null, "getBookById");

    public List<BookVersionRow> GetBookVersions(int bookId) =>
        Run(() => _queries.GetBookVersions(bookId), new List<BookVersionRow>(), "getBookVersions");

    public List<LineRow> GetLinesPaged(int bookId, int limit, int offset, int versionId = 0) =>
        Run(() => _queries.GetLinesPaged(bookId, limit, offset, versionId), new List<LineRow>(), "getLinesPaged");

    public List<TocEntryRow> GetAllTocEntries(int bookId) =>
        Run(() => _queries.GetAllTocEntries(bookId), new List<TocEntryRow>(), "getAllTocEntries");

    public List<TocEntryRow> GetAllAltTocEntries(int structureId) =>
        Run(() => _queries.GetAllAltTocEntries(structureId), new List<TocEntryRow>(), "getAllAltTocEntries");

    public List<AltTocStructureRow> GetAltTocStructures(int bookId) =>
        Run(() => _queries.GetAltTocStructures(bookId), new List<AltTocStructureRow>(), "getAltTocStructures");

    public List<TocPrefixRow> GetTocEntryByTextPrefix(int bookId, string pattern) =>
        Run(() => _queries.GetTocEntryByTextPrefix(bookId, pattern), new List<TocPrefixRow>(), "getTocEntryByTextPrefix");

    public List<TocTitleRow> GetTocTitlesForBooks(List<int> bookIds, string? filterWord) =>
        Run(() => _queries.GetTocTitlesForBooks(bookIds, filterWord), new List<TocTitleRow>(), "getTocTitlesForBooks");

    public List<CommentaryLinkRow> GetCommentaryLinksForSourceLineRange(List<int> lineIds) =>
        Run(() => _queries.GetCommentaryLinksForSourceLineRange(lineIds), new List<CommentaryLinkRow>(), "getCommentaryLinksForSourceLineRange");

    // A failed or DB-less call answers Supported = true with no rows. Supported = false means
    // "this schema has no link_anchor", which the page caches until it reloads — a transient
    // failure must not switch word links off for the rest of the session.
    public WordLinkAnchorsResult GetWordLinkAnchorsForLines(List<int> lineIds) =>
        Run(() => _queries.GetWordLinkAnchorsForLines(lineIds), new WordLinkAnchorsResult { Supported = true }, "getWordLinkAnchorsForLines");

    public WordLinkTargetsResult GetWordLinkAnchorTargetsForBook(int bookId) =>
        Run(() => _queries.GetWordLinkAnchorTargetsForBook(bookId), new WordLinkTargetsResult { Supported = true }, "getWordLinkAnchorTargetsForBook");

    public List<LineContentRow> GetLineContents(List<int> lineIds) =>
        Run(() => _queries.GetLineContents(lineIds), new List<LineContentRow>(), "getLineContents");

    public List<ConnectionTypeRow> GetAllConnectionTypes() =>
        Run(() => _queries.GetAllConnectionTypes(), new List<ConnectionTypeRow>(), "getAllConnectionTypes");

    public List<DefaultCommentatorRow> GetDefaultCommentators(int bookId) =>
        Run(() => _queries.GetDefaultCommentators(bookId), new List<DefaultCommentatorRow>(), "getDefaultCommentators");

    public List<ReverseLineRow> GetReverseLineData(List<int> lineIds, List<int> typeIds) =>
        Run(() => _queries.GetReverseLineData(lineIds, typeIds), new List<ReverseLineRow>(), "getReverseLineData");

    public List<ReverseBookRow> GetReverseBooks(int bookId, List<int> typeIds) =>
        Run(() => _queries.GetReverseBooks(bookId, typeIds), new List<ReverseBookRow>(), "getReverseBooks");

    public List<StaticFilterRow> GetStaticFilterBooks(int sourceBookId, List<int> typeIds) =>
        Run(() => _queries.GetStaticFilterBooks(sourceBookId, typeIds), new List<StaticFilterRow>(), "getStaticFilterBooks");

    public List<SectionNavRow> GetSectionWithCommentary(int mainBookId, int commentaryBookId, int lineIndex, bool next) =>
        Run(() => _queries.GetSectionWithCommentary(mainBookId, commentaryBookId, lineIndex, next), new List<SectionNavRow>(), "getSectionWithCommentary");

    public List<TocSectionRow> GetTocSectionWithCommentary(int mainBookId, int commentaryBookId, List<int> rangePairs, bool next) =>
        Run(() => _queries.GetTocSectionWithCommentary(mainBookId, commentaryBookId, rangePairs, next), new List<TocSectionRow>(), "getTocSectionWithCommentary");

    public List<LinkTargetRow> GetLinkTargetForSourceLineAndBook(int sourceLineId, int targetBookId) =>
        Run(() => _queries.GetLinkTargetForSourceLineAndBook(sourceLineId, targetBookId), new List<LinkTargetRow>(), "getLinkTargetForSourceLineAndBook");

    public List<TocPathRow> GetTocPathsForLines(List<int> lineIds) =>
        Run(() => _queries.GetTocPathsForLines(lineIds), new List<TocPathRow>(), "getTocPathsForLines");

    public List<EnclosingTocPathRow> GetEnclosingTocPathForLineRanges(List<int> triples) =>
        Run(() => _queries.GetEnclosingTocPathForLineRanges(triples), new List<EnclosingTocPathRow>(), "getEnclosingTocPathForLineRanges");

    public List<LineBookRow> GetBookIdsForLines(List<int> lineIds) =>
        Run(() => _queries.GetBookIdsForLines(lineIds), new List<LineBookRow>(), "getBookIdsForLines");

    public List<LineIndexRow> GetLineIndexFromLineId(int lineId) =>
        Run(() => _queries.GetLineIndexFromLineId(lineId), new List<LineIndexRow>(), "getLineIndexFromLineId");

    public List<BookIdRow> GetBookIdsByTitlePattern(string pattern) =>
        Run(() => _queries.GetBookIdsByTitlePattern(pattern), new List<BookIdRow>(), "getBookIdsByTitlePattern");

    public List<BookIdRow> GetBookIdByExactTitle(string title) =>
        Run(() => _queries.GetBookIdByExactTitle(title), new List<BookIdRow>(), "getBookIdByExactTitle");

    public List<BoldLineRow> GetLinesWithContentPatternForBooks(List<int> bookIds, string pattern) =>
        Run(() => _queries.GetLinesWithContentPatternForBooks(bookIds, pattern), new List<BoldLineRow>(), "getLinesWithContentPatternForBooks");

    public List<RawLineRow> GetLinesWithEitherContentPattern(int bookId, string p1, string p2) =>
        Run(() => _queries.GetLinesWithEitherContentPattern(bookId, p1, p2), new List<RawLineRow>(), "getLinesWithEitherContentPattern");

    public List<RawLineRow> GetLineByBookAndLineIndex(int bookId, int lineIndex) =>
        Run(() => _queries.GetLineByBookAndLineIndex(bookId, lineIndex), new List<RawLineRow>(), "getLineByBookAndLineIndex");

    public List<BookSourceNameRow> GetBookSourceName(int bookId) =>
        Run(() => _queries.GetBookSourceName(bookId), new List<BookSourceNameRow>(), "getBookSourceName");

    public List<SchemaMetaValueRow> GetLibraryDbVersion() =>
        Run(() => _queries.GetLibraryDbVersion(), new List<SchemaMetaValueRow>(), "getLibraryDbVersion");

    public List<LineRefRow> GetLineRefAndContent(int bookId, int lineIndex) =>
        Run(() => _queries.GetLineRefAndContent(bookId, lineIndex), new List<LineRefRow>(), "getLineRefAndContent");
    /// <summary>Runs one Core query under the service's error policy: no database, or a query
    /// that threw, becomes <paramref name="whenUnavailable"/> plus a log line — never an
    /// exception escaping into the RPC loop.</summary>
    private T Run<T>(Func<T> query, T whenUnavailable, string op)
    {
        if (!HasDb)
        {
            logger.LogWarning("seforim DB not available (path={Path})", _queries.DatabasePath ?? "<unset>");
            return whenUnavailable;
        }
        try { return query(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Op} failed", op);
            return whenUnavailable;
        }
    }
}
