using System;
using System.Collections.Generic;

namespace FtsLib.Indexing
{
    /// <summary>
    /// One contiguous docId range mapped to a source corpus by an affine rule:
    /// <c>sourceId = SrcLo + (docId - DocLo)</c>.
    ///
    /// Persisted per segment in the <c>doc_source</c> table of the segment's .db
    /// file (see <see cref="SegmentWriter.WriteMetaDb"/>), so the index records
    /// which corpus each doc came from instead of baking "docId == seforim
    /// line.id" into every consumer. Source 0 is the library; the identity
    /// mapping (SrcLo == DocLo) is the compatibility default that matches every
    /// index built before this table existed.
    /// </summary>
    internal readonly struct DocSourceRange
    {
        /// <summary>First docId of the range (inclusive).</summary>
        public readonly int DocLo;
        /// <summary>Last docId of the range (inclusive).</summary>
        public readonly int DocHi;
        /// <summary>Corpus id. 0 = library (seforim.db).</summary>
        public readonly int Source;
        /// <summary>Source-local id of <see cref="DocLo"/>.</summary>
        public readonly int SrcLo;

        public DocSourceRange(int docLo, int docHi, int source, int srcLo)
        {
            if (docHi < docLo)
                throw new ArgumentException($"docHi ({docHi}) < docLo ({docLo})");
            DocLo  = docLo;
            DocHi  = docHi;
            Source = source;
            SrcLo  = srcLo;
        }

        /// <summary>Additive offset of the affine rule: sourceId = docId + Offset.
        /// Long, because a future corpus base (e.g. 1e9) minus a small source id
        /// must not overflow int during comparison arithmetic.</summary>
        public long Offset => (long)SrcLo - DocLo;

        public bool Contains(int docId) => docId >= DocLo && docId <= DocHi;

        /// <summary>Source-local id for a docId inside this range.</summary>
        public int ToSourceId(int docId) => SrcLo + (docId - DocLo);

        public override string ToString() =>
            $"[{DocLo}..{DocHi}] src={Source} srcLo={SrcLo}";
    }

    /// <summary>
    /// Ordered, disjoint set of <see cref="DocSourceRange"/> rows describing which
    /// corpus (and which source-local id) every docId belongs to.
    ///
    /// Two flavors:
    ///   <see cref="Identity"/> — the implicit mapping of indexes/segments built
    ///   before the doc_source table existed: every docId is library line.id.
    ///   Row-backed — built from persisted rows via <see cref="FromRows"/>.
    ///
    /// Resolution falls back to identity for docIds not covered by any row, so a
    /// mixed index (old segments without the table + new segments with it) still
    /// resolves every doc correctly: pre-table segments are, by definition,
    /// library-identity.
    ///
    /// Thread-safe after construction (immutable).
    /// </summary>
    internal sealed class DocSourceMap
    {
        /// <summary>Corpus id of the library (seforim.db).</summary>
        public const int LibrarySource = 0;

        // Sorted by DocLo, disjoint. Empty for the identity map.
        private readonly DocSourceRange[] _rows;
        private readonly bool             _isIdentity;

        /// <summary>
        /// The implicit library-identity mapping: every docId IS a library
        /// line.id. Used for indexes built before doc_source existed, and as the
        /// default corpus layout of <see cref="IndexWriter"/> until a caller
        /// supplies a multi-corpus map.
        /// </summary>
        public static readonly DocSourceMap Identity = new DocSourceMap(new DocSourceRange[0], isIdentity: true);

        private DocSourceMap(DocSourceRange[] rows, bool isIdentity)
        {
            _rows       = rows;
            _isIdentity = isIdentity;
        }

        /// <summary>Rows backing this map (empty for <see cref="Identity"/>).</summary>
        public IReadOnlyList<DocSourceRange> Rows => _rows;

        /// <summary>True when this is the implicit identity map (no persisted rows).</summary>
        public bool IsIdentity => _isIdentity;

        // ── Construction ─────────────────────────────────────────────

        /// <summary>
        /// Builds a map from persisted rows (any order, duplicates/overlaps from
        /// independently-written segments allowed). Rows are sorted and coalesced:
        ///   - identical/overlapping rows that agree (same Source, same affine
        ///     Offset) are merged — the normal case, since every segment holding
        ///     docs of a corpus records the same affine rule for it;
        ///   - strictly adjacent agreeing rows (hi+1 == lo) are joined;
        ///   - rows that DISAGREE about an overlapping docId indicate a corrupt
        ///     or foreign segment mix — logged loudly, first row wins (resolution
        ///     stays deterministic; validation tests treat the log as a failure).
        /// An empty input returns <see cref="Identity"/>.
        /// </summary>
        public static DocSourceMap FromRows(IEnumerable<DocSourceRange> rows)
        {
            var list = new List<DocSourceRange>(rows);
            if (list.Count == 0) return Identity;

            list.Sort((a, b) =>
            {
                int c = a.DocLo.CompareTo(b.DocLo);
                return c != 0 ? c : a.DocHi.CompareTo(b.DocHi);
            });

            var merged = new List<DocSourceRange>(list.Count);
            var cur    = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var next = list[i];
                bool agrees = next.Source == cur.Source && next.Offset == cur.Offset;

                if (agrees && next.DocLo <= (long)cur.DocHi + 1)
                {
                    // Overlapping or adjacent, same affine rule — extend.
                    if (next.DocHi > cur.DocHi)
                        cur = new DocSourceRange(cur.DocLo, next.DocHi, cur.Source, cur.SrcLo);
                    continue;
                }

                if (!agrees && next.DocLo <= cur.DocHi)
                {
                    // Conflicting claim for the same docId — should be impossible
                    // on a healthy index. Keep the first row, clip the second to
                    // its non-overlapping tail (or drop it entirely).
                    FtsLog.Write("DocSourceMap.FromRows",
                        $"CONFLICT: {next} overlaps {cur} with a different mapping — first row wins");
                    if (next.DocHi <= cur.DocHi) continue; // fully shadowed — drop
                    int clippedLo = cur.DocHi + 1;
                    next = new DocSourceRange(clippedLo, next.DocHi, next.Source,
                                              next.ToSourceId(clippedLo));
                }

                merged.Add(cur);
                cur = next;
            }
            merged.Add(cur);

            return new DocSourceMap(merged.ToArray(), isIdentity: false);
        }

        // ── Clip (write-side: segment rows from the corpus layout) ───

        /// <summary>
        /// Returns this map's rows intersected with the doc range
        /// [<paramref name="docLo"/>..<paramref name="docHi"/>] — the rows a
        /// segment covering exactly that doc range must persist.
        /// For the identity map this is the single row (lo, hi, library, lo).
        /// </summary>
        public List<DocSourceRange> Clip(int docLo, int docHi)
        {
            var result = new List<DocSourceRange>(2);
            if (docHi < docLo) return result;

            if (_isIdentity)
            {
                result.Add(new DocSourceRange(docLo, docHi, LibrarySource, docLo));
                return result;
            }

            foreach (var r in _rows)
            {
                if (r.DocHi < docLo) continue;
                if (r.DocLo > docHi) break;
                int lo = r.DocLo > docLo ? r.DocLo : docLo;
                int hi = r.DocHi < docHi ? r.DocHi : docHi;
                result.Add(new DocSourceRange(lo, hi, r.Source, r.ToSourceId(lo)));
            }

            // Any sub-range not covered by a row falls back to library-identity —
            // same rule as Resolve, made explicit on disk so the segment is
            // self-describing even when the layout map was partial.
            if (result.Count == 0)
                result.Add(new DocSourceRange(docLo, docHi, LibrarySource, docLo));

            return result;
        }

        // ── Resolve (read-side) ──────────────────────────────────────

        /// <summary>
        /// Maps a docId to its (source, source-local id). docIds not covered by
        /// any row resolve as library-identity — the correct meaning of docs in
        /// segments that predate the doc_source table.
        /// </summary>
        public void Resolve(int docId, out int source, out int sourceId)
        {
            var idx = FindRow(docId);
            if (idx >= 0)
            {
                source   = _rows[idx].Source;
                sourceId = _rows[idx].ToSourceId(docId);
                return;
            }
            source   = LibrarySource;
            sourceId = docId;
        }

        /// <summary>Binary search for the row containing docId; -1 when uncovered.</summary>
        private int FindRow(int docId)
        {
            int lo = 0, hi = _rows.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (docId < _rows[mid].DocLo)      hi = mid - 1;
                else if (docId > _rows[mid].DocHi) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        // ── Split (read-side: batch fetch routing) ───────────────────

        /// <summary>One contiguous run of result ids that share a source and affine offset.</summary>
        public readonly struct SourceRun
        {
            /// <summary>Corpus id of every id in the run.</summary>
            public readonly int Source;
            /// <summary>sourceId = docId + Offset for every id in the run.</summary>
            public readonly long Offset;
            /// <summary>Start index in the input id list.</summary>
            public readonly int Start;
            /// <summary>Number of ids in the run.</summary>
            public readonly int Count;

            public SourceRun(int source, long offset, int start, int count)
            {
                Source = source;
                Offset = offset;
                Start  = start;
                Count  = count;
            }
        }

        /// <summary>
        /// Splits an ASCENDING id list into contiguous runs per (source, offset).
        /// Because corpus bases partition the docId space and search results
        /// ascend, a result stream is always a small number of runs — resolution
        /// for a batch is O(n + rows), not a per-id map lookup.
        ///
        /// Returns null when every id is library-identity (source 0, offset 0) —
        /// the fast path: callers use their existing single-corpus fetch verbatim,
        /// zero translation, zero copying.
        /// </summary>
        public List<SourceRun> SplitBySource(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return null;
            if (_isIdentity || _rows.Length == 0) return null;

            List<SourceRun> runs = null;
            int  runStart  = 0;
            int  curSource = LibrarySource;
            long curOffset = 0;
            int  rowIdx    = 0; // advance-only cursor — ids ascend

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                while (rowIdx < _rows.Length && _rows[rowIdx].DocHi < id) rowIdx++;

                int  source;
                long offset;
                if (rowIdx < _rows.Length && _rows[rowIdx].DocLo <= id)
                {
                    source = _rows[rowIdx].Source;
                    offset = _rows[rowIdx].Offset;
                }
                else
                {
                    source = LibrarySource; // uncovered → identity fallback
                    offset = 0;
                }

                if (i == 0)
                {
                    curSource = source;
                    curOffset = offset;
                }
                else if (source != curSource || offset != curOffset)
                {
                    if (runs == null) runs = new List<SourceRun>(2);
                    runs.Add(new SourceRun(curSource, curOffset, runStart, i - runStart));
                    runStart  = i;
                    curSource = source;
                    curOffset = offset;
                }
            }

            // Single run that is pure library-identity → fast path.
            if (runs == null && curSource == LibrarySource && curOffset == 0)
                return null;

            if (runs == null) runs = new List<SourceRun>(1);
            runs.Add(new SourceRun(curSource, curOffset, runStart, ids.Count - runStart));
            return runs;
        }
    }
}
