using FtsLib.Indexing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FtsLib.Search
{
    /// <summary>
    /// Searches a segment-based index. Works at any point — mid-build or finalized.
    /// Queries all live segment pairs (seg_L_ID.dat + seg_L_ID.db) and merges results.
    ///
    /// Three search modes:
    ///   Search(terms)   — AND: all terms must appear
    ///   SearchOr(terms) — OR:  any term must appear
    ///   Search(groups)  — Mixed: each group is OR'd, all groups are AND'd
    /// </summary>
    internal sealed class IndexReader : IndexDirectory, IDisposable
    {
        private readonly List<SegmentHandle> _segments = new List<SegmentHandle>();
        private readonly DeleteSet           _deletes;
        private bool _disposed;

        /// <summary>
        /// Opens an IndexReader using pre-opened segment handles.
        /// The handles were opened inside the live-state lock in
        /// <see cref="SegmentLiveState.OpenLiveSegmentHandles"/>, so they are
        /// guaranteed to be consistent — no TOCTOU race with concurrent merges.
        /// This IndexReader takes ownership of the handles and disposes them on Dispose.
        /// </summary>
        public IndexReader(string indexPath, List<SegmentHandle> handles)
            : base(indexPath)
        {
            _deletes = DeleteSet.Load(DeletesFile);
            if (handles == null || handles.Count == 0) return;
            // Handles are already sorted by segId from OpenLiveSegmentHandles.
            _segments.AddRange(handles);
        }

        /// <summary>
        /// Opens an IndexReader using an explicit snapshot of live segment paths.
        /// Use this overload when a SegmentStore is available — it reads the live
        /// path list under the store's lock, so the snapshot is consistent and never
        /// races with a concurrent merge that is deleting source segments.
        /// </summary>
        public IndexReader(string indexPath, List<(string dat, string db)> livePaths)
            : base(indexPath)
        {
            _deletes = DeleteSet.Load(DeletesFile);
            if (livePaths == null || livePaths.Count == 0) return;

            // Sort by segId so ConcatIterator sees doc IDs in ascending order.
            livePaths.Sort((a, b) => ParseSegId(a.dat).CompareTo(ParseSegId(b.dat)));

            foreach (var (dat, db) in livePaths)
            {
                if (File.Exists(dat) && File.Exists(db))
                    _segments.Add(new SegmentHandle(dat, db));
            }
        }

        /// <summary>
        /// Opens an IndexReader by scanning the index directory for seg_*.dat files.
        /// Only use this when no SegmentStore is available (e.g. a read-only search
        /// process that never writes). During an active build, use the overload that
        /// accepts a live-path snapshot to avoid racing with concurrent merges.
        /// </summary>
        public IndexReader(string indexPath) : base(indexPath)
        {
            _deletes = DeleteSet.Load(DeletesFile);

            if (!Directory.Exists(IndexPath)) return;

            // Sort by segId so ConcatIterator sees doc IDs in ascending order
            var datFiles = Directory.GetFiles(IndexPath, "seg_*.dat");
            System.Array.Sort(datFiles, (a, b) => ParseSegId(a).CompareTo(ParseSegId(b)));

            foreach (var datFile in datFiles)
            {
                string dbFile = Path.ChangeExtension(datFile, ".db");
                if (File.Exists(dbFile))
                    _segments.Add(new SegmentHandle(datFile, dbFile));
            }
        }

        private static int ParseSegId(string path)
        {
            string name  = Path.GetFileNameWithoutExtension(path); // seg_L_ID
            var    parts = name.Split('_');
            return parts.Length == 3 && int.TryParse(parts[2], out int id) ? id : 0;
        }

        // ── Wildcard expansion ────────────────────────────────────────

        /// <summary>
        /// Expands a wildcard pattern (containing '*') to all matching terms
        /// across every live segment's term_index.
        /// Returns an empty list when nothing matches.
        /// </summary>
        public List<string> ExpandWildcard(string pattern)
            => HebrewWildcardExpander.Expand(pattern, _segments);

        // ── Fuzzy expansion ───────────────────────────────────────────

        /// <summary>
        /// Expands a fuzzy query term to all index terms within
        /// <paramref name="maxDistance"/> Levenshtein edits (clamped to 3).
        ///
        /// Uses trigram pre-filtering against each segment's term_index to
        /// narrow candidates before running the full edit-distance check.
        /// Returns an empty list when nothing matches.
        /// </summary>
        public List<string> ExpandFuzzy(string term, int maxDistance = 1)
            => FuzzyExpander.Expand(term, maxDistance, _segments);

        // ── AND search ───────────────────────────────────────────────

        public IEnumerable<int> Search(IEnumerable<string> terms, CancellationToken ct = default)
        {
            if (_segments.Count == 0) return Enumerable.Empty<int>();
            return PostingIntersector.AndSearch(terms, ResolveIterator, GetTermCount, ct);
        }

        // ── OR search ────────────────────────────────────────────────

        public IEnumerable<int> SearchOr(IEnumerable<string> terms, CancellationToken ct = default)
        {
            if (_segments.Count == 0) return Enumerable.Empty<int>();
            return PostingIntersector.OrSearch(terms, ResolveIterator, ct);
        }

        // ── Mixed AND/OR search ──────────────────────────────────────

        public IEnumerable<int> Search(IEnumerable<IEnumerable<string>> groups, CancellationToken ct = default)
        {
            if (_segments.Count == 0) return Enumerable.Empty<int>();
            return PostingIntersector.MixedSearch(groups, ResolveIterator, ct);
        }

        // ── Term count ───────────────────────────────────────────────

        public int GetTermCount(string term) => TotalCount(LookupTerm(term));

        // ── Helpers ──────────────────────────────────────────────────

        private PostingIterator ResolveIterator(string term)
        {
            var chunks = LookupTerm(term);
            if (chunks.Count == 0) return PostingIterator.Empty;
            var iter = BuildIterator(chunks);
            return _deletes.IsEmpty ? iter : new FilteringIterator(iter, _deletes);
        }

        private List<SegmentChunk> LookupTerm(string term)
        {
            var result = new List<SegmentChunk>();
            foreach (var seg in _segments)
            {
                seg.Lookup.Parameters["@t"].Value = term;
                using (var r = seg.Lookup.ExecuteReader())
                {
                    if (r.Read())
                        result.Add(new SegmentChunk(seg, r.GetInt64(0), r.GetInt32(1), r.GetInt32(2)));
                }
            }
            return result;
        }

        private static int TotalCount(List<SegmentChunk> chunks)
        {
            int n = 0;
            foreach (var c in chunks) n += c.Count;
            return n;
        }

        private static PostingIterator BuildIterator(List<SegmentChunk> chunks)
        {
            if (chunks.Count == 1)
                return LoadChunk(chunks[0]);

            // Segments are flushed in doc ID order — seg_0_0 has lower IDs than seg_0_1 etc.
            // ConcatIterator sequences them end-to-end, producing a globally ascending stream.
            var iters = new PostingIterator[chunks.Count];
            for (int i = 0; i < chunks.Count; i++)
                iters[i] = LoadChunk(chunks[i]);
            return new ConcatIterator(iters);
        }

        private static PostingIterator LoadChunk(SegmentChunk chunk)
        {
            var buf = new byte[chunk.Length];
            chunk.Seg.DataStream.Seek(chunk.Offset, SeekOrigin.Begin);
            chunk.Seg.DataStream.Read(buf, 0, chunk.Length);
            return new PostingIterator(buf, chunk.Length, null, 0);
        }

        // ── Dispose ──────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var seg in _segments) seg.Dispose();
            _segments.Clear();
        }
    }
}
