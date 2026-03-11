using System;
using System.Collections.Generic;

namespace Zayit.Services
{
    public class DbService
    {
        private readonly DbQueries _db;

        public DbService(DbQueries db) => _db = db;

        public object GetTree(string cq, string bq)
        {
            try
            {
                return new { categoriesFlat = _db.ExecuteQuery(cq), booksFlat = _db.ExecuteQuery(bq) };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetTree: {ex.Message}");
                return new { categoriesFlat = new object[0], booksFlat = new object[0] };
            }
        }

        public object GetConnectionTypes(string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetConnectionTypes: {ex.Message}");
                return new object[0];
            }
        }

        public object GetToc(int bookId, string q)
        {
            try
            {
                return new { tocEntriesFlat = _db.ExecuteQuery(q) };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetToc for bookId {bookId}: {ex.Message}");
                return new { tocEntriesFlat = new object[0] };
            }
        }

        public int GetTotalLines(int bookId, string q)
        {
            try
            {
                var result = _db.ExecuteQuery(q);
                if (result is Array arr && arr.Length > 0)
                {
                    var row = arr.GetValue(0);
                    if (row is IDictionary<string, object> dict)
                    {
                        object v1;
                        if (dict.TryGetValue("totalLines", out v1) || dict.TryGetValue("TotalLines", out v1) || dict.TryGetValue("TOTALLINES", out v1))
                            return Convert.ToInt32(v1);
                        foreach (var kvp in dict)
                        {
                            int n;
                            if (int.TryParse(kvp.Value != null ? kvp.Value.ToString() : null, out n))
                                return n;
                        }
                    }
                    var prop = row.GetType().GetProperty("totalLines");
                    if (prop == null)
                        prop = row.GetType().GetProperty("TotalLines");
                    if (prop == null)
                        prop = row.GetType().GetProperty("TOTALLINES");
                    if (prop != null)
                        return Convert.ToInt32(prop.GetValue(row));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetTotalLines for bookId {bookId}: {ex.Message}");
                return 0;
            }
        }

        public object GetLineId(int bookId, int idx, string q)
        {
            try
            {
                var result = _db.ExecuteQuery(q);
                if (result is Array arr && arr.Length > 0)
                {
                    var row = arr.GetValue(0);
                    if (row is IDictionary<string, object> dict)
                    {
                        object v;
                        if (dict.TryGetValue("id", out v) || dict.TryGetValue("Id", out v) || dict.TryGetValue("ID", out v))
                            return Convert.ToInt32(v);
                        foreach (var kvp in dict)
                        {
                            int n;
                            if (int.TryParse(kvp.Value != null ? kvp.Value.ToString() : null, out n))
                                return n;
                        }
                    }
                    var prop = row.GetType().GetProperty("id");
                    if (prop == null)
                        prop = row.GetType().GetProperty("Id");
                    if (prop == null)
                        prop = row.GetType().GetProperty("ID");
                    if (prop != null)
                        return Convert.ToInt32(prop.GetValue(row));
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLineId for bookId {bookId}, idx {idx}: {ex.Message}");
                return null;
            }
        }

        public object GetLineContent(int bookId, int idx, string q)
        {
            try
            {
                var result = _db.ExecuteQuery(q);
                if (result is Array arr && arr.Length > 0)
                {
                    var row = arr.GetValue(0);
                    if (row is IDictionary<string, object> dict)
                    {
                        object v;
                        if (dict.TryGetValue("content", out v) || dict.TryGetValue("Content", out v))
                            return v;
                        foreach (var kvp in dict)
                        {
                            if (kvp.Value is string s)
                                return s;
                        }
                    }
                    var prop = row.GetType().GetProperty("content");
                    if (prop == null)
                        prop = row.GetType().GetProperty("Content");
                    if (prop != null)
                        return prop.GetValue(row);
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLineContent for bookId {bookId}, idx {idx}: {ex.Message}");
                return null;
            }
        }

        public object GetLineRange(int bookId, int start, int end, string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLineRange for bookId {bookId}, start {start}, end {end}: {ex.Message}");
                return new object[0];
            }
        }

        public object GetLinks(int lineId, string tabId, int bookId, string q, object[] p = null)
        {
            try
            {
                return _db.ExecuteQuery(q, p);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLinks for lineId {lineId}, bookId {bookId}: {ex.Message}");
                return new object[0];
            }
        }

        public object SearchLines(int bookId, string term, string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in SearchLines for bookId {bookId}, term '{term}': {ex.Message}");
                return new object[0];
            }
        }

        public object GetLineIdsByTocEntry(int tocEntryId, string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLineIdsByTocEntry for tocEntryId {tocEntryId}: {ex.Message}");
                return new object[0];
            }
        }

        public object GetLinesByIds(int bookId, object lineIds, string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLinesByIds for bookId {bookId}: {ex.Message}");
                return new object[0];
            }
        }

        public object GetLineIndexFromLineId(int lineId, string q)
        {
            try
            {
                return _db.ExecuteQuery(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in GetLineIndexFromLineId for lineId {lineId}: {ex.Message}");
                return null;
            }
        }

        public object DiagnoseDatabaseContent()
        {
            try
            {
                return new
                {
                    books = _db.ExecuteQuery("SELECT Id, Title, TotalLines FROM book LIMIT 10"),
                    lineCountForBook1 = _db.ExecuteQuery("SELECT COUNT(*) as lineCount FROM line WHERE bookId = 1"),
                    diagnosis = "Database content check completed"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in DiagnoseDatabaseContent: {ex.Message}");
                return new
                {
                    books = new object[0],
                    lineCountForBook1 = new object[0],
                    diagnosis = $"Database diagnosis failed: {ex.Message}"
                };
            }
        }

        public object ExecuteQuery(string q, object[] p = null)
        {
            try
            {
                return _db.ExecuteQuery(q, p);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbService] ERROR in ExecuteQuery: {ex.Message}");
                return new object[0];
            }
        }
    }
}