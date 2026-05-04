using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace FtsLib.Core
{
    /// <summary>
    /// Searches a persisted index (postings.dat + Meta.db).
    ///
    /// Three search modes:
    ///
    ///   Search(terms)         — AND: all terms must appear in the line
    ///   SearchOr(terms)       — OR:  any term must appear in the line
    ///   Search(groups)        — Mixed: each group is OR'd internally,
    ///                           all groups are AND'd together.
    ///                           e.g. (כי OR אשר) AND ביצחק
    ///
    /// Merge algorithms are provided by PostingMatcher — the same algorithms
    /// used by RamIndex, so both index types behave identically.
    /// </summary>
    public sealed class IndexReader : IndexPaths, IDisposable
    {
        private readonly FileStream       _postings;
        private readonly SQLiteConnection _conn;
        private readonly SQLiteCommand    _lookup;

        public IndexReader(string indexPath) : base(indexPath)
        {
            // Auto-recover if segments exist but postings.dat/Meta.db are missing or incomplete.
            // This handles the case where the user opens a reader on an index whose previous
            // build was interrupted mid-commit.
            string segDir = Path.Combine(IndexPath, "segments");
            if (Directory.Exists(segDir) &&
                (Directory.GetFiles(segDir, "seg_*.dat").Length > 0 ||
                 File.Exists(Path.Combine(segDir, "wal.log"))))
            {
                Console.WriteLine("[IndexReader] Incomplete index detected — running crash recovery...");
                var store = new SegmentStore(segDir);
                store.Recover(PostingsPath, MetaDbPath);
                Console.WriteLine("[IndexReader] Recovery complete.");
            }

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

        // ── AND search ───────────────────────────────────────────────

        /// <summary>
        /// Returns line IDs that contain ALL of the supplied terms (AND semantics).
        /// Rarest term drives the outer loop; PostingMatcher.Intersect handles the merge.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = new List<string>(terms);

            var entries = new List<(string term, long offset, int length, int count)>(termList.Count);
            foreach (var term in termList)
            {
                _lookup.Parameters["@t"].Value = term;
                using (var r = _lookup.ExecuteReader())
                {
                    if (!r.Read()) return Enumerable.Empty<int>();
                    entries.Add((term, r.GetInt64(0), r.GetInt32(1), r.GetInt32(2)));
                }
            }

            entries.Sort((a, b) => a.count.CompareTo(b.count));

            return AndMerge(entries);
        }

        // ── OR search ────────────────────────────────────────────────

        /// <summary>
        /// Returns line IDs that contain ANY of the supplied terms (OR semantics).
        /// Results are in ascending order with no duplicates.
        /// Uses a min-heap: O(n log k), zero allocation during iteration.
        /// </summary>
        public IEnumerable<int> SearchOr(IEnumerable<string> terms)
        {
            var started = LoadStarted(terms, skipMissing: true);
            if (started.Count == 0) yield break;
            if (started.Count == 1)
            {
                yield return started[0].Current;
                while (started[0].MoveNext()) yield return started[0].Current;
                yield break;
            }

            foreach (var v in PostingMatcher.Union(started.ToArray()))
                yield return v;
        }

        // ── Mixed AND/OR search ──────────────────────────────────────

        /// <summary>
        /// Mixed AND/OR search.
        /// Each group is a set of terms joined by OR; all groups are joined by AND.
        ///
        /// Example:
        ///   Search(new[]{ new[]{"כי","אשר"}, new[]{"ביצחק"} })
        ///   → lines containing ("כי" OR "אשר") AND "ביצחק"
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<IEnumerable<string>> groups)
        {
            var groupList  = new List<IEnumerable<string>>(groups);
            var groupIters = new List<PostingIterator>(groupList.Count);

            foreach (var group in groupList)
            {
                var started = LoadStarted(group, skipMissing: true);
                if (started.Count == 0)
                    return Enumerable.Empty<int>(); // AND: one group has no matches → no results

                if (started.Count == 1)
                    groupIters.Add(started[0]);
                else
                    groupIters.Add(new UnionIterator(started.ToArray()));
            }

            if (groupIters.Count == 0) return Enumerable.Empty<int>();
            if (groupIters.Count == 1) return groupIters[0].AsEnumerable();

            return PostingMatcher.Intersect(groupIters.ToArray());
        }

        // ── Helpers ──────────────────────────────────────────────────

        public int GetTermCount(string term)
        {
            _lookup.Parameters["@t"].Value = term;
            using (var r = _lookup.ExecuteReader())
                return r.Read() ? r.GetInt32(2) : 0;
        }

        private IEnumerable<int> AndMerge(
            List<(string term, long offset, int length, int count)> entries)
        {
            var iters = new PostingIterator[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var (_, offset, length, _) = entries[i];
                var buf = new byte[length];
                _postings.Seek(offset, SeekOrigin.Begin);
                _postings.Read(buf, 0, length);
                iters[i] = new PostingIterator(buf, length, null, 0);
            }

            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext())
                    yield break;

            foreach (var id in PostingMatcher.Intersect(iters))
                yield return id;
        }

        /// <summary>
        /// Loads posting iterators for the given terms and advances each one once.
        /// Missing terms are skipped when skipMissing is true.
        /// </summary>
        private List<PostingIterator> LoadStarted(IEnumerable<string> terms, bool skipMissing)
        {
            var result = new List<PostingIterator>();
            foreach (var term in terms)
            {
                _lookup.Parameters["@t"].Value = term;
                using (var r = _lookup.ExecuteReader())
                {
                    if (!r.Read())
                    {
                        if (!skipMissing) return null;
                        continue;
                    }
                    long offset = r.GetInt64(0);
                    int  length = r.GetInt32(1);
                    var  buf    = new byte[length];
                    _postings.Seek(offset, SeekOrigin.Begin);
                    _postings.Read(buf, 0, length);
                    var it = new PostingIterator(buf, length, null, 0);
                    if (it.MoveNext()) result.Add(it);
                }
            }
            return result;
        }

        public void Dispose()
        {
            _lookup?.Dispose();
            _conn?.Dispose();
            _postings?.Dispose();
        }
    }
}
