using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace FtsLib.Core
{
    /// <summary>
    /// Searches a persisted index (postings.bin + index.db) without loading
    /// everything into RAM. Each search reads only the posting bytes for the
    /// queried terms.
    /// </summary>
    public sealed class IndexReader : IndexPaths, IDisposable
    {
        private readonly FileStream      _postings;
        private readonly SQLiteConnection _conn;
        private readonly SQLiteCommand   _lookup;

        public IndexReader(string indexPath) : base(indexPath)
        {
            _postings = new FileStream(PostingsPath, FileMode.Open,
                                       FileAccess.Read, FileShare.Read,
                                       bufferSize: 64 * 1024);

            var connStr = $"Data Source={MetaDbPath};Version=3;Read Only=True;";
            _conn = new SQLiteConnection(connStr);
            _conn.Open();

            _lookup = _conn.CreateCommand();
            _lookup.CommandText =
                "SELECT offset, length, count FROM term_index WHERE term = @t";
            _lookup.Parameters.Add("@t", System.Data.DbType.String);
        }

        /// <summary>
        /// Returns line IDs that contain ALL of the supplied terms (AND semantics).
        /// Reads posting bytes from disk on demand — only the queried terms are loaded.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = new List<string>(terms);

            // Look up all terms, bail early if any is missing
            var entries = new List<(string term, long offset, int length, int count)>(termList.Count);
            foreach (var term in termList)
            {
                _lookup.Parameters["@t"].Value = term;
                using (var r = _lookup.ExecuteReader())
                {
                    if (!r.Read()) return Enumerable.Empty<int>(); // term not in index
                    entries.Add((term, r.GetInt64(0), r.GetInt32(1), r.GetInt32(2)));
                }
            }

            // Sort rarest first
            entries.Sort((a, b) => a.count.CompareTo(b.count));

            // Load posting bytes for each term
            var iters = new PostingIterator[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var (_, offset, length, _) = entries[i];
                var buf = new byte[length];
                _postings.Seek(offset, SeekOrigin.Begin);
                _postings.Read(buf, 0, length);
                iters[i] = new PostingIterator(buf, length, null, 0);
            }

            return MergeIntersect(iters);
        }

        public int GetTermCount(string term)
        {
            _lookup.Parameters["@t"].Value = term;
            using (var r = _lookup.ExecuteReader())
                return r.Read() ? r.GetInt32(2) : 0;
        }

        private static IEnumerable<int> MergeIntersect(PostingIterator[] iters)
        {
            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            while (!iters[0].IsDone)
            {
                int  candidate = iters[0].Current;
                bool match     = true;

                for (int i = 1; i < iters.Length; i++)
                {
                    if (!iters[i].SkipTo(candidate))
                        yield break;

                    if (iters[i].Current != candidate)
                    {
                        int newTarget = iters[i].Current;
                        if (!iters[0].SkipTo(newTarget))
                            yield break;
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    yield return candidate;
                    if (!iters[0].MoveNext())
                        yield break;
                }
            }
        }

        public void Dispose()
        {
            _lookup?.Dispose();
            _conn?.Dispose();
            _postings?.Dispose();
        }
    }
}
