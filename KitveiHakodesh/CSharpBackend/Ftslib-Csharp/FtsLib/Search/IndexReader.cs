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
        private readonly SearchLease         _lease;   // held for our lifetime; null when no store
        private bool _disposed;

        // F01: chunk metadata piggybacked from expansion scans, so LookupTerm
        // skips its per-term-per-segment point SELECT for every expanded term.
        // Lives exactly as long as this reader's immutable segment snapshot.
        private readonly TermChunkCache _chunkCache = new TermChunkCache();

        /// <summary>
        /// Opens an IndexReader using an explicit snapshot of live segment paths,
        /// holding a <see cref="SearchLease"/> for the reader's entire lifetime.
        ///
        /// The lease keeps the store's read lock held so that any merge needing to
        /// delete source segment files will block until this reader is disposed.
        /// Use this overload whenever a <see cref="SegmentStore"/> is available.
        /// </summary>
        public IndexReader(string indexPath, List<(string dat, string db)> livePaths, SearchLease lease)
            : base(indexPath)
        {
            _lease   = lease;
            _deletes = DeleteSet.Load(DeletesFile);
            if (livePaths == null || livePaths.Count == 0) return;

            foreach (var (dat, db) in livePaths)
            {
                if (File.Exists(dat) && File.Exists(db))
                    _segments.Add(new SegmentHandle(dat, db));
            }

            OrderSegmentsByFirstDoc(_segments);
        }

        /// <summary>
        /// Opens an IndexReader using an explicit snapshot of live segment paths.
        /// Use this overload when a SegmentStore is available — it reads the live
        /// path list under the store's lock, so the snapshot is consistent and never
        /// races with a concurrent merge that is deleting source segments.
        /// </summary>
        public IndexReader(string indexPath, List<(string dat, string db)> livePaths)
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
            _deletes = DeleteSet.Load(DeletesFile);

            if (!Directory.Exists(IndexPath)) return;

            var datFiles = Directory.GetFiles(IndexPath, "seg_*.dat");

            foreach (var datFile in datFiles)
            {
                string dbFile = Path.ChangeExtension(datFile, ".db");
                if (File.Exists(dbFile))
                    _segments.Add(new SegmentHandle(datFile, dbFile));
            }

            OrderSegmentsByFirstDoc(_segments);
        }

        // ── Segment ordering ─────────────────────────────────────────

        /// <summary>
        /// Orders segments by the doc ID of their first stored posting so that
        /// <see cref="ConcatIterator"/> streams globally ascending doc IDs.
        ///
        /// Segment IDs do NOT order doc ranges: a merge target's ID is allocated
        /// when the merge STARTS, so a merge of older segments that runs after a
        /// newer flush exists (e.g. L1_30 + L1_35 → L2_37 while L0_36 already
        /// holds newer docs) receives the highest segment ID while holding lower
        /// doc IDs. Sorting by segment ID then breaks the ascending-stream
        /// contract that the filter leap-frog, the AND intersection, and the
        /// "results ascend by line ID" API guarantee all rely on — filtered
        /// queries silently DROP every match in the out-of-place segment.
        ///
        /// Live segments hold contiguous, disjoint slices of the build's doc
        /// timeline, so ANY posting is a valid representative of its segment's
        /// position; we read the first posting of the chunk at the smallest .dat
        /// offset (one SQL row + at most 5 bytes of IO per segment).
        /// </summary>
        private static void OrderSegmentsByFirstDoc(List<SegmentHandle> segments)
        {
            if (segments.Count < 2) return;

            var keyed = new KeyValuePair<int, SegmentHandle>[segments.Count];
            for (int i = 0; i < segments.Count; i++)
                keyed[i] = new KeyValuePair<int, SegmentHandle>(
                    ReadFirstDocId(segments[i]), segments[i]);

            // Ties are impossible on a healthy index (disjoint ranges); order by
            // path as a deterministic fallback for degenerate states.
            Array.Sort(keyed, (a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Value.DatPath, b.Value.DatPath);
            });

            segments.Clear();
            foreach (var kv in keyed)
                segments.Add(kv.Value);
        }

        /// <summary>
        /// Doc ID of the segment's first stored posting: the first varint of the
        /// chunk at the smallest .dat offset is that chunk's absolute encoded
        /// first doc (deltas start from 0). Returns int.MaxValue for a segment
        /// with no terms — it contributes nothing and sorts last.
        /// </summary>
        private static int ReadFirstDocId(SegmentHandle seg)
        {
            long offset;
            using (var cmd = seg.Conn.CreateCommand())
            {
                cmd.CommandText = "SELECT MIN(offset) FROM term_index";
                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value) return int.MaxValue;
                offset = Convert.ToInt64(result);
            }

            var buf  = new byte[5]; // a uint varint is at most 5 bytes
            int read = seg.ReadBytes(offset, buf, 0, buf.Length);
            if (read <= 0) return int.MaxValue;

            int  pos     = 0;
            uint encoded = VarInt.Read(buf, ref pos, read);
            return (int)((long)encoded + int.MinValue);
        }

        // ── Wildcard expansion ────────────────────────────────────────

        /// <summary>
        /// Expands a wildcard pattern (containing '*') to all matching terms
        /// across every live segment's term_index.
        /// Returns an empty list when nothing matches.
        /// </summary>
        public List<string> ExpandWildcard(string pattern)
            => HebrewWildcardExpander.Expand(pattern, _segments, _chunkCache);

        // ── Grammar expansion ─────────────────────────────────────────

        /// <summary>
        /// Expands a word with Hebrew grammatical prefixes and/or suffixes,
        /// verifying each candidate against the segment term_index.
        /// Returns an empty list when nothing matches.
        /// </summary>
        public List<string> ExpandGrammar(string word, bool expandPrefixes, bool expandSuffixes)
            => GrammarExpander.Expand(word, expandPrefixes, expandSuffixes, _segments, _chunkCache);

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
            => FuzzyExpander.Expand(term, maxDistance, _segments, _chunkCache);

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

        /// <param name="filter">Optional doc-ID keep-set. When non-null, only IDs
        /// present in the set are returned; a small set drives the intersection
        /// (candidate-driven path) instead of merely trimming its output.</param>
        public IEnumerable<int> Search(IEnumerable<IEnumerable<string>> groups,
                                       RoaringBitmap filter = null,
                                       CancellationToken ct = default)
        {
            if (_segments.Count == 0) return Enumerable.Empty<int>();
            return PostingIntersector.MixedSearch(groups, ResolveIterator, GetTermCount, filter, ct);
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
            // F01: expansion scans already recorded this term's chunks (complete
            // across all segments, in segment order) — skip the point SELECTs.
            if (_chunkCache.TryGet(term, out var cached))
                return cached;

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
            int skipBytes  = chunk.SkipCount * 3 * sizeof(int); // 12 bytes per entry
            int totalBytes = skipBytes + chunk.Length;

            var  buf      = new byte[totalBytes];
            long readFrom = chunk.SkipCount > 0 ? chunk.SkipOffset : chunk.Offset;

            // SegmentHandle.ReadBytes does a lock-guarded Seek+Read on a plain
            // FileStream.  It always fills the buffer for a well-formed segment.
            chunk.Seg.ReadBytes(readFrom, buf, 0, totalBytes);

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
