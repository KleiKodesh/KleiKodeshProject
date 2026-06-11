using System.Collections.Generic;
using System.Threading;

namespace FtsLib.Search
{
    /// <summary>
    /// Reusable merge algorithms over PostingIterator sequences.
    /// Both RamIndex and IndexReader delegate here so the logic lives in one place.
    ///
    /// All methods are static and allocation-free during iteration (beyond the
    /// iterator array itself, which callers supply).
    /// </summary>
    internal static class PostingMatcher
    {
        // ── AND merge ────────────────────────────────────────────────

        /// <summary>
        /// Skip-list-accelerated AND intersection.
        ///
        /// Precondition: all iterators have already been advanced once (MoveNext called,
        /// returned true). iters[0] is the rarest (smallest) list — it drives the loop.
        ///
        /// Zero heap allocation during iteration.
        /// </summary>
        public static IEnumerable<int> Intersect(PostingIterator[] iters, CancellationToken ct = default)
        {
            while (!iters[0].IsDone)
            {
                ct.ThrowIfCancellationRequested();

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

        // ── OR merge (min-heap) ──────────────────────────────────────

        /// <summary>
        /// Min-heap OR union: yields every doc ID that appears in at least one iterator,
        /// in ascending order with no duplicates.
        ///
        /// Precondition: all iterators have already been advanced once (MoveNext called,
        /// returned true).
        ///
        /// O(n log k) where n = total postings, k = number of iterators.
        /// Zero heap allocation during iteration beyond the fixed-size heap array.
        /// </summary>
        public static IEnumerable<int> Union(PostingIterator[] iters, CancellationToken ct = default)
        {
            int   heapSize = iters.Length;
            int[] heap     = new int[heapSize];
            for (int i = 0; i < heapSize; i++) heap[i] = i;
            for (int i = heapSize / 2 - 1; i >= 0; i--)
                SiftDown(heap, iters, i, heapSize);

            int lastYielded = int.MinValue;

            while (heapSize > 0)
            {
                ct.ThrowIfCancellationRequested();

                int topIdx = heap[0];
                int val    = iters[topIdx].Current;

                if (val != lastYielded)
                {
                    yield return val;
                    lastYielded = val;
                }

                if (iters[topIdx].MoveNext())
                {
                    SiftDown(heap, iters, 0, heapSize);
                }
                else
                {
                    heapSize--;
                    if (heapSize > 0)
                    {
                        heap[0] = heap[heapSize];
                        SiftDown(heap, iters, 0, heapSize);
                    }
                }
            }
        }

        // ── Heap helper ──────────────────────────────────────────────

        private static void SiftDown(int[] heap, PostingIterator[] iters, int i, int size)
        {
            while (true)
            {
                int smallest = i;
                int left     = (i << 1) + 1;
                int right    = left + 1;

                if (left  < size && iters[heap[left ]].Current < iters[heap[smallest]].Current)
                    smallest = left;
                if (right < size && iters[heap[right]].Current < iters[heap[smallest]].Current)
                    smallest = right;

                if (smallest == i) break;

                int tmp = heap[i]; heap[i] = heap[smallest]; heap[smallest] = tmp;
                i = smallest;
            }
        }
    }
}
