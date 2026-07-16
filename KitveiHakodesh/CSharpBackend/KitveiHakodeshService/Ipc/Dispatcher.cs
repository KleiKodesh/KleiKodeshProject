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
    UserSettingsService userSettings,
    IHostApplicationLifetime lifetime)
{
    public async Task<byte[]> DispatchAsync(byte[] request, CancellationToken ct)
    {
        RpcRequest? req;
        try
        {
            req = MessagePack.MessagePackSerializer.Deserialize<RpcRequest>(request, MsgPack.Options);
        }
        catch (Exception ex)
        {
            return RpcResponse.Err("Invalid request: " + ex.Message);
        }

        if (req is null || string.IsNullOrEmpty(req.Op))
            return RpcResponse.Err("Missing 'op'.");

        try
        {
            switch (req.Op)
            {
                case "ping":
                    return RpcResponse.Ok(MsgPack.Ser(new PongResult()));

                // Graceful shutdown: triggers host stop → FtsIndexingStarter.StopAsync
                // cancels the build cleanly (aborts any merge, releases the index lock)
                // before the process exits. The dev killer calls this before taskkill so
                // a restart never hard-kills a build in progress (index-corruption path).
                case "shutdown":
                    lifetime.StopApplication();
                    return RpcResponse.Ok(MsgPack.Ser(new ShuttingDownResult()));

                case "locateDocuments":
                {
                    var args = MsgPack.De<LocateDocumentsArgs>(req.Args);
                    int max = args.Max > 0 ? args.Max : 200;
                    var result = await locator.LocateAsync(args.Query ?? "", max, ct);
                    return RpcResponse.Ok(
                        MsgPack.Ser(result));
                }

                case "locateDocumentsWarmup":
                    locator.Warmup();
                    return RpcResponse.Ok(MsgPack.Ser(new StartedResult()));

                case "resetDocumentLocatorIndex":
                    await locator.ReindexAsync(ct);
                    return RpcResponse.Ok(MsgPack.Ser(new ResetResult()));

                case "hbSearch":
                {
                    var args = MsgPack.De<HbSearchArgs>(req.Args);
                    var result = hebrewBooks.Search(args.Query ?? "", args.LocalFolder, args.Limit);
                    return RpcResponse.Ok(
                        MsgPack.Ser(result));
                }

                // ── Dictionary (KitveiHakodesh_dictionary.db) ──────────────────
                case "dictExact":
                    return RpcResponse.Ok(MsgPack.Ser(dictionary.Exact(Term(req.Args))));

                case "dictPrefix":
                    return RpcResponse.Ok(MsgPack.Ser(new DictSensesResult { Rows = dictionary.Prefix(Term(req.Args)) }));

                case "dictContains":
                    return RpcResponse.Ok(MsgPack.Ser(new DictSensesResult { Rows = dictionary.Contains(Term(req.Args)) }));

                case "dictLinks":
                    return RpcResponse.Ok(MsgPack.Ser(new DictLinksResult { Links = dictionary.Links(Term(req.Args)) }));

                case "dictSynonyms":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.Synonyms(Term(req.Args)) }));

                case "dictVariants":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.Variants(Term(req.Args)) }));

                case "dictSpellCandidates":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.SpellCandidates(Term(req.Args)) }));

                case "dictAbbrevSenses":
                    return RpcResponse.Ok(MsgPack.Ser(dictionary.AbbrevSenses(Candidates(req.Args))));

                case "dictKetivVariants":
                    return RpcResponse.Ok(MsgPack.Ser(new DictWordsResult { Words = dictionary.KetivVariants(Candidates(req.Args)) }));

                // ── Seforim DB — catalog ───────────────────────────────────────
                case "getAllCategories":
                    return RpcResponse.Ok(MsgPack.Ser(new CategoriesResult { Rows = seforim.GetAllCategories() }));

                case "getAllBooks":
                    return RpcResponse.Ok(MsgPack.Ser(new BooksResult { Rows = seforim.GetAllBooks() }));

                case "getBookById":
                {
                    var a = MsgPack.De<BookByIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookByIdResult { Book = seforim.GetBookById(a.Id) }));
                }

                case "getLinesPaged":
                {
                    var a = MsgPack.De<LinesPagedArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LinesResult { Rows = seforim.GetLinesPaged(a.BookId, a.Limit, a.Offset) }));
                }

                // ── Seforim DB — TOC ───────────────────────────────────────────
                case "getAllTocEntries":
                {
                    var a = MsgPack.De<TocByBookArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocEntriesResult { Rows = seforim.GetAllTocEntries(a.BookId) }));
                }

                case "getAltTocStructures":
                {
                    var a = MsgPack.De<TocByBookArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new AltTocStructuresResult { Rows = seforim.GetAltTocStructures(a.BookId) }));
                }

                case "getAllAltTocEntries":
                {
                    var a = MsgPack.De<TocByStructureArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocEntriesResult { Rows = seforim.GetAllAltTocEntries(a.StructureId) }));
                }

                case "getTocTitlesForBooks":
                {
                    var a = MsgPack.De<TocTitlesArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocTitlesResult { Rows = seforim.GetTocTitlesForBooks(a.BookIds, a.FilterWord) }));
                }

                case "getTocEntryByTextPrefix":
                {
                    var a = MsgPack.De<TocPrefixArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocPrefixResult { Rows = seforim.GetTocEntryByTextPrefix(a.BookId, a.Pattern) }));
                }

                // ── Seforim DB — commentary/links ──────────────────────────────
                case "getCommentaryLinksForSourceLineRange":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new CommentaryLinksResult { Rows = seforim.GetCommentaryLinksForSourceLineRange(a.LineIds) }));
                }

                case "getLineContents":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineContentsResult { Rows = seforim.GetLineContents(a.LineIds) }));
                }

                case "getAllConnectionTypes":
                    return RpcResponse.Ok(MsgPack.Ser(new ConnectionTypesResult { Rows = seforim.GetAllConnectionTypes() }));

                case "getDefaultCommentators":
                {
                    var a = MsgPack.De<BookIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new DefaultCommentatorsResult { Rows = seforim.GetDefaultCommentators(a.BookId) }));
                }

                case "getReverseLineData":
                {
                    var a = MsgPack.De<ReverseLineDataArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new ReverseLineDataResult { Rows = seforim.GetReverseLineData(a.LineIds, a.TypeIds) }));
                }

                case "getReverseBooks":
                {
                    var a = MsgPack.De<ReverseBooksArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new ReverseBooksResult { Rows = seforim.GetReverseBooks(a.BookId, a.TypeIds) }));
                }

                case "getStaticFilterBooks":
                {
                    var a = MsgPack.De<StaticFilterArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new StaticFilterResult { Rows = seforim.GetStaticFilterBooks(a.SourceBookId, a.TypeIds) }));
                }

                case "getSectionWithCommentary":
                {
                    var a = MsgPack.De<SectionNavArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new SectionNavResult { Rows = seforim.GetSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.LineIndex, a.Direction != "prev") }));
                }

                case "getTocSectionWithCommentary":
                {
                    var a = MsgPack.De<TocSectionArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocSectionResult { Rows = seforim.GetTocSectionWithCommentary(a.MainBookId, a.CommentaryBookId, a.RangePairs, a.Direction != "prev") }));
                }

                case "getLinkTargetForSourceLineAndBook":
                {
                    var a = MsgPack.De<LinkTargetArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LinkTargetResult { Rows = seforim.GetLinkTargetForSourceLineAndBook(a.SourceLineId, a.TargetBookId) }));
                }

                case "getTocPathsForLines":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new TocPathsResult { Rows = seforim.GetTocPathsForLines(a.LineIds) }));
                }

                case "getEnclosingTocPathForLineRanges":
                {
                    var a = MsgPack.De<EnclosingTocPathArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new EnclosingTocPathResult { Rows = seforim.GetEnclosingTocPathForLineRanges(a.Triples) }));
                }

                case "getBookIdsForLines":
                {
                    var a = MsgPack.De<LineIdsArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineBooksResult { Rows = seforim.GetBookIdsForLines(a.LineIds) }));
                }

                case "getLineIndexFromLineId":
                {
                    var a = MsgPack.De<LineIdArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new LineIndexResult { Rows = seforim.GetLineIndexFromLineId(a.LineId) }));
                }

                // ── Seforim DB — dictionary sources (מצודת/מלבי״ם/מנחם/ערוך) ────
                case "getBookIdsByTitlePattern":
                {
                    var a = MsgPack.De<TitlePatternArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookIdsResult { Rows = seforim.GetBookIdsByTitlePattern(a.Pattern) }));
                }

                case "getBookIdByExactTitle":
                {
                    var a = MsgPack.De<ExactTitleArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BookIdsResult { Rows = seforim.GetBookIdByExactTitle(a.Title) }));
                }

                case "getLinesWithContentPatternForBooks":
                {
                    var a = MsgPack.De<BoldLinesArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new BoldLinesResult { Rows = seforim.GetLinesWithContentPatternForBooks(a.BookIds, a.Pattern) }));
                }

                case "getLinesWithEitherContentPattern":
                {
                    var a = MsgPack.De<EitherPatternArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new RawLinesResult { Rows = seforim.GetLinesWithEitherContentPattern(a.BookId, a.P1, a.P2) }));
                }

                case "getLineByBookAndLineIndex":
                {
                    var a = MsgPack.De<LineByIndexArgs>(req.Args);
                    return RpcResponse.Ok(MsgPack.Ser(new RawLinesResult { Rows = seforim.GetLineByBookAndLineIndex(a.BookId, a.LineIndex) }));
                }

                // ── User settings (highlights/notes) — read + write ────────────
                case "userSettingsQuery":
                {
                    var a = MsgPack.De<RawSqlArgs>(req.Args);
                    using var pd = ParseParams(a.ParamsJson);
                    string rowsJson = userSettings.QueryRowsJson(a.Sql ?? "", pd.Elements);
                    return RpcResponse.Ok(MsgPack.Ser(new RawRowsResult { RowsJson = rowsJson }));
                }

                case "userSettingsExecute":
                {
                    var a = MsgPack.De<RawSqlArgs>(req.Args);
                    using var pd = ParseParams(a.ParamsJson);
                    long id = userSettings.Execute(a.Sql ?? "", pd.Elements);
                    return RpcResponse.Ok(MsgPack.Ser(new ExecuteResult { LastInsertId = id }));
                }

                // ── Full-text search (FtsLib) ──────────────────────────────────
                case "ftsSearch":
                {
                    var a = MsgPack.De<FtsSearchArgs>(req.Args);
                    var res = fts.Search(a.Query ?? "", a.Cap, a.MaxWordDistance, a.RequireOrdered, a.ContextWords, a.ExpandKetiv);
                    return RpcResponse.Ok(MsgPack.Ser(res));
                }

                // Streaming FTS: start a background search, then poll for incremental
                // batches — mirrors the hosted C# streaming so the first hits paint fast.
                case "ftsSearchStart":
                {
                    var a = MsgPack.De<FtsSearchStartArgs>(req.Args);
                    var res = fts.StartSearch(a.Query ?? "", a.MaxWordDistance, a.RequireOrdered, a.ContextWords, a.ExpandKetiv);
                    return RpcResponse.Ok(MsgPack.Ser(res));
                }

                case "ftsSearchPoll":
                {
                    var a = MsgPack.De<FtsSearchPollArgs>(req.Args);
                    var res = await fts.PollSearch(a.SearchId ?? "", a.Offset, ct);
                    return RpcResponse.Ok(MsgPack.Ser(res));
                }

                case "ftsSearchCancel":
                {
                    var a = MsgPack.De<FtsCancelArgs>(req.Args);
                    fts.CancelSearch(a.SearchId ?? "");
                    return RpcResponse.Ok(MsgPack.Ser(new CancelledResult()));
                }

                case "ftsIndexingStatus":
                    return RpcResponse.Ok(MsgPack.Ser(fts.Status()));

                case "ftsResetIndex":
                    fts.ResetIndex();
                    return RpcResponse.Ok(MsgPack.Ser(new ResetResult()));

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

    private static string Term(byte[]? args) => MsgPack.De<DictTermArgs>(args).Term ?? "";

    private static List<string> Candidates(byte[]? args) => MsgPack.De<DictCandidatesArgs>(args).Candidates ?? [];

    /// <summary>Parse the user-settings <c>paramsJson</c> (a JSON array string carried inside
    /// the msgpack envelope) into JsonElement bind values. The values reference the returned
    /// document's memory, so the caller must keep it alive (a <c>using</c>) until the SQL runs.</summary>
    private static ParsedParams ParseParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new ParsedParams(null, []);
        var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToArray()
            : [];
        return new ParsedParams(doc, arr);
    }

    private sealed class ParsedParams(JsonDocument? doc, JsonElement[] elements) : IDisposable
    {
        public JsonElement[] Elements { get; } = elements;
        public void Dispose() => doc?.Dispose();
    }
}
