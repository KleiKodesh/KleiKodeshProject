// The row types below are KitveiHakodesh.Core's — aliased, not redeclared. Core's models ARE
// the wire types: they carry the same [MessagePackObject(keyAsPropertyName: true)] contract and
// the same property names, so what reaches the frontend is unchanged, and there is no mirror set
// to keep in step with a SELECT list. What remains in this file is the *Args / *Result ENVELOPES,
// which shape RPC calls rather than data, and so belong to the transport.

namespace KitveiHakodeshService.SeforimDb;

// Result-row DTOs for the seforim DB, matching the Vue row shapes exactly
// (camelCase on the wire), serialized by the source-generated MessagePack resolver.
//
// The frontend counterpart is vue-frontend/src/webview-host/queries.types.ts — the row
// types live beside the SQL that produces them, so both sides of the wire point at the
// same place. Keep the two files in step when a SELECT list changes.

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

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookByIdResult { public BookInfo? Book { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesPagedArgs
{
    public int BookId { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
    /// <summary>Read the page through this version's overlay. 0 = the merged text.</summary>
    public int VersionId { get; set; }
}

// ── Book metadata (info popover) ──────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookMetadataArgs { public int BookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookMetadataResult { public BookMetadataRow? Metadata { get; set; } }

// ── Versions ────────────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookVersionsArgs { public int BookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookVersionsResult { public List<BookVersionRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesResult { public List<LineRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineCharCountsArgs { public int BookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsByRangeArgs { public int BookId { get; set; } public int FromIndex { get; set; } public int ToIndex { get; set; } }

/// <summary>Ids of the lines in [FromIndex, ToIndex) placed by lineIndex, 0 where a lineIndex has no row.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsByRangeResult { public int[] Ids { get; set; } = Array.Empty<int>(); }

/// <summary>Counts by lineIndex; null when the library has no <c>line.charCount</c> column.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineCharCountsResult { public int[]? Counts { get; set; } }

// ── TOC ─────────────────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByBookArgs { public int BookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByStructureArgs { public int StructureId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocEntriesResult { public List<TocEntryRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class AltTocStructuresResult { public List<AltTocStructureRow> Rows { get; set; } = new(); }

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

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocPrefixResult { public List<TocPrefixRow> Rows { get; set; } = new(); }

// ── Commentary / links ────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsArgs { public List<int> LineIds { get; set; } = new(); }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookIdArgs { public int BookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CommentaryLinksResult { public List<CommentaryLinkRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CommentaryLinkGroupsResult { public List<CommentaryLinkGroupRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineContentsResult { public List<LineContentRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ConnectionTypesResult { public List<ConnectionTypeRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DefaultCommentatorsResult { public List<DefaultCommentatorRow> Rows { get; set; } = new(); }

// ── Reverse lookups (source & targum) + static filter books ────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLineDataArgs
{
    public List<int> LineIds { get; set; } = new();
    public List<int> TypeIds { get; set; } = new();
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
public sealed class ReverseBooksResult { public List<ReverseBookRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StaticFilterArgs
{
    public int SourceBookId { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

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
public sealed class TocSectionResult { public List<TocSectionRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinkTargetArgs { public int SourceLineId { get; set; } public int TargetBookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinkTargetResult { public List<LinkTargetRow> Rows { get; set; } = new(); }

// ── TOC paths & line→book/index helpers ────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EnclosingTocPathArgs { public List<int> Triples { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class EnclosingTocPathResult { public List<EnclosingTocPathRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineBooksResult { public List<LineBookRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdArgs { public int LineId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIndexResult { public List<LineIndexRow> Rows { get; set; } = new(); }

// ── Dictionary sources in the seforim DB ───────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TitlePatternArgs { public string Pattern { get; set; } = ""; }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExactTitleArgs { public string Title { get; set; } = ""; }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookIdsResult { public List<BookIdRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineByIndexArgs { public int BookId { get; set; } public int LineIndex { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawLinesResult { public List<RawLineRow> Rows { get; set; } = new(); }

// ── Error-report provenance ───────────────────────────────────────────────────
// What a "report a mistake" stamps itself with: which corpus and which library build
// the reported text came from, and the line's canonical reference.

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookSourceNameResult { public List<BookSourceNameRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class SchemaMetaValueResult { public List<SchemaMetaValueRow> Rows { get; set; } = new(); }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineRefResult { public List<LineRefRow> Rows { get; set; } = new(); }

// ── Full-text search (FtsLib) ──────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSearchArgs
{
    public string? Query { get; set; }
    public int Cap { get; set; }
    public int MaxWordDistance { get; set; } = 10;
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
    // Spelling variants / related forms are per word, in the query: '^' / '+'.
}


// ── Streaming FTS (ftsSearchStream) — the service PUSHES result frames continuously
//    over one pipe connection until the search finishes. No polling anywhere. ──

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSnippetsArgs
{
    public string? Query { get; set; }
    public List<int>? LineIds { get; set; }
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsLinesSearchArgs
{
    public string? Query { get; set; }
    public List<global::KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsLineRange>? Ranges { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSearchStreamArgs
{
    public string? Query { get; set; }
    public int MaxWordDistance { get; set; } = 10;
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
    // Spelling variants / related forms are per word, in the query: '^' / '+'.
}

