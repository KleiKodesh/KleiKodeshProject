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
}
