using System.Text.Json;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SefroimDb;
using KitveiHakodeshService.UserSettings;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Routes a decoded RPC request to the owning handler and returns the
/// serialized response envelope. This is the single place ops are registered —
/// each future capability adds one case here.
/// </summary>
public sealed class Dispatcher(
    DocumentLocatorService locator,
    HebrewBooksService hebrewBooks,
    DictionaryService dictionary,
    SeforimDbService seforim,
    FullTextSearchService fts,
    UserSettingsService userSettings)
{
    public async Task<string> DispatchAsync(string requestJson, CancellationToken ct)
    {
        RpcRequest? req;
        try
        {
            req = JsonSerializer.Deserialize(requestJson, RpcJsonContext.Default.RpcRequest);
        }
        catch (Exception ex)
        {
            return RpcResponse.Err("Invalid request JSON: " + ex.Message);
        }

        if (req is null || string.IsNullOrEmpty(req.Op))
            return RpcResponse.Err("Missing 'op'.");

        try
        {
            switch (req.Op)
            {
                case "ping":
                    return RpcResponse.Ok("{\"pong\":true}");

                case "locateDocuments":
                {
                    var args = req.Args.ValueKind == JsonValueKind.Object
                        ? req.Args.Deserialize(RpcJsonContext.Default.LocateDocumentsArgs) ?? new LocateDocumentsArgs()
                        : new LocateDocumentsArgs();
                    int max = args.Max > 0 ? args.Max : 200;
                    var result = await locator.LocateAsync(args.Query ?? "", max, ct);
                    return RpcResponse.Ok(
                        JsonSerializer.Serialize(result, RpcJsonContext.Default.LocateDocumentsResult));
                }

                case "locateDocumentsWarmup":
                    locator.Warmup();
                    return RpcResponse.Ok("{\"started\":true}");

                case "hbSearch":
                {
                    var args = req.Args.ValueKind == JsonValueKind.Object
                        ? req.Args.Deserialize(RpcJsonContext.Default.HbSearchArgs) ?? new HbSearchArgs()
                        : new HbSearchArgs();
                    var result = hebrewBooks.Search(args.Query ?? "", args.LocalFolder, args.Limit);
                    return RpcResponse.Ok(
                        JsonSerializer.Serialize(result, RpcJsonContext.Default.HbSearchResult));
                }

                // ── Dictionary (KitveiHakodesh_dictionary.db) ──────────────────
                case "dictExact":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        dictionary.Exact(Term(req.Args)), RpcJsonContext.Default.DictExactResult));

                case "dictPrefix":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictSensesResult { Rows = dictionary.Prefix(Term(req.Args)) },
                        RpcJsonContext.Default.DictSensesResult));

                case "dictContains":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictSensesResult { Rows = dictionary.Contains(Term(req.Args)) },
                        RpcJsonContext.Default.DictSensesResult));

                case "dictLinks":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictLinksResult { Links = dictionary.Links(Term(req.Args)) },
                        RpcJsonContext.Default.DictLinksResult));

                case "dictSynonyms":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictWordsResult { Words = dictionary.Synonyms(Term(req.Args)) },
                        RpcJsonContext.Default.DictWordsResult));

                case "dictVariants":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictWordsResult { Words = dictionary.Variants(Term(req.Args)) },
                        RpcJsonContext.Default.DictWordsResult));

                case "dictSpellCandidates":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictWordsResult { Words = dictionary.SpellCandidates(Term(req.Args)) },
                        RpcJsonContext.Default.DictWordsResult));

                case "dictAbbrevSenses":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        dictionary.AbbrevSenses(Candidates(req.Args)),
                        RpcJsonContext.Default.DictAbbrevResult));

                case "dictKetivVariants":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DictWordsResult { Words = dictionary.KetivVariants(Candidates(req.Args)) },
                        RpcJsonContext.Default.DictWordsResult));

                // ── Seforim DB — catalog ───────────────────────────────────────
                case "getAllCategories":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new CategoriesResult { Rows = seforim.GetAllCategories() },
                        RpcJsonContext.Default.CategoriesResult));

                case "getAllBooks":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new BooksResult { Rows = seforim.GetAllBooks() },
                        RpcJsonContext.Default.BooksResult));

                case "getBookById":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.BookByIdArgs) ?? new BookByIdArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new BookByIdResult { Book = seforim.GetBookById(a.Id) },
                        RpcJsonContext.Default.BookByIdResult));
                }

                case "getLinesPaged":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LinesPagedArgs) ?? new LinesPagedArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new LinesResult { Rows = seforim.GetLinesPaged(a.BookId, a.Limit, a.Offset) },
                        RpcJsonContext.Default.LinesResult));
                }

                // ── Seforim DB — TOC ───────────────────────────────────────────
                case "getAllTocEntries":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocByBookArgs) ?? new TocByBookArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocEntriesResult { Rows = seforim.GetAllTocEntries(a.BookId) },
                        RpcJsonContext.Default.TocEntriesResult));
                }

                case "getAltTocStructures":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocByBookArgs) ?? new TocByBookArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new AltTocStructuresResult { Rows = seforim.GetAltTocStructures(a.BookId) },
                        RpcJsonContext.Default.AltTocStructuresResult));
                }

                case "getAllAltTocEntries":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocByStructureArgs) ?? new TocByStructureArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocEntriesResult { Rows = seforim.GetAllAltTocEntries(a.StructureId) },
                        RpcJsonContext.Default.TocEntriesResult));
                }

                case "getTocTitlesForBooks":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocTitlesArgs) ?? new TocTitlesArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocTitlesResult { Rows = seforim.GetTocTitlesForBooks(a.BookIds, a.FilterWord) },
                        RpcJsonContext.Default.TocTitlesResult));
                }

                case "getTocEntryByTextPrefix":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocPrefixArgs) ?? new TocPrefixArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocPrefixResult { Rows = seforim.GetTocEntryByTextPrefix(a.BookId, a.Pattern) },
                        RpcJsonContext.Default.TocPrefixResult));
                }

                // ── Seforim DB — commentary/links ──────────────────────────────
                case "getCommentaryLinksForSourceLineRange":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineIdsArgs) ?? new LineIdsArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new CommentaryLinksResult { Rows = seforim.GetCommentaryLinksForSourceLineRange(a.LineIds) },
                        RpcJsonContext.Default.CommentaryLinksResult));
                }

                case "getLineContents":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineIdsArgs) ?? new LineIdsArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new LineContentsResult { Rows = seforim.GetLineContents(a.LineIds) },
                        RpcJsonContext.Default.LineContentsResult));
                }

                case "getAllConnectionTypes":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new ConnectionTypesResult { Rows = seforim.GetAllConnectionTypes() },
                        RpcJsonContext.Default.ConnectionTypesResult));

                case "getDefaultCommentators":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.BookIdArgs) ?? new BookIdArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new DefaultCommentatorsResult { Rows = seforim.GetDefaultCommentators(a.BookId) },
                        RpcJsonContext.Default.DefaultCommentatorsResult));
                }

                case "getReverseLineData":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.ReverseLineDataArgs) ?? new ReverseLineDataArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new ReverseLineDataResult { Rows = seforim.GetReverseLineData(a.LineIds, a.TypeIds) },
                        RpcJsonContext.Default.ReverseLineDataResult));
                }

                case "getReverseBooks":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.ReverseBooksArgs) ?? new ReverseBooksArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new ReverseBooksResult { Rows = seforim.GetReverseBooks(a.BookId, a.TypeIds) },
                        RpcJsonContext.Default.ReverseBooksResult));
                }

                case "getStaticFilterBooks":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.StaticFilterArgs) ?? new StaticFilterArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new StaticFilterResult { Rows = seforim.GetStaticFilterBooks(a.SourceBookId, a.TypeIds) },
                        RpcJsonContext.Default.StaticFilterResult));
                }

                case "getSectionWithCommentary":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.SectionNavArgs) ?? new SectionNavArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new SectionNavResult { Rows = seforim.GetSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.LineIndex, a.Direction != "prev") },
                        RpcJsonContext.Default.SectionNavResult));
                }

                case "getTocSectionWithCommentary":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TocSectionArgs) ?? new TocSectionArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocSectionResult { Rows = seforim.GetTocSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.RangePairs, a.Direction != "prev") },
                        RpcJsonContext.Default.TocSectionResult));
                }

                case "getLinkTargetForSourceLineAndBook":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LinkTargetArgs) ?? new LinkTargetArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new LinkTargetResult { Rows = seforim.GetLinkTargetForSourceLineAndBook(a.SourceLineId, a.TargetBookId) },
                        RpcJsonContext.Default.LinkTargetResult));
                }

                case "getTocPathsForLines":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineIdsArgs) ?? new LineIdsArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new TocPathsResult { Rows = seforim.GetTocPathsForLines(a.LineIds) },
                        RpcJsonContext.Default.TocPathsResult));
                }

                case "getEnclosingTocPathForLineRanges":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.EnclosingTocPathArgs) ?? new EnclosingTocPathArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new EnclosingTocPathResult { Rows = seforim.GetEnclosingTocPathForLineRanges(a.Triples) },
                        RpcJsonContext.Default.EnclosingTocPathResult));
                }

                case "getBookIdsForLines":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineIdsArgs) ?? new LineIdsArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new LineBooksResult { Rows = seforim.GetBookIdsForLines(a.LineIds) },
                        RpcJsonContext.Default.LineBooksResult));
                }

                case "getLineIndexFromLineId":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineIdArgs) ?? new LineIdArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new LineIndexResult { Rows = seforim.GetLineIndexFromLineId(a.LineId) },
                        RpcJsonContext.Default.LineIndexResult));
                }

                // ── Seforim DB — dictionary sources (מצודת/מלבי״ם/מנחם/ערוך) ────
                case "getBookIdsByTitlePattern":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.TitlePatternArgs) ?? new TitlePatternArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new BookIdsResult { Rows = seforim.GetBookIdsByTitlePattern(a.Pattern) },
                        RpcJsonContext.Default.BookIdsResult));
                }

                case "getBookIdByExactTitle":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.ExactTitleArgs) ?? new ExactTitleArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new BookIdsResult { Rows = seforim.GetBookIdByExactTitle(a.Title) },
                        RpcJsonContext.Default.BookIdsResult));
                }

                case "getLinesWithContentPatternForBooks":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.BoldLinesArgs) ?? new BoldLinesArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new BoldLinesResult { Rows = seforim.GetLinesWithContentPatternForBooks(a.BookIds, a.Pattern) },
                        RpcJsonContext.Default.BoldLinesResult));
                }

                case "getLinesWithEitherContentPattern":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.EitherPatternArgs) ?? new EitherPatternArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new RawLinesResult { Rows = seforim.GetLinesWithEitherContentPattern(a.BookId, a.P1, a.P2) },
                        RpcJsonContext.Default.RawLinesResult));
                }

                case "getLineByBookAndLineIndex":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.LineByIndexArgs) ?? new LineByIndexArgs();
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        new RawLinesResult { Rows = seforim.GetLineByBookAndLineIndex(a.BookId, a.LineIndex) },
                        RpcJsonContext.Default.RawLinesResult));
                }

                // ── User settings (highlights/notes) — read + write ────────────
                case "userSettingsQuery":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.RawSqlArgs) ?? new RawSqlArgs();
                    string rowsJson = userSettings.QueryRowsJson(a.Sql ?? "", a.Params ?? []);
                    return RpcResponse.Ok("{\"rows\":" + rowsJson + "}");
                }

                case "userSettingsExecute":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.RawSqlArgs) ?? new RawSqlArgs();
                    long id = userSettings.Execute(a.Sql ?? "", a.Params ?? []);
                    return RpcResponse.Ok("{\"lastInsertId\":" + id + "}");
                }

                // ── Full-text search (FtsLib) ──────────────────────────────────
                case "ftsSearch":
                {
                    var a = req.Args.Deserialize(RpcJsonContext.Default.FtsSearchArgs) ?? new FtsSearchArgs();
                    var res = fts.Search(a.Query ?? "", a.Cap, a.MaxWordDistance, a.RequireOrdered, a.ContextWords, a.ExpandKetiv);
                    return RpcResponse.Ok(JsonSerializer.Serialize(res, RpcJsonContext.Default.FtsSearchResult));
                }

                case "ftsIndexingStatus":
                    return RpcResponse.Ok(JsonSerializer.Serialize(
                        fts.Status(), RpcJsonContext.Default.FtsIndexStatus));

                default:
                    return RpcResponse.Err("Unknown op: " + req.Op);
            }
        }
        catch (OperationCanceledException)
        {
            return RpcResponse.Err("Cancelled.");
        }
        catch (Exception ex)
        {
            return RpcResponse.Err(ex.Message);
        }
    }

    private static string Term(JsonElement args) =>
        (args.ValueKind == JsonValueKind.Object
            ? args.Deserialize(RpcJsonContext.Default.DictTermArgs)?.Term
            : null) ?? "";

    private static List<string> Candidates(JsonElement args) =>
        (args.ValueKind == JsonValueKind.Object
            ? args.Deserialize(RpcJsonContext.Default.DictCandidatesArgs)?.Candidates
            : null) ?? [];
}
