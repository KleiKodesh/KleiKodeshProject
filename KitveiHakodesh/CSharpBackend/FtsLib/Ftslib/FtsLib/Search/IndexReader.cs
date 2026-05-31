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
        private readonly SearchLease         _lease;   // held for our lifetime; null when no store
        private bool _disposed;

        /// <summary>
        /// Opens an IndexReader using an explicit snapshot of live segment paths,
        /// holding a <see cref="SearchLease"/> for the reader's entire lifetime.
        ///
        /// Each segment's .del file is loaded independently — only segments with
        /// actual deletions pay the filtering cost during search.
        ///
        /// The lease keeps the store's read lock held so that any merge needing to
        /// delete source segment files will block until this reader is disposed.
        /// Use this overload whenever a <see cref="SegmentStore"/> is available.
        /// </summary>
        public IndexReader(string indexPath, List<(string dat, string db, int segId)> livePaths, SearchLease lease)
            : base(indexPath)
        {
            _lease = lease;
            if (livePaths == null || livePaths.Count == 0) return;

            // Sort by segId so ConcatIterator sees doc IDs in ascending order.
            livePaths.Sort((a, b) => ParseSegId(a.dat).CompareTo(ParseSegId(b.dat)));

            foreach (var (dat, db, segId) in livePaths)
            {
                if (!File.Exists(dat) || !File.Exists(db)) continue;
                var handle = new SegmentHandle(dat, db);
                // Load this segment's per-segment delete set (null if no .del file).
                string delPath = Path.Combine(IndexPath, $"seg_{ParseLevel(dat)}_{segId}.del");
                handle.Deletes = DeleteSet.Load(delPath); // returns empty set if file absent
                if (handle.Deletes.IsEmpty) handle.Deletes = null; // null = fast path, no filtering
                _segments.Add(handle);
            }
        }

        /// <summary>
        /// Opens an IndexReader using an explicit snapshot of live segment paths.
        /// </summary>
        public IndexReader(string indexPath, List<(string dat, string db, int segId)> livePaths)
            : this(indexPath, livePaths, lease: null)
        {
        }

        /// <summary>
        /// Opens an IndexReader by scanning the index directory for seg_*.dat files.
        /// Only use this when no SegmentStore is available (e.g. a read-only search
        /// process that never writes). During an active build, use the overload that
        /// accepts a live-path snapshot to avoid racing with concurrent merges.
        /// </summary>
        public IndexReader(string indexPath) : base(indexPath)
        {
            if (!Directory.Exists(IndexPath)) return;

            // Sort by segId so ConcatIterator sees doc IDs in ascending order
            var datFiles = Directory.GetFiles(IndexPath, "seg_*.dat");
            System.Array.Sort(datFiles, (a, b) => ParseSegId(a).CompareTo(ParseSegId(b)));

            foreach (var datFile in datFiles)
            {
                string dbFile = Path.ChangeExtension(datFile, ".db");
                if (!File.Exists(dbFile)) continue;

                int segId = ParseSegId(datFile);
                var handle = new SegmentHandle(datFile, dbFile);
                string delPath = Path.ChangeExtension(datFile, ".del");
                handle.Deletes = DeleteSet.Load(delPath);
                if (handle.Deletes.IsEmpty) handle.Deletes = null;
                _segments.Add(handle);
            }
        }

        private static int ParseSegId(string path)
        {
            string name  = Path.GetFileNameWithoutExtension(path); // seg_L_ID
            var    parts = name.Split('_');
            return parts.Length == 3 && int.TryParse(parts[2], out int id) ? id : 0;
        }

        private static int ParseLevel(string path)
        {
            string name  = Path.GetFileNameWithoutExtension(path); // seg_L_ID
            var    parts = name.Split('_');
            return parts.Length == 3 && int.TryParse(parts[1], out int level) ? level : 0;
        }

        // ── Wildcard expansion ────────────────────────────────────────

        /// <summary>
        /// Expands a wildcard pattern (containing '*') to all matching terms
        /// across every live segment's term_index.
        /// Returns an empty list when nothing matches.
        /// </summary>
        public List<string> ExpandWildcard(string pattern)
            => HebrewWildcardExpander.Expand(pattern, _segments);

        // ── Grammar expansion ─────────────────────────────────────────

        /// <summary>
        /// Expands <paramref name="word"/> by prepending grammatical prefixes,
        /// appending grammatical suffixes, or both, then verifying each candidate
        /// against the segment term_index via exact lookup.
        ///
        /// Returns only forms that actually exist in the index.
        /// </summary>
        public List<string> ExpandGrammar(string word, bool prefixes, bool suffixes)
            => GrammarExpander.Expand(word, prefixes, suffixes, _segments);

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

            // Build per-chunk iterators, wrapping with FilteringIterator only for
            // segments that actually have deletions. Clean segments pay zero overhead.
            if (chunks.Count == 1)
            {
                var iter = LoadChunk(chunks[0]);
                var del  = chunks[0].Seg.Deletes;
                return del != null ? new FilteringIterator(iter, del) : iter;
            }

            var iters = new PostingIterator[chunks.Count];
            for (int i = 0; i < chunks.Count; i++)
            {
                var iter = LoadChunk(chunks[i]);
                var del  = chunks[i].Seg.Deletes;
                iters[i] = del != null ? new FilteringIterator(iter, del) : iter;
            }
            return new ConcatIterator(iters);
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
                        result.Add(new SegmentChunk(seg,
                            r.GetInt64(0),  // skip_offset
                            r.GetInt32(1),  // skip_count
                            r.GetInt64(2),  // offset
                            r.GetInt32(3),  // length
                            r.GetInt32(4)   // count
                        ));
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

        private static PostingIterator LoadChunk(SegmentChunk chunk)
        {
            int skipBytes  = chunk.SkipCount * 3 * sizeof(int); // 12 bytes per entry
            int totalBytes = skipBytes + chunk.Length;

            var buf = new byte[totalBytes];
            chunk.Seg.DataStream.Seek(chunk.SkipCount > 0 ? chunk.SkipOffset : chunk.Offset,
                                      SeekOrigin.Begin);

            // FileStream.Read may return fewer bytes than requested — read in a loop
            // to guarantee the full buffer is populated before decoding.
            int read = 0;
            while (read < totalBytes)
            {
                int n = chunk.Seg.DataStream.Read(buf, read, totalBytes - read);
                if (n == 0) break; // end of stream — should never happen on a valid segment
                read += n;
            }

            // Deserialise skip table from the front of the buffer.
            int[] skip    = null;
            int   skipLen = 0;
            if (chunk.SkipCount > 0)
            {
                skipLen = chunk.SkipCount * 3;
                skip    = new int[skipLen];
                for (int i = 0; i < skipLen; i++)
                    skip[i] = BitConverter.ToInt32(buf, i * sizeof(int));
            }

            // Posting bytes follow immediately after the skip table.
            // Since PostingIterator reads from index 0, copy the posting slice to a
            // separate array when a skip table precedes it.
            byte[] postBuf;
            if (skipBytes == 0)
            {
                postBuf = buf; // no skip table — buf is already just posting bytes
            }
            else
            {
                postBuf = new byte[chunk.Length];
                Buffer.BlockCopy(buf, skipBytes, postBuf, 0, chunk.Length);
            }

            return new PostingIterator(postBuf, chunk.Length, skip, skipLen);
        }

        // ── Dispose ──────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var seg in _segments)
                seg.Dispose();
            _segments.Clear();
            // Release the search lease last — this unblocks any merge that was
            // waiting for the write lock while we held open segment file handles.
            _lease?.Dispose();
        }
    }
}
