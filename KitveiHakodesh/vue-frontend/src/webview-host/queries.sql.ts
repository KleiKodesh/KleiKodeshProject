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
   * Get bookId directly from the line table for a batch of line ids.
   * Used as a fallback for lines that have no line_toc entry (e.g. custom books).
   */
  GET_BOOK_IDS_FOR_LINES: (count: number) => `
    SELECT id AS lineId, bookId
    FROM line
    WHERE id IN (${Array(count).fill('?').join(', ')})
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
    SELECT lineId, bookId, group_concat(text, ' ') AS tocPath
    FROM (
      SELECT lineId, bookId, text
      FROM ordered
      WHERE NOT (depth = maxDepth AND text = bookTitle)
      ORDER BY lineId, depth DESC
    )
    GROUP BY lineId, bookId
  `,

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

  /** All available connection type IDs and names */
  GET_ALL_CONNECTION_TYPES: `
    SELECT id, name
    FROM connection_type
  `,

  /** Distinct static filter books for one source book using link.sourceBookId */
  GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK: `
    SELECT DISTINCT l.targetBookId, l.connectionTypeId
    FROM link l
    WHERE l.sourceBookId = ?
      AND l.connectionTypeId IN (?, ?, ?)
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

} as const
