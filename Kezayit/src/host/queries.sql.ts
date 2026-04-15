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
    SELECT b.id, b.categoryId, b.title,
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
    SELECT totalLines,
           hasTargumConnection, hasReferenceConnection, hasSourceConnection,
           hasCommentaryConnection, hasOtherConnection
    FROM book
    WHERE id = ?
  `,

  /** Multiple books by id list — used by commentary loader */
  GET_BOOKS_BY_IDS: (count: number) => `
    SELECT id, title
    FROM book
    WHERE id IN (${Array(count).fill('?').join(',')})
  `,

  /** Multiple lines by id list — used by commentary loader */
  GET_LINES_BY_IDS: (count: number) => `
    SELECT id, lineIndex, content
    FROM line
    WHERE id IN (${Array(count).fill('?').join(',')})
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

  /** Get lineIndex and bookId from a line id — used after bloom search to open the book at the right position */
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
      SELECT a.lineId, a.text, a.depth,
             MAX(a.depth) OVER (PARTITION BY a.lineId) AS maxDepth,
             b.title AS bookTitle
      FROM ancestors a
      JOIN book b ON b.id = a.bookId
    )
    SELECT lineId, group_concat(text, ' > ') AS tocPath
    FROM (
      SELECT lineId, text
      FROM ordered
      WHERE NOT (depth = maxDepth AND text = bookTitle)
      ORDER BY lineId, depth DESC
    )
    GROUP BY lineId
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

  /** First default commentator for a book (lowest position) */
  GET_DEFAULT_COMMENTATORS: `
    SELECT commentatorBookId
    FROM default_commentator
    WHERE bookId = ?
    ORDER BY position ASC
  `,

  // ── Kezayit Dictionary (public/dicts/kezayit_dictionary.db) ─────────────────

  /**
   * Search Aramaic entries by headword prefix or exact match.
   * Returns senses with their first definition text.
   * Params: [term, prefixPattern, term]  e.g. ['אבא', 'אבא%', 'אבא']
   */
  SEARCH_DICT_SENSES: `
    SELECT s.id, s.headword, s.nikud, NULL AS pos, src.label AS source_label,
           d.text AS definition
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword = ? OR s.headword LIKE ?
    ORDER BY
      CASE WHEN s.headword = ? THEN 0 ELSE 1 END,
      length(s.headword),
      s.headword
    LIMIT 100
  `,

  /**
   * Autosuggest: one row per (headword, source) combination, with all definitions
   * for that source concatenated. Uses contains match (%word%) — negligible perf
   * difference vs prefix at this data size, and the index is still used.
   * Ordered: prefix matches first, then alphabetical, then by source.
   * Params: [containsPattern, term]  e.g. ['%דיל%', 'דיל']
   */
  DICT_SUGGEST: `
    SELECT s.headword, src.label AS source_label,
           d.text AS definition
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword LIKE ?
    GROUP BY s.headword, s.source_id
    ORDER BY
      CASE WHEN s.headword LIKE ? THEN 0 ELSE 1 END,
      s.headword,
      s.source_id
    LIMIT 50
  `,

  /**
   * All senses for a headword, with source label joined.
   * pos/binyan/shoresh/ktiv_male are not stored in kezayit_dictionary.db (always NULL) —
   * returned as NULL literals so the result shape matches the wikidict version.
   */
  GET_DICT_SENSES_FOR_WORD: `
    SELECT s.id, s.headword, s.nikud,
           NULL AS pos, NULL AS binyan, NULL AS shoresh, NULL AS ktiv_male,
           src.label AS source_label, s.sense_order
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    WHERE s.headword = ?
    ORDER BY s.sense_order
  `,

  /**
   * Bulk fetch all definitions for a set of sense ids.
   * Pass the ids array as the single param — the function expands the placeholders.
   * Returns: id, sense_id, text, layer, def_order
   */
  GET_DICT_ALL_DEFINITIONS: (ids: number[]) => `
    SELECT id, sense_id, text, def_order
    FROM definition
    WHERE sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY sense_id, def_order
  `,

  /**
   * Bulk fetch all examples for all definitions belonging to a set of sense ids.
   * Returns: definition_id, text, source
   */
  GET_DICT_ALL_EXAMPLES: (ids: number[]) => `
    SELECT e.definition_id, e.text, e.source
    FROM example e
    JOIN definition d ON d.id = e.definition_id
    WHERE d.sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY e.definition_id, e.id
  `,

  /**
   * Bulk fetch all section items for a set of sense ids.
   * Returns: sense_id, section_name, item_text, item_order
   */
  GET_DICT_ALL_SECTIONS: (ids: number[]) => `
    SELECT s.sense_id, s.name AS section_name, si.text AS item_text, si.item_order
    FROM section s
    JOIN section_item si ON si.section_id = s.id
    WHERE s.sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY s.sense_id, s.id, si.item_order
  `,

  // ── Dictionary (old schema — main app DB) ────────────────────────────────────

  /**
   * All books under the reference/dictionary top-level categories:
   * cat 75 (מילונים וספרי יעץ) and cat 1220 (ספרות עזר), including all their sub-categories.
   * Returns books with author names, grouped by sub-category.
   */
  GET_DICTIONARY_BOOKS: `
    SELECT b.id, b.title, b.totalLines, b.categoryId,
           c.title AS categoryTitle,
           group_concat(a.name, ', ') AS authors
    FROM book b
    JOIN category c ON c.id = b.categoryId
    LEFT JOIN book_author ba ON ba.bookId = b.id
    LEFT JOIN author a ON a.id = ba.authorId
    WHERE b.categoryId IN (
      SELECT id FROM category WHERE id IN (75, 1220) OR parentId IN (75, 1220)
    )
    GROUP BY b.id
    ORDER BY c.parentId, c.id, b.orderIndex
  `,

  /**
   * Search dict_entry by headword.
   * Ordering: exact → prefix of query → query contains headword → headword is root of query
   * "Root match": headword is a prefix of the search term (e.g. ברא matches בראשית)
   * Params: [containsPattern, term, prefixPattern, prefixPattern, rootPattern]
   *   containsPattern = '%term%'
   *   term            = 'term'        (exact)
   *   prefixPattern   = 'term%'       (headword starts with term)
   *   rootPattern     = 'term%'       (reused for ORDER BY)
   * The WHERE clause uses LIKE '%term%' OR the term LIKE headword||'%'
   * so roots shorter than the query are also returned.
   */
  SEARCH_DICTIONARY_ENTRIES: `
    SELECT e.id, e.bookId, e.lineIndex, e.headword, e.nikud, e.definition, e.type, e.source,
      CASE WHEN e.headword = ?                                                          THEN 0
           WHEN e.headword LIKE ?                                                       THEN 1
           WHEN e.type = 'aramaic' AND length(e.headword) >= 3
                AND ? LIKE (e.headword || '%')                                          THEN 2
           ELSE                                                                              3
      END AS matchTier
    FROM entry e
    WHERE e.headword LIKE ?
       OR (e.type = 'aramaic' AND length(e.headword) >= 3 AND ? LIKE (e.headword || '%'))
    ORDER BY
      matchTier,
      CASE WHEN e.type = 'aramaic' AND length(e.headword) >= 3
                AND ? LIKE (e.headword || '%') THEN length(e.headword) ELSE 0 END DESC,
      e.headword,
      e.source
    LIMIT 200
  `,

  /**
   * Fetch the full content of a dictionary entry.
   * For ספר הערוך / הפלאה שבערכין / אוצר לעזי רשי: the entry is a single line.
   * For ספר השרשים: the entry is 2 lines (h3 header + content line).
   * Returns up to 2 lines starting at lineIndex.
   */
  GET_DICTIONARY_ENTRY_LINES: `
    SELECT lineIndex, content
    FROM line
    WHERE bookId = ? AND lineIndex >= ? AND lineIndex < ?
    ORDER BY lineIndex
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

  // ── Wiktionary offline DB (public/wikidictionary.db) ─────────────────────────

  /**
   * Autosuggest for wikidictionary.db.
   * Returns one row per headword (senses are merged at display time).
   * Matches headword OR ktiv_male (alternative spelling).
   * Ordered: prefix matches first, then alphabetical.
   * Params: [containsPattern, containsPattern, prefixPattern, prefixPattern]
   */
  WIKIDICT_SUGGEST: `
    SELECT s.headword,
           d.text AS definition
    FROM sense s
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword LIKE ? OR s.ktiv_male LIKE ?
    GROUP BY s.headword
    ORDER BY
      CASE WHEN s.headword LIKE ? OR s.ktiv_male LIKE ? THEN 0 ELSE 1 END,
      s.headword
    LIMIT 50
  `,

  /**
   * All senses for a headword in wikidictionary.db.
   * Returns one row per sense with source label and pos name joined.
   */
  GET_WIKIDICT_SENSES_FOR_WORD: `
    SELECT s.id, s.headword, s.nikud, p.name AS pos, s.binyan, s.shoresh, s.ktiv_male,
           src.label AS source_label, s.sense_order
    FROM sense s
    JOIN source src ON src.id = s.source_id
    LEFT JOIN pos p ON p.id = s.pos_id
    WHERE s.headword = ? OR s.ktiv_male = ?
    ORDER BY s.sense_order
  `,

  /**
   * Bulk fetch all definitions for a set of sense ids from wikidictionary.db.
   * Returns: id, sense_id, text, def_order
   */
  GET_WIKIDICT_ALL_DEFINITIONS: (ids: number[]) => `
    SELECT id, sense_id, text, def_order
    FROM definition
    WHERE sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY sense_id, def_order
  `,

  /**
   * Bulk fetch all examples for a set of sense ids from wikidictionary.db.
   * Returns: definition_id, text, source
   */
  GET_WIKIDICT_ALL_EXAMPLES: (ids: number[]) => `
    SELECT e.definition_id, e.text, es.name AS source
    FROM example e
    JOIN definition d ON d.id = e.definition_id
    LEFT JOIN example_source es ON es.id = e.source_id
    WHERE d.sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY e.definition_id, e.id
  `,

  /**
   * Bulk fetch all section items for a set of sense ids from wikidictionary.db.
   * Returns: sense_id, section_name, item_text, item_order
   */
  GET_WIKIDICT_ALL_SECTIONS: (ids: number[]) => `
    SELECT s.sense_id, sn.name AS section_name, si.text AS item_text, si.item_order
    FROM section s
    JOIN section_name sn ON sn.id = s.name_id
    JOIN section_item si ON si.section_id = s.id
    WHERE s.sense_id IN (${ids.map(() => '?').join(',')})
    ORDER BY s.sense_id, s.id, si.item_order
  `,

} as const
