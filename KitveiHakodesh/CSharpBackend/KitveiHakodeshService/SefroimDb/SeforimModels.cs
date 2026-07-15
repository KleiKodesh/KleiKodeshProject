namespace KitveiHakodeshService.SefroimDb;

// Result-row DTOs for the seforim DB, matching the Vue row shapes exactly
// (camelCase on the wire). Registered in RpcJsonContext for AOT-safe serialization.

/// <summary>A category tree row — matches bookCatalogTree.ts CategoryRow.</summary>
public sealed class CategoryRow
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
}

/// <summary>A catalog book row — matches bookCatalogTree.ts BookRow (query subset).</summary>
public sealed class BookRow
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Title { get; set; } = "";
    public int? HasTeamim { get; set; }
    public string? Authors { get; set; }
}

public sealed class CategoriesResult
{
    public List<CategoryRow> Rows { get; set; } = new();
}

public sealed class BooksResult
{
    public List<BookRow> Rows { get; set; } = new();
}

// ── Book + lines ──────────────────────────────────────────────────────────────

public sealed class BookByIdArgs { public int Id { get; set; } }

/// <summary>Single-book metadata — matches the BookRow in useBookViewLinesTable.ts load().</summary>
public sealed class BookInfo
{
    public int TotalLines { get; set; }
    public int HasTeamim { get; set; }
    public int HasTargumConnection { get; set; }
    public int HasReferenceConnection { get; set; }
    public int HasSourceConnection { get; set; }
    public int HasCommentaryConnection { get; set; }
    public int HasOtherConnection { get; set; }
}

public sealed class BookByIdResult { public BookInfo? Book { get; set; } }

public sealed class LinesPagedArgs
{
    public int BookId { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

/// <summary>A streamed line row — matches { id, lineIndex, content } in fetchRange().</summary>
public sealed class LineRow
{
    public int Id { get; set; }
    public int LineIndex { get; set; }
    public string Content { get; set; } = "";
}

public sealed class LinesResult { public List<LineRow> Rows { get; set; } = new(); }

// ── TOC ─────────────────────────────────────────────────────────────────────

public sealed class TocByBookArgs { public int BookId { get; set; } }
public sealed class TocByStructureArgs { public int StructureId { get; set; } }

/// <summary>Main/alt TOC entry — matches TocEntry (TreeNodeItem + lineId/lineIndex).</summary>
public sealed class TocEntryRow
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Level { get; set; }
    public int? LineId { get; set; }
    public int HasChildren { get; set; }
    public string Text { get; set; } = "";
    public int? LineIndex { get; set; }
}

public sealed class TocEntriesResult { public List<TocEntryRow> Rows { get; set; } = new(); }

/// <summary>Alt-TOC structure — matches AltTocStructure.</summary>
public sealed class AltTocStructureRow
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Title { get; set; }
    public string? HeTitle { get; set; }
}

public sealed class AltTocStructuresResult { public List<AltTocStructureRow> Rows { get; set; } = new(); }

/// <summary>TOC-search row — matches TocRow { id, parentId, bookId, text, lineIndex }.</summary>
public sealed class TocTitleRow
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int BookId { get; set; }
    public string Text { get; set; } = "";
    public int? LineIndex { get; set; }
}

public sealed class TocTitlesArgs
{
    public List<int> BookIds { get; set; } = new();
    public string? FilterWord { get; set; }
}

public sealed class TocTitlesResult { public List<TocTitleRow> Rows { get; set; } = new(); }

public sealed class TocPrefixArgs { public int BookId { get; set; } public string Pattern { get; set; } = ""; }

/// <summary>Daf-yomi prefix hit — matches { id, lineIndex }.</summary>
public sealed class TocPrefixRow { public int Id { get; set; } public int? LineIndex { get; set; } }

public sealed class TocPrefixResult { public List<TocPrefixRow> Rows { get; set; } = new(); }

// ── Commentary / links ────────────────────────────────────────────────────────

public sealed class LineIdsArgs { public List<int> LineIds { get; set; } = new(); }
public sealed class BookIdArgs { public int BookId { get; set; } }

/// <summary>Links-only commentary row — matches useCommentary's forward query shape.</summary>
public sealed class CommentaryLinkRow
{
    public int TargetBookId { get; set; }
    public int TargetLineId { get; set; }
    public int ConnectionTypeId { get; set; }
    public int LineIndex { get; set; }
}

public sealed class CommentaryLinksResult { public List<CommentaryLinkRow> Rows { get; set; } = new(); }

public sealed class LineContentRow { public int Id { get; set; } public string Content { get; set; } = ""; }
public sealed class LineContentsResult { public List<LineContentRow> Rows { get; set; } = new(); }

public sealed class ConnectionTypeRow { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class ConnectionTypesResult { public List<ConnectionTypeRow> Rows { get; set; } = new(); }

public sealed class DefaultCommentatorRow { public int CommentatorBookId { get; set; } }
public sealed class DefaultCommentatorsResult { public List<DefaultCommentatorRow> Rows { get; set; } = new(); }

// ── Reverse lookups (source & targum) + static filter books ────────────────────

public sealed class ReverseLineDataArgs
{
    public List<int> LineIds { get; set; } = new();
    public List<int> TypeIds { get; set; } = new();
}

/// <summary>Reverse-lookup source/targum line — matches the reverse-query row shape.</summary>
public sealed class ReverseLineRow
{
    public int SourceBookId { get; set; }
    public int SourceLineId { get; set; }
    public int LineIndex { get; set; }
    public string Content { get; set; } = "";
}

public sealed class ReverseLineDataResult { public List<ReverseLineRow> Rows { get; set; } = new(); }

public sealed class ReverseBooksArgs
{
    public int BookId { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

public sealed class ReverseBookRow { public int SourceBookId { get; set; } }
public sealed class ReverseBooksResult { public List<ReverseBookRow> Rows { get; set; } = new(); }

public sealed class StaticFilterArgs
{
    public int SourceBookId { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

public sealed class StaticFilterRow { public int TargetBookId { get; set; } public int ConnectionTypeId { get; set; } }
public sealed class StaticFilterResult { public List<StaticFilterRow> Rows { get; set; } = new(); }

// ── Commentary navigation ──────────────────────────────────────────────────────

public sealed class SectionNavArgs
{
    public int MainBookId { get; set; }
    public int CommentaryBookId { get; set; }
    public int LineIndex { get; set; }
    public string? Direction { get; set; } // "next" | "prev"
}

public sealed class SectionNavRow { public int Id { get; set; } public int LineIndex { get; set; } }
public sealed class SectionNavResult { public List<SectionNavRow> Rows { get; set; } = new(); }

public sealed class TocSectionArgs
{
    public int MainBookId { get; set; }
    public int CommentaryBookId { get; set; }
    public List<int> RangePairs { get; set; } = new();
    public string? Direction { get; set; }
}

public sealed class TocSectionRow { public int SectionStart { get; set; } }
public sealed class TocSectionResult { public List<TocSectionRow> Rows { get; set; } = new(); }

public sealed class LinkTargetArgs { public int SourceLineId { get; set; } public int TargetBookId { get; set; } }
public sealed class LinkTargetRow { public int TargetLineId { get; set; } public int LineIndex { get; set; } }
public sealed class LinkTargetResult { public List<LinkTargetRow> Rows { get; set; } = new(); }

// ── TOC paths & line→book/index helpers ────────────────────────────────────────

public sealed class TocPathRow { public int LineId { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }
public sealed class TocPathsResult { public List<TocPathRow> Rows { get; set; } = new(); }

public sealed class EnclosingTocPathArgs { public List<int> Triples { get; set; } = new(); }
public sealed class EnclosingTocPathRow { public int GroupKey { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }
public sealed class EnclosingTocPathResult { public List<EnclosingTocPathRow> Rows { get; set; } = new(); }

public sealed class LineBookRow { public int LineId { get; set; } public int BookId { get; set; } }
public sealed class LineBooksResult { public List<LineBookRow> Rows { get; set; } = new(); }

public sealed class LineIdArgs { public int LineId { get; set; } }
public sealed class LineIndexRow { public int LineIndex { get; set; } public int BookId { get; set; } }
public sealed class LineIndexResult { public List<LineIndexRow> Rows { get; set; } = new(); }

// ── Dictionary sources in the seforim DB ───────────────────────────────────────

public sealed class TitlePatternArgs { public string Pattern { get; set; } = ""; }
public sealed class ExactTitleArgs { public string Title { get; set; } = ""; }
public sealed class BookIdRow { public int Id { get; set; } }
public sealed class BookIdsResult { public List<BookIdRow> Rows { get; set; } = new(); }

public sealed class BoldLinesArgs { public List<int> BookIds { get; set; } = new(); public string Pattern { get; set; } = ""; }
public sealed class BoldLineRow
{
    public string Content { get; set; } = "";
    public string Title { get; set; } = "";
    public int BookId { get; set; }
    public int LineId { get; set; }
    public int LineIndex { get; set; }
}
public sealed class BoldLinesResult { public List<BoldLineRow> Rows { get; set; } = new(); }

public sealed class EitherPatternArgs { public int BookId { get; set; } public string P1 { get; set; } = ""; public string P2 { get; set; } = ""; }
public sealed class LineByIndexArgs { public int BookId { get; set; } public int LineIndex { get; set; } }
public sealed class RawLineRow { public int Id { get; set; } public int LineIndex { get; set; } public string Content { get; set; } = ""; }
public sealed class RawLinesResult { public List<RawLineRow> Rows { get; set; } = new(); }

// ── Full-text search (FtsLib) ──────────────────────────────────────────────────

public sealed class FtsSearchArgs
{
    public string? Query { get; set; }
    public int Cap { get; set; }
    public int MaxWordDistance { get; set; } = 10;
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
    public bool ExpandKetiv { get; set; }
}

/// <summary>One FTS hit — matches the frontend FullTextSearchResult shape.</summary>
public sealed class FtsHit
{
    public int LineId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string TocText { get; set; } = "";
    public int Score { get; set; }
    public string Snippet { get; set; } = "";
    public List<string> MatchedTerms { get; set; } = new();
}

/// <summary>FTS result set. <c>Ready</c> is false while the index is still building.</summary>
public sealed class FtsSearchResult
{
    public bool Ready { get; set; }
    public string? Error { get; set; }
    public List<FtsHit> Results { get; set; } = new();
}

/// <summary>Background-indexing status — matches the frontend indexing-progress shape.</summary>
public sealed class FtsIndexStatus
{
    public bool IsReady { get; set; }
    public bool IsIndexing { get; set; }
    public double Percentage { get; set; }
    public int ProcessedChunks { get; set; }
    public int TotalChunks { get; set; }
}
