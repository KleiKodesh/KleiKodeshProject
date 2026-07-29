using FtsLib.Indexing;
using System;
using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Per-IndexReader cache of term → posting-chunk metadata, filled as a
    /// by-product of expansion scans (wildcard / fuzzy / grammar).
    ///
    /// The expansion scans already read exactly the term_index rows that resolve
    /// needs but used to keep only the term string; ResolveIterator then re-fetched
    /// the metadata with one point SELECT per term per segment — ~110k SELECTs for
    /// a query like *כי* (F01 in PERF_AUDIT_2026-07-12.md). Recording the metadata
    /// here lets LookupTerm skip those SELECTs entirely for expanded terms.
    ///
    /// Correctness invariants:
    ///   - A cached term's chunk list is always COMPLETE: every expander scan runs
    ///     the same predicate against every segment, so a term matched in one
    ///     segment is matched in every segment that contains it.
    ///   - Chunks are appended in the reader's segment-list order (ascending
    ///     segment id) — the same order LookupTerm produces, required by
    ///     ConcatIterator's ascending doc-id contract.
    ///   - Add is idempotent per (term, segment): repeated scans (multiple '?'
    ///     sub-patterns, overlapping OR alternatives, multiple query groups)
    ///     cannot duplicate a chunk.
    ///   - The cache lives and dies with one IndexReader, which holds a
    ///     SearchLease over an immutable segment snapshot — no staleness.
    /// </summary>
    internal sealed class TermChunkCache
    {
        private readonly Dictionary<string, List<SegmentChunk>> _map =
            new Dictionary<string, List<SegmentChunk>>(StringComparer.Ordinal);

        public bool TryGet(string term, out List<SegmentChunk> chunks)
            => _map.TryGetValue(term, out chunks);

        /// <summary>
        /// Records one term's chunk in one segment. Idempotent per (term, segment).
        /// </summary>
        public void Add(string term, SegmentChunk chunk)
        {
            if (!_map.TryGetValue(term, out var list))
            {
                list = new List<SegmentChunk>(2);
                _map[term] = list;
            }
            foreach (var existing in list)
                if (ReferenceEquals(existing.Seg, chunk.Seg))
                    return;
            list.Add(chunk);
        }

        public int Count => _map.Count;
    }
}
