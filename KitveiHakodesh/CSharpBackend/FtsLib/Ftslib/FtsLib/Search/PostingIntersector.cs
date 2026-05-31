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
            CancellationToken                ct = default)
        {
            var groupIters = new List<PostingIterator>();
            foreach (var group in groups)
            {
                var termList = group as IReadOnlyList<string> ?? new List<string>(group);
                if (termList.Count == 0) return Enumerable.Empty<int>();

                PostingIterator groupIter;

                if (termList.Count >= RoaringOrThreshold)
                {
                    // Large OR group — materialise into a Roaring bitmap.
                    groupIter = BuildRoaringIterator(termList, resolve, ct);
                    if (groupIter.IsDone) return Enumerable.Empty<int>();
                    // RoaringBitmapIterator requires an explicit MoveNext before use
                    // in PostingMatcher.Intersect (pre-advanced contract).
                    if (!groupIter.MoveNext()) return Enumerable.Empty<int>();
                }
                else
                {
                    var started = StartedIterators(termList, resolve, skipMissing: true);
                    if (started.Count == 0) return Enumerable.Empty<int>();
                    if (started.Count == 1)
                    {
                        groupIter = started[0]; // already pre-advanced by StartedIterators
                    }
                    else
                    {
                        // UnionIterator is not pre-advanced — advance it now so it is
                        // consistent with the single-iterator case and with the
                        // pre-advanced contract expected by PostingMatcher.Intersect
                        // and DrainStarted.
                        var union = new UnionIterator(started.ToArray());
                        if (!union.MoveNext()) continue; // all sub-iterators exhausted
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
