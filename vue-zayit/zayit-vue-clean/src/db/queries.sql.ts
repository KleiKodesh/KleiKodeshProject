/**
 * All SQL query strings live here.
 * Import from this file — never write inline SQL elsewhere.
 */

export const SQL = {

  // ── Categories ──────────────────────────────────────────────────────────────

  /** All categories flat — used to build the full tree in memory once */
  GET_ALL_CATEGORIES: `
    SELECT id, parentId, title, level, orderIndex
    FROM category
    ORDER BY level, orderIndex
  `,

  /** All books flat — attached to tree nodes by categoryId */
  GET_ALL_BOOKS: `
    SELECT id, categoryId, title
    FROM book
    ORDER BY orderIndex
  `,

  // ── Books ────────────────────────────────────────────────────────────────────

  /** Single book by id — only totalLines needed for virtual scroll init */
  GET_BOOK_BY_ID: `
    SELECT totalLines
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

  // ── Lines ────────────────────────────────────────────────────────────────────

  /** A page of lines (for virtual scrolling) */
  GET_LINES_PAGED: `
    SELECT id, lineIndex, content
    FROM line
    WHERE bookId = ?
    ORDER BY lineIndex
    LIMIT ? OFFSET ?
  `,

  // ── Links ────────────────────────────────────────────────────────────────────

  /** All links where a line is the source */
  GET_LINKS_FOR_SOURCE_LINE: `
    SELECT l.targetBookId, l.targetLineId, ct.name AS connectionType
    FROM link l
    JOIN connection_type ct ON ct.id = l.connectionTypeId
    WHERE l.sourceLineId = ?
  `,

  /** All links where source line is within a range (for toc-section commentary) */
  GET_LINKS_FOR_SOURCE_LINE_RANGE: (count: number) => `
    SELECT l.targetBookId, l.targetLineId, ct.name AS connectionType
    FROM link l
    JOIN connection_type ct ON ct.id = l.connectionTypeId
    WHERE l.sourceLineId IN (${Array(count).fill('?').join(',')})
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

} as const
