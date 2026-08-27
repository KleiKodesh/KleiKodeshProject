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
        public static string GetReverseLineData(int lineCount, int typeCount) => $@"
            SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
            FROM link l
            JOIN line ln ON ln.id = l.sourceLineId
            WHERE l.targetLineId IN ({InPlaceholders("t", lineCount)})
              AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
              AND l.sourceBookId != l.targetBookId
              -- Drop lateral citations: a book that merely QUOTES one line of this one is
              -- not its base text. Keep a link only when the data DECLARES the base
              -- relationship (baseProvenance > 0), or both books sit in the same top-level
              -- corpus. A responsum citing a targum crosses corpora and is not declared.
              AND (
                l.baseProvenance > 0
                OR (SELECT cc.ancestorId FROM book src
                      JOIN category_closure cc ON cc.descendantId = src.categoryId
                      JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                     WHERE src.id = l.sourceBookId)
                 = (SELECT cc.ancestorId FROM book tb
                      JOIN category_closure cc ON cc.descendantId = tb.categoryId
                      JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                     WHERE tb.id = l.targetBookId)
              )";

        /// <summary>Reverse lookup: the base text(s) of the given book, best first. A declared base
        /// beats an inferred one, a book flagged as a base text beats one that is not, then catalogue
        /// order, then how much of this book the candidate covers.</summary>
        public static string GetReverseBooks(int typeCount) => $@"
            SELECT l.sourceBookId
            FROM link l
            JOIN book sb ON sb.id = l.sourceBookId
            WHERE l.targetBookId = @bookId
              AND l.connectionTypeId IN ({InPlaceholders("c", typeCount)})
              AND l.sourceBookId != l.targetBookId
              -- Drop lateral citations: a book that merely QUOTES one line of this one is
              -- not its base text. Keep a link only when the data DECLARES the base
              -- relationship (baseProvenance > 0), or both books sit in the same top-level
              -- corpus. A responsum citing a targum crosses corpora and is not declared.
              AND (
                l.baseProvenance > 0
                OR (SELECT cc.ancestorId FROM book src
                      JOIN category_closure cc ON cc.descendantId = src.categoryId
                      JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                     WHERE src.id = l.sourceBookId)
                 = (SELECT cc.ancestorId FROM book tb
                      JOIN category_closure cc ON cc.descendantId = tb.categoryId
                      JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                     WHERE tb.id = l.targetBookId)
              )
            GROUP BY l.sourceBookId
            ORDER BY MAX(l.baseProvenance) DESC,
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
        // ── Full-text search corpus feed (SeforimDbFtsCorpus) ─────────────────────────
        // The reads the FTS engine's corpus seam is served from. Ports of FtsLib's internal
        // ZayitDb queries, kept semantically identical so an index built through Core is
        // byte-for-byte the index the built-in reader would have built.

        internal const string FtsCountLines = "SELECT COUNT(*) FROM line";

        internal const string FtsCountLinesUpTo = "SELECT COUNT(*) FROM line WHERE id <= @id";

        internal const string FtsGetLineContent = "SELECT content FROM line WHERE id = @id";

        /// <summary>Every line ascending, optionally capped — the from-scratch index feed.
        /// Streamed by the caller; no ORDER BY alternative exists, ascending id IS the contract.</summary>
        internal static string FtsReadLines(bool hasLimit) => hasLimit
            ? "SELECT id, content FROM line ORDER BY id LIMIT @lim"
            : "SELECT id, content FROM line ORDER BY id";

        /// <summary>Lines strictly after @after, ascending — the resume feed. Exclusive bound,
        /// so the resume point itself is never indexed twice.</summary>
        internal static string FtsReadLinesAfter(bool hasLimit) => hasLimit
            ? "SELECT id, content FROM line WHERE id > @after ORDER BY id LIMIT @lim"
            : "SELECT id, content FROM line WHERE id > @after ORDER BY id";

        /// <summary>One chunk of search-result rows: content + book title in a single JOIN, so
        /// titles never need a second query. IN-list sized to the chunk.</summary>
        internal static string FtsFetchLinesWithTitles(int count) =>
            "SELECT l.id, l.content, b.title" +
            " FROM line l LEFT JOIN book b ON b.id = l.bookId" +
            " WHERE l.id IN (" + InPlaceholders("p", count) + ")";

        /// <summary>
        /// Neighbour lines for one chunk of matched ids: m is the matched line, n every line in
        /// the SAME BOOK within ±@radius rows by lineIndex, excluding m itself. Returns the
        /// matched id, the signed delta, and the neighbour's content — the caller buckets by the
        /// delta's sign and joins each side in document order. One self-join per chunk, so a
        /// whole batch of short matches costs one round-trip, and the bookId bound keeps a
        /// snippet from ever bleeding across a book boundary.
        /// </summary>
        internal static string FtsFetchNeighborLines(int count) =>
            "SELECT m.id, n.lineIndex - m.lineIndex AS delta, n.content" +
            " FROM line m JOIN line n" +
            " ON n.bookId = m.bookId" +
            " AND n.lineIndex BETWEEN m.lineIndex - @radius AND m.lineIndex + @radius" +
            " AND n.lineIndex <> m.lineIndex" +
            " WHERE m.id IN (" + InPlaceholders("p", count) + ")";

        // ── Catalog index build (SeforimDbCatalogIndex) ───────────────────────────────
        // The reads the Lucene catalog index is built from. Here rather than beside the
        // indexer because SQL belongs to the DATABASE, not the feature (rule 9) — these read
        // the same seforim.db as everything above.

        /// <summary>Every line of one book, in reading order — the verse-marker scan.</summary>
        internal const string CatalogIndexBookLines =
            "SELECT lineIndex, content FROM line WHERE bookId = @b ORDER BY lineIndex";

        internal const string CatalogIndexTocRowsForBook = @"
            SELECT te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            WHERE te.bookId = @b
            ORDER BY te.id";

        /// <summary>Same order the frontend loads categories in (level, then orderIndex when
        /// the column exists) — the catalog tree-order computation depends on it. Distinct
        /// from GetAllCategories above: different SELECT list, different reader.</summary>
        internal static string CatalogIndexCategories(bool hasOrderIndex) => hasOrderIndex
            ? "SELECT id, parentId, title FROM category ORDER BY level, orderIndex"
            : "SELECT id, parentId, title FROM category ORDER BY level";

        /// <summary>Distinct from GetAllBooks above: no hasTeamim, and the index consumes the
        /// columns positionally in this order.</summary>
        internal const string CatalogIndexBooks = @"
            SELECT b.id, b.categoryId, b.title, group_concat(a.name, ', ') AS authors
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

        /// <summary>All alt-TOC entries ordered by structure — one group per structure.</summary>
        internal const string CatalogIndexAltTocRowsAllStructures = @"
            SELECT ae.structureId, ae.id, ae.parentId, tt.text, l.lineIndex
            FROM alt_toc_entry ae
            JOIN tocText tt ON tt.id = ae.textId
            LEFT JOIN line l ON l.id = ae.lineId
            ORDER BY ae.structureId, ae.id";

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
