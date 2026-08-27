namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// SQL strings for the seforim (Torah library) database — kept in this file ONLY,
/// separate from the query logic in SeforimDbService (Sqlite.Queries.cs), mirroring
/// the frontend's queries.sql.ts / seforimDb.ts split. Add new SQL here; add the
/// method that runs it in Sqlite.Queries.cs.
/// </summary>
internal static class SeforimSql
{
    // ── Categories ──────────────────────────────────────────────────────────────

    /// <summary>All categories flat — the frontend builds the tree in memory once.</summary>
    public static string GetAllCategories(bool hasOrderIndex) => hasOrderIndex
        ? "SELECT id, parentId, title, level FROM category ORDER BY level, orderIndex"
        : "SELECT id, parentId, title, level FROM category ORDER BY level";

    // ── Books ────────────────────────────────────────────────────────────────────

    /// <summary>All books flat, with aggregated author names — attached to tree nodes by categoryId.</summary>
    public const string GetAllBooks = @"
        SELECT b.id, b.categoryId, b.title, b.hasTeamim,
               group_concat(a.name, ', ') AS authors
        FROM book b
        LEFT JOIN book_author ba ON ba.bookId = b.id
        LEFT JOIN author a ON a.id = ba.authorId
        GROUP BY b.id
        ORDER BY b.orderIndex";

    /// <summary>Single book by id — totalLines for virtual-scroll init + has* flags for the toolbar.</summary>
    public const string GetBookById = @"
        SELECT totalLines, hasTeamim,
               hasTargumConnection, hasReferenceConnection, hasSourceConnection,
               hasCommentaryConnection, hasOtherConnection
        FROM book
        WHERE id = @id";

    // ── Lines ─────────────────────────────────────────────────────────────────

    /// <summary>A page of lines for streaming load.</summary>
    public const string GetLinesPaged = @"
        SELECT id, lineIndex, content
        FROM line
        WHERE bookId = @bookId
        ORDER BY lineIndex
        LIMIT @limit OFFSET @offset";

    /// <summary>
    /// A page of lines as a specific version reads them.
    ///
    /// A version is an OVERLAY, not a separate book: version_line carries replacement
    /// content keyed by the SAME line ids, so ids, lineIndex, the TOC, links and
    /// highlights all stay valid when the text swaps underneath them.
    ///
    /// Structural lines (heRef IS NULL — headings and titles) keep the base text: a
    /// version supplies body text, not the book's scaffolding. Body lines a partial
    /// version does not cover come back EMPTY rather than falling back to line.content,
    /// which would silently interleave two different versions in one page and read as
    /// a single text. Partial versions are real here — some cover 31 of 1584 lines.
    /// </summary>
    public const string GetVersionLinesPaged = @"
        SELECT l.id, l.lineIndex,
               CASE WHEN l.heRef IS NULL THEN l.content
                    ELSE COALESCE(vl.content, '') END AS content
        FROM line l
        LEFT JOIN version_line vl ON vl.versionId = @versionId AND vl.lineId = l.id
        WHERE l.bookId = @bookId
        ORDER BY l.lineIndex
        LIMIT @limit OFFSET @offset";

    // ── Versions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The alternate versions of a book that actually carry text.
    ///
    /// hasContent = 0 rows are metadata-only records (edition provenance and licence)
    /// with no version_line rows behind them — most version rows in the shipped DB are
    /// these, and offering one would open a blank book. Ordered by the publisher's
    /// priority, then title, so the best-regarded edition heads the list.
    /// </summary>
    public const string GetBookVersions = @"
        SELECT id, versionTitle, heVersionTitle, versionSource, versionNotes, heVersionNotes
        FROM book_version
        WHERE bookId = @bookId AND hasContent = 1
        ORDER BY priority DESC, versionTitle";

    // ── TOC ───────────────────────────────────────────────────────────────────

    /// <summary>All TOC entries for a book, flat — the frontend builds the tree in memory.</summary>
    public const string GetAllTocEntries = @"
        SELECT te.id, te.parentId, te.level, te.lineId, te.hasChildren,
               tt.text, l.lineIndex
        FROM tocEntry te
        JOIN tocText tt ON tt.id = te.textId
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.bookId = @bookId
        ORDER BY te.id";

    /// <summary>All alt_toc structures for a book.</summary>
    public const string GetAltTocStructures = @"
        SELECT id, key, title, heTitle
        FROM alt_toc_structure
        WHERE bookId = @bookId
        ORDER BY id";

    /// <summary>All alt_toc entries for a structure, flat.</summary>
    public const string GetAllAltTocEntries = @"
        SELECT ae.id, ae.parentId, ae.level, ae.lineId, ae.hasChildren,
               tt.text, l.lineIndex
        FROM alt_toc_entry ae
        JOIN tocText tt ON tt.id = ae.textId
        LEFT JOIN line l ON l.id = ae.lineId
        WHERE ae.structureId = @structureId
        ORDER BY ae.id";

    /// <summary>First TOC entry for a book whose text matches a LIKE pattern (daf-yomi nav).</summary>
    public const string GetTocEntryByTextPrefix = @"
        SELECT te.id, l.lineIndex
        FROM tocEntry te
        JOIN tocText tt ON tt.id = te.textId
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.bookId = @bookId
          AND tt.text LIKE @pattern
        ORDER BY te.id ASC
        LIMIT 1";

    /// <summary>TOC titles for a set of books (TOC-search fallback). Dynamic IN over book ids.</summary>
    public static string GetTocTitlesForBooks(int count) => $@"
        SELECT te.id, te.parentId, te.bookId, tt.text, l.lineIndex
        FROM tocEntry te
        JOIN tocText tt ON tt.id = te.textId
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.bookId IN ({InPlaceholders("b", count)})
        ORDER BY te.id";

    /// <summary>Prefiltered TOC titles: entries whose text contains @word plus all their
    /// ancestors (so segment chains stay complete). Dynamic IN over book ids + @word.</summary>
    public static string GetTocTitlesMatchingForBooks(int count) => $@"
        WITH RECURSIVE matched(id, parentId) AS (
          SELECT te.id, te.parentId
          FROM tocEntry te
          JOIN tocText tt ON tt.id = te.textId
          WHERE te.bookId IN ({InPlaceholders("b", count)})
            AND tt.text LIKE '%' || @word || '%' ESCAPE '\'
        ),
        anc(id) AS (
          SELECT parentId FROM matched WHERE parentId IS NOT NULL
          UNION
          SELECT te.parentId FROM tocEntry te JOIN anc ON te.id = anc.id WHERE te.parentId IS NOT NULL
        )
        SELECT te.id, te.parentId, te.bookId, tt.text, l.lineIndex
        FROM tocEntry te
        JOIN tocText tt ON tt.id = te.textId
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.id IN (SELECT id FROM matched UNION SELECT id FROM anc)
        ORDER BY te.id";

    // ── Commentary / links ─────────────────────────────────────────────────────

    /// <summary>Links-only (no content JOIN) for a range of source lines — group structure
    /// renders from this; content is backfilled via GetLineContents.
    /// The Zayit DB denormalizes link.targetLineIndex (verified identical to line.lineIndex);
    /// the Otzaria DB has no such column, so it needs a JOIN to line just for the index —
    /// pass hasTargetLineIndex from a ColumnExists check on the open DB.
    /// NOTE: schema difference verified 2026-07-19 against both real DBs; both DB projects
    /// evolve independently — RE-VERIFY when either ships a new schema. The ColumnExists
    /// probe adapts at runtime either way; only the perf reasoning can go stale.</summary>
    public static string GetCommentaryLinksForSourceLineRange(int count, bool hasTargetLineIndex) => hasTargetLineIndex
        ? $@"
        SELECT l.targetBookId, l.targetLineId, l.connectionTypeId,
               l.targetLineIndex AS lineIndex
        FROM link l
        WHERE l.sourceLineId IN ({InPlaceholders("p", count)})"
        : $@"
        SELECT l.targetBookId, l.targetLineId, l.connectionTypeId,
               ln.lineIndex
        FROM link l
        JOIN line ln ON ln.id = l.targetLineId
        WHERE l.sourceLineId IN ({InPlaceholders("p", count)})";

    /// <summary>Content backfill for a batch of line ids.</summary>
    public static string GetLineContents(int count) =>
        $"SELECT id, content FROM line WHERE id IN ({InPlaceholders("p", count)})";

    /// <summary>Word-level link anchors (link_anchor, SeforimLibrary schema v2+) for a batch of
    /// source lines. side 0 = the anchor sits in the source line's text. Offsets are visible-char
    /// (HTML tags = 0 chars, each entity = 1 char, everything else — including nikud/te'amim — = 1;
    /// upstream's countVisibleChars convention, verified against real v17 data 2026-07-27).
    /// Guard the call behind a TableExists("link_anchor") probe. Same targetLineIndex variant
    /// split as GetCommentaryLinksForSourceLineRange — schema v2 always has the column, but the
    /// probe, not the schema doc, decides.</summary>
    public static string GetWordLinkAnchorsForLines(int count, bool hasTargetLineIndex) => hasTargetLineIndex
        ? $@"
        SELECT l.sourceLineId AS lineId, la.charStart, la.charEnd, la.label,
               l.targetBookId, l.targetLineId, l.targetLineIndex, l.sourceBookId
        FROM link l
        JOIN link_anchor la ON la.linkId = l.id AND la.side = 0
        WHERE l.sourceLineId IN ({InPlaceholders("p", count)})
        ORDER BY l.sourceLineId, la.charStart"
        : $@"
        SELECT l.sourceLineId AS lineId, la.charStart, la.charEnd, la.label,
               l.targetBookId, l.targetLineId, ln.lineIndex AS targetLineIndex, l.sourceBookId
        FROM link l
        JOIN link_anchor la ON la.linkId = l.id AND la.side = 0
        JOIN line ln ON ln.id = l.targetLineId
        WHERE l.sourceLineId IN ({InPlaceholders("p", count)})
        ORDER BY l.sourceLineId, la.charStart";

    /// <summary>Distinct word-link targets (commentary book id + anchor label) of one source
    /// book's side-0 POINT anchors (range citations never render markers), ascending by book
    /// id. Feeds the frontend's per-book fallback treatment ranking (smaller book id =
    /// simpler decoration), its marker visibility dropdown, and its sign-vocabulary guard
    /// (the labels reveal which glyphs the book's printed signs already use, so app-assigned
    /// wrappers never imitate them). Guard behind the link_anchor probe like the query above.</summary>
    public const string GetWordLinkAnchorTargetsForBook = @"
        SELECT DISTINCT l.targetBookId, la.label
        FROM link l
        JOIN link_anchor la ON la.linkId = l.id AND la.side = 0
        WHERE l.sourceBookId = @bookId
          AND (la.charEnd IS NULL OR la.charEnd <= la.charStart)
        ORDER BY l.targetBookId";

    /// <summary>All connection type ids and names.</summary>
    public const string GetAllConnectionTypes = "SELECT id, name FROM connection_type";

    /// <summary>Default commentator book ids for a book, by ascending position.</summary>
    public const string GetDefaultCommentators = @"
        SELECT commentatorBookId
        FROM default_commentator
        WHERE bookId = @bookId
        ORDER BY position ASC";

    // ── Reverse lookups (source & targum share identical SQL — only the type ids differ) ──

    /// <summary>Reverse lookup: source/targum lines that link TO the given target lines via the
    /// given connection types. A single-element IN handles both the single-line and range cases.</summary>
    public static string GetReverseLineData(int lineCount, int typeCount, string declaredCol) => $@"
        SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
        FROM link l
        JOIN line ln ON ln.id = l.sourceLineId
        WHERE l.targetLineId IN ({InPlaceholders("t", lineCount)})
          AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
{DeclaredOrSameCorpus(declaredCol)}";

    /// <summary>Reverse lookup: the base text(s) of the given book, best first. A declared base
    /// beats an inferred one, a book flagged as a base text beats one that is not, then catalogue
    /// order, then how much of this book the candidate covers.
    /// The declared/inferred tier only exists on the otzaria schema, whose baseProvenance
    /// distinguishes them (2 vs 1); Zayit's isDeclaredBase is a plain 0/1, so there the first
    /// key simply separates declared from undeclared.</summary>
    public static string GetReverseBooks(int typeCount, string declaredCol) => $@"
        SELECT l.sourceBookId
        FROM link l
        JOIN book sb ON sb.id = l.sourceBookId
        WHERE l.targetBookId = @bookId
          AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
{DeclaredOrSameCorpus(declaredCol)}
        GROUP BY l.sourceBookId
        ORDER BY MAX(l.{declaredCol}) DESC,
                 sb.isBaseBook DESC,
                 sb.orderIndex,
                 COUNT(DISTINCT l.targetLineId) DESC";

    /// <summary>
    /// Name of the column flagging a link as a DECLARED base-text relationship. The two
    /// seforim-DB schemas spell the same idea differently: Zayit's own DB has
    /// `isDeclaredBase` (0/1), the newer otzaria build has `baseProvenance`
    /// (0=none, 1=inferred from the title, 2=declared by Sefaria). Both are "> 0 means
    /// the data asserts this relationship", so callers only need the name.
    /// Probe with ColumnExists and pass the result to the queries below.
    /// </summary>
    public const string DeclaredBaseColumnZayit = "isDeclaredBase";
    public const string DeclaredBaseColumnOtzaria = "baseProvenance";

    /// <summary>
    /// Keeps a reversed link only when it is a real base-text relationship rather than a
    /// passing citation: either the data DECLARES it, or both books sit in the same
    /// top-level corpus AND the book we are asking about is not itself a base text.
    ///
    /// The corpus test alone only catches citations that cross corpora - a responsum
    /// quoting a targum. It says nothing about the commonest case, because a commentary
    /// on a tractate lives in the same corpus as the tractate: every unprovenanced
    /// commentary linking into Berachot would be reported as Berachot's source. A book
    /// flagged as a base text has no base text, so the corpus fallback does not apply to
    /// one. The declared branch is deliberately left alone - if the data asserts the
    /// relationship, that outranks any inference we make here.
    /// </summary>
    private static string DeclaredOrSameCorpus(string declaredCol) => $@"
          AND l.sourceBookId != l.targetBookId
          AND (
            l.{declaredCol} > 0
            OR ((SELECT cc.ancestorId FROM book src
                   JOIN category_closure cc ON cc.descendantId = src.categoryId
                   JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                  WHERE src.id = l.sourceBookId)
              = (SELECT cc.ancestorId FROM book tb
                   JOIN category_closure cc ON cc.descendantId = tb.categoryId
                   JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                  WHERE tb.id = l.targetBookId)
                AND (SELECT tb2.isBaseBook FROM book tb2 WHERE tb2.id = l.targetBookId) = 0)
          )";

    /// <summary>Distinct forward static-filter books for a source book (COMMENTARY/EIN_MISHPAT etc.).</summary>
    public static string GetStaticFilterBooks(int typeCount) => $@"
        SELECT DISTINCT l.targetBookId, l.connectionTypeId
        FROM link l
        WHERE l.sourceBookId = @bookId
          AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})";

    // ── Commentary navigation ───────────────────────────────────────────────────

    /// <summary>Next (or prev) main-book line, by lineIndex, that links to a given commentary book.</summary>
    public static string GetSectionWithCommentary(bool next) => $@"
        SELECT ln.id, ln.lineIndex
        FROM line ln
        JOIN link lk ON lk.sourceLineId = ln.id
        WHERE ln.bookId = @mainBookId
          AND lk.targetBookId = @commentaryBookId
          AND ln.lineIndex {(next ? ">" : "<")} @lineIndex
        ORDER BY ln.lineIndex {(next ? "ASC" : "DESC")}
        LIMIT 1";

    /// <summary>Of a batch of TOC section ranges, the smallest (next) / largest (prev) sectionStart
    /// that contains a link to the commentary book. Bind: (s0,e0),(s1,e1)…, then main + commentary book.</summary>
    public static string GetTocSectionWithCommentary(int count, bool next)
    {
        var values = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) values.Append(", ");
            values.Append("(@s").Append(i).Append(", @e").Append(i).Append(')');
        }
        return $@"
        WITH ranges(sectionStart, sectionEnd) AS (VALUES {values})
        SELECT ranges.sectionStart
        FROM ranges
        JOIN line ln ON ln.bookId = @mainBookId AND ln.lineIndex >= ranges.sectionStart AND ln.lineIndex < ranges.sectionEnd
        JOIN link lk ON lk.sourceLineId = ln.id AND lk.targetBookId = @commentaryBookId
        ORDER BY ranges.sectionStart {(next ? "ASC" : "DESC")}
        LIMIT 1";
    }

    /// <summary>Target line id + lineIndex for the first link from a source line to a target book.</summary>
    public const string GetLinkTargetForSourceLineAndBook = @"
        SELECT lk.targetLineId, ln.lineIndex
        FROM link lk
        JOIN line ln ON ln.id = lk.targetLineId
        WHERE lk.sourceLineId = @sourceLineId
          AND lk.targetBookId = @targetBookId
        LIMIT 1";

    // ── TOC paths & line→book/index helpers (search + commentary labels) ─────────

    /// <summary>Full TOC path (root→leaf, ' '-joined, book-title root stripped) per line id.</summary>
    public static string GetTocPathsForLines(int count) => $@"
        WITH RECURSIVE ancestors(lineId, bookId, entryId, parentId, text, depth) AS (
          SELECT lt.lineId, te.bookId, te.id, te.parentId, tt.text, 0
          FROM line_toc lt
          JOIN tocEntry te ON te.id = lt.tocEntryId
          JOIN tocText tt ON tt.id = te.textId
          WHERE lt.lineId IN ({InPlaceholders("p", count)})
          UNION ALL
          SELECT a.lineId, a.bookId, te.id, te.parentId, tt.text, a.depth + 1
          FROM ancestors a
          JOIN tocEntry te ON te.id = a.parentId
          JOIN tocText tt ON tt.id = te.textId
        ),
        ordered AS (
          SELECT a.lineId, a.bookId, a.text, a.depth,
                 MAX(a.depth) OVER (PARTITION BY a.lineId) AS maxDepth,
                 b.title AS bookTitle
          FROM ancestors a
          JOIN book b ON b.id = a.bookId
        )
        SELECT lineId, MAX(bookId) AS bookId, group_concat(text, ' ') AS tocPath
        FROM (
          SELECT lineId, bookId, text
          FROM ordered
          WHERE NOT (depth = maxDepth AND text = bookTitle)
          ORDER BY lineId, depth DESC
        )
        GROUP BY lineId";

    /// <summary>Deepest common-ancestor TOC path covering both endpoints of each (groupKey, first, last)
    /// range. Bind interleaved triples: (@g0,@f0,@l0),(@g1,@f1,@l1)…</summary>
    public static string GetEnclosingTocPathForLineRanges(int groupCount)
    {
        var values = new System.Text.StringBuilder();
        for (int i = 0; i < groupCount; i++)
        {
            if (i > 0) values.Append(", ");
            values.Append("(@g").Append(i).Append(", @f").Append(i).Append(", @l").Append(i).Append(')');
        }
        return $@"
        WITH
        groups(groupKey, firstLineId, lastLineId) AS (VALUES {values}),
        firstAncestors(groupKey, entryId, parentId, depth) AS (
          SELECT g.groupKey, te.id, te.parentId, 0
          FROM groups g JOIN line_toc lt ON lt.lineId = g.firstLineId JOIN tocEntry te ON te.id = lt.tocEntryId
          UNION ALL
          SELECT fa.groupKey, te.id, te.parentId, fa.depth + 1
          FROM firstAncestors fa JOIN tocEntry te ON te.id = fa.parentId
        ),
        lastAncestors(groupKey, entryId, parentId, depth) AS (
          SELECT g.groupKey, te.id, te.parentId, 0
          FROM groups g JOIN line_toc lt ON lt.lineId = g.lastLineId JOIN tocEntry te ON te.id = lt.tocEntryId
          UNION ALL
          SELECT la.groupKey, te.id, te.parentId, la.depth + 1
          FROM lastAncestors la JOIN tocEntry te ON te.id = la.parentId
        ),
        commonAncestors AS (
          SELECT fa.groupKey, fa.entryId, fa.depth AS firstDepth
          FROM firstAncestors fa JOIN lastAncestors la ON la.groupKey = fa.groupKey AND la.entryId = fa.entryId
        ),
        bestAncestor AS (
          SELECT groupKey, entryId FROM commonAncestors
          WHERE firstDepth = (SELECT MIN(firstDepth) FROM commonAncestors ca2 WHERE ca2.groupKey = commonAncestors.groupKey)
        ),
        pathAncestors(groupKey, entryId, parentId, text, depth, bookId) AS (
          SELECT ba.groupKey, te.id, te.parentId, tt.text, 0, te.bookId
          FROM bestAncestor ba JOIN tocEntry te ON te.id = ba.entryId JOIN tocText tt ON tt.id = te.textId
          UNION ALL
          SELECT pa.groupKey, te.id, te.parentId, tt.text, pa.depth + 1, pa.bookId
          FROM pathAncestors pa JOIN tocEntry te ON te.id = pa.parentId JOIN tocText tt ON tt.id = te.textId
        ),
        ordered AS (
          SELECT pa.groupKey, pa.text, pa.depth, pa.bookId,
                 MAX(pa.depth) OVER (PARTITION BY pa.groupKey) AS maxDepth,
                 b.title AS bookTitle
          FROM pathAncestors pa JOIN book b ON b.id = pa.bookId
        )
        SELECT groupKey, MAX(bookId) AS bookId, group_concat(text, ' ') AS tocPath
        FROM (
          SELECT groupKey, bookId, text FROM ordered
          WHERE NOT (depth = maxDepth AND text = bookTitle)
          ORDER BY groupKey, depth DESC
        )
        GROUP BY groupKey";
    }

    /// <summary>bookId for a batch of line ids (fallback when line_toc has no entry).</summary>
    public static string GetBookIdsForLines(int count) =>
        $"SELECT id AS lineId, bookId FROM line WHERE id IN ({InPlaceholders("p", count)})";

    /// <summary>lineIndex + bookId for a single line id (open book at position after FTS).</summary>
    public const string GetLineIndexFromLineId =
        "SELECT lineIndex, bookId FROM line WHERE id = @id LIMIT 1";

    // ── Dictionary sources living in the seforim DB (מצודת ציון, מלבי״ם, מנחם, ערוך) ──

    public const string GetBookIdsByTitlePattern = "SELECT id FROM book WHERE title LIKE @pattern";

    public const string GetBookIdByExactTitle = "SELECT id FROM book WHERE title = @title";

    /// <summary>Bold-tagged headword lines for a set of books matching a content LIKE pattern.</summary>
    public static string GetLinesWithContentPatternForBooks(int bookCount) => $@"
        SELECT l.content, b.title, b.id AS bookId, l.id AS lineId, l.lineIndex
        FROM line l JOIN book b ON b.id = l.bookId
        WHERE l.bookId IN ({InPlaceholders("b", bookCount)})
          AND l.content LIKE @pattern
        LIMIT 50";

    /// <summary>Lines from one book matching either of two content LIKE patterns.</summary>
    public const string GetLinesWithEitherContentPattern = @"
        SELECT id, lineIndex, content FROM line
        WHERE bookId = @bookId AND (content LIKE @p1 OR content LIKE @p2)
        ORDER BY lineIndex LIMIT 20";

    /// <summary>A single line from a book by its lineIndex.</summary>
    public const string GetLineByBookAndLineIndex =
        "SELECT id, lineIndex, content FROM line WHERE bookId = @bookId AND lineIndex = @lineIndex";

    /// <summary>Builds "@p0, @p1, …, @pN-1" for a dynamic IN clause.</summary>
    internal static string InPlaceholders(string prefix, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('@').Append(prefix).Append(i);
        }
        return sb.ToString();
    }
}
