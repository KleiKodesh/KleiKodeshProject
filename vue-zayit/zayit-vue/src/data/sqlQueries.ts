export const SqlQueries = {
  getAllCategories: `
    SELECT DISTINCT 
      Id,
      ParentId,
      Title,
      Level
    FROM category
    ORDER BY Level, Id
  `,

  getAllBooks: `
    SELECT 
      Id,
      CategoryId,
      Title,
      HeShortDesc,
      OrderIndex,
      TotalLines,
      HasTargumConnection,
      HasReferenceConnection,
      HasCommentaryConnection,
      HasOtherConnection
    FROM book
    ORDER BY CategoryId
  `,

  getToc: (docId: number) => `
    SELECT DISTINCT
      te.id,
      te.bookId,
      te.parentId,
      te.textId,
      te.level,
      te.lineId,
      te.isLastChild,
      te.hasChildren,
      tt.text,
      l.lineIndex
    FROM tocEntry AS te
    LEFT JOIN tocText AS tt ON te.textId = tt.id
    LEFT JOIN line AS l ON l.id = te.lineId
    WHERE te.bookId = ${docId}
  `,

  getLinks: (lineId: number) => `
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
    WHERE l.sourceLineId = ${lineId}
    ORDER BY l.connectionTypeId, bk.title
  `,

  getLineContent: (bookId: number, lineIndex: number) => `
    SELECT content 
    FROM line 
    WHERE bookId = ${bookId} AND lineIndex = ${lineIndex}
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
  `,

  getLineRange: (bookId: number, start: number, end: number) => `
    SELECT lineIndex, content 
    FROM line 
    WHERE bookId = ${bookId} 
      AND lineIndex >= ${start} 
      AND lineIndex <= ${end} 
    ORDER BY lineIndex
  `,

  searchLines: (bookId: number, searchTerm: string) => `
    SELECT lineIndex, content 
    FROM line 
    WHERE bookId = ${bookId} 
      AND content LIKE '%${searchTerm}%'
    ORDER BY lineIndex
  `
}
