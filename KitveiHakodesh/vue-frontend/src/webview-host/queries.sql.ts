/**
 * All SQL query strings live here.
 * Import from this file — never write inline SQL elsewhere.
 */

export const SQL = {
  // ── Categories ──────────────────────────────────────────────────────────────

  /** All categories flat — used to build the full tree in memory once */
  GET_ALL_CATEGORIES: (hasOrderIndex: boolean) =>
    hasOrderIndex
      ? `SELECT id, parentId, title, level FROM category ORDER BY level, orderIndex`
      : `SELECT id, parentId, title, level FROM category ORDER BY level`,

  /** All books flat — attached to tree nodes by categoryId, with aggregated author names */
  GET_ALL_BOOKS: `
    SELECT b.id, b.categoryId, b.title, b.hasTeamim,
           group_concat(a.name, ', ') AS authors
    FROM book b
    LEFT JOIN book_author ba ON ba.bookId = b.id
    LEFT JOIN author a ON a.id = ba.authorId
    GROUP BY b.id
    ORDER BY b.orderIndex
  `,

  // ── Books ────────────────────────────────────────────────────────────────────

  /** Single book by id — totalLines for virtual scroll init + has* flags for toolbar */
  GET_BOOK_BY_ID: `
    SELECT totalLines, hasTeamim,
           hasTargumConnection, hasReferenceConnection, hasSourceConnection,
           hasCommentaryConnection, hasOtherConnection
    FROM book
    WHERE id = ?
  `,

  // ── TOC ──────────────────────────────────────────────────────────────────────

  /** All TOC entries for a book, flat — build tree in memory */
  GET_ALL_TOC_ENTRIES: `
    SELECT te.id, te.parentId, te.level, te.lineId, te.hasChildren,
           tt.text, l.lineIndex
    FROM tocEntry te
    JOIN tocText tt ON tt.id = te.textId
    LEFT JOIN line l ON l.id = te.lineId
    WHERE te.bookId = ?
    ORDER BY te.id
  `,

  /** TOC entry ids, parentIds, bookIds, titles and lineIndex for multiple books — used for TOC search fallback */
  GET_TOC_TITLES_FOR_BOOKS: (count: number) => `
    SELECT te.id, te.parentId, te.bookId, tt.text, l.lineIndex
    FROM tocEntry te
    JOIN tocText tt ON tt.id = te.textId
    LEFT JOIN line l ON l.id = te.lineId
    WHERE te.bookId IN (${Array(count).fill('?').join(', ')})
    ORDER BY te.id
  `,

  /** All alt_toc structures for a book */
  GET_ALT_TOC_STRUCTURES: `
    SELECT id, key, title, heTitle
    FROM alt_toc_structure
    WHERE bookId = ?
    ORDER BY id
  `,

  /** All alt_toc entries for a structure, flat — build tree in memory */
  GET_ALL_ALT_TOC_ENTRIES: `
    SELECT ae.id, ae.parentId, ae.level, ae.lineId, ae.hasChildren,
           tt.text, l.lineIndex
    FROM alt_toc_entry ae
    JOIN tocText tt ON tt.id = ae.textId
    LEFT JOIN line l ON l.id = ae.lineId
    WHERE ae.structureId = ?
    ORDER BY ae.id
  `,

  // ── Search ───────────────────────────────────────────────────────────────────

  /** Get lineIndex and bookId from a line id — used after full-text search to open the book at the right position */
  GET_LINE_INDEX_FROM_LINE_ID: `
    SELECT lineIndex, bookId
    FROM line
    WHERE id = ?
    LIMIT 1
  `,

  /**
   * Get the full TOC path for a batch of line ids.
   * Uses a recursive CTE to walk tocEntry.parentId up to the root,
   * then concatenates ancestor texts root→leaf separated by ' / '.
   * Strips the root segment if it duplicates the book title.
   * Returns one row per lineId — lineId + tocPath.
   */
  GET_TOC_PATHS_FOR_LINES: (count: number) => `
    WITH RECURSIVE ancestors(lineId, bookId, entryId, parentId, text, depth) AS (
      SELECT lt.lineId, te.bookId, te.id, te.parentId, tt.text, 0
      FROM line_toc lt
      JOIN tocEntry te ON te.id = lt.tocEntryId
      JOIN tocText tt ON tt.id = te.textId
      WHERE lt.lineId IN (${Array(count).fill('?').join(', ')})
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
    GROUP BY lineId
  `,

  /**
   * Same as GET_TOC_PATHS_FOR_LINES but also returns the book title.
   * Used by full-text search phase 2 to avoid a separate book lookup.
   */
  GET_TOC_PATHS_AND_TITLES_FOR_LINES: (count: number) => `
    WITH RECURSIVE ancestors(lineId, bookId, entryId, parentId, text, depth) AS (
      SELECT lt.lineId, te.bookId, te.id, te.parentId, tt.text, 0
      FROM line_toc lt
      JOIN tocEntry te ON te.id = lt.tocEntryId
      JOIN tocText tt ON tt.id = te.textId
      WHERE lt.lineId IN (${Array(count).fill('?').join(', ')})
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
    SELECT lineId, MAX(bookId) AS bookId, MAX(bookTitle) AS bookTitle, group_concat(text, ' ') AS tocPath
    FROM (
      SELECT lineId, bookId, bookTitle, text
      FROM ordered
      WHERE NOT (depth = maxDepth AND text = bookTitle)
      ORDER BY lineId, depth DESC
    )
    GROUP BY lineId
  `,

  /**
   * For a batch of (groupKey, firstLineId, lastLineId) triples, finds the deepest common
   * ancestor TOC entry that covers both endpoints of each group, then builds and returns
   * the full path string for that ancestor.
   *
   * Used by useCommentaryTocPaths in TOC-section mode, where a commentary group spans
   * multiple source lines and we want the enclosing section label rather than the specific
   * label of the first line.
   *
   * Parameters are interleaved: groupKey1, firstLineId1, lastLineId1, groupKey2, ...
   * groupKey is an arbitrary integer tag (e.g. array index) used to correlate results.
   */
  GET_ENCLOSING_TOC_PATH_FOR_LINE_RANGES: (groupCount: number) => {
    const valuePlaceholders = Array(groupCount)
      .fill('(?, ?, ?)')
      .join(', ')
    return `
      WITH
      -- Input table: one row per group with its two endpoint lineIds
      groups(groupKey, firstLineId, lastLineId) AS (
        VALUES ${valuePlaceholders}
      ),
      -- Ancestor chain for the first endpoint of each group
      firstAncestors(groupKey, entryId, parentId, depth) AS (
        SELECT g.groupKey, te.id, te.parentId, 0
        FROM groups g
        JOIN line_toc lt ON lt.lineId = g.firstLineId
        JOIN tocEntry te ON te.id = lt.tocEntryId
        UNION ALL
        SELECT fa.groupKey, te.id, te.parentId, fa.depth + 1
        FROM firstAncestors fa
        JOIN tocEntry te ON te.id = fa.parentId
      ),
      -- Ancestor chain for the last endpoint of each group
      lastAncestors(groupKey, entryId, parentId, depth) AS (
        SELECT g.groupKey, te.id, te.parentId, 0
        FROM groups g
        JOIN line_toc lt ON lt.lineId = g.lastLineId
        JOIN tocEntry te ON te.id = lt.tocEntryId
        UNION ALL
        SELECT la.groupKey, te.id, te.parentId, la.depth + 1
        FROM lastAncestors la
        JOIN tocEntry te ON te.id = la.parentId
      ),
      -- Common ancestors: entries that appear in both chains for the same group.
      -- The deepest one (min depth value = closest to the leaves) is the answer.
      commonAncestors AS (
        SELECT fa.groupKey, fa.entryId, fa.depth AS firstDepth
        FROM firstAncestors fa
        JOIN lastAncestors la ON la.groupKey = fa.groupKey AND la.entryId = fa.entryId
      ),
      -- Pick the deepest (smallest depth value = furthest from root = most specific)
      -- common ancestor per group.
      bestAncestor AS (
        SELECT groupKey, entryId
        FROM commonAncestors
        WHERE firstDepth = (
          SELECT MIN(firstDepth) FROM commonAncestors ca2 WHERE ca2.groupKey = commonAncestors.groupKey
        )
      ),
      -- Walk up from the best ancestor to the root to build the full path string.
      pathAncestors(groupKey, entryId, parentId, text, depth, bookId) AS (
        SELECT ba.groupKey, te.id, te.parentId, tt.text, 0, te.bookId
        FROM bestAncestor ba
        JOIN tocEntry te ON te.id = ba.entryId
        JOIN tocText tt ON tt.id = te.textId
        UNION ALL
        SELECT pa.groupKey, te.id, te.parentId, tt.text, pa.depth + 1, pa.bookId
        FROM pathAncestors pa
        JOIN tocEntry te ON te.id = pa.parentId
        JOIN tocText tt ON tt.id = te.textId
      ),
      ordered AS (
        SELECT pa.groupKey, pa.text, pa.depth, pa.bookId,
               MAX(pa.depth) OVER (PARTITION BY pa.groupKey) AS maxDepth,
               b.title AS bookTitle
        FROM pathAncestors pa
        JOIN book b ON b.id = pa.bookId
      )
      SELECT groupKey, MAX(bookId) AS bookId, group_concat(text, ' ') AS tocPath
      FROM (
        SELECT groupKey, bookId, text
        FROM ordered
        WHERE NOT (depth = maxDepth AND text = bookTitle)
        ORDER BY groupKey, depth DESC
      )
      GROUP BY groupKey
    `
  },

  /** Fetch bookId for a batch of lineIds directly from the line table (fallback when line_toc has no entry). */
  GET_BOOK_IDS_FOR_LINES: (count: number) =>
    `SELECT id AS lineId, bookId FROM line WHERE id IN (${Array(count).fill('?').join(', ')})`,

  // ── Lines ────────────────────────────────────────────────────────────────────

  /** All lines for a book */
  // GET_ALL_LINES is intentionally omitted — use GET_LINES_PAGED for streaming load

  /** A page of lines for streaming load */
  GET_LINES_PAGED: `
    SELECT id, lineIndex, content
    FROM line
    WHERE bookId = ?
    ORDER BY lineIndex
    LIMIT ? OFFSET ?
  `,

  // ── Links ────────────────────────────────────────────────────────────────────

  /** Combined links and lines data for commentary loader (single query) */
  GET_COMMENTARY_DATA_FOR_SOURCE_LINE: `
    SELECT l.targetBookId, l.targetLineId, l.connectionTypeId,
           ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.targetLineId
    WHERE l.sourceLineId = ?
  `,

  /** Combined links and lines data for commentary loader (range) */
  GET_COMMENTARY_DATA_FOR_SOURCE_LINE_RANGE: (count: number) => `
    SELECT l.targetBookId, l.targetLineId, l.connectionTypeId,
           ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.targetLineId
    WHERE l.sourceLineId IN (${Array(count).fill('?').join(',')})
  `,

  /**
   * Links-only variant of GET_COMMENTARY_DATA_FOR_SOURCE_LINE_RANGE — no content column
   * and no JOIN. A section click can match thousands of commentary lines; shipping their
   * full text in one payload (measured ~10MB for one chumash chapter) delayed first render
   * by ~1s, and joining `line` for each hit costs thousands of random point-reads on a
   * multi-GB table (measured 147ms warm / 1.4s cold vs 16ms without the JOIN).
   * `link.targetLineIndex` is denormalized and verified identical to `line.lineIndex`.
   * The commentary loader renders group structure from this query and backfills content
   * with GET_LINE_CONTENTS afterwards.
   */
  GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE: (count: number) => `
    SELECT l.targetBookId, l.targetLineId, l.connectionTypeId,
           l.targetLineIndex AS lineIndex
    FROM link l
    WHERE l.sourceLineId IN (${Array(count).fill('?').join(',')})
  `,

  /** Content backfill for a batch of line ids (commentary lazy-content second phase). */
  GET_LINE_CONTENTS: (count: number) => `
    SELECT id, content FROM line WHERE id IN (${Array(count).fill('?').join(',')})
  `,

  /**
   * Reverse source lookup (single line): find lines in source books that link TO the
   * given target line via a commentary-type connection. Used instead of the unreliable
   * SOURCE connection type — the source text is discovered by reversing the commentary link.
   * Returns sourceBookId, sourceLineId, lineIndex and content of the source line.
   * The connectionTypeId placeholders must cover all DB names that canonicalize to COMMENTARY.
   */
  GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP: (commentaryTypeCount: number) => `
    SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.sourceLineId
    WHERE l.targetLineId = ?
      AND l.connectionTypeId IN (${Array(commentaryTypeCount).fill('?').join(',')})
  `,

  /**
   * Reverse source lookup (range): same as above but for a set of target line IDs.
   * Bind order: targetLineId1, targetLineId2, ..., commentaryTypeId1, commentaryTypeId2, ...
   */
  GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP_RANGE: (commentaryTypeCount: number, targetLineCount: number) => `
    SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.sourceLineId
    WHERE l.targetLineId IN (${Array(targetLineCount).fill('?').join(',')})
      AND l.connectionTypeId IN (${Array(commentaryTypeCount).fill('?').join(',')})
  `,

  /**
   * Reverse source book lookup: find distinct source books that link to any line in the
   * given base book via a commentary-type connection. Used to populate the SOURCE section
   * of the static commentary filter panel.
   * Bind order: targetBookId, commentaryTypeId1, commentaryTypeId2, ...
   */
  GET_SOURCE_BOOKS_BY_REVERSE_COMMENTARY_LOOKUP: (commentaryTypeCount: number) => `
    SELECT DISTINCT l.sourceBookId
    FROM link l
    WHERE l.targetBookId = ?
      AND l.connectionTypeId IN (${Array(commentaryTypeCount).fill('?').join(',')})
  `,

  /**
   * Reverse targum lookup (single line): find lines in targum books that link TO the
   * given target line via a TARGUM-type connection. Mirrors the reverse source lookup —
   * the targum text is discovered by reversing the TARGUM link rather than relying on
   * the forward TARGUM connection type.
   * Bind order: targetLineId, targumTypeId1, targumTypeId2, ...
   */
  GET_TARGUM_DATA_BY_REVERSE_TARGUM_LOOKUP: (targumTypeCount: number) => `
    SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.sourceLineId
    WHERE l.targetLineId = ?
      AND l.connectionTypeId IN (${Array(targumTypeCount).fill('?').join(',')})
  `,

  /**
   * Reverse targum lookup (range): same as above but for a set of target line IDs.
   * Bind order: targetLineId1, targetLineId2, ..., targumTypeId1, targumTypeId2, ...
   */
  GET_TARGUM_DATA_BY_REVERSE_TARGUM_LOOKUP_RANGE: (targumTypeCount: number, targetLineCount: number) => `
    SELECT l.sourceBookId, l.sourceLineId, ln.lineIndex, ln.content
    FROM link l
    JOIN line ln ON ln.id = l.sourceLineId
    WHERE l.targetLineId IN (${Array(targetLineCount).fill('?').join(',')})
      AND l.connectionTypeId IN (${Array(targumTypeCount).fill('?').join(',')})
  `,

  /**
   * Reverse targum book lookup: find distinct targum books that link to any line in the
   * given base book via a TARGUM-type connection. Used to populate the תרגומים section
   * of the static commentary filter panel.
   * Bind order: targetBookId, targumTypeId1, targumTypeId2, ...
   */
  GET_TARGUM_BOOKS_BY_REVERSE_TARGUM_LOOKUP: (targumTypeCount: number) => `
    SELECT DISTINCT l.sourceBookId
    FROM link l
    WHERE l.targetBookId = ?
      AND l.connectionTypeId IN (${Array(targumTypeCount).fill('?').join(',')})
  `,

  /** All available connection type IDs and names */
  GET_ALL_CONNECTION_TYPES: `
    SELECT id, name
    FROM connection_type
  `,

  /** Distinct static filter books for one source book using link.sourceBookId.
   *  The count parameter controls how many connection type ID placeholders are generated —
   *  the caller passes all IDs that map to SOURCE, TARGUM, or COMMENTARY (including any
   *  new DB-side aliases like SUPER_COMMENTARY, PARSHANUT, MIDRASH). */
  GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK: (count: number) => `
    SELECT DISTINCT l.targetBookId, l.connectionTypeId
    FROM link l
    WHERE l.sourceBookId = ?
      AND l.connectionTypeId IN (${Array(count).fill('?').join(',')})
  `,

  /** Next line in main book (by lineIndex) that has a link to a given commentary book */
  GET_NEXT_SECTION_WITH_COMMENTARY: `
    SELECT ln.id, ln.lineIndex
    FROM line ln
    JOIN link lk ON lk.sourceLineId = ln.id
    WHERE ln.bookId = ?
      AND lk.targetBookId = ?
      AND ln.lineIndex > ?
    ORDER BY ln.lineIndex ASC
    LIMIT 1
  `,

  /** Previous line in main book (by lineIndex) that has a link to a given commentary book */
  GET_PREV_SECTION_WITH_COMMENTARY: `
    SELECT ln.id, ln.lineIndex
    FROM line ln
    JOIN link lk ON lk.sourceLineId = ln.id
    WHERE ln.bookId = ?
      AND lk.targetBookId = ?
      AND ln.lineIndex < ?
    ORDER BY ln.lineIndex DESC
    LIMIT 1
  `,

  /**
   * Find the first TOC entry for a book whose text starts with a given prefix.
   * Used by daf yomi navigation to locate a specific daf without loading the full TOC.
   * Bind: bookId, textPrefix (e.g. 'דף יח')
   */
  GET_TOC_ENTRY_BY_TEXT_PREFIX: `
    SELECT te.id, l.lineIndex
    FROM tocEntry te
    JOIN tocText tt ON tt.id = te.textId
    LEFT JOIN line l ON l.id = te.lineId
    WHERE te.bookId = ?
      AND tt.text LIKE ?
    ORDER BY te.id ASC
    LIMIT 1
  `,

  /** First default commentator for a book (lowest position) */
  GET_DEFAULT_COMMENTATORS: `
    SELECT commentatorBookId
    FROM default_commentator
    WHERE bookId = ?
    ORDER BY position ASC
  `,

  /** Next toc entry (by lineIndex) whose section contains a link to a given commentary book */
  HAS_COMMENTARY_IN_RANGE: `
    SELECT 1
    FROM line ln
    JOIN link lk ON lk.sourceLineId = ln.id
    WHERE ln.bookId = ?
      AND lk.targetBookId = ?
      AND ln.lineIndex >= ?
      AND ln.lineIndex < ?
    LIMIT 1
  `,

  /**
   * Given a batch of TOC section ranges (sectionStart, sectionEnd pairs) find the
   * one with the smallest sectionStart that contains at least one link to commentaryBookId.
   * Used by next-section TOC navigation to replace the serial HAS_COMMENTARY_IN_RANGE loop.
   * Bind order: interleaved (sectionStart, sectionEnd) pairs, then mainBookId, commentaryBookId.
   */
  GET_NEXT_TOC_SECTION_WITH_COMMENTARY: (count: number) => `
    WITH ranges(sectionStart, sectionEnd) AS (VALUES ${Array(count).fill('(?, ?)').join(', ')})
    SELECT ranges.sectionStart
    FROM ranges
    JOIN line ln ON ln.bookId = ? AND ln.lineIndex >= ranges.sectionStart AND ln.lineIndex < ranges.sectionEnd
    JOIN link lk ON lk.sourceLineId = ln.id AND lk.targetBookId = ?
    ORDER BY ranges.sectionStart ASC
    LIMIT 1
  `,

  /**
   * Same as GET_NEXT_TOC_SECTION_WITH_COMMENTARY but returns the candidate with the
   * largest sectionStart — used for prev-section TOC navigation.
   * Bind order: interleaved (sectionStart, sectionEnd) pairs, then mainBookId, commentaryBookId.
   */
  GET_PREV_TOC_SECTION_WITH_COMMENTARY: (count: number) => `
    WITH ranges(sectionStart, sectionEnd) AS (VALUES ${Array(count).fill('(?, ?)').join(', ')})
    SELECT ranges.sectionStart
    FROM ranges
    JOIN line ln ON ln.bookId = ? AND ln.lineIndex >= ranges.sectionStart AND ln.lineIndex < ranges.sectionEnd
    JOIN link lk ON lk.sourceLineId = ln.id AND lk.targetBookId = ?
    ORDER BY ranges.sectionStart DESC
    LIMIT 1
  `,

  /**
   * Given a source line id and a target book id, return the target line id and
   * its lineIndex — used to resolve the opening position when a direct link
   * already exists on the current top line.
   * Returns at most one row (first link found for that book).
   */
  GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK: `
    SELECT lk.targetLineId, ln.lineIndex
    FROM link lk
    JOIN line ln ON ln.id = lk.targetLineId
    WHERE lk.sourceLineId = ?
      AND lk.targetBookId = ?
    LIMIT 1
  `,

  // ── Dictionary seforim lookups ────────────────────────────────────────────

  /**
   * Look up book IDs by title LIKE pattern.
   * Used by dictionarySeforimDb.ts to resolve book IDs for מצודת ציון, מלבי"ם, etc.
   * Bind: titlePattern (e.g. '%מצודת ציון%')
   */
  GET_BOOK_IDS_BY_TITLE_PATTERN: `
    SELECT id FROM book WHERE title LIKE ?
  `,

  /**
   * Look up a single book ID by exact title.
   * Used by dictionarySeforimDb.ts for ספר הערוך (where LIKE would match unrelated books).
   * Bind: title (exact string)
   */
  GET_BOOK_ID_BY_EXACT_TITLE: `
    SELECT id FROM book WHERE title = ?
  `,

  /**
   * Fetch lines from a set of books whose content matches a LIKE pattern.
   * Used by dictionarySeforimDb.ts to find bold-tagged headword lines in
   * מצודת ציון and מלבי"ם באור המילות.
   * Bind: bookId1, bookId2, ..., contentPattern (e.g. '<b>TERM</b>%')
   */
  GET_LINES_WITH_CONTENT_PATTERN_FOR_BOOKS: (bookCount: number) => `
    SELECT l.content, b.title, b.id AS bookId, l.id AS lineId, l.lineIndex
    FROM line l JOIN book b ON b.id = l.bookId
    WHERE l.bookId IN (${Array(bookCount).fill('?').join(', ')})
      AND l.content LIKE ?
    LIMIT 50
  `,

  /**
   * Fetch lines from a single book whose content matches either of two LIKE patterns.
   * Used by dictionarySeforimDb.ts to find <big>-tagged headword lines in
   * מחברת מנחם and ספר הערוך (with/without trailing space before the closing tag).
   * Bind: bookId, pattern1, pattern2
   */
  GET_LINES_WITH_EITHER_CONTENT_PATTERN: `
    SELECT id, lineIndex, content FROM line
    WHERE bookId = ? AND (content LIKE ? OR content LIKE ?)
    ORDER BY lineIndex LIMIT 20
  `,

  /**
   * Fetch a single line from a book by its lineIndex.
   * Used by dictionarySeforimDb.ts to retrieve the definition line that immediately
   * follows a מחברת מנחם headword line.
   * Bind: bookId, lineIndex
   */
  GET_LINE_BY_BOOK_AND_LINE_INDEX: `
    SELECT id, lineIndex, content FROM line WHERE bookId = ? AND lineIndex = ?
  `,

} as const
