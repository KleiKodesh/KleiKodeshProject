namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>One catalog TOC search hit. Level 0 = a book-title hit (LineIndex is the
/// book's first line); Level ≥ 1 = a TOC entry at that depth.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocHit
{
    public int BookId { get; set; }
    /// <summary>-1 when the entry has no resolved line.</summary>
    public int LineIndex { get; set; }
    /// <summary>Display path WITHOUT the book title: the " / "-joined TOC segments ("" for a
    /// book-title hit). The client prefixes the title from its own catalog, so the title is
    /// not sent once per hit.</summary>
    public string TocPath { get; set; } = "";
    /// <summary>Internal (not on the wire): the stored path INCLUDING the title — the text the
    /// ranking rules measure and the per-pass dedup key.</summary>
    [MessagePack.IgnoreMember]
    public string FullTocPath { get; set; } = "";
    /// <summary>0 = book title, 1+ = TOC depth.</summary>
    public int Level { get; set; }
    /// <summary>Catalog tree position + original TOC order: the order among hits that tie on
    /// every ranking key.</summary>
    public long TreeOrder { get; set; }

    /// <summary>Internal (not on the wire): the hit's ranking keys, all read from index data
    /// while collecting — see <see cref="RankKeys"/>.</summary>
    [MessagePack.IgnoreMember]
    internal RankKeys Keys;
    /// <summary>Internal (not on the wire): <see cref="RankKeys.IsLiteral"/>.</summary>
    [MessagePack.IgnoreMember]
    public bool IsLiteral => Keys.IsLiteral;
    /// <summary>Internal (not on the wire): which TOC structure the entry came from
    /// (0 = the regular TOC).</summary>
    [MessagePack.IgnoreMember]
    public int StructureId { get; set; }

    /// <summary>Internal (not on the wire): the book's category ids, its own first, then each
    /// parent up to the root. Filled only by a category-SCOPED search (empty otherwise), which
    /// uses it to tell which scope root the hit fell under.</summary>
    [MessagePack.IgnoreMember]
    public int[] CategoryAncestors { get; set; } = [];
}

/// <summary>Result of a catalog TOC search (catalogTocSearch wire shape).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocSearchResult
{
    /// <summary>False while no index is available yet (first build) — retry later.</summary>
    public bool Ready { get; set; }
    public List<CatalogTocHit> Results { get; set; } = new();
    /// <summary>True when a newer search cancelled this one — discard this response.</summary>
    public bool Superseded { get; set; }
    public string? Error { get; set; }

    /// <summary>Set with <see cref="Ready"/> = false: the seforim DB is missing, so the index
    /// can never become ready — the caller stops retrying and tells the user.</summary>
    public bool DbMissing { get; set; }
    /// <summary>Build progress while not ready, so the caller can show it instead of a bare spinner.</summary>
    public int BuiltBooks { get; set; }
    public int TotalBooks { get; set; }
    /// <summary>Set with <see cref="Ready"/> = false when the last build failed and none is
    /// running: the index will not become ready on its own. Exception message (diagnostic).</summary>
    public string? BuildFailure { get; set; }
    /// <summary>The last build failed and an automatic retry is running now.</summary>
    public bool RetryingAfterBuildFailure { get; set; }
    /// <summary>Not built yet; a timed retry is pending (DB mid-update, index lock held by
    /// another writer). Transient: keep polling, show "building", not a failure.</summary>
    public bool Waiting { get; set; }
}

/// <summary>Result of a book-title search (catalogTitleSearch wire shape). The not-ready
/// fields mean what they mean on <see cref="CatalogTocSearchResult"/>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTitleSearchResult
{
    /// <summary>False while no index is available yet (first build) — retry later.</summary>
    public bool Ready { get; set; }
    /// <summary>Matching book ids, best first. Uncapped.</summary>
    public int[] BookIds { get; set; } = [];
    /// <summary>The index was reset under this search — discard and retry.</summary>
    public bool Superseded { get; set; }
    public string? Error { get; set; }
    public bool DbMissing { get; set; }
    public int BuiltBooks { get; set; }
    public int TotalBooks { get; set; }
    public string? BuildFailure { get; set; }
    public bool RetryingAfterBuildFailure { get; set; }
    public bool Waiting { get; set; }
}

/// <summary>Where the catalog index stands (catalogTocStatus wire shape).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocStatus
{
    public bool Ready { get; set; }
    public bool Indexing { get; set; }
    public int BuiltBooks { get; set; }
    public int TotalBooks { get; set; }
    public bool DbMissing { get; set; }
    /// <summary>Why the last build failed, when none is running now; null otherwise.</summary>
    public string? BuildFailure { get; set; }
    /// <summary>The last build failed and an automatic retry is running now.</summary>
    public bool RetryingAfterBuildFailure { get; set; }
    /// <summary>Not built yet; a timed retry is pending (DB mid-update, index lock held by
    /// another writer). Transient: keep polling, show "building", not a failure.</summary>
    public bool Waiting { get; set; }

    /// <summary>Why the index last started building, and when; null if it never has in this
    /// process.</summary>
    public KitveiHakodesh.Core.Common.IndexBuildReason? BuildReason { get; set; }
}
