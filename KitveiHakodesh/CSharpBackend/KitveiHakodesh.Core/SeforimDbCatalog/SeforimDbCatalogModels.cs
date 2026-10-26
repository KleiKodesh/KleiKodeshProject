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

    /// <summary>Internal (not on the wire): <see cref="FullTocPath"/> tokenized ONCE at
    /// materialization; every ranking key reads this list instead of re-tokenizing the path
    /// (five times per hit, a thousand hits per keystroke, was the search's own cost).</summary>
    [MessagePack.IgnoreMember]
    public List<string> PathTokens { get; set; } = [];
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

    /// <summary>Internal (not on the wire): the query NAMES this hit's book — every word of the
    /// book title is a query word (the query's words all being present is
    /// <see cref="MatchesTitleOrAuthor"/>). For a level-0 hit that is an exact title: "Tur"
    /// for the query "Tur", not "Kapei Aharon al Tur". For a TOC hit it means the user typed
    /// the book and an address in it: "Tur Orach Chaim" is Tur's own section before a
    /// commentary whose title merely contains those words. Ranks right below
    /// MatchesTitleOrAuthor; ties break on the shorter title (<see cref="TitleTokenCount"/>).</summary>
    [MessagePack.IgnoreMember]
    public bool TitleCovered { get; set; }

    /// <summary>Internal (not on the wire): DISTINCT title words, meaningful when
    /// <see cref="TitleCovered"/>: the title that explains more of the query wins — "Levush
    /// Mordechai Bava Metzia" (4) over the TOC row "Levush Mordechai / Bava Metzia" whose
    /// title covers 2.</summary>
    [MessagePack.IgnoreMember]
    public int TitleDistinctWords { get; set; }

    /// <summary>Internal (not on the wire): how far the title's word count is from the
    /// query's, so that among covered titles explaining equally much the one sized like the
    /// query wins: "Machaneh Yisrael" for that query, "Machaneh Yisrael (Yisrael)" when the
    /// user typed that.</summary>
    [MessagePack.IgnoreMember]
    public int TitleLengthGap { get; set; }

    /// <summary>Internal (not on the wire): every word of the hit's own LEAF segment (the
    /// title for a level-0 hit) is a query word — the address typed IS this entry: "Shemini"
    /// before "Shemini Atzeret".</summary>
    [MessagePack.IgnoreMember]
    public bool LeafCovered { get; set; }

    /// <summary>Internal (not on the wire): <see cref="LeafCovered"/> AND the leaf holds a typed
    /// word that is not a title word — the entry is an address the user typed, not a TOC line
    /// that merely repeats the title ("Chavruta, hagahot" under "Hagahot al Chavruta").</summary>
    [MessagePack.IgnoreMember]
    public bool LeafAddsWord { get; set; }

    /// <summary>Internal (not on the wire): how many of the query's words, COUNTED WITH
    /// REPETITION, the whole stored path holds. The per-book truncation keeps the entries
    /// holding the most: for "Rambam al Mishnah Eruvin mishnah 5" the path "perek 1 / mishnah
    /// 5" holds "mishnah" twice as the query does, while "perek 5" holds it once — so the
    /// typed address wins over the shallower entry that merely shares the "5".</summary>
    [MessagePack.IgnoreMember]
    public int WordsMatched { get; set; }

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

    /// <summary>Internal (not on the wire): a BOOK-TITLE hit (level 0) whose stored title carries
    /// the quoted acronym the query was typed with. Separates an author acronym from the ordinary
    /// word it strips to — see SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKey. Never set
    /// on a TOC hit: a chapter that merely cites the acronym must not outrank the book the
    /// acronym names (typing shu"a wants Shulchan Aruch, not every TOC line quoting it).</summary>
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
