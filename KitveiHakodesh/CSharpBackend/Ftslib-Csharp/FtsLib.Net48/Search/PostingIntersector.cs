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
            Func<string, int>                getCount,
            RoaringBitmap                    filter = null,
            CancellationToken                ct = default)
        {
            var termLists = new List<IReadOnlyList<string>>();
            foreach (var group in groups)
            {
                var tl = group as IReadOnlyList<string> ?? new List<string>(group);
                if (tl.Count == 0) return Enumerable.Empty<int>();
                termLists.Add(tl);
            }
            if (termLists.Count == 0) return Enumerable.Empty<int>();

            // An empty (non-null) filter matches nothing — same semantics as
            // Lucene's TermInSetQuery over an empty set.
            if (filter != null && filter.Count == 0) return Enumerable.Empty<int>();

            // Estimated union size per group: sum of term doc-counts (an upper
            // bound; overlap only shrinks the true union). With the F01 chunk
            // cache these lookups are dictionary hits, not SQLite queries.
            // Needed by the candidate-driven path and by filter placement below.
            long[] est = null;
            if (termLists.Count >= 2 || filter != null)
            {
                est = new long[termLists.Count];
                for (int i = 0; i < termLists.Count; i++)
                {
                    long sum = 0;
                    foreach (var term in termLists[i]) sum += getCount(term);
                    est[i] = sum;
                }

                // F03: {small side} AND {huge OR group} — drive the intersection
                // from the small side's candidates instead of materializing the
                // huge union. With a filter, the filter itself is a candidate
                // source contender (its count is exact, not an upper bound).
                var candidateDriven = TryCandidateDrivenSearch(termLists, est, filter, resolve, getCount, ct);
                if (candidateDriven != null) return candidateDriven;
            }

            var groupIters = new List<PostingIterator>();
            foreach (var termList in termLists)
            {
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

            // The filter joins the intersection as one more AND clause (Lucene's
            // Occur.FILTER). Placement matters for speed, not correctness:
            // PostingMatcher.Intersect drives from iters[0], so a filter smaller
            // than every group leads the leap-frog and the group posting lists
            // get skip-jumped instead of fully decoded.
            if (filter != null)
            {
                var fIter = new RoaringBitmapIterator(filter);
                if (!fIter.MoveNext()) return Enumerable.Empty<int>();

                long minEst = long.MaxValue;
                foreach (var e in est) if (e < minEst) minEst = e;

                if (filter.Count <= minEst) groupIters.Insert(0, fIter);
                else                        groupIters.Add(fIter);
            }

            if (groupIters.Count == 1) return DrainStarted(groupIters[0], ct);
            return PostingMatcher.Intersect(groupIters.ToArray(), ct);
        }

        // ── Candidate-driven AND (F03) ───────────────────────────────

        /// <summary>
        /// The small side's estimated union must be at least this many times
        /// smaller than the biggest huge group's estimate for the candidate-driven
        /// path to engage. Probing costs roughly one skip-jump per (term, candidate)
        /// while draining costs ~one decode per posting, so a wide margin keeps the
        /// path strictly cheaper even when the early exit never fires.
        /// </summary>
        internal const int SmallSideRatio = 64;

        /// <summary>
        /// The huge group's estimated union must also clear this absolute floor.
        /// Probing pays a fixed per-term overhead (iterator setup, chunk load,
        /// skip re-entry) across a possibly full tail walk; that only amortizes
        /// when the drain being avoided is multi-million postings with fat head
        /// terms whose skip tables get leveraged. Measured: below ~1M the drain
        /// is already tens of ms and probing shows a small net tax.
        /// </summary>
        internal const long MinUnionEstimateForProbing = 1_000_000;

        /// <summary>
        /// Lower floor used when the candidate source is a caller-supplied ID
        /// filter. Unlike a query group, the filter's candidates cost nothing to
        /// materialize (no segment IO, no union drain), so the break-even point
        /// sits far lower: the probe only has to beat draining the huge group's
        /// union, not also pay for building its own candidate set.
        /// </summary>
        internal const long MinUnionEstimateForFilterProbing = 100_000;

        /// <summary>
        /// Candidate-driven mixed AND (F03): when one side is tiny and another is
        /// a huge OR expansion (e.g. <c>*כי* ביצחק</c> — 2.6k docs vs a 27.5k-term
        /// union of millions of postings), materializing the huge union just to
        /// intersect it away is almost all waste. Instead: materialize the SMALL
        /// side as a sorted candidate array, then filter the candidates through
        /// every other group — probing huge groups term-by-term (highest doc-count
        /// first) and stopping the moment every candidate is confirmed.
        ///
        /// The small side is either the smallest query group or, when present and
        /// smaller, the caller-supplied ID <paramref name="filter"/> — whose count
        /// is exact rather than an upper bound, and whose candidates cost nothing
        /// to materialize (no segment IO). A small filter therefore engages this
        /// path even for a single-group query.
        ///
        /// Semantics are exact: a candidate passes a group iff it appears in at
        /// least one of the group's terms, and a group's terms are consulted until
        /// every candidate is matched or no terms remain — candidates are never
        /// excluded early. Results stay ascending because the candidate array is.
        ///
        /// Returns null when the shape does not apply (no huge group, or the
        /// small side is not decisively small) — the caller falls through to
        /// the standard materialization path.
        /// </summary>
        private static IEnumerable<int> TryCandidateDrivenSearch(
            List<IReadOnlyList<string>>   groups,
            long[]                        est,
            RoaringBitmap                 filter,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct)
        {
            int  smallest   = -1;
            long maxHugeEst = 0;
            bool anyHuge    = false;

            for (int i = 0; i < groups.Count; i++)
            {
                if (smallest < 0 || est[i] < est[smallest]) smallest = i;
                if (groups[i].Count >= RoaringOrThreshold)
                {
                    anyHuge = true;
                    if (est[i] > maxHugeEst) maxHugeEst = est[i];
                }
            }

            if (!anyHuge) return null;
            // Every term of the smallest group is missing → empty intersection
            // (same outcome the standard path produces via StartedIterators).
            if (est[smallest] == 0) return Enumerable.Empty<int>();

            // Prefer the filter as candidate source when it is at least as small
            // as the smallest group's estimate — its candidates are free.
            if (filter != null && filter.Count <= est[smallest] &&
                maxHugeEst >= MinUnionEstimateForFilterProbing &&
                (long)filter.Count * SmallSideRatio <= maxHugeEst)
            {
                return CandidateDrivenSearch(groups, est, -1, filter, resolve, getCount, ct);
            }

            // Group-sourced path (original F03 rules).
            if (maxHugeEst < MinUnionEstimateForProbing) return null;
            // The candidate side must itself be cheap to materialize.
            if (groups[smallest].Count >= RoaringOrThreshold) return null;
            if (est[smallest] * SmallSideRatio > maxHugeEst) return null;

            return CandidateDrivenSearch(groups, est, smallest, filter, resolve, getCount, ct);
        }

        private static IEnumerable<int> CandidateDrivenSearch(
            List<IReadOnlyList<string>>   groups,
            long[]                        est,
            int                           sourceGroup, // -1 = candidates come from the filter
            RoaringBitmap                 filter,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct)
        {
            int[] candidates;
            int   count;

            if (sourceGroup < 0)
            {
                // Filter-sourced: the bitmap already holds the sorted candidate set.
                candidates = new int[filter.Count];
                count      = 0;
                foreach (var v in filter.GetValues()) candidates[count++] = v;
            }
            else
            {
                candidates = MaterializeGroup(groups[sourceGroup], resolve, ct);
                count      = candidates.Length;

                // Apply the filter first — it is pure in-memory work, so shrinking
                // the candidate set here makes every segment probe below cheaper.
                if (count > 0 && filter != null)
                {
                    var fIter = new RoaringBitmapIterator(filter);
                    count = fIter.MoveNext()
                        ? FilterByIterator(fIter, candidates, count, ct)
                        : 0;
                }
            }
            if (count == 0) yield break;

            // Filter through the remaining groups, cheapest first so the candidate
            // set is smallest by the time the expensive probes run.
            var order = new List<int>();
            for (int i = 0; i < groups.Count; i++)
                if (i != sourceGroup) order.Add(i);
            order.Sort((a, b) => est[a].CompareTo(est[b]));

            foreach (int gi in order)
            {
                count = groups[gi].Count >= RoaringOrThreshold
                    ? FilterByTermProbing(groups[gi], candidates, count, resolve, getCount, ct)
                    : FilterByGroupIterator(groups[gi], candidates, count, resolve, ct);
                if (count == 0) yield break;
            }

            for (int i = 0; i < count; i++)
                yield return candidates[i];
        }

        /// <summary>
        /// Drains one group's full union into a sorted, distinct doc-ID array,
        /// using the same per-group construction as the standard MixedSearch path.
        /// </summary>
        private static int[] MaterializeGroup(
            IReadOnlyList<string>         termList,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct)
        {
            PostingIterator it;
            if (termList.Count >= RoaringOrThreshold)
            {
                it = BuildRoaringIterator(termList, resolve, ct);
                if (!it.MoveNext()) return new int[0];
            }
            else
            {
                var started = StartedIterators(termList, resolve, skipMissing: true);
                if (started.Count == 0) return new int[0];
                if (started.Count == 1)
                {
                    it = started[0];
                }
                else
                {
                    var union = new UnionIterator(started.ToArray());
                    if (!union.MoveNext()) return new int[0];
                    it = union;
                }
            }

            // Iterator is pre-advanced (Current valid). Streams are ascending;
            // duplicates across a union's sub-iterators are removed here.
            var list = new List<int>();
            int last = int.MinValue;
            do
            {
                ct.ThrowIfCancellationRequested();
                int v = it.Current;
                if (v != last || list.Count == 0) { list.Add(v); last = v; }
            }
            while (it.MoveNext());
            return list.ToArray();
        }

        /// <summary>
        /// Keeps only the candidates present in the group's union, by one forward
        /// SkipTo pass over the group iterator (both sides ascending).
        /// Compacts the candidate array in place; returns the surviving count.
        /// </summary>
        private static int FilterByGroupIterator(
            IReadOnlyList<string>         termList,
            int[]                         candidates,
            int                           count,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct)
        {
            PostingIterator it;
            var started = StartedIterators(termList, resolve, skipMissing: true);
            if (started.Count == 0) return 0;
            if (started.Count == 1) it = started[0];
            else
            {
                var union = new UnionIterator(started.ToArray());
                if (!union.MoveNext()) return 0;
                it = union;
            }

            return FilterByIterator(it, candidates, count, ct);
        }

        /// <summary>
        /// Keeps only the candidates present in <paramref name="it"/> (pre-advanced,
        /// ascending), by one forward SkipTo pass — both sides ascending.
        /// Compacts the candidate array in place; returns the surviving count.
        /// </summary>
        private static int FilterByIterator(
            PostingIterator   it,
            int[]             candidates,
            int               count,
            CancellationToken ct)
        {
            int w = 0;
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int c = candidates[i];
                // SkipTo is a no-op when Current >= c already (returns true).
                if (!it.SkipTo(c)) break;         // iterator exhausted — rest all fail
                if (it.Current == c) candidates[w++] = c;
            }
            return w;
        }

        /// <summary>
        /// Keeps only the candidates present in at least one of the group's terms.
        /// Probes term-by-term, highest doc-count first (high-df terms confirm most
        /// candidates immediately), and stops consuming terms as soon as every
        /// candidate is matched. A candidate matching no term forces all terms to
        /// be consulted — correctness requires it; the probe cost per term is
        /// bounded by min(term postings, remaining candidates) skip-jumps, so even
        /// that worst case never exceeds the cost of draining the full union.
        /// Compacts the candidate array in place; returns the surviving count.
        /// </summary>
        private static int FilterByTermProbing(
            IReadOnlyList<string>         termList,
            int[]                         candidates,
            int                           count,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct)
        {
            // Precompute counts once. Calling getCount inside the sort comparator
            // would cost O(n log n) delegate calls — ~800k for a 27.5k-term group.
            var sorted = new (string term, int count)[termList.Count];
            for (int i = 0; i < termList.Count; i++)
                sorted[i] = (termList[i], getCount(termList[i]));
            Array.Sort(sorted, (a, b) => b.count.CompareTo(a.count));

            // pending = indices of candidates not yet matched, ascending. Each term
            // probes only these, and the list compacts as candidates confirm — after
            // the first high-df terms it is typically tiny, so walking a long tail
            // of rare terms costs one chunk load each, not an O(count) scan each.
            var matched      = new bool[count];
            var pending      = new int[count];
            int pendingCount = count;
            for (int i = 0; i < count; i++) pending[i] = i;

            foreach (var (term, termCount) in sorted)
            {
                ct.ThrowIfCancellationRequested();
                if (termCount == 0) break;        // sorted desc — only missing terms remain
                var it = resolve(term);
                if (it.IsDone) continue;

                int keep = 0;
                for (int j = 0; j < pendingCount; j++)
                {
                    int idx = pending[j];
                    int c   = candidates[idx];
                    if (!it.SkipTo(c))
                    {
                        // Term exhausted — every later candidate stays pending.
                        Array.Copy(pending, j, pending, keep, pendingCount - j);
                        keep += pendingCount - j;
                        break;
                    }
                    if (it.Current == c) matched[idx] = true;
                    else                 pending[keep++] = idx;
                }
                pendingCount = keep;

                if (pendingCount == 0) break;     // all candidates confirmed — early exit
            }

            int w = 0;
            for (int i = 0; i < count; i++)
                if (matched[i]) candidates[w++] = candidates[i];
            return w;
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

                // General path: bulk-drain the posting list (F05) — one tight
                // decode loop instead of two virtual calls per posting.
                it.DrainInto(bitmap);
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
