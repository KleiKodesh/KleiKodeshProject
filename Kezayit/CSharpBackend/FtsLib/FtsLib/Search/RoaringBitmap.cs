using System;
using System.Collections.Generic;
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
    ///                     OR is a tight loop over 1024 ulongs — SIMD-friendly.
    ///
    /// The crossover at 4096 is exact: at that point both containers cost 8 KB,
    /// but the bitmap is faster for OR and iteration, so we switch there.
    ///
    /// This implementation supports only the operations needed by the FTS pipeline:
    ///   - Add(int docId)          — insert a single doc ID
    ///   - GetEnumerator()         — iterate all doc IDs in ascending order
    ///   - Count                   — total number of doc IDs stored
    ///
    /// Thread safety: not thread-safe. Use from a single thread.
    /// </summary>
    internal sealed class RoaringBitmap
    {
        // Threshold at which an ArrayContainer is promoted to a BitmapContainer.
        // At exactly 4096 values both containers cost 8 KB; the bitmap is faster
        // for OR and iteration above this point.
        private const int PromotionThreshold = 4096;

        // Sparse index: block key (high 16 bits) → container index in _containers.
        // Kept sorted by key so iteration yields doc IDs in ascending order.
        private readonly List<ushort>    _keys       = new List<ushort>();
        private readonly List<Container> _containers = new List<Container>();

        /// <summary>Total number of doc IDs stored across all containers.</summary>
        public int Count { get; private set; }

        // ── Mutation ──────────────────────────────────────────────────

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
                // New block — start with an ArrayContainer.
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
                {
                    // Promote to BitmapContainer.
                    _containers[idx] = array.ToBitmapContainer();
                }
            }
            else
            {
                added = ((BitmapContainer)existing).Add(low);
            }

            if (added) Count++;
        }

        // ── Iteration ─────────────────────────────────────────────────

        /// <summary>
        /// Enumerates all doc IDs in ascending order.
        /// </summary>
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

        /// <summary>
        /// Returns the index of <paramref name="key"/> in <c>_keys</c>, or the
        /// bitwise complement of the insertion point if not found (same contract
        /// as <see cref="Array.BinarySearch"/>).
        /// </summary>
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
        }

        // ── Array container ───────────────────────────────────────────

        /// <summary>
        /// Sorted array of ushort values. Used when cardinality &lt; 4096.
        /// Add is O(n) due to insertion-sort, but blocks are small and the
        /// amortised cost is acceptable for the OR-accumulation pattern.
        /// </summary>
        private sealed class ArrayContainer : Container
        {
            // Over-allocate to reduce copies during the fill phase.
            private ushort[] _values = new ushort[64];
            private int      _count;

            public override int Cardinality => _count;

            public override bool Add(ushort value)
            {
                // Binary search for insertion point.
                int lo = 0, hi = _count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_values[mid] == value) return false; // duplicate
                    if (_values[mid] < value)  lo = mid + 1;
                    else                       hi = mid - 1;
                }
                // lo is the insertion point.
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
        /// 65536-bit flat bitset stored as 1024 ulongs.
        /// Used when cardinality ≥ 4096.
        /// OR of two bitmap containers is a tight loop over 1024 ulongs.
        /// </summary>
        private sealed class BitmapContainer : Container
        {
            private readonly ulong[] _bits = new ulong[1024]; // 65536 bits = 8 KB
            private int _cardinality;

            public override int Cardinality => _cardinality;

            public override bool Add(ushort value)
            {
                int  word = value >> 6;
                ulong mask = 1UL << (value & 63);
                if ((_bits[word] & mask) != 0) return false; // already set
                _bits[word] |= mask;
                _cardinality++;
                return true;
            }

            public override IEnumerable<int> GetValues()
            {
                for (int word = 0; word < 1024; word++)
                {
                    ulong w = _bits[word];
                    if (w == 0) continue;
                    int basePos = word << 6;
                    while (w != 0)
                    {
                        // Isolate lowest set bit.
                        ulong lsb = w & (ulong)(-(long)w);
                        yield return basePos + PopCount64Below(lsb);
                        w ^= lsb;
                    }
                }
            }

            /// <summary>
            /// Returns the bit position of the single set bit in <paramref name="lsb"/>.
            /// Equivalent to BitOperations.TrailingZeroCount on .NET 5+, but we target
            /// .NET 4.8 so we use a De Bruijn sequence lookup instead.
            /// </summary>
            private static int PopCount64Below(ulong lsb)
            {
                // De Bruijn sequence for 64-bit trailing zero count.
                // This is a standard technique for .NET 4.x where BitOperations is unavailable.
                return _debruijn64Table[((ulong)((long)lsb * 0x03F79D71B4CA8B09L)) >> 58];
            }

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
