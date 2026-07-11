using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FtsLib.Search
{
    /// <summary>
    /// Search orchestration shared by RamIndex and IndexReader.
    ///
    /// All three search modes (AND, OR, mixed AND/OR) are implemented here.
    /// Callers supply two delegates:
    ///   resolve  — term → PostingIterator (return PostingIterator.Empty if missing)
    ///   getCount — term → doc count       (used to sort rarest-first for AND)
    ///
    /// The "missing term" contract for AND:
    ///   If resolve returns PostingIterator.Empty (IsDone = true), AndSearch
    ///   treats the term as absent and returns an empty result immediately.
    ///
    /// OR groups with many expanded terms (wildcards, fuzzy) use a RoaringBitmap
    /// accumulator instead of a min-heap union iterator. The bitmap drains each
    /// posting list with a tight sequential loop and then wraps the result in a
    /// RoaringBitmapIterator that plugs into the existing AND intersection unchanged.
    /// The crossover threshold is RoaringOrThreshold terms — below that the heap
    /// is faster due to lower setup cost; above it the bitmap wins because heap
    /// overhead scales with O(n log k) while bitmap OR is O(n).
    /// </summary>
    internal static class PostingIntersector
    {
        /// <summary>
        /// Minimum number of OR-group terms that triggers the Roaring bitmap path.
        /// Below this threshold the min-heap union iterator has lower overhead.
        /// Chosen empirically: at 20 terms the heap cost (~20 * log(20) ≈ 86 ops
        /// per doc) starts to exceed the bitmap setup cost.
        /// </summary>
        internal const int RoaringOrThreshold = 20;

        /// <summary>
        /// When one group's total doc count exceeds this multiple of the other
        /// group's total doc count, skip materialising the large group into a
        /// RoaringBitmap and instead use the probe-intersect strategy: iterate
        /// the small group with a UnionIterator and for each candidate call
        /// SkipTo on each term of the large group until one hits.
        ///
        /// Rationale: building a RoaringBitmap for a large group costs
        /// O(total_docs_in_group) in decompression + insertion work regardless of
        /// how few docs the other group has.  If the other group is tiny, almost
        /// all of that work produces doc IDs that are immediately discarded by the
        /// AND.  The probe strategy decodes only the posting lists that actually
        /// match, costing O(small_side_results * log(large_side_per_term)) instead.
        /// </summary>
        internal const int ProbeIntersectRatioThreshold = 10;
        // ── AND ──────────────────────────────────────────────────────

        public static IEnumerable<int> AndSearch(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct = default)
        {
            var termList = new List<string>(terms);
            if (termList.Count == 0) return Enumerable.Empty<int>();

            termList.Sort((a, b) => getCount(a).CompareTo(getCount(b)));
            return AndMerge(termList, resolve, ct);
        }

        // ── OR ───────────────────────────────────────────────────────

        public static IEnumerable<int> OrSearch(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct = default)
        {
            var termList = terms as IReadOnlyList<string> ?? new List<string>(terms);

            // Large OR groups (wildcard/fuzzy expansions) use the Roaring bitmap path.
            // The bitmap drains all posting lists with a tight sequential loop and
            // avoids the O(n log k) heap overhead of UnionIterator.
            if (termList.Count >= RoaringOrThreshold)
            {
                var roaringIter = BuildRoaringIterator(termList, resolve, ct);
                if (!roaringIter.MoveNext()) return Enumerable.Empty<int>();
                return DrainStarted(roaringIter, ct);
            }

            var started = StartedIterators(termList, resolve, skipMissing: true);
            if (started.Count == 0) return Enumerable.Empty<int>();
            if (started.Count == 1) return DrainStarted(started[0], ct);
            return PostingMatcher.Union(started.ToArray(), ct);
        }

        // ── Mixed AND/OR ─────────────────────────────────────────────

        public static IEnumerable<int> MixedSearch(
            IEnumerable<IEnumerable<string>> groups,
            Func<string, PostingIterator>    resolve,
            Func<string, int>                getCount,
            CancellationToken                ct = default)
        {
            // Materialise groups into term lists so we can inspect counts before
            // deciding which execution strategy to use.
            var groupTerms = new List<IReadOnlyList<string>>();
            foreach (var group in groups)
            {
                var termList = group as IReadOnlyList<string> ?? new List<string>(group);
                if (termList.Count == 0) return Enumerable.Empty<int>();
                groupTerms.Add(termList);
            }

            if (groupTerms.Count == 0) return Enumerable.Empty<int>();

            // ── Two-group special case: probe-intersect when one side is huge ──
            //
            // If we have exactly two groups and one has vastly more total docs than
            // the other, materialising the large group into a RoaringBitmap wastes
            // time decoding docs that will immediately be discarded.
            //
            // Instead: resolve both groups to iterators without materialising the
            // large one.  Use a UnionIterator (heap) for the large group — it only
            // decodes values on demand as the AND intersection calls SkipTo, so we
            // only pay for the docs that survive the intersection.
            //
            // This is safe only when both groups are large enough for a heap
            // (>= RoaringOrThreshold terms on the large side), because otherwise
            // the bitmap path was already fast (small number of terms to drain).
            if (groupTerms.Count == 2)
            {
                int count0 = TotalGroupCount(groupTerms[0], getCount);
                int count1 = TotalGroupCount(groupTerms[1], getCount);

                // Determine large and small.
                int largeIdx = count0 >= count1 ? 0 : 1;
                int smallIdx = 1 - largeIdx;
                int largeCount = largeIdx == 0 ? count0 : count1;
                int smallCount = largeIdx == 0 ? count1 : count0;

                // Only apply when the large group has enough terms to matter AND
                // the imbalance is significant enough.
                if (groupTerms[largeIdx].Count >= RoaringOrThreshold &&
                    smallCount > 0 &&
                    largeCount / smallCount >= ProbeIntersectRatioThreshold)
                {
                    return ProbeIntersect(
                        groupTerms[smallIdx], groupTerms[largeIdx],
                        resolve, getCount, ct);
                }
            }

            // ── General path (original logic) ────────────────────────
            var groupIters = new List<PostingIterator>();
            foreach (var termList in groupTerms)
            {
                PostingIterator groupIter;

                if (termList.Count >= RoaringOrThreshold)
                {
                    groupIter = BuildRoaringIterator(termList, resolve, ct);
                    if (groupIter.IsDone) return Enumerable.Empty<int>();
                    if (!groupIter.MoveNext()) return Enumerable.Empty<int>();
                }
                else
                {
                    var started = StartedIterators(termList, resolve, skipMissing: true);
                    if (started.Count == 0) return Enumerable.Empty<int>();
                    if (started.Count == 1)
                    {
                        groupIter = started[0];
                    }
                    else
                    {
                        var union = new UnionIterator(started.ToArray());
                        if (!union.MoveNext()) continue;
                        groupIter = union;
                    }
                }

                groupIters.Add(groupIter);
            }

            if (groupIters.Count == 0) return Enumerable.Empty<int>();
            if (groupIters.Count == 1) return DrainStarted(groupIters[0], ct);
            return PostingMatcher.Intersect(groupIters.ToArray(), ct);
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Probe-intersect strategy for asymmetric two-group AND queries.
        ///
        /// Iterates the small group (via a heap UnionIterator or single iterator)
        /// and for each candidate doc ID checks whether it exists in the large
        /// group by calling SkipTo on each large-group term's PostingIterator.
        ///
        /// This avoids materialising the large group into a RoaringBitmap entirely.
        /// Cost: O(small_docs * large_terms_per_hit * log(large_term_posting_size))
        /// in the best case, which is much cheaper than O(large_total_docs) when
        /// small_docs << large_total_docs.
        ///
        /// The large group uses a UnionIterator so SkipTo propagates via the heap,
        /// advancing only the iterators that need to move — the skip-list
        /// acceleration in PostingIterator keeps each SkipTo at O(log df).
        /// </summary>
        private static IEnumerable<int> ProbeIntersect(
            IReadOnlyList<string>         smallTerms,
            IReadOnlyList<string>         largeTerms,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct)
        {
            // Build the small-side iterator (pre-advanced).
            PostingIterator smallIter;
            {
                var started = StartedIterators(smallTerms, resolve, skipMissing: true);
                if (started.Count == 0) yield break;
                if (started.Count == 1)
                {
                    smallIter = started[0];
                }
                else
                {
                    var union = new UnionIterator(started.ToArray());
                    if (!union.MoveNext()) yield break;
                    smallIter = union;
                }
            }

            // Build the large-side iterator as a lazy UnionIterator — NOT materialised
            // into a bitmap.  SkipTo on UnionIterator propagates to only the iterators
            // that need to advance, so we only decode posting data for docs that survive.
            PostingIterator largeIter;
            {
                var started = StartedIterators(largeTerms, resolve, skipMissing: true);
                if (started.Count == 0) yield break;
                if (started.Count == 1)
                {
                    largeIter = started[0];
                }
                else
                {
                    var union = new UnionIterator(started.ToArray());
                    if (!union.MoveNext()) yield break;
                    largeIter = union;
                }
            }

            // Leapfrog: for each doc on the small side, seek the large side to it.
            do
            {
                ct.ThrowIfCancellationRequested();
                int candidate = smallIter.Current;

                if (!largeIter.SkipTo(candidate)) yield break;

                if (largeIter.Current == candidate)
                    yield return candidate;
                // else: large jumped past candidate — loop advances small side next
            }
            while (smallIter.MoveNext());
        }

        /// <summary>
        /// Returns the total number of doc IDs across all terms in a group.
        /// Used to decide which group is "small" vs "large" for probe-intersect.
        /// </summary>
        private static int TotalGroupCount(
            IReadOnlyList<string> terms,
            Func<string, int>     getCount)
        {
            int total = 0;
            foreach (var term in terms)
            {
                int c = getCount(term);
                total += c;
                // Cap to avoid int overflow on huge wildcard groups.
                if (total < 0) return int.MaxValue;
            }
            return total;
        }

        /// <summary>
        /// Drains all posting lists for <paramref name="terms"/> into a
        /// <see cref="RoaringBitmap"/> and returns a <see cref="RoaringBitmapIterator"/>
        /// over the result. The iterator is NOT pre-advanced — callers must call
        /// MoveNext() before reading Current.
        ///
        /// Missing terms (resolve returns IsDone=true) are silently skipped.
        /// If no terms produce any doc IDs the returned iterator is immediately done.
        ///
        /// When the resolved iterator is itself a <see cref="RoaringBitmapIterator"/>
        /// (e.g. a cached sub-expansion), the underlying bitmap is merged via
        /// <see cref="RoaringBitmap.Or"/> which uses a SIMD bulk-OR loop over the
        /// 1024-word BitmapContainer arrays instead of per-doc <see cref="RoaringBitmap.Add"/>.
        /// </summary>
        private static RoaringBitmapIterator BuildRoaringIterator(
            IReadOnlyList<string>         terms,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct)
        {
            var bitmap = new RoaringBitmap();
            foreach (var term in terms)
            {
                ct.ThrowIfCancellationRequested();
                var it = resolve(term);
                if (it.IsDone) continue;

                // Fast path: if the resolved iterator wraps a RoaringBitmap (e.g. a
                // cached wildcard expansion), merge the whole bitmap in one SIMD OR
                // instead of calling Add() for every individual doc ID.
                if (it is RoaringBitmapIterator rbIter)
                {
                    bitmap.Or(rbIter.Bitmap);
                    continue;
                }

                // General path: drain the posting list one doc at a time.
                while (it.MoveNext())
                    bitmap.Add(it.Current);
            }
            return new RoaringBitmapIterator(bitmap);
        }

        private static IEnumerable<int> AndMerge(
            List<string>                  terms,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct)
        {
            var iters = new PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
            {
                iters[i] = resolve(terms[i]);
                if (iters[i].IsDone) yield break; // term not in index
            }

            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext()) yield break;

            foreach (var id in PostingMatcher.Intersect(iters, ct))
                yield return id;
        }

        private static List<PostingIterator> StartedIterators(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            bool                          skipMissing)
        {
            var result = new List<PostingIterator>();
            foreach (var term in terms)
            {
                var it = resolve(term);
                if (it.IsDone) { if (!skipMissing) return null; continue; }
                if (it.MoveNext()) result.Add(it);
            }
            return result;
        }

        /// <summary>
        /// Yields all values from a pre-advanced iterator (Current is already valid).
        /// Unlike <see cref="PostingIterator.AsEnumerable"/>, this does NOT call
        /// MoveNext before yielding the first value.
        /// </summary>
        private static IEnumerable<int> DrainStarted(PostingIterator it, CancellationToken ct)
        {
            do
            {
                ct.ThrowIfCancellationRequested();
                yield return it.Current;
            }
            while (it.MoveNext());
        }
    }
}
