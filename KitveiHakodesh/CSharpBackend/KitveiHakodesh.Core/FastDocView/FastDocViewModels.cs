using MessagePack;

namespace KitveiHakodesh.Core.FastDocView;

// The FastDocView wire types (vue-frontend/src/features/fast-doc-view/README.md is the contract).
// Every type here, nested ones included, is a [MessagePackObject] so the AOT service never
// falls back to reflection.

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewOpenArgs
{
    public string Path { get; set; } = "";
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewDocumentArgs
{
    public string DocumentId { get; set; } = "";
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewBlocksArgs
{
    public string DocumentId { get; set; } = "";
    public int First { get; set; }
    public int Count { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewScanArgs
{
    public string DocumentId { get; set; } = "";
    public int Blocks { get; set; }
}

/// <summary>What <c>fastDocViewOpen</c> returns. <see cref="BlockCount"/> is the blocks known so far:
/// while the index is still being built it only grows, it is never an estimate.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewSummary
{
    public string DocumentId { get; set; } = "";
    public int BlockCount { get; set; }
    public int KnownBlockCount { get; set; }
    public bool IsIndexComplete { get; set; }
    public bool IsRightToLeft { get; set; }
    public string Stylesheet { get; set; } = "";
    public int HeadingCount { get; set; }
    public int FootnoteCount { get; set; }
    public int EndnoteCount { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewNote
{
    /// <summary>"footnote" or "endnote".</summary>
    public string Kind { get; set; } = "";
    public int Number { get; set; }
    public string Label { get; set; } = "";
    public string Html { get; set; } = "";
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewBlock
{
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public string Html { get; set; } = "";
    public List<FastDocViewNote> Notes { get; set; } = [];
    /// <summary>Drawn before the index was final with something only the final index knows - fetch it again once the index is complete.</summary>
    public bool Provisional { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewBookmarkArgs
{
    public string DocumentId { get; set; } = "";
    public string Name { get; set; } = "";
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewPictureArgs
{
    public string DocumentId { get; set; } = "";
    /// <summary>The id a rendered block's &lt;img data-word-picture&gt; carries (opaque, per open document).</summary>
    public string PictureId { get; set; } = "";
}

/// <summary>What <c>fastDocViewPicture</c> returns: one picture's bytes (null when the document has no such picture).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewPicture
{
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewSearchArgs
{
    public string DocumentId { get; set; } = "";
    public string Query { get; set; } = "";
    public bool MatchCase { get; set; }
    public int FromBlock { get; set; }
    public int MaxBlocks { get; set; }
}

/// <summary>One bounded search step: the hit blocks of the scanned range (ascending) with, parallel to them, how
/// many times the query occurs in each (the page steps match by match, as the book view does per line), where to
/// continue (-1 at the end), and whether the whole document was scanned. Numbers only - no text.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewSearchResult
{
    public int[] HitBlocks { get; set; } = [];
    public int[] HitCounts { get; set; } = [];
    public int NextBlock { get; set; }
    public bool ReachedEnd { get; set; }
}

/// <summary>The block holding a bookmark (an internal link's target), or null when there is none.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewBookmarkResult
{
    public int? BlockIndex { get; set; }
}

/// <summary>A run of blocks laid out at one page text width (a Word section): from
/// <see cref="FirstBlockIndex"/> to the next section's first block. Twips = 1/1440 inch.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewSection
{
    public int FirstBlockIndex { get; set; }
    public int TextWidthTwips { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewHeading
{
    public int BlockIndex { get; set; }
    public int Level { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>What <c>fastDocViewScan</c> returns: the index state after one more step.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class FastDocViewScanResult
{
    public int KnownBlockCount { get; set; }
    public bool IsIndexComplete { get; set; }
    /// <summary>Note counts grow with the index, like the block count.</summary>
    public int FootnoteCount { get; set; }
    public int EndnoteCount { get; set; }
}
