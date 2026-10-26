using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDb
{
    // The row shapes the seforim-DB queries return. One file because they are one contract:
    // a SELECT list and the type it fills belong together, and jumping between 29 files to
    // read one query's result is worse than scrolling one.
    //
    // These ARE the wire types. Both transports serialize them directly, so a change to a
    // SELECT list here reaches the frontend with nothing in between to keep in step.
    // The frontend counterpart is vue-frontend/src/webview-host/queries.types.ts.
    //
    // The *Args and {Rows} envelopes that used to sit alongside these stay with the
    // transport that shapes them — they describe RPC calls, not data.

    /// <summary>An alternate text version of a book — the version overlay picker's rows.
    /// Only versions that actually carry content are returned.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookVersionRow
    {
        public int Id { get; set; }
        /// <summary>The upstream key, usually English. Stable; used for persistence.</summary>
        public string VersionTitle { get; set; } = "";
        /// <summary>Display name. Often absent, in which case the caller shows VersionTitle.</summary>
        public string? HeVersionTitle { get; set; }
        public string? VersionSource { get; set; }
        public string? VersionNotes { get; set; }
        public string? HeVersionNotes { get; set; }
    }

    /// <summary>The name of the upstream source a book came from.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookSourceNameRow { public string SourceName { get; set; } = ""; }

    /// <summary>
    /// Everything the library records ABOUT a book - its credits, provenance and licence - for
    /// the book view's info popover. Every field is optional: the two library schemas differ
    /// (heDesc, collectiveTitleHe and the licence tables exist in only one of them), and most
    /// books fill only a few. The lists are empty, never null, when the book has none.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookMetadataRow
    {
        public string Title { get; set; } = "";
        public string? HeRef { get; set; }
        public string? CollectiveTitle { get; set; }
        public string? ShortDescription { get; set; }
        public string? Description { get; set; }
        public string? CategoryTitle { get; set; }
        public string? SourceName { get; set; }
        public List<string> Authors { get; set; } = new();
        public List<string> PublicationPlaces { get; set; } = new();
        public List<string> PublicationDates { get; set; } = new();
        public List<string> Topics { get; set; } = new();
        /// <summary>Licence codes as the library spells them (CC-BY, Public Domain, ...).</summary>
        public List<string> Licenses { get; set; } = new();
        /// <summary>Where the edition text came from - a name or a URL, as recorded.</summary>
        public List<string> EditionSources { get; set; } = new();
    }

    /// <summary>One value out of the library's schema_meta table.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class SchemaMetaValueRow { public string Value { get; set; } = ""; }

    /// <summary>A line's canonical reference and stored text, for verifying a reported mistake
    /// against what the library actually holds.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineRefRow
    {
        public int Id { get; set; }
        public int LineIndex { get; set; }
        /// <summary>Nullable in the database — structural lines carry no reference.</summary>
        public string? HeRef { get; set; }
        public string Content { get; set; } = "";
    }

    /// <summary>A category tree row — matches CategoryRow in webview-host/queries.types.ts.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class CategoryRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; } = "";
        public int Level { get; set; }
    }

    /// <summary>A catalog book row — matches BookRow in webview-host/queries.types.ts (query subset).</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookRow
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Title { get; set; } = "";
        public int? HasTeamim { get; set; }
        public string? Authors { get; set; }
        /// <summary>The book's precedence in the library (<see cref="SeforimDbBookTreeOrder"/>):
        /// the one order every ranking — catalog search, full-text results, commentary
        /// panel — ties on. Rows are returned sorted by it.</summary>
        public int TreeOrder { get; set; }
    }

    /// <summary>Single-book metadata — matches BookInfo in webview-host/queries.types.ts.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
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

    /// <summary>A streamed line row — matches { id, lineIndex, content } in fetchRange().</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineRow
    {
        public int Id { get; set; }
        public int LineIndex { get; set; }
        public string Content { get; set; } = "";
    }

    /// <summary>Main/alt TOC entry — matches TocEntry (TreeNodeItem + lineId/lineIndex).</summary>
    [MessagePackObject(keyAsPropertyName: true)]
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

    /// <summary>Alt-TOC structure — matches AltTocStructure.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class AltTocStructureRow
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string? Title { get; set; }
        public string? HeTitle { get; set; }
    }

    /// <summary>TOC-search row — matches TocRow { id, parentId, bookId, text, lineIndex }.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocTitleRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public int BookId { get; set; }
        public string Text { get; set; } = "";
        public int? LineIndex { get; set; }
    }

    /// <summary>Daf-yomi prefix hit — matches { id, lineIndex }.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocPrefixRow { public int Id { get; set; } public int? LineIndex { get; set; } }

    /// <summary>Links-only commentary row — matches useCommentary's forward query shape.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class CommentaryLinkRow
    {
        public int TargetBookId { get; set; }
        public int TargetLineId { get; set; }
        public int ConnectionTypeId { get; set; }
        public int LineIndex { get; set; }
    }

    /// <summary>Forward commentary links for a range of source lines, grouped by target book and
    /// connection type: one entry per (book, type) with its DISTINCT target line ids and their
    /// line indexes as parallel arrays. Ids and numbers only — a whole-book range (~55k links)
    /// is a few hundred KB instead of the ~4 MB the per-row shape cost.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class CommentaryLinkGroupRow
    {
        public int TargetBookId { get; set; }
        public int ConnectionTypeId { get; set; }
        public int[] LineIds { get; set; } = System.Array.Empty<int>();
        public int[] LineIndexes { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>Word-level link anchor (link_anchor ⋈ link) for a source line. CharStart/CharEnd
    /// are visible-char offsets into the line's raw content (upstream countVisibleChars convention:
    /// tags = 0, entity = 1, everything else — including diacritics — = 1). CharEnd null = point
    /// anchor (inline marker); Label = the printed marker letter when the source declares one.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class WordLinkAnchorRow
    {
        public int LineId { get; set; }
        public int CharStart { get; set; }
        public int? CharEnd { get; set; }
        public string? Label { get; set; }
        public int TargetBookId { get; set; }
        public int TargetLineId { get; set; }
        public int TargetLineIndex { get; set; }
        public int SourceBookId { get; set; }
    }

    /// <summary>Supported=false → the open DB's schema predates link_anchor; callers should stop asking.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class WordLinkAnchorsResult
    {
        public bool Supported { get; set; }
        public List<WordLinkAnchorRow> Rows { get; set; } = new();
    }

    /// <summary>One distinct (commentary book, anchor label) pair of a source book's word-link
    /// anchors. Feeds the frontend's per-book fallback-treatment ranking and its sign-vocabulary
    /// guard — see SeforimDbSqlStrings.GetWordLinkAnchorTargetsForBook.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class WordLinkTargetRow
    {
        public int TargetBookId { get; set; }
        public string? Label { get; set; }
    }

    /// <summary>Supported=false → the open DB's schema predates link_anchor; callers should stop asking.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class WordLinkTargetsResult
    {
        public bool Supported { get; set; }
        public List<WordLinkTargetRow> Rows { get; set; } = new();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineContentRow { public int Id { get; set; } public string Content { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class ConnectionTypeRow { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class DefaultCommentatorRow { public int CommentatorBookId { get; set; } }

    /// <summary>Reverse-lookup source/targum line — matches the reverse-query row shape.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class ReverseLineRow
    {
        public int SourceBookId { get; set; }
        public int SourceLineId { get; set; }
        public int LineIndex { get; set; }
        public string Content { get; set; } = "";
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class ReverseBookRow { public int SourceBookId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class StaticFilterRow { public int TargetBookId { get; set; } public int ConnectionTypeId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class SectionNavRow { public int Id { get; set; } public int LineIndex { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocSectionRow { public int SectionStart { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LinkTargetRow { public int TargetLineId { get; set; } public int LineIndex { get; set; } }

    /// <summary>Which TOC entry a requested line sits under. The path TEXT is not here: lines of one
    /// section share an entry, so the text rides once per entry in <see cref="TocEntryPathRow"/>.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineTocEntryRow { public int LineId { get; set; } public int BookId { get; set; } public int TocEntryId { get; set; } }

    /// <summary>Full TOC path (root to leaf, space-joined, book-title root stripped) of one entry.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocEntryPathRow { public int TocEntryId { get; set; } public string Path { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocPathsForLinesResult
    {
        public List<LineTocEntryRow> Lines { get; set; } = new();
        public List<TocEntryPathRow> Paths { get; set; } = new();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class EnclosingTocPathRow { public int GroupKey { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineBookRow { public int LineId { get; set; } public int BookId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineIndexRow { public int LineIndex { get; set; } public int BookId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookIdRow { public int Id { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class RawLineRow { public int Id { get; set; } public int LineIndex { get; set; } public string Content { get; set; } = ""; }
}
