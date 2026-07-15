using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FtsBench
{
    /// <summary>
    /// Popcount over a ulong[] bitset (RoaringBitmap BitmapContainer is 1024 words).
    ///
    /// Scalar    = the exact Hamming-weight loop production uses in CountBits().
    /// HarleySeal = portable SIMD popcount via System.Numerics.Vector&lt;ulong&gt;
    ///              (carry-save-adder tree; NO shuffle intrinsics, so it runs on net48).
    ///              Reduces ~1024 scalar popcounts to ~80 by folding 16 vectors per pass.
    /// </summary>
    internal static class PopCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Hamming64(ulong v)
        {
            v -= (v >> 1) & 0x5555555555555555UL;
            v  = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v  = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }

        /// <summary>Reference scalar popcount — identical to production CountBits().</summary>
        public static int Scalar(ulong[] bits)
        {
            int c = 0;
            for (int i = 0; i < bits.Length; i++) c += Hamming64(bits[i]);
            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<ulong> Csa(ref Vector<ulong> l, Vector<ulong> a, Vector<ulong> b, Vector<ulong> c)
        {
            var u = a ^ b;
            var newL = u ^ c;
            var h = (a & b) | (u & c);
            l = newL;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopVec(Vector<ulong> v)
        {
            int lanes = Vector<ulong>.Count;
            int c = 0;
            for (int k = 0; k < lanes; k++) c += Hamming64(v[k]);
            return c;
        }

        public static int HarleySeal(ulong[] bits)
        {
            if (!Vector.IsHardwareAccelerated) return Scalar(bits);

            int lanes = Vector<ulong>.Count;      // 4 on AVX2, 2 on SSE2
            int totalVecs = bits.Length / lanes;  // 256 (AVX2) or 512 (SSE2) for 1024 words

            var ones = Vector<ulong>.Zero;
            var twos = Vector<ulong>.Zero;
            var fours = Vector<ulong>.Zero;
            var eights = Vector<ulong>.Zero;
            long total = 0;

            Vector<ulong> twosA = default, twosB = default, foursA = default, foursB = default,
                          eightsA = default, eightsB = default, sixteens = default;

            int v = 0;
            for (; v + 16 <= totalVecs; v += 16)
            {
                twosA = Csa(ref ones, ones, V(bits, v + 0, lanes), V(bits, v + 1, lanes));
                twosB = Csa(ref ones, ones, V(bits, v + 2, lanes), V(bits, v + 3, lanes));
                foursA = Csa(ref twos, twos, twosA, twosB);
                twosA = Csa(ref ones, ones, V(bits, v + 4, lanes), V(bits, v + 5, lanes));
                twosB = Csa(ref ones, ones, V(bits, v + 6, lanes), V(bits, v + 7, lanes));
                foursB = Csa(ref twos, twos, twosA, twosB);
                eightsA = Csa(ref fours, fours, foursA, foursB);
                twosA = Csa(ref ones, ones, V(bits, v + 8, lanes), V(bits, v + 9, lanes));
                twosB = Csa(ref ones, ones, V(bits, v + 10, lanes), V(bits, v + 11, lanes));
                foursA = Csa(ref twos, twos, twosA, twosB);
                twosA = Csa(ref ones, ones, V(bits, v + 12, lanes), V(bits, v + 13, lanes));
                twosB = Csa(ref ones, ones, V(bits, v + 14, lanes), V(bits, v + 15, lanes));
                foursB = Csa(ref twos, twos, twosA, twosB);
                eightsB = Csa(ref fours, fours, foursA, foursB);
                sixteens = Csa(ref eights, eights, eightsA, eightsB);
                total += PopVec(sixteens);
            }

            total *= 16;
            total += 8 * PopVec(eights);
            total += 4 * PopVec(fours);
            total += 2 * PopVec(twos);
            total += 1 * PopVec(ones);

            // Scalar tail for any vectors past the 16-block boundary, then any word tail.
            int wordTail = v * lanes;
            for (int i = wordTail; i < bits.Length; i++) total += Hamming64(bits[i]);

            return (int)total;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<ulong> V(ulong[] bits, int vecIndex, int lanes)
            => new Vector<ulong>(bits, vecIndex * lanes);
    }
}
