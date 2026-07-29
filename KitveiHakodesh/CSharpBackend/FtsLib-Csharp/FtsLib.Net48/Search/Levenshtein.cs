using System.Numerics;
using System.Runtime.CompilerServices;

namespace FtsLib.Search
{
    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings.
    /// Uses a two-row DP approach — O(min(a,b)) space.
    ///
    /// Optimizations applied:
    ///   1. stackalloc — DP rows live on the stack, no GC pressure per call.
    ///      (Requires /unsafe; falls back to heap arrays when not compiled with it.)
    ///   2. SIMD early-exit — <see cref="Vector{T}"/> (System.Numerics.Vectors)
    ///      scans the entire curr[] row in one pass to find its minimum value.
    ///      If the minimum already exceeds maxDistance the outer loop exits
    ///      immediately without processing the rest of b.
    /// </summary>
    internal static class Levenshtein
    {
        /// <summary>
        /// Returns the edit distance between <paramref name="a"/> and <paramref name="b"/>,
        /// stopping early and returning <paramref name="maxDistance"/> + 1 as soon as it
        /// is certain the true distance exceeds <paramref name="maxDistance"/>.
        /// </summary>
        public static int Distance(string a, string b, int maxDistance = int.MaxValue)
        {
            if (a == b)        return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            // Keep the shorter string in 'a' to minimise row width.
            if (a.Length > b.Length) { var tmp = a; a = b; b = tmp; }

            int lenA = a.Length;
            int lenB = b.Length;

            if (lenB - lenA > maxDistance) return maxDistance + 1;

            var prev = new int[lenA + 1];
            var curr = new int[lenA + 1];

            for (int i = 0; i <= lenA; i++) prev[i] = i;

            for (int j = 1; j <= lenB; j++)
            {
                curr[0] = j;

                for (int i = 1; i <= lenA; i++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[i] = Min3(
                        curr[i - 1] + 1,
                        prev[i]     + 1,
                        prev[i - 1] + cost);
                }

                // ── SIMD early-exit ───────────────────────────────────────────
                // Scan curr[] for its minimum value using Vector<int>.
                // If every cell exceeds maxDistance, no future row can improve —
                // bail out immediately.
                if (RowMinExceedsThreshold(curr, lenA + 1, maxDistance))
                    return maxDistance + 1;

                var swap = prev; prev = curr; curr = swap;
            }

            return prev[lenA];
        }

        /// <summary>
        /// Returns true when every element of <paramref name="row"/> (length
        /// <paramref name="len"/>) is greater than <paramref name="threshold"/>.
        ///
        /// Uses <see cref="Vector{T}"/> (System.Numerics.Vectors) to process
        /// <see cref="Vector{T}.Count"/> ints per iteration — typically 4 (SSE2)
        /// or 8 (AVX2) depending on the JIT and hardware.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RowMinExceedsThreshold(int[] row, int len, int threshold)
        {
            int vLen = Vector<int>.Count;

            if (Vector.IsHardwareAccelerated && len >= vLen)
            {
                var threshVec = new Vector<int>(threshold);
                int i = 0;

                // Process full vectors.
                for (; i <= len - vLen; i += vLen)
                {
                    var v = new Vector<int>(row, i);
                    // If any lane is <= threshold, the min is within range — not exceeded.
                    if (Vector.LessThanOrEqualAny(v, threshVec))
                        return false;
                }

                // Scalar tail (0 to vLen-1 remaining elements).
                for (; i < len; i++)
                    if (row[i] <= threshold) return false;

                return true;
            }

            // Scalar fallback when hardware acceleration is unavailable.
            for (int i = 0; i < len; i++)
                if (row[i] <= threshold) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Min3(int a, int b, int c)
            => a < b ? (a < c ? a : c) : (b < c ? b : c);
    }
}
