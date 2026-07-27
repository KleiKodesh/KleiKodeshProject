namespace KitveiHakodeshService.SefroimDb;

// Result-row DTOs for the seforim DB, matching the Vue row shapes exactly
// (camelCase on the wire). Registered in RpcJsonContext for AOT-safe serialization.

/// <summary>A category tree row — matches bookCatalogTree.ts CategoryRow.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CategoryRow
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
}

/// <summary>A catalog book row — matches bookCatalogTree.ts BookRow (query subset).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookRow
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Title { get; set; } = "";
    public int? HasTeamim { get; set; }
    public string? Authors { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CategoriesResult
{
    public List<CategoryRow> Rows { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BooksResult
{
    public List<BookRow> Rows { get; set; } = new();
}

// ── Book + lines ──────────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookByIdArgs { public int Id { get; set; } }

/// <summary>Single-book metadata — matches the BookRow in useBookViewLinesTable.ts load().</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
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

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookByIdResult { public BookInfo? Book { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesPagedArgs
{
    public int BookId { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

/// <summary>A streamed line row — matches { id, lineIndex, content } in fetchRange().</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineRow
{
    public int Id { get; set; }
    public int LineIndex { get; set; }
    public string Content { get; set; } = "";
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesResult { public List<LineRow> Rows { get; set; } = new(); }

// ── TOC ─────────────────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByBookArgs { public int BookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByStructureArgs { public int StructureId { get; set; } }

/// <summary>Main/alt TOC entry — matches TocEntry (TreeNodeItem + lineId/lineIndex).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
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

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocEntriesResult { public List<TocEntryRow> Rows { get; set; } = new(); }

/// <summary>Alt-TOC structure — matches AltTocStructure.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class AltTocStructureRow
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Title { get; set; }
    public string? HeTitle { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class AltTocStructuresResult { public List<AltTocStructureRow> Rows { get; set; } = new(); }

/// <summary>TOC-search row — matches TocRow { id, parentId, bookId, text, lineIndex }.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocTitleRow
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int BookId { get; set; }
    public string Text { get; set; } = "";
    public int? LineIndex { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocTitlesArgs
{
    public List<int> BookIds { get; set; } = new();
    public string? FilterWord { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocTitlesResult { public List<TocTitleRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPrefixArgs { public int BookId { get; set; } public string Pattern { get; set; } = ""; }

/// <summary>Daf-yomi prefix hit — matches { id, lineIndex }.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPrefixRow { public int Id { get; set; } public int? LineIndex { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPrefixResult { public List<TocPrefixRow> Rows { get; set; } = new(); }

// ── Commentary / links ────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsArgs { public List<int> LineIds { get; set; } = new(); }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookIdArgs { public int BookId { get; set; } }

/// <summary>Links-only commentary row — matches useCommentary's forward query shape.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CommentaryLinkRow
{
    public int TargetBookId { get; set; }
    public int TargetLineId { get; set; }
    public int ConnectionTypeId { get; set; }
    public int LineIndex { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CommentaryLinksResult { public List<CommentaryLinkRow> Rows { get; set; } = new(); }

/// <summary>Word-level link anchor (link_anchor ⋈ link) for a source line. CharStart/CharEnd
/// are visible-char offsets into the line's raw content (upstream countVisibleChars convention:
/// tags = 0, entity = 1, everything else — including diacritics — = 1). CharEnd null = point
/// anchor (inline marker); Label = the printed marker letter when the source declares one.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class WordLinkAnchorRow
{
    public int LineId { get; set; }
    public int CharStart { get; set; }
    public int? CharEnd { get; set; }
    public string? Label { get; set; }
    public int TargetBookId { get; set; }
    public int TargetLineId { get; set; }
    public int TargetLineIndex { get; set; }
}

/// <summary>Supported=false → the open DB's schema predates link_anchor; callers should stop asking.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class WordLinkAnchorsResult
{
    public bool Supported { get; set; }
    public List<WordLinkAnchorRow> Rows { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineContentRow { public int Id { get; set; } public string Content { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineContentsResult { public List<LineContentRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ConnectionTypeRow { public int Id { get; set; } public string Name { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ConnectionTypesResult { public List<ConnectionTypeRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DefaultCommentatorRow { public int CommentatorBookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DefaultCommentatorsResult { public List<DefaultCommentatorRow> Rows { get; set; } = new(); }

// ── Reverse lookups (source & targum) + static filter books ────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLineDataArgs
{
    public List<int> LineIds { get; set; } = new();
    public List<int> TypeIds { get; set; } = new();
}

/// <summary>Reverse-lookup source/targum line — matches the reverse-query row shape.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLineRow
{
    public int SourceBookId { get; set; }
    public int SourceLineId { get; set; }
    public int LineIndex { get; set; }
    public string Content { get; set; } = "";
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLineDataResult { public List<ReverseLineRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseBooksArgs
{
    public int BookId { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseBookRow { public int SourceBookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseBooksResult { public List<ReverseBookRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StaticFilterArgs
{
    public int SourceBookId { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StaticFilterRow { public int TargetBookId { get; set; } public int ConnectionTypeId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StaticFilterResult { public List<StaticFilterRow> Rows { get; set; } = new(); }

// ── Commentary navigation ──────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class SectionNavArgs
{
    public int MainBookId { get; set; }
    public int CommentaryBookId { get; set; }
    public int LineIndex { get; set; }
    public string? Direction { get; set; } // "next" | "prev"
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class SectionNavRow { public int Id { get; set; } public int LineIndex { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class SectionNavResult { public List<SectionNavRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocSectionArgs
{
    public int MainBookId { get; set; }
    public int CommentaryBookId { get; set; }
    public List<int> RangePairs { get; set; } = new();
    public string? Direction { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocSectionRow { public int SectionStart { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocSectionResult { public List<TocSectionRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinkTargetArgs { public int SourceLineId { get; set; } public int TargetBookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinkTargetRow { public int TargetLineId { get; set; } public int LineIndex { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinkTargetResult { public List<LinkTargetRow> Rows { get; set; } = new(); }

// ── TOC paths & line→book/index helpers ────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPathRow { public int LineId { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPathsResult { public List<TocPathRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EnclosingTocPathArgs { public List<int> Triples { get; set; } = new(); }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EnclosingTocPathRow { public int GroupKey { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EnclosingTocPathResult { public List<EnclosingTocPathRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineBookRow { public int LineId { get; set; } public int BookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineBooksResult { public List<LineBookRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdArgs { public int LineId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIndexRow { public int LineIndex { get; set; } public int BookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIndexResult { public List<LineIndexRow> Rows { get; set; } = new(); }

// ── Dictionary sources in the seforim DB ───────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TitlePatternArgs { public string Pattern { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExactTitleArgs { public string Title { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookIdRow { public int Id { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookIdsResult { public List<BookIdRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BoldLinesArgs { public List<int> BookIds { get; set; } = new(); public string Pattern { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BoldLineRow
{
    public string Content { get; set; } = "";
    public string Title { get; set; } = "";
    public int BookId { get; set; }
    public int LineId { get; set; }
    public int LineIndex { get; set; }
}
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BoldLinesResult { public List<BoldLineRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EitherPatternArgs { public int BookId { get; set; } public string P1 { get; set; } = ""; public string P2 { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineByIndexArgs { public int BookId { get; set; } public int LineIndex { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawLineRow { public int Id { get; set; } public int LineIndex { get; set; } public string Content { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawLinesResult { public List<RawLineRow> Rows { get; set; } = new(); }

// ── Full-text search (FtsLib) ──────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
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
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsHit
{
    public int LineId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string TocText { get; set; } = "";
    public int Score { get; set; }
    /// <summary>Word-distance of the tightest window (0 = query words adjacent). Used for relevancy sorting on the frontend.</summary>
    public int WordDistance { get; set; }
    public string Snippet { get; set; } = "";
    public List<string> MatchedTerms { get; set; } = new();
}

/// <summary>FTS result set. <c>Ready</c> is false while the index is still building.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSearchResult
{
    public bool Ready { get; set; }
    public string? Error { get; set; }
    public List<FtsHit> Results { get; set; } = new();
}

/// <summary>Background-indexing status — matches the frontend indexing-progress shape.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsIndexStatus
{
    public bool IsReady { get; set; }
    public bool IsIndexing { get; set; }
    public double Percentage { get; set; }
    public int ProcessedChunks { get; set; }
    public int TotalChunks { get; set; }
    /// <summary>True when no seforim DB exists at the resolved path — nothing to index.</summary>
    public bool DbMissing { get; set; }
}

// ── Streaming FTS (ftsSearchStream) — the service PUSHES result frames continuously
//    over one pipe connection until the search finishes. No polling anywhere. ──

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSearchStreamArgs
{
    public string? Query { get; set; }
    public int MaxWordDistance { get; set; } = 10;
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
    public bool ExpandKetiv { get; set; }
}

/// <summary>One pushed frame of a streaming search. The final frame has <c>Done</c> true
/// (and the connection closes after it). <c>Ready</c> false = index still building —
/// sent as the first and only frame.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsStreamChunk
{
    public bool Ready { get; set; } = true;
    public List<FtsHit> Results { get; set; } = new();
    public bool Done { get; set; }
    public string? Error { get; set; }
}
