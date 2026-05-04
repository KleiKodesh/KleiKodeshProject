using FtsLib.Misc;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Wraps ZayitDb and provides additional query methods for the demo app.
    /// </summary>
    public sealed class DbService : IDbService
    {
        private ZayitDb _db;
        private bool _disposed;

        public void Open(string dbPath)
        {
            Close();
            _db = new ZayitDb(dbPath);
        }

        public long CountLines()
        {
            EnsureOpen();
            return _db.CountLines();
        }

        public string GetLineContent(int id)
        {
            EnsureOpen();
            return _db.GetLineContent(id);
        }

        public IEnumerable<(int Id, string Content)> ReadLines(int limit = 0)
        {
            EnsureOpen();
            return _db.ReadLines(limit);
        }

        public List<SearchResultItem> FetchSearchResults(List<int> ids)
        {
            EnsureOpen();
            var rows = new List<SearchResultItem>(ids.Count);

            var results = _db.FetchSearchResults(ids);
            foreach (var (lineId, lineIndex, heRef, content, bookTitle) in results)
            {
                string reference = heRef ?? $"שורה {lineIndex}";
                string snippet   = StripHtml(content);
                rows.Add(new SearchResultItem(lineId, bookTitle, reference, snippet));
            }

            return rows;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void Close()
        {
            _db?.Dispose();
            _db = null;
        }

        private void EnsureOpen()
        {
            if (_db == null)
                throw new InvalidOperationException("DbService: database is not open.");
        }

        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var  sb    = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
