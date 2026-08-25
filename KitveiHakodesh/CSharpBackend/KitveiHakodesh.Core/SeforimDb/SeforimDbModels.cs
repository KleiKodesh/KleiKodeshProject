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

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class TocPathRow { public int LineId { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class EnclosingTocPathRow { public int GroupKey { get; set; } public int BookId { get; set; } public string TocPath { get; set; } = ""; }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineBookRow { public int LineId { get; set; } public int BookId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class LineIndexRow { public int LineIndex { get; set; } public int BookId { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookIdRow { public int Id { get; set; } }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BoldLineRow
    {
        public string Content { get; set; } = "";
        public string Title { get; set; } = "";
        public int BookId { get; set; }
        public int LineId { get; set; }
        public int LineIndex { get; set; }
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class RawLineRow { public int Id { get; set; } public int LineIndex { get; set; } public string Content { get; set; } = ""; }
}
