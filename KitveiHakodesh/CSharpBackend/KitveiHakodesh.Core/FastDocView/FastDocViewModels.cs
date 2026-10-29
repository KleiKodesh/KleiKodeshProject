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
