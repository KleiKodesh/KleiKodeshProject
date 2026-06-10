using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FtsLib.Search
{
    /// <summary>
    /// A 32-bit Roaring bitmap optimised for the OR-accumulation pattern used during
    /// wildcard and fuzzy query expansion.
    ///
    /// The 32-bit doc ID space is split into 65536 blocks of 65536 values each.
    /// The high 16 bits of a doc ID select the block; the low 16 bits are the
    /// position within the block.
    ///
    /// Each block independently chooses its storage format:
    ///
    ///   ArrayContainer  — used when the block holds fewer than 4096 values.
    ///                     Stores the low-16 values as a sorted ushort[].
    ///                     Cost: 2 bytes per value.
    ///
    ///   BitmapContainer — used when the block holds 4096 or more values.
    ///                     Stores a flat 65536-bit array (1024 × ulong).
    ///                     Cost: always 8 KB regardless of cardinality.
    ///
    ///                     SIMD optimizations (System.Numerics.Vectors):
    ///                       • Or()        — ORs two 1024-ulong arrays in chunks of
    ///                                       Vector&lt;ulong&gt;.Count (4 on SSE2, 8 on AVX2).
    ///                       • GetValues() — skips zero-word runs with a Vector&lt;ulong&gt;
    ///                                       all-zero test before falling back to the
    ///                                       per-bit De Bruijn loop.
    ///
    /// The crossover at 4096 is exact: at that point both containers cost 8 KB,
    /// but the bitmap is faster for OR and iteration, so we switch there.
    ///
    /// This implementation supports only the operations needed by the FTS pipeline:
    ///   - Add(int docId)          — insert a single doc ID
    ///   - Or(RoaringBitmap other) — bulk-merge another bitmap into this one
    ///   - GetValues()             — iterate all doc IDs in ascending order
    ///   - Count                   — total number of doc IDs stored
    ///
    /// Thread safety: not thread-safe. Use from a single thread.
    /// </summary>
    internal sealed class RoaringBitmap
    {
        // Threshold at which an ArrayContainer is promoted to a BitmapContainer.
        private const int PromotionThreshold = 4096;

        // Sparse index: block key (high 16 bits) → container index in _containers.
        // Kept sorted by key so iteration yields doc IDs in ascending order.
        private readonly List<ushort>    _keys       = new List<ushort>();
        private readonly List<Container> _containers = new List<Container>();

        /// <summary>Total number of doc IDs stored across all containers.</summary>
        public int Count { get; private set; }

        // ── Single-value insert ───────────────────────────────────────

        /// <summary>
        /// Inserts <paramref name="docId"/> into the bitmap.
        /// Duplicate inserts are silently ignored.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int docId)
        {
            ushort high = (ushort)((uint)docId >> 16);
            ushort low  = (ushort)((uint)docId & 0xFFFF);

            int idx = FindKey(high);
            if (idx < 0)
            {
                idx = ~idx;
                _keys.Insert(idx, high);
                var container = new ArrayContainer();
                container.Add(low);
                _containers.Insert(idx, container);
                Count++;
                return;
            }

            var existing = _containers[idx];
            bool added;

            if (existing is ArrayContainer array)
            {
                added = array.Add(low);
                if (added && array.Cardinality == PromotionThreshold)
                    _containers[idx] = array.ToBitmapContainer();
            }
            else
            {
                added = ((BitmapContainer)existing).Add(low);
            }

            if (added) Count++;
        }

        // ── Bulk OR merge ─────────────────────────────────────────────

        /// <summary>
        /// Merges all doc IDs from <paramref name="other"/> into this bitmap.
        ///
        /// When both the source and destination block are <see cref="BitmapContainer"/>s
        /// the merge is done with a SIMD loop over <see cref="Vector{T}.Count"/> ulongs
        /// at a time (4 on SSE2 / 8 on AVX2), which is the fastest possible path for
        /// dense blocks.  Mixed and sparse cases fall back to per-value <see cref="Add"/>.
        /// </summary>
        public void Or(RoaringBitmap other)
        {
            for (int ob = 0; ob < other._keys.Count; ob++)
            {
                ushort key      = other._keys[ob];
                var    otherCon = other._containers[ob];

                int idx = FindKey(key);

                if (idx < 0)
                {
                    // Block doesn't exist in this bitmap yet — clone the other container.
                    idx = ~idx;
                    _keys.Insert(idx, key);
                    _containers.Insert(idx, otherCon.Clone());
                    Count += otherCon.Cardinality;
                    continue;
                }

                var thisCon = _containers[idx];

                // Both BitmapContainers: SIMD bulk OR.
                if (thisCon is BitmapContainer thisBm && otherCon is BitmapContainer otherBm)
                {
                    int before = thisBm.Cardinality;
                    thisBm.OrWith(otherBm);
                    Count += thisBm.Cardinality - before;
                    continue;
                }

                // All other combinations: fall back to per-value Add.
                // This covers Array+Array, Array+Bitmap, Bitmap+Array.
                // Array+Array is rare (both blocks sparse) and cheap enough scalar.
                foreach (int low in otherCon.GetValues())
                {
                    bool added;
                    if (thisCon is ArrayContainer arr)
                    {
                        added = arr.Add((ushort)low);
                        if (added && arr.Cardinality == PromotionThreshold)
                        {
                            _containers[idx] = arr.ToBitmapContainer();
                            thisCon = _containers[idx];
                        }
                    }
                    else
                    {
                        added = ((BitmapContainer)thisCon).Add((ushort)low);
                    }
                    if (added) Count++;
                }
            }
        }

        // ── Iteration ─────────────────────────────────────────────────

        /// <summary>Enumerates all doc IDs in ascending order.</summary>
        public IEnumerable<int> GetValues()
        {
            for (int b = 0; b < _keys.Count; b++)
            {
                int baseValue = _keys[b] << 16;
                foreach (int low in _containers[b].GetValues())
                    yield return baseValue | low;
            }
        }

        // ── Binary search over sorted key list ────────────────────────

        private int FindKey(ushort key)
        {
            int lo = 0, hi = _keys.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ushort k = _keys[mid];
                if (k == key) return mid;
                if (k < key)  lo = mid + 1;
                else          hi = mid - 1;
            }
            return ~lo;
        }

        // ── Container base ────────────────────────────────────────────

        private abstract class Container
        {
            public abstract int  Cardinality { get; }
            public abstract bool Add(ushort value);
            public abstract IEnumerable<int> GetValues();
            public abstract Container Clone();
        }

        // ── Array container ───────────────────────────────────────────

        /// <summary>
        /// Sorted array of ushort values. Used when cardinality &lt; 4096.
        /// </summary>
        private sealed class ArrayContainer : Container
        {
            private ushort[] _values = new ushort[64];
            private int      _count;

            public override int Cardinality => _count;

            public override bool Add(ushort value)
            {
                int lo = 0, hi = _count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_values[mid] == value) return false;
                    if (_values[mid] < value)  lo = mid + 1;
                    else                       hi = mid - 1;
                }
                EnsureCapacity();
                if (lo < _count)
                    Array.Copy(_values, lo, _values, lo + 1, _count - lo);
                _values[lo] = value;
                _count++;
                return true;
            }

            public override IEnumerable<int> GetValues()
            {
                for (int i = 0; i < _count; i++)
                    yield return _values[i];
            }

            public override Container Clone()
            {
                var c = new ArrayContainer();
                c._values = (ushort[])_values.Clone();
                c._count  = _count;
                return c;
            }

            public BitmapContainer ToBitmapContainer()
            {
                var bm = new BitmapContainer();
                for (int i = 0; i < _count; i++)
                    bm.Add(_values[i]);
                return bm;
            }

            private void EnsureCapacity()
            {
                if (_count < _values.Length) return;
                int newSize = Math.Min(_values.Length * 2, PromotionThreshold);
                Array.Resize(ref _values, newSize);
            }
        }

        // ── Bitmap container ──────────────────────────────────────────

        /// <summary>
        /// 65536-bit flat bitset stored as 1024 ulongs (8 KB).
        /// Used when cardinality ≥ 4096.
        ///
        /// SIMD operations:
        ///   <see cref="OrWith"/>   — bulk OR using <see cref="Vector{T}"/> over the
        ///                            1024-word array; processes Count/vLen words per cycle.
        ///   <see cref="GetValues"/> — skips zero-word runs with a Vector all-zero test
        ///                            before entering the per-bit De Bruijn loop.
        /// </summary>
        private sealed class BitmapContainer : Container
        {
            private readonly ulong[] _bits = new ulong[1024];
            private int _cardinality;

            public override int Cardinality => _cardinality;

            public override bool Add(ushort value)
            {
                int   word = value >> 6;
                ulong mask = 1UL << (value & 63);
                if ((_bits[word] & mask) != 0) return false;
                _bits[word] |= mask;
                _cardinality++;
                return true;
            }

            // ── SIMD bulk OR ──────────────────────────────────────────

            /// <summary>
            /// ORs all 1024 words of <paramref name="other"/> into this container
            /// using <see cref="Vector{T}"/> (System.Numerics.Vectors).
            ///
            /// On SSE2 (Vector&lt;ulong&gt;.Count == 2) this processes 2 words per cycle.
            /// On AVX2 (Count == 4) it processes 4 words per cycle.
            /// The cardinality is recomputed from scratch after the OR because
            /// counting only the newly-set bits would require a popcount loop anyway.
            /// </summary>
            public void OrWith(BitmapContainer other)
            {
                int vLen = Vector<ulong>.Count;
                int i    = 0;

                if (Vector.IsHardwareAccelerated)
                {
                    for (; i <= 1024 - vLen; i += vLen)
                    {
                        var va = new Vector<ulong>(_bits,       i);
                        var vb = new Vector<ulong>(other._bits, i);
                        Vector.BitwiseOr(va, vb).CopyTo(_bits, i);
                    }
                }

                // Scalar tail (0 to vLen-1 remaining words).
                for (; i < 1024; i++)
                    _bits[i] |= other._bits[i];

                // Recount cardinality.
                _cardinality = CountBits();
            }

            // ── SIMD-accelerated iteration ────────────────────────────

            /// <summary>
            /// Yields all set bit positions (0–65535) in ascending order.
            ///
            /// Uses <see cref="Vector{T}"/> to test <see cref="Vector{T}.Count"/> words
            /// at a time for all-zero; only non-zero words enter the per-bit De Bruijn
            /// loop.  On a sparse bitmap this skips large zero runs in one comparison.
            /// </summary>
            public override IEnumerable<int> GetValues()
            {
                int vLen = Vector<ulong>.Count;
                var zero = Vector<ulong>.Zero;
                int word = 0;

                if (Vector.IsHardwareAccelerated)
                {
                    for (; word <= 1024 - vLen; word += vLen)
                    {
                        var v = new Vector<ulong>(_bits, word);
                        // If the entire chunk is zero, skip it without entering the inner loop.
                        if (v == zero) continue;

                        // At least one word in this chunk is non-zero — process each word.
                        for (int w = word; w < word + vLen; w++)
                        {
                            ulong bits = _bits[w];
                            if (bits == 0) continue;
                            int basePos = w << 6;
                            while (bits != 0)
                            {
                                ulong lsb = bits & (ulong)(-(long)bits);
                                yield return basePos + TrailingZeroCount64(lsb);
                                bits ^= lsb;
                            }
                        }
                    }
                }

                // Scalar tail.
                for (; word < 1024; word++)
                {
                    ulong bits = _bits[word];
                    if (bits == 0) continue;
                    int basePos = word << 6;
                    while (bits != 0)
                    {
                        ulong lsb = bits & (ulong)(-(long)bits);
                        yield return basePos + TrailingZeroCount64(lsb);
                        bits ^= lsb;
                    }
                }
            }

            public override Container Clone()
            {
                var c = new BitmapContainer();
                Array.Copy(_bits, c._bits, 1024);
                c._cardinality = _cardinality;
                return c;
            }

            // ── Helpers ───────────────────────────────────────────────

            private int CountBits()
            {
                int count = 0;
                for (int i = 0; i < 1024; i++)
                    count += PopCount64(_bits[i]);
                return count;
            }

            /// <summary>
            /// Counts set bits in a 64-bit word.
            /// Uses the standard bit-twiddling approach (Hamming weight).
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int PopCount64(ulong v)
            {
                // Parallel bit count — standard Hamming weight algorithm.
                v -= (v >> 1) & 0x5555555555555555UL;
                v  = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
                v  = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((v * 0x0101010101010101UL) >> 56);
            }

            /// <summary>
            /// Returns the position of the single set bit in <paramref name="lsb"/>
            /// (i.e. trailing zero count).  Uses a De Bruijn sequence lookup —
            /// the standard technique for .NET 4.x where BitOperations is unavailable.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int TrailingZeroCount64(ulong lsb)
                => _debruijn64Table[((ulong)((long)lsb * 0x03F79D71B4CA8B09L)) >> 58];

            private static readonly int[] _debruijn64Table = BuildDebruijn64Table();

            private static int[] BuildDebruijn64Table()
            {
                var table = new int[64];
                for (int i = 0; i < 64; i++)
                    table[((ulong)(1UL << i) * 0x03F79D71B4CA8B09UL) >> 58] = i;
                return table;
            }
        }
    }
}
