namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>One catalog TOC search hit. Level 0 = a book-title hit (LineIndex is the
/// book's first line); Level ≥ 1 = a TOC entry at that depth.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocHit
{
    public int BookId { get; set; }
    /// <summary>-1 when the entry has no resolved line.</summary>
    public int LineIndex { get; set; }
    /// <summary>Display path: the book title, then " / "-joined TOC segments.</summary>
    public string FullTocPath { get; set; } = "";
    /// <summary>0 = book title, 1+ = TOC depth. First sort key.</summary>
    public int Level { get; set; }
    /// <summary>Catalog tree position + original TOC order. Third sort key.</summary>
    public long TreeOrder { get; set; }
    /// <summary>True when this hit matched every query word LITERALLY (exact / non-
    /// variant) — false when at least one word only matched through a כתיב/ה-prefix
    /// variant or the fuzzy fallback. The PRIMARY sort key: literal matches rank ahead
    /// of variant ones (accuracy first), before Level and TreeOrder.</summary>
    [MessagePack.IgnoreMember]
    public bool IsLiteral { get; set; }
    /// <summary>Internal (not on the wire): which TOC structure the entry came from
    /// (0 = the regular TOC). Scopes the per-structure level truncation.</summary>
    [MessagePack.IgnoreMember]
    public int StructureId { get; set; }
    /// <summary>Internal (not on the wire): query tokens appear in typed order in the
    /// path — the last tiebreak within a (book, level) group.</summary>
    [MessagePack.IgnoreMember]
    public bool QueryInOrder { get; set; }

    /// <summary>Internal (not on the wire): how many DISTINCT query words this hit's own path
    /// contains. Ranks above <see cref="WordSpan"/>, because a span is only meaningful between
    /// hits that found equally many words.</summary>
    [MessagePack.IgnoreMember]
    public int WordsFound { get; set; }

    /// <summary>Internal (not on the wire): width of the smallest run of path tokens covering
    /// every word counted in <see cref="WordsFound"/> — smaller is tighter, so ascending. A
    /// path that gathers the query into one phrase beats one scattering it over four levels.</summary>
    [MessagePack.IgnoreMember]
    public int WordSpan { get; set; }

    /// <summary>Internal (not on the wire): the query was typed with a quoted acronym and this
    /// hit's own stored text carries the same one. Separates an author acronym from the ordinary
    /// word it strips to — see SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKey.</summary>
    [MessagePack.IgnoreMember]
    public bool QuotedFormMatch { get; set; }

    /// <summary>Internal (not on the wire): EVERY query token landed in the book's own title or
    /// author rather than only in the category path. A category is the weaker signal — it is
    /// shared by every book filed under it — so a book that IS the query outranks one merely
    /// filed under a category named like it.</summary>
    [MessagePack.IgnoreMember]
    public bool MatchesTitleOrAuthor { get; set; }
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
}
