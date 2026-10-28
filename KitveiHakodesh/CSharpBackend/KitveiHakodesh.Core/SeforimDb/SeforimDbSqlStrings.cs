namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// SQL for the seforim (Torah library) database, and nothing else.
    ///
    /// This is the ONE database in Core with its SQL in a file of its own. Everywhere else the
    /// statements sit as consts at the top of the queries file, where they read beside their
    /// caller; here there are nearly four hundred lines of them, and inlining that would bury
    /// the code around it. The frontend splits the same way (queries.sql.ts / seforimDb.ts).
    ///
    /// The SQL belongs to the DATABASE, not to a feature — so the catalog indexer's statements
    /// live here too, rather than beside the catalog code that runs them.
    ///
    /// Add new SQL here; add the method that runs it to SeforimDbQueries.
    /// </summary>
    internal static class SeforimDbSqlStrings
    {
        // ── Categories ──────────────────────────────────────────────────────────────

        /// <summary>Sibling order within a level: orderIndex, except that a NEGATIVE orderIndex
        /// sorts AFTER every non-negative one. The library marks its "mefarshim" subfolders with
        /// -5 — a commentaries folder belongs below the text it comments on, not above it (the
        /// Otzaria app reads the sign the same way). Shared by the browse query and the
        /// tree-order query so the catalog view and every ranking agree.</summary>
        private const string CategorySiblingOrder = "CASE WHEN orderIndex < 0 THEN 1 ELSE 0 END, orderIndex";

        /// <summary>All categories flat — the frontend builds the tree in memory once.</summary>
        public static string GetAllCategories(bool hasOrderIndex) => hasOrderIndex
            ? "SELECT id, parentId, title, level FROM category ORDER BY level, " + CategorySiblingOrder
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

        /// <summary>
        /// Every line's character count for one book, by lineIndex — the reader's
        /// scrollbar estimates row heights from these without loading any text.
        /// Only valid on libraries whose <c>line</c> table has the column.
        /// </summary>
        public const string GetLineCharCounts = @"
            SELECT lineIndex, charCount
            FROM line
            WHERE bookId = @bookId
            ORDER BY lineIndex";

        /// <summary>The ids of a book's lines in [from, to) by lineIndex — no text.</summary>
        public const string GetLineIds = @"
            SELECT lineIndex, id
            FROM line
            WHERE bookId = @bookId AND lineIndex >= @fromIndex AND lineIndex < @toIndex
            ORDER BY lineIndex";

        /// <summary>A page of lines for streaming load.</summary>
        public static string GetLinesPaged(SeforimDbSchema schema) => $@"
            SELECT l.id, l.lineIndex, {SeforimDbLineContentSql.ContentColumn(schema, "l")}
            FROM line l{SeforimDbLineContentSql.ContentJoin(schema, "l")}
            WHERE l.bookId = @bookId
            ORDER BY l.lineIndex
            LIMIT @limit OFFSET @offset";

        /// <summary>The lines of a book with lineIndex in [@from, @to] — a search scan reads them.</summary>
        public static string GetLinesInIndexRange(SeforimDbSchema schema) => $@"
            SELECT l.id, l.lineIndex, {SeforimDbLineContentSql.ContentColumn(schema, "l")}
            FROM line l{SeforimDbLineContentSql.ContentJoin(schema, "l")}
            WHERE l.bookId = @bookId AND l.lineIndex BETWEEN @from AND @to
            ORDER BY l.lineIndex";

        /// <summary>The lines of a book whose lineIndex is in the list — a sparse scan reads them.</summary>
        public static string GetLinesByIndexList(SeforimDbSchema schema, string indexList) => $@"
            SELECT l.id, l.lineIndex, {SeforimDbLineContentSql.ContentColumn(schema, "l")}
            FROM line l{SeforimDbLineContentSql.ContentJoin(schema, "l")}
            WHERE l.bookId = @bookId AND l.lineIndex IN ({indexList})
            ORDER BY l.lineIndex";

        /// <summary>Like <see cref="GetVersionLinesPaged"/>, for lineIndex in a list.</summary>
        public static string GetVersionLinesByIndexList(SeforimDbSchema schema, string indexList) => $@"
            SELECT l.id, l.lineIndex,
                   {C_RawContentExpression(schema, "l")} AS baseContent,
                   vl.content AS versionContent,
                   CASE WHEN l.heRef IS NULL  THEN 0
                        WHEN vl.lineId IS NULL THEN 1
                        WHEN vl.content IS NULL THEN 0
                        ELSE 2 END AS contentSource
            FROM line l{C_ContentJoin(schema, "l")}
            LEFT JOIN version_line vl ON vl.versionId = @versionId AND vl.lineId = l.id
            WHERE l.bookId = @bookId AND l.lineIndex IN ({indexList})
            ORDER BY l.lineIndex";

        /// <summary>Like <see cref="GetVersionLinesPaged"/>, for lineIndex in [@from, @to].</summary>
        public static string GetVersionLinesInIndexRange(SeforimDbSchema schema) => $@"
            SELECT l.id, l.lineIndex,
                   {C_RawContentExpression(schema, "l")} AS baseContent,
                   vl.content AS versionContent,
                   CASE WHEN l.heRef IS NULL  THEN 0
                        WHEN vl.lineId IS NULL THEN 1
                        WHEN vl.content IS NULL THEN 0
                        ELSE 2 END AS contentSource
            FROM line l{C_ContentJoin(schema, "l")}
            LEFT JOIN version_line vl ON vl.versionId = @versionId AND vl.lineId = l.id
            WHERE l.bookId = @bookId AND l.lineIndex BETWEEN @from AND @to
            ORDER BY l.lineIndex";

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

        /// <summary>Grouped/de-duplicated variant of GetCommentaryLinksForSourceLineRange: ordered so
        /// the reader can cut groups on a (book, type) change; DISTINCT because a range of source
        /// lines links the same target line many times over.</summary>
        public static string GetCommentaryLinkGroupsForSourceLineRange(int count, bool hasTargetLineIndex) => hasTargetLineIndex
            ? $@"
            SELECT DISTINCT l.targetBookId, l.connectionTypeId, l.targetLineId,
                   l.targetLineIndex AS lineIndex
            FROM link l
            WHERE l.sourceLineId IN ({InPlaceholders("p", count)})
            ORDER BY l.targetBookId, l.connectionTypeId, lineIndex, l.targetLineId"
            : $@"
            SELECT DISTINCT l.targetBookId, l.connectionTypeId, l.targetLineId,
                   ln.lineIndex
            FROM link l
            JOIN line ln ON ln.id = l.targetLineId
            WHERE l.sourceLineId IN ({InPlaceholders("p", count)})
            ORDER BY l.targetBookId, l.connectionTypeId, ln.lineIndex, l.targetLineId";

        /// <summary>Content backfill for a batch of line ids.</summary>
        public static string GetLineContents(SeforimDbSchema schema, int count) =>
            $"SELECT l.id, {SeforimDbLineContentSql.ContentColumn(schema, "l")}" +
            $" FROM line l{SeforimDbLineContentSql.ContentJoin(schema, "l")}" +
            $" WHERE l.id IN ({InPlaceholders("p", count)})";

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
        public static string GetReverseLineData(SeforimDbSchema schema, int lineCount, int typeCount) => $@"
            SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, {C_ContentColumn(schema, "ln")}
            FROM link l
            JOIN line ln ON ln.id = l.sourceLineId{C_ContentJoin(schema, "ln")}
            WHERE l.targetLineId IN ({InPlaceholders("t", lineCount)})
              AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
              AND l.sourceBookId != l.targetBookId
              -- Drop lateral citations: a book that merely QUOTES one line of this one is
              -- not its base text. Keep a link only when the data DECLARES the base
              -- relationship (the declared-base column > 0), or both books sit in the same
              -- top-level corpus AND the book being asked about is not itself a base text.
              --
              -- The corpus test alone only catches citations that cross corpora - a responsum
              -- quoting a targum. It says nothing about the commonest case, because a
              -- commentary on a tractate lives in the same corpus as the tractate: without the
              -- isBaseBook guard, every unprovenanced commentary linking into Berachot would
              -- be reported as Berachot's source. A book flagged as a base text has no base
              -- text, so the corpus fallback does not apply to one. The declared branch is
              -- deliberately left alone - if the data asserts the relationship, that outranks
              -- any inference made here.
              AND (
                l.{schema.DeclaredBaseColumn} > 0
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

        /// <summary>Reverse lookup: the base text(s) of the given book, best first. A declared base
        /// beats an inferred one, a book flagged as a base text beats one that is not, then catalogue
        /// order, then how much of this book the candidate covers.</summary>
        public static string GetReverseBooks(SeforimDbSchema schema, int typeCount) => $@"
            SELECT l.sourceBookId
            FROM link l
            JOIN book sb ON sb.id = l.sourceBookId
            WHERE l.targetBookId = @bookId
              AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
              AND l.sourceBookId != l.targetBookId
              -- Drop lateral citations: a book that merely QUOTES one line of this one is
              -- not its base text. Keep a link only when the data DECLARES the base
              -- relationship (the declared-base column > 0), or both books sit in the same
              -- top-level corpus AND the book being asked about is not itself a base text.
              --
              -- The corpus test alone only catches citations that cross corpora - a responsum
              -- quoting a targum. It says nothing about the commonest case, because a
              -- commentary on a tractate lives in the same corpus as the tractate: without the
              -- isBaseBook guard, every unprovenanced commentary linking into Berachot would
              -- be reported as Berachot's source. A book flagged as a base text has no base
              -- text, so the corpus fallback does not apply to one. The declared branch is
              -- deliberately left alone - if the data asserts the relationship, that outranks
              -- any inference made here.
              AND (
                l.{schema.DeclaredBaseColumn} > 0
                OR ((SELECT cc.ancestorId FROM book src
                       JOIN category_closure cc ON cc.descendantId = src.categoryId
                       JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                      WHERE src.id = l.sourceBookId)
                  = (SELECT cc.ancestorId FROM book tb
                       JOIN category_closure cc ON cc.descendantId = tb.categoryId
                       JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                      WHERE tb.id = l.targetBookId)
                    AND (SELECT tb2.isBaseBook FROM book tb2 WHERE tb2.id = l.targetBookId) = 0)
              )
            GROUP BY l.sourceBookId
            ORDER BY MAX(l.{schema.DeclaredBaseColumn}) DESC,
                     sb.isBaseBook DESC,
                     sb.orderIndex,
                     COUNT(DISTINCT l.targetLineId) DESC";

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

        /// <summary>Target line id + lineIndex for the first link from a source line to a target book.
        /// A line can link to the same book both as its commentary and by a lateral REFERENCE/OTHER
        /// link to some other place (Berachot 45a links Rashi on 45a AND Rashi on 20b), so the
        /// commentary-shaped links win, then the earliest target line - never the raw row order.</summary>
        public const string GetLinkTargetForSourceLineAndBook = @"
            SELECT lk.targetLineId, ln.lineIndex
            FROM link lk
            JOIN line ln ON ln.id = lk.targetLineId
            LEFT JOIN connection_type ct ON ct.id = lk.connectionTypeId
            WHERE lk.sourceLineId = @sourceLineId
              AND lk.targetBookId = @targetBookId
            ORDER BY CASE WHEN ct.name IN ('COMMENTARY', 'SUPER_COMMENTARY', 'TARGUM') THEN 0 ELSE 1 END,
                     ln.lineIndex
            LIMIT 1";

        // ── TOC paths & line→book/index helpers (search + commentary labels) ─────────

        /// <summary>The TOC entry (and book) each line id sits under.</summary>
        public static string GetTocEntryForLines(int count) => $@"
            SELECT lt.lineId, te.bookId, lt.tocEntryId
            FROM line_toc lt
            JOIN tocEntry te ON te.id = lt.tocEntryId
            WHERE lt.lineId IN ({InPlaceholders("p", count)})
            ORDER BY lt.lineId, lt.tocEntryId";

        /// <summary>Full TOC path (root→leaf, ' '-joined, book-title root stripped) per TOC entry id.</summary>
        public static string GetTocPathsForTocEntries(int count) => $@"
            WITH RECURSIVE ancestors(entryId, bookId, id, parentId, text, depth) AS (
              SELECT te.id, te.bookId, te.id, te.parentId, tt.text, 0
              FROM tocEntry te
              JOIN tocText tt ON tt.id = te.textId
              WHERE te.id IN ({InPlaceholders("e", count)})
              UNION ALL
              SELECT a.entryId, a.bookId, te.id, te.parentId, tt.text, a.depth + 1
              FROM ancestors a
              JOIN tocEntry te ON te.id = a.parentId
              JOIN tocText tt ON tt.id = te.textId
            ),
            ordered AS (
              SELECT a.entryId, a.text, a.depth,
                     MAX(a.depth) OVER (PARTITION BY a.entryId) AS maxDepth,
                     b.title AS bookTitle
              FROM ancestors a
              JOIN book b ON b.id = a.bookId
            )
            SELECT entryId, group_concat(text, ' ') AS path
            FROM (
              SELECT entryId, text
              FROM ordered
              WHERE NOT (depth = maxDepth AND text = bookTitle)
              ORDER BY entryId, depth DESC
            )
            GROUP BY entryId";

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

        public static string GetBookIdsForLines(int count) =>
            $"SELECT id AS lineId, bookId FROM line WHERE id IN ({InPlaceholders("p", count)})";

        /// <summary>lineIndex + bookId for a single line id (open book at position after FTS).</summary>
        public const string GetLineIndexFromLineId =
            "SELECT lineIndex, bookId FROM line WHERE id = @id LIMIT 1";

        // ── Dictionary sources living in the seforim DB (מצודת ציון, מלבי״ם, מנחם, ערוך) ──

        public const string GetBookIdsByTitlePattern = "SELECT id FROM book WHERE title LIKE @pattern";

        public const string GetBookIdByExactTitle = "SELECT id FROM book WHERE title = @title";

        /// <summary>A single line from a book by its lineIndex.</summary>
        public static string GetLineByBookAndLineIndex(SeforimDbSchema schema) =>
            "SELECT l.id, l.lineIndex, " + C_ContentColumn(schema, "l")
            + " FROM line l" + C_ContentJoin(schema, "l")
            + " WHERE l.bookId = @bookId AND l.lineIndex = @lineIndex";

        /// <summary>
        /// A page of lines as a specific version reads them.
        ///
        /// A version is an OVERLAY, not a separate book: version_line carries replacement
        /// content keyed by the SAME line ids, so ids, lineIndex, the TOC, links and
        /// highlights all stay valid when the text swaps underneath them.
        ///
        /// THREE cases, not two, and COALESCE cannot tell the last two apart:
        ///
        ///   no version_line row      the version does not cover this line  -> empty
        ///   row with NULL content    schema 6 only: the version's text is IDENTICAL to the
        ///                            base line, stored once rather than twice -> base text
        ///   row with content         the version's own text
        ///
        /// Before schema 6 the column was NOT NULL, so NULL only ever meant "no row" and
        /// COALESCE(vl.content, '') was right. In schema 6 it is nullable and 744 of 1542
        /// versions are entirely NULL, so that same expression would render each of them as a
        /// completely blank book. `vl.lineId IS NULL` is what distinguishes a missing row from
        /// a present one, which is why the fallback is spelled out rather than coalesced.
        ///
        /// Structural lines (heRef IS NULL — headings and titles) keep the base text: a version
        /// supplies body text, not the book's scaffolding. Body lines a partial version does not
        /// cover come back EMPTY rather than falling back to the base, which would silently
        /// interleave two different versions in one page and read as a single text. Partial
        /// versions are real here — some cover 31 of 1584 lines.
        ///
        /// WHY TWO COLUMNS RATHER THAN ONE CASE: on schema 6 both `line_content.content` and
        /// `version_line.content` hold zstd BLOBs, and SQLite applies ONE affinity to a CASE
        /// expression as a whole. Mixing a BLOB arm with the `''` arm coerced the chosen BLOB
        /// to TEXT, so the reader received the frame's raw bytes as a string, the codec's type
        /// dispatch passed it through as already-decoded text, and the page rendered base64.
        /// Selecting each candidate in its own column keeps both BLOBs intact; the reader picks
        /// between them, where the choice costs nothing and no affinity is imposed.
        /// </summary>
        public static string GetVersionLinesPaged(SeforimDbSchema schema) => $@"
            SELECT l.id, l.lineIndex,
                   {C_RawContentExpression(schema, "l")} AS baseContent,
                   vl.content AS versionContent,
                   CASE WHEN l.heRef IS NULL  THEN 0
                        WHEN vl.lineId IS NULL THEN 1
                        WHEN vl.content IS NULL THEN 0
                        ELSE 2 END AS contentSource
            FROM line l{C_ContentJoin(schema, "l")}
            LEFT JOIN version_line vl ON vl.versionId = @versionId AND vl.lineId = l.id
            WHERE l.bookId = @bookId
            ORDER BY l.lineIndex
            LIMIT @limit OFFSET @offset";

        /// <summary>
        /// What <c>contentSource</c> in <see cref="GetVersionLinesPaged"/> says to read.
        /// Kept beside the SQL that produces it so the two cannot drift.
        /// </summary>
        public static class VersionLineContentSource
        {
            /// <summary>Read `baseContent` — a structural line, or a version row that stores
            /// NULL to mean "identical to the base".</summary>
            public const int Base = 0;

            /// <summary>Read neither — this version does not cover the line.</summary>
            public const int Uncovered = 1;

            /// <summary>Read `versionContent` — the version's own replacement text.</summary>
            public const int Version = 2;
        }

        // ── Book versions, provenance and schema stamp ───────────────────────────────

        /// <summary>
        /// The alternate versions of a book that actually carry text.
        ///
        /// hasContent = 0 rows are metadata-only records (edition provenance and licence) with
        /// no version_line rows behind them — most version rows in the shipped database are
        /// these, and offering one would open a blank book. Ordered by the publisher's
        /// priority, then title, so the best-regarded edition heads the list.
        ///
        /// The caller probes for the table first: book_version arrives in a later library schema.
        /// </summary>
        public const string GetBookVersions = @"
            SELECT id, versionTitle, heVersionTitle, versionSource, versionNotes, heVersionNotes
            FROM book_version
            WHERE bookId = @bookId AND hasContent = 1
            ORDER BY priority DESC, versionTitle";

        /// <summary>The upstream source a book came from.</summary>
        public const string GetBookSourceName =
            "SELECT s.name AS sourceName FROM book b JOIN source s ON s.id = b.sourceId WHERE b.id = @bookId";

        // ── Book metadata (the book view's info popover) ──────────────────────────────

        /// <summary>
        /// A book's own descriptive columns plus its category and source names. heDesc and
        /// collectiveTitleHe exist in only one library schema, so the caller passes NULL in
        /// their place where the column is missing.
        /// </summary>
        public static string GetBookMetadata(string descriptionColumn, string collectiveTitleColumn) => $@"
            SELECT b.title, b.heRef, b.heShortDesc, {descriptionColumn}, {collectiveTitleColumn},
                   c.title, s.name
            FROM book b
            LEFT JOIN category c ON c.id = b.categoryId
            LEFT JOIN source s ON s.id = b.sourceId
            WHERE b.id = @bookId";

        public const string GetBookMetadataAuthors =
            "SELECT a.name FROM book_author ba JOIN author a ON a.id = ba.authorId WHERE ba.bookId = @bookId";

        public const string GetBookMetadataPublicationPlaces =
            "SELECT p.name FROM book_pub_place bp JOIN pub_place p ON p.id = bp.pubPlaceId WHERE bp.bookId = @bookId";

        /// <summary>pub_date's value column is `date`, not `name`, in both library schemas.</summary>
        public const string GetBookMetadataPublicationDates =
            "SELECT d.date FROM book_pub_date bd JOIN pub_date d ON d.id = bd.pubDateId WHERE bd.bookId = @bookId";

        public const string GetBookMetadataTopics =
            "SELECT t.name FROM book_topic bt JOIN topic t ON t.id = bt.topicId WHERE bt.bookId = @bookId";

        /// <summary>Licences recorded on the book's versions (Otzaria schema: book_version.license).</summary>
        public const string GetBookMetadataLicensesFromVersions =
            "SELECT DISTINCT license FROM book_version WHERE bookId = @bookId";

        /// <summary>Licences recorded on the book's editions (SeforimApp schema: book_edition → license).</summary>
        public const string GetBookMetadataLicensesFromEditions =
            "SELECT DISTINCT l.code FROM book_edition be JOIN license l ON l.id = be.licenseId WHERE be.bookId = @bookId";

        public const string GetBookMetadataEditionSourcesFromVersions =
            "SELECT DISTINCT versionSource FROM book_version WHERE bookId = @bookId";

        public const string GetBookMetadataEditionSourcesFromEditions =
            "SELECT DISTINCT e.sourceUrl FROM book_edition be JOIN edition e ON e.id = be.editionId WHERE be.bookId = @bookId";

        /// <summary>
        /// The library build, for STAMPING a report only. Schema detection goes by table
        /// structure and never by this number — see SeforimDbSchema.
        /// </summary>
        public const string GetLibraryDbVersion =
            "SELECT value FROM schema_meta WHERE key = 'db_version' LIMIT 1";

        /// <summary>One line's canonical reference and stored text, for verifying a report.</summary>
        public static string GetLineRefAndContent(SeforimDbSchema schema) =>
            "SELECT l.id, l.lineIndex, l.heRef, " + C_ContentColumn(schema, "l")
            + " FROM line l" + C_ContentJoin(schema, "l")
            + " WHERE l.bookId = @bookId AND l.lineIndex = @lineIndex";

        /// <summary>Builds "@p0, @p1, …, @pN-1" for a dynamic IN clause.</summary>
        // ── Full-text search corpus feed (SeforimDbFtsCorpus) ─────────────────────────
        // The reads the FTS engine's corpus seam is served from. Ports of FtsLib's internal
        // ZayitDb queries, kept semantically identical so an index built through Core is
        // byte-for-byte the index the built-in reader would have built.

        internal const string FtsCountLines = "SELECT COUNT(*) FROM line";

        internal const string FtsCountLinesUpTo = "SELECT COUNT(*) FROM line WHERE id <= @id";
        internal static string FtsGetLineContent(SeforimDbSchema schema) =>
            "SELECT " + C_ContentExpression(schema, "l") +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " WHERE l.id = @id";

        /// <summary>Every line ascending, optionally capped - the from-scratch index feed.
        /// Streamed by the caller; no ORDER BY alternative exists, ascending id IS the contract.</summary>
        internal static string FtsReadLines(SeforimDbSchema schema, bool hasLimit) =>
            "SELECT l.id, " + C_ContentColumn(schema, "l") +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " ORDER BY l.id" + (hasLimit ? " LIMIT @lim" : "");

        /// <summary>Lines strictly after @after, ascending - the resume feed. Exclusive bound,
        /// so the resume point itself is never indexed twice.</summary>
        internal static string FtsReadLinesAfter(SeforimDbSchema schema, bool hasLimit) =>
            "SELECT l.id, " + C_ContentColumn(schema, "l") +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " WHERE l.id > @after ORDER BY l.id" + (hasLimit ? " LIMIT @lim" : "");

        /// <summary>A line's rank inside its book in the index's order (lineIndex; id breaks a
        /// tie — exactly <see cref="FtsReadBookLines"/>'s order). Its index position is the
        /// book's first position plus this. Served by idx_line_book_index.</summary>
        internal const string FtsLineRankInBook =
            "SELECT COUNT(*) FROM line WHERE bookId = @b AND (lineIndex < @li OR (lineIndex = @li AND id < @id))";

        /// <summary>Lines per book — lets the catalog-order feed skip whole books on resume.</summary>
        internal const string FtsLineCountsByBook = "SELECT bookId, COUNT(*) FROM line GROUP BY bookId";

        /// <summary>One book's lines in reading order (lineIndex; id breaks a tie), from the
        /// @skip-th on — the catalog-order index feed, one book at a time. Served by
        /// idx_line_book_index.</summary>
        internal static string FtsReadBookLines(SeforimDbSchema schema) =>
            "SELECT l.id, " + C_ContentColumn(schema, "l") +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " WHERE l.bookId = @b ORDER BY l.lineIndex, l.id LIMIT -1 OFFSET @skip";

        /// <summary>One chunk of search-result TEXT for verification (locate, filters, snippets):
        /// id and content only, read by primary key from the table that holds the text -
        /// `line_content` on a split database (no `line` row, no join), else `line`. A line
        /// with no text row is absent (it cannot match anyway). Placement is a separate,
        /// later read (<see cref="FtsFetchPlacements"/>) for the hits that pass.</summary>
        internal static string FtsFetchLines(SeforimDbSchema schema, int count) =>
            (schema.HasSplitLineContent ? "SELECT id, content FROM line_content" : "SELECT id, content FROM line") +
            " WHERE id IN (" + InPlaceholders("p", count) + ")";

        /// <summary>One chunk of PLACEMENTS: id, bookId, lineIndex. No text, no TOC entry (the
        /// page fetches TOC paths for the rows it renders). Read after verification, for the
        /// hits that passed.</summary>
        internal static string FtsFetchPlacements(SeforimDbSchema schema, int count) =>
            "SELECT l.id, l.bookId, l.lineIndex FROM line l" +
            " WHERE l.id IN (" + InPlaceholders("p", count) + ")";

        /// <summary>One chunk of search-result rows: id, content, and the line's PLACEMENT -
        /// bookId and lineIndex - read off the same `line` row the content fetch visits
        /// anyway, so hits leave the searcher complete without a second query per batch. No
        /// book title or TOC entry: the page resolves both for the rows it renders.
        /// IN-list sized to the chunk.</summary>
        internal static string FtsFetchPlacedLines(SeforimDbSchema schema, int count) =>
            "SELECT l.id, " + C_ContentColumn(schema, "l") + ", l.bookId, l.lineIndex" +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " WHERE l.id IN (" + InPlaceholders("p", count) + ")";

        /// <summary>
        /// The texts of the snippets a page shows, in ONE statement: (matched id, lineIndex
        /// delta, content). Rows @p* are plain lines, read by primary key from the table that
        /// holds the text, exactly as <see cref="FtsFetchLines"/> (delta 0). Rows @s* are SHORT
        /// lines: the line itself (delta 0) and every line of the SAME BOOK within
        /// ±@radius by lineIndex, over the (bookId, lineIndex) index — the bookId bound keeps
        /// context from bleeding across a book boundary. Either list may be empty.
        /// </summary>
        internal static string FtsFetchSnippetLines(SeforimDbSchema schema, int plainCount, int shortCount)
        {
            string? plain = plainCount == 0 ? null :
                (schema.HasSplitLineContent ? "SELECT id, 0, content FROM line_content" : "SELECT id, 0, content FROM line") +
                " WHERE id IN (" + InPlaceholders("p", plainCount) + ")";
            string? withNeighbours = shortCount == 0 ? null :
                "SELECT m.id, n.lineIndex - m.lineIndex, " + C_ContentColumn(schema, "n") +
                // The sidecar join must come AFTER this JOIN's own ON clause (see FtsFetchNeighborLines).
                " FROM line m JOIN line n" +
                " ON n.bookId = m.bookId" +
                " AND n.lineIndex BETWEEN m.lineIndex - @radius AND m.lineIndex + @radius" +
                " AND (n.lineIndex <> m.lineIndex OR n.id = m.id)" +
                C_ContentJoin(schema, "n") +
                " WHERE m.id IN (" + InPlaceholders("s", shortCount) + ")";
            return plain == null ? withNeighbours! : withNeighbours == null ? plain : plain + " UNION ALL " + withNeighbours;
        }

        /// <summary>
        /// Neighbour lines for one chunk of matched ids: m is the matched line, n every line in
        /// the SAME BOOK within ±@radius rows by lineIndex, excluding m itself. Returns the
        /// matched id, the signed delta, the neighbour's content and (last, so readers of the
        /// first three columns are unaffected) the neighbour's own id — the caller buckets by the
        /// delta's sign and joins each side in document order. One self-join per chunk, so a
        /// whole batch of short matches costs one round-trip, and the bookId bound keeps a
        /// snippet from ever bleeding across a book boundary.
        /// </summary>
        internal static string FtsFetchNeighborLines(SeforimDbSchema schema, int count) =>
            "SELECT m.id, n.lineIndex - m.lineIndex AS delta, " + C_ContentColumn(schema, "n") + ", n.id" +
            // The sidecar join must come AFTER this JOIN's own ON clause: putting it between
            // `JOIN line n` and its ON is a syntax error, because the ON would then bind to
            // the sidecar rather than to `n`.
            " FROM line m JOIN line n" +
            " ON n.bookId = m.bookId" +
            " AND n.lineIndex BETWEEN m.lineIndex - @radius AND m.lineIndex + @radius" +
            " AND n.lineIndex <> m.lineIndex" +
            C_ContentJoin(schema, "n") +
            " WHERE m.id IN (" + InPlaceholders("p", count) + ")";

        // ── Catalog index build (SeforimDbCatalogIndex) ───────────────────────────────
        // The reads the Lucene catalog index is built from. Here rather than beside the
        // indexer because SQL belongs to the DATABASE, not the feature (rule 9) — these read
        // the same seforim.db as everything above.

        /// <summary>Every line of one book, in reading order — the verse-marker scan.</summary>
        internal static string CatalogIndexBookLines(SeforimDbSchema schema) =>
            "SELECT l.lineIndex, " + C_ContentColumn(schema, "l") +
            " FROM line l" + C_ContentJoin(schema, "l") +
            " WHERE l.bookId = @b ORDER BY l.lineIndex";

        internal const string CatalogIndexTocRowsForBook = @"
            SELECT te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            WHERE te.bookId = @b
            ORDER BY te.id";

        /// <summary>Same order the frontend loads categories in (level, then the sibling order
        /// above when the column exists) — the catalog tree-order computation depends on it.
        /// Distinct from GetAllCategories above: different SELECT list, different reader.</summary>
        internal static string CatalogIndexCategories(bool hasOrderIndex) => hasOrderIndex
            ? "SELECT id, parentId, title FROM category ORDER BY level, " + CategorySiblingOrder
            : "SELECT id, parentId, title FROM category ORDER BY level";

        /// <summary>Distinct from GetAllBooks above: no hasTeamim, and the index consumes the
        /// columns positionally in this order. The last two columns feed the tree order
        /// (<see cref="SeforimDb.SeforimDbBookTreeOrder"/>): a library whose schema lacks them
        /// gets 0 / NULL, i.e. no base-text promotion.</summary>
        internal static string CatalogIndexBooks(bool hasIsBaseBook, bool hasDependenceType) => $@"
            SELECT b.id, b.categoryId, b.title, group_concat(a.name, ', ') AS authors,
                   {(hasIsBaseBook ? "b.isBaseBook" : "0")} AS isBaseBook,
                   {(hasDependenceType ? "b.dependenceType" : "NULL")} AS dependenceType
            FROM book b
            LEFT JOIN book_author ba ON ba.bookId = b.id
            LEFT JOIN author a ON a.id = ba.authorId
            GROUP BY b.id
            ORDER BY b.orderIndex";

        /// <summary>Each book's first line. SQLite's MIN() aggregate guarantees the bare
        /// columns come from the minimal row.</summary>
        internal const string CatalogIndexFirstLines =
            "SELECT bookId, id, MIN(lineIndex) FROM line GROUP BY bookId";

        internal const string CatalogIndexAltStructures =
            "SELECT id, bookId, title, heTitle FROM alt_toc_structure";

        /// <summary>All TOC entries ordered by book, so the build materialises one book's tree
        /// at a time instead of all of them at once.</summary>
        internal const string CatalogIndexTocRowsAllBooks = @"
            SELECT te.bookId, te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            ORDER BY te.bookId, te.id";

        /// <summary>All alt-TOC entries ordered by structure — one group per structure. The last
        /// column flags an entry that repeats a REGULAR TOC entry of the same book: same text and
        /// same line, or same text when the alt entry has no line of its own (a section heading
        /// such as an alt root "Orach Chaim" over the regular one). The "Chapters" structures are
        /// 96% such repeats, and indexing them put the same address in the results twice. ~4s on
        /// the shipped library (idx_toc_line / idx_toc_book).</summary>
        internal const string CatalogIndexAltTocRowsAllStructures = @"
            SELECT ae.structureId, ae.id, ae.parentId, tt.text, l.lineIndex,
                   -- Same answer as one EXISTS on (bookId, textId, lineId-or-null), split so
                   -- an entry with a line probes by line: probing by text scanned every
                   -- entry sharing a common text (perek 1) and took minutes per build.
                   CASE WHEN ae.lineId IS NOT NULL
                        THEN EXISTS (SELECT 1 FROM tocEntry te
                                     WHERE te.lineId = ae.lineId AND +te.textId = ae.textId AND +te.bookId = s.bookId)
                        ELSE EXISTS (SELECT 1 FROM tocEntry te
                                     WHERE te.bookId = s.bookId AND te.textId = ae.textId)
                   END AS dupOfRegular
            FROM alt_toc_entry ae
            JOIN alt_toc_structure s ON s.id = ae.structureId
            JOIN tocText tt ON tt.id = ae.textId
            LEFT JOIN line l ON l.id = ae.lineId
            ORDER BY ae.structureId, ae.id";

        // Shorthands for the two line-content fragments, so a statement built by string
        // concatenation stays on one readable line. Same thing SeforimDbLineContentSql
        // exposes; see there for why the SQL differs per schema.
        private static string C_ContentColumn(SeforimDbSchema schema, string lineAlias)
            => SeforimDbLineContentSql.ContentColumn(schema, lineAlias);

        private static string C_ContentExpression(SeforimDbSchema schema, string lineAlias)
            => SeforimDbLineContentSql.ContentExpression(schema, lineAlias);

        private static string C_ContentJoin(SeforimDbSchema schema, string lineAlias)
            => SeforimDbLineContentSql.ContentJoin(schema, lineAlias);

        private static string C_RawContentExpression(SeforimDbSchema schema, string lineAlias)
            => SeforimDbLineContentSql.RawContentExpression(schema, lineAlias);

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
}
