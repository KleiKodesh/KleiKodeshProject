using System;

namespace Zayit.SeforimDb
{
    /// <summary>
    /// SQL Query Definitions
    /// Mirrors sqlQueries.ts from zayit-vue for consistency
    /// </summary>
    public static class SqlQueries
    {
        public static string GetAllCategories => @"
            SELECT DISTINCT 
              Id,
              ParentId,
              Title,
              Level
            FROM category
            ORDER BY Level, Id
        ";

        public static string GetAllBooks => @"
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
        ";

        public static string GetToc(int bookId) => $@"
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
            WHERE te.bookId = {bookId}
        ";

        public static string GetLinks(int lineId) => $@"
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
            WHERE l.sourceLineId = {lineId}
            ORDER BY l.connectionTypeId, bk.title
        ";

        public static string GetBookLineCount(int bookId) => $@"
            SELECT TotalLines as totalLines
            FROM book 
            WHERE Id = {bookId}
        ";

        public static string GetLineContent(int bookId, int lineIndex) => $@"
            SELECT content 
            FROM line 
            WHERE bookId = {bookId} AND lineIndex = {lineIndex}
        ";

        public static string GetLineId(int bookId, int lineIndex) => $@"
            SELECT id 
            FROM line 
            WHERE bookId = {bookId} AND lineIndex = {lineIndex}
        ";

        public static string GetLineRange(int bookId, int start, int end) => $@"
            SELECT lineIndex, content 
            FROM line 
            WHERE bookId = {bookId} 
              AND lineIndex >= {start} 
              AND lineIndex <= {end} 
            ORDER BY lineIndex
        ";

        public static string SearchLines(int bookId, string searchTerm) => $@"
            SELECT lineIndex, content 
            FROM line 
            WHERE bookId = {bookId} 
              AND content LIKE '%{searchTerm}%'
            ORDER BY lineIndex
        ";
    }
}
