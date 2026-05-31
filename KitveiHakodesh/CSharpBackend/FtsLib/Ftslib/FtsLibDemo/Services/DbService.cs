using FtsLib.Misc;
using System;
using System.Collections.Generic;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Wraps ZayitDb and provides additional query methods for the demo app.
    /// ZayitDb refers to the external Zayit/Otzaria app's database — not this app's old name.
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

            foreach (var (lineId, content, bookTitle) in _db.FetchSearchResults(ids))
            {
                rows.Add(new SearchResultItem(lineId, bookTitle, content));
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
    }
}
