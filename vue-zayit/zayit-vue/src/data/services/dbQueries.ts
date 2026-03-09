export const SqlQueries = {
  getAllCategories: `
    SELECT DISTINCT 
      Id,
      ParentId,
      Title,
      OrderIndex
    FROM category
  `,

  getAllBooks: `
    SELECT 
      b.Id,
      b.CategoryId,
      b.Title,
      b.HeShortDesc,
      b.OrderIndex,
      b.TotalLines,
      b.HasTargumConnection,
      b.HasReferenceConnection,
      b.HasCommentaryConnection,
      b.HasOtherConnection,
      b.HasSourceConnection,
      dc.commentatorBookId as defaultCommentatorBookId,
      pd.date as pubDate
    FROM book b
    LEFT JOIN (
      SELECT bookId, commentatorBookId
      FROM default_commentator
      WHERE position = 0
    ) dc ON dc.bookId = b.Id
    LEFT JOIN book_pub_date bpd ON bpd.bookId = b.Id
    LEFT JOIN pub_date pd ON pd.id = bpd.pubDateId
    WHERE b.externalLibraryId IS NULL
  `,

  getToc: (docId: number) => `
    SELECT DISTINCT
      te.id,
      ${docId} as bookId,
      te.parentId,
      te.level,
      te.lineId,
      te.hasChildren,
      tt.text,
      l.lineIndex,
      0 as isAltToc
    FROM tocEntry AS te
    LEFT JOIN tocText AS tt ON te.textId = tt.id
    LEFT JOIN line AS l ON l.id = te.lineId
    WHERE te.bookId = ${docId}
    UNION ALL
    SELECT DISTINCT
      ate.id,
      ${docId} as bookId,
      ate.parentId,
      ate.level,
      ate.lineId,
      ate.hasChildren,
      tt.text,
      l.lineIndex,
      1 as isAltToc
    FROM alt_toc_entry AS ate
    JOIN alt_toc_structure AS ats ON ats.id = ate.structureId
    LEFT JOIN tocText AS tt ON ate.textId = tt.id
    LEFT JOIN line AS l ON l.id = ate.lineId
    WHERE ats.bookId = ${docId}
  `,

  getLinks: (lineId: number, connectionTypeId?: number) => ({
    query: `
      SELECT
        l.targetLineId,
        l.targetBookId,
        l.connectionTypeId,
        bk.title,
        ln.content,
        ln.lineIndex AS lineIndex
      FROM link l
      JOIN line ln ON ln.id = l.targetLineId
      JOIN book bk ON bk.id = l.targetBookId
      WHERE l.sourceLineId = ?
      ${connectionTypeId !== undefined ? 'AND l.connectionTypeId = ?' : ''}
        AND bk.externalLibraryId IS NULL
      ORDER BY bk.title
    `,
    params: connectionTypeId !== undefined ? [lineId, connectionTypeId] : [lineId]
  }),

  getLinkBookIds: (lineId: number, connectionTypeId?: number) => ({
    query: `
      SELECT DISTINCT l.targetBookId
      FROM link l
      JOIN book bk ON bk.id = l.targetBookId
      WHERE l.sourceLineId = ?
      ${connectionTypeId !== undefined ? 'AND l.connectionTypeId = ?' : ''}
        AND bk.externalLibraryId IS NULL
    `,
    params: connectionTypeId !== undefined ? [lineId, connectionTypeId] : [lineId]
  }),

  getLineContent: (bookId: number, lineIndex: number) => `
    SELECT l.content, lt.tocEntryId
    FROM line l
    LEFT JOIN line_toc lt ON l.id = lt.lineId
    WHERE l.bookId = ${bookId} AND l.lineIndex = ${lineIndex}
  `,

  getLineId: (bookId: number, lineIndex: number) => `
    SELECT id 
    FROM line 
    WHERE bookId = ${bookId} AND lineIndex = ${lineIndex}
  `,

  getBookLineCount: (bookId: number) => `
    SELECT TotalLines as totalLines
    FROM book 
    WHERE Id = ${bookId}
      AND externalLibraryId IS NULL
  `,

  getLineRange: (bookId: number, start: number, end: number) => `
    SELECT l.lineIndex, l.content, lt.tocEntryId
    FROM line l
    LEFT JOIN line_toc lt ON l.id = lt.lineId
    WHERE l.bookId = ${bookId} 
      AND l.lineIndex >= ${start} 
      AND l.lineIndex <= ${end} 
    ORDER BY l.lineIndex
  `,

  searchLines: (bookId: number, searchTerm: string) => `
    SELECT l.lineIndex, l.content, lt.tocEntryId
    FROM line l
    LEFT JOIN line_toc lt ON l.id = lt.lineId
    WHERE l.bookId = ${bookId} 
      AND l.content LIKE '%${searchTerm}%'
    ORDER BY l.lineIndex
  `,

  getConnectionTypes: `
    SELECT 
      id,
      name
    FROM connection_type
    ORDER BY name
  `,

  getLineIdsByTocEntry: (tocEntryId: number) => `
    SELECT lineId
    FROM line_toc
    WHERE tocEntryId = ${tocEntryId}
    ORDER BY lineId
  `,

  getLinesByIds: (bookId: number, lineIds: number[]) => `
    SELECT l.lineIndex, l.content, lt.tocEntryId
    FROM line l
    LEFT JOIN line_toc lt ON l.id = lt.lineId
    WHERE l.bookId = ${bookId}
      AND l.id IN (${lineIds.join(',')})
    ORDER BY l.lineIndex
  `,

  getLineIndexFromLineId: (lineId: number) => `
    SELECT lineIndex, bookId
    FROM line
    WHERE id = ${lineId}
  `,

  getTopicsForBooks: (bookIds: number[]) => {
    if (bookIds.length === 0) return { query: 'SELECT 1 WHERE 0', params: [] }
    const placeholders = bookIds.map(() => '?').join(',')
    return {
      query: `
        SELECT DISTINCT t.Id as id, t.Name as name
        FROM topic t
        JOIN book_topic bt ON bt.TopicId = t.Id
        WHERE bt.BookId IN (${placeholders})
        ORDER BY t.Name
      `,
      params: bookIds
    }
  },

  getBookTopics: (bookIds: number[]) => {
    if (bookIds.length === 0) return { query: 'SELECT 1 WHERE 0', params: [] }
    const placeholders = bookIds.map(() => '?').join(',')
    return {
      query: `
        SELECT BookId as bookId, TopicId as topicId
        FROM book_topic
        WHERE BookId IN (${placeholders})
      `,
      params: bookIds
    }
  },

  findNextLineWithCommentary: (bookId: number, startLineIndex: number, targetBookId: number, connectionTypeId?: number) => ({
    query: `
      SELECT l.lineIndex
      FROM line l
      WHERE l.bookId = ?
        AND l.lineIndex > ?
        AND EXISTS (
          SELECT 1 FROM link lk
          WHERE lk.sourceLineId = l.id
            AND lk.targetBookId = ?
            ${connectionTypeId !== undefined ? 'AND lk.connectionTypeId = ?' : ''}
        )
      ORDER BY l.lineIndex ASC
      LIMIT 1
    `,
    params: connectionTypeId !== undefined
      ? [bookId, startLineIndex, targetBookId, connectionTypeId]
      : [bookId, startLineIndex, targetBookId]
  }),

  findPreviousLineWithCommentary: (bookId: number, startLineIndex: number, targetBookId: number, connectionTypeId?: number) => ({
    query: `
      SELECT l.lineIndex
      FROM line l
      WHERE l.bookId = ?
        AND l.lineIndex < ?
        AND EXISTS (
          SELECT 1 FROM link lk
          WHERE lk.sourceLineId = l.id
            AND lk.targetBookId = ?
            ${connectionTypeId !== undefined ? 'AND lk.connectionTypeId = ?' : ''}
        )
      ORDER BY l.lineIndex DESC
      LIMIT 1
    `,
    params: connectionTypeId !== undefined
      ? [bookId, startLineIndex, targetBookId, connectionTypeId]
      : [bookId, startLineIndex, targetBookId]
  })
}