/**
 * All SQL query strings live here.
 * Import from this file — never write inline SQL elsewhere.
 */

export const SQL = {

  // ── Categories ──────────────────────────────────────────────────────────────

  /** All top-level categories (level 0), ordered */
  GET_ROOT_CATEGORIES: `
    SELECT id, title, level, orderIndex
    FROM category
    WHERE parentId IS NULL
    ORDER BY orderIndex
  `,

  /** Direct children of a given category */
  GET_CHILD_CATEGORIES: `
    SELECT id, title, level, orderIndex
    FROM category
    WHERE parentId = ?
    ORDER BY orderIndex
  `,

  /** All descendants of a category via closure table */
  GET_CATEGORY_DESCENDANTS: `
    SELECT c.id, c.parentId, c.title, c.level, c.orderIndex
    FROM category c
    JOIN category_closure cc ON cc.descendantId = c.id
    WHERE cc.ancestorId = ?
    ORDER BY c.level, c.orderIndex
  `,

  /** All categories flat — used to build the full tree in memory once */
  GET_ALL_CATEGORIES: `
    SELECT id, parentId, title, level, orderIndex
    FROM category
    ORDER BY level, orderIndex
  `,

  /** All books flat — attached to tree nodes by categoryId */
  GET_ALL_BOOKS: `
    SELECT id, categoryId, title, heShortDesc, orderIndex
    FROM book
    ORDER BY orderIndex
  `,

  // ── Books ────────────────────────────────────────────────────────────────────

  /** All books in a category, ordered */
  GET_BOOKS_BY_CATEGORY: `
    SELECT id, title, heShortDesc, orderIndex, totalLines,
           isBaseBook, hasTargumConnection, hasReferenceConnection,
           hasSourceConnection, hasCommentaryConnection, hasOtherConnection,
           hasAltStructures, externalLibraryId
    FROM book
    WHERE categoryId = ?
    ORDER BY orderIndex
  `,

  /** Single book by id */
  GET_BOOK_BY_ID: `
    SELECT b.*,
           s.name AS sourceName
    FROM book b
    LEFT JOIN source s ON s.id = b.sourceId
    WHERE b.id = ?
  `,

  /** Authors for a book */
  GET_BOOK_AUTHORS: `
    SELECT a.id, a.name
    FROM author a
    JOIN book_author ba ON ba.authorId = a.id
    WHERE ba.bookId = ?
  `,

  /** Topics for a book */
  GET_BOOK_TOPICS: `
    SELECT t.id, t.name
    FROM topic t
    JOIN book_topic bt ON bt.topicId = t.id
    WHERE bt.bookId = ?
  `,

  /** Publication places for a book */
  GET_BOOK_PUB_PLACES: `
    SELECT p.id, p.name
    FROM pub_place p
    JOIN book_pub_place bp ON bp.pubPlaceId = p.id
    WHERE bp.bookId = ?
  `,

  /** Publication dates for a book */
  GET_BOOK_PUB_DATES: `
    SELECT d.id, d.date
    FROM pub_date d
    JOIN book_pub_date bd ON bd.pubDateId = d.id
    WHERE bd.bookId = ?
  `,

  // ── TOC ──────────────────────────────────────────────────────────────────────

  /** All TOC entries for a book, flat — build tree in memory */
  GET_ALL_TOC_ENTRIES: `
    SELECT te.id, te.parentId, te.level, te.lineId, te.isLastChild, te.hasChildren,
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
    SELECT ae.id, ae.parentId, ae.level, ae.lineId, ae.isLastChild, ae.hasChildren,
           tt.text, l.lineIndex
    FROM alt_toc_entry ae
    JOIN tocText tt ON tt.id = ae.textId
    LEFT JOIN line l ON l.id = ae.lineId
    WHERE ae.structureId = ?
    ORDER BY ae.id
  `,

  // ── Lines ────────────────────────────────────────────────────────────────────

  /** Lines for a book in order */
  GET_LINES_BY_BOOK: `
    SELECT id, lineIndex, content, tocEntryId
    FROM line
    WHERE bookId = ?
    ORDER BY lineIndex
  `,

  /** A page of lines (for virtual scrolling) */
  GET_LINES_PAGED: `
    SELECT id, lineIndex, content, tocEntryId
    FROM line
    WHERE bookId = ?
    ORDER BY lineIndex
    LIMIT ? OFFSET ?
  `,

  /** Single line by id */
  GET_LINE_BY_ID: `
    SELECT id, lineIndex, content, tocEntryId
    FROM line
    WHERE id = ?
  `,

  // ── Links ────────────────────────────────────────────────────────────────────

  /** All links where a line is the source */
  GET_LINKS_FOR_SOURCE_LINE: `
    SELECT l.id, l.targetBookId, l.targetLineId, ct.name AS connectionType
    FROM link l
    JOIN connection_type ct ON ct.id = l.connectionTypeId
    WHERE l.sourceLineId = ?
  `,

  /** All links where a line is the target */
  GET_LINKS_FOR_TARGET_LINE: `
    SELECT l.id, l.sourceBookId, l.sourceLineId, ct.name AS connectionType
    FROM link l
    JOIN connection_type ct ON ct.id = l.connectionTypeId
    WHERE l.targetLineId = ?
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

} as const
