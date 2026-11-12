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

/// <summary>The in-book / commentary substring search: the query as typed, the ranges to
/// search as flat triples (bookId, firstLineIndex, lastLineIndex — flat so a panel's
/// thousands of ranges stay small on the wire), and the version to read through (0 = the
/// merged text).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesSubstringSearchArgs
{
    public string? Query { get; set; }
    public int[]? RangeTriples { get; set; }
    public int VersionId { get; set; }
}

/// <summary>Parallel arrays: the lines holding the query, by book in first-seen order then
/// reading order, and how many times each holds it. Exactly what the find-in-text results
/// need (the line positions and their counts); line ids are not sent - nothing reads them.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LinesSubstringSearchResult
{
    public int[] BookIds { get; set; } = Array.Empty<int>();
    public int[] LineIndexes { get; set; } = Array.Empty<int>();
    public int[] Counts { get; set; } = Array.Empty<int>();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsByRangeArgs { public int BookId { get; set; } public int FromIndex { get; set; } public int ToIndex { get; set; } }

/// <summary>Ids of the lines in [FromIndex, ToIndex) placed by lineIndex, 0 where a lineIndex has no row.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineIdsByRangeResult { public int[] Ids { get; set; } = Array.Empty<int>(); }

/// <summary>Counts by lineIndex as little-endian uint16 (a MessagePack bin: 2 bytes a line, not
/// an int array - 7414's 151 KB of ints); a count over 65535 is clamped (the bar's estimate
/// only). Null when the library has no <c>line.charCount</c> column.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineCharCountsResult
{
    public byte[]? Packed { get; set; }

    public static LineCharCountsResult Pack(int[]? counts)
    {
        if (counts == null) return new LineCharCountsResult();
        var bytes = new byte[counts.Length * 2];
        for (int i = 0; i < counts.Length; i++)
        {
            int c = Math.Clamp(counts[i], 0, ushort.MaxValue);
            bytes[2 * i] = (byte)c;
            bytes[2 * i + 1] = (byte)(c >> 8);
        }
        return new LineCharCountsResult { Packed = bytes };
    }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LineDecorationsArgs { public List<int> AnchorLineIds { get; set; } = new(); public List<int> NoteLineIds { get; set; } = new(); }

// ── TOC ─────────────────────────────────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByBookArgs { public int BookId { get; set; } }
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocByStructureArgs { public int StructureId { get; set; } }

/// <summary>
/// A TOC as columns, not one keyed record per entry: entry i is the i-th value of every
/// array. Field names go over the wire once instead of once per entry, and each distinct
/// text once (`Texts`, indexed by `TextIndex`) - TOC texts repeat heavily ("seif a"...).
/// Absent values: ParentIds/LineIds/AltStructureIds 0 (database ids start at 1),
/// LineIndexes -1. Decoded back to TocEntry rows by the frontend's seforimApi.
/// </summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class TocEntriesResult
{
    public int[] Ids { get; set; } = [];
    public int[] ParentIds { get; set; } = [];
    public int[] Levels { get; set; } = [];
    public int[] LineIds { get; set; } = [];
    public int[] LineIndexes { get; set; } = [];
    public byte[] HasChildren { get; set; } = [];
    public string[] Texts { get; set; } = [];
    public int[] TextIndex { get; set; } = [];
    /// <summary>Null when no entry came from an alt structure (the usual case).</summary>
    public int[]? AltStructureIds { get; set; }

    public static TocEntriesResult From(List<TocEntryRow> rows)
    {
        int n = rows.Count;
        var result = new TocEntriesResult
        {
            Ids = new int[n], ParentIds = new int[n], Levels = new int[n], LineIds = new int[n],
            LineIndexes = new int[n], HasChildren = new byte[n], TextIndex = new int[n],
        };
        var textIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var texts = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var r = rows[i];
            result.Ids[i] = r.Id;
            result.ParentIds[i] = r.ParentId ?? 0;
            result.Levels[i] = r.Level;
            result.LineIds[i] = r.LineId ?? 0;
            result.LineIndexes[i] = r.LineIndex ?? -1;
            result.HasChildren[i] = r.HasChildren != 0 ? (byte)1 : (byte)0;
            if (!textIndexOf.TryGetValue(r.Text, out var t))
            {
                t = texts.Count;
                textIndexOf[r.Text] = t;
                texts.Add(r.Text);
            }
            result.TextIndex[i] = t;
            if (r.AltStructureId is int structureId)
            {
                result.AltStructureIds ??= new int[n];
                result.AltStructureIds[i] = structureId;
            }
        }
        result.Texts = texts.ToArray();
        return result;
    }
}

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

/// <summary>Lines to look up plus the book they belong to (0 = unknown; the catalogue's
/// commentary verdict is then skipped). The result is Core's CommentaryLinkGroups as-is.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CommentaryLinkGroupsArgs
{
    public List<int> LineIds { get; set; } = new();
    public int BookId { get; set; }
}

/// <summary>The lines of a book with lineIndex in [FromIndex, ToIndex) - a section sent as
/// its bounds instead of its ids.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BookLineRangeArgs
{
    public int BookId { get; set; }
    public int FromIndex { get; set; }
    public int ToIndex { get; set; }
}

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

/// <summary>getReverseLines: the lines as ids (LineIds) or, when BookId > 0, as the book's
/// lines with lineIndex in [FromIndex, ToIndex).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLinesArgs
{
    public List<int> LineIds { get; set; } = new();
    public int BookId { get; set; }
    public int FromIndex { get; set; }
    public int ToIndex { get; set; }
    public List<int> TypeIds { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ReverseLinesResult { public List<ReverseLineRefRow> Rows { get; set; } = new(); }

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

// ── Related books (book view toolbar) ───────────────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RelatedBooksArgs { public int BookId { get; set; } }

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RelatedTargetArgs
{
    public int FromBookId { get; set; }
    public int TopLineIndex { get; set; }
    /// <summary>The clicked row as getRelatedBooks returned it.</summary>
    public RelatedBook Target { get; set; } = new();
}

/// <summary>Wrapped, not a bare int?: the line the target opens at, null = its start.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RelatedTargetResult { public int? LineIndex { get; set; } }

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
public sealed class FtsSnippetTextsArgs
{
    /// <summary>Lines whose own text is wanted.</summary>
    public List<long>? LineIds { get; set; }
    /// <summary>Lines flagged short: their text plus neighbour context.</summary>
    public List<long>? ShortLineIds { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FtsSearchStreamArgs
{
    public string? Query { get; set; }
    public int MaxWordDistance { get; set; } = 10;
    public bool RequireOrdered { get; set; }
    public int ContextWords { get; set; } = 8;
    /// <summary>Resume after this line (the last hit the caller holds); 0 = from the start.</summary>
    public long ResumeAfterLineId { get; set; }
    // Spelling variants / related forms are per word, in the query: '^' / '+'.
}

