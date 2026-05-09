using System;
using System.Runtime.CompilerServices;

namespace MinimalIndexer
{
    /// <summary>
    /// Standard Bloom filter with double-hashing (Kirsch-Mitzenmacher).
    ///
    /// Layout
    /// ------
    /// A flat bit array of m bits, addressed directly. No block structure —
    /// this avoids the 256-bit minimum block size of the SBBF which wastes
    /// space for small item counts (the dominant case at 50 lines/chunk).
    ///
    /// Hash function
    /// -------------
    /// xxHash64 split into two 32-bit halves h1, h2.
    /// The k-th probe position is (h1 + k*h2) % m — Kirsch-Mitzenmacher
    /// double hashing, proven to achieve the same FP rate as k independent
    /// hash functions while requiring only two hash evaluations total.
    ///
    /// Space
    /// -----
    /// m = ceil(-n * ln(ε) / ln(2)²) bits, k = ceil(-log2(ε)) hash functions.
    /// At ε=0.001: ~14.4 bits/item, k=10. No block-rounding waste.
    ///
    /// Serialization
    /// -------------
    /// GetBytes() returns the raw bit array as bytes (ceil(m/8) bytes).
    /// The .dat header stores (size=m, hashFunctions=k).
    /// Magic: hashFunctions is always in range [1..20] for a valid standard
    /// Bloom filter. The old SBBF always wrote hashFunctions=8 with
    /// bitCount % 256 == 0. The validator in SearchHandler detects the old
    /// format and forces a rebuild.
    /// </summary>
    public sealed class BloomFilter
    {
        private readonly byte[] _bits;
        private readonly int    _m;       // total bit count
        private readonly int    _k;       // number of hash probes

        public int Size          => _m;
        public int HashFunctions => _k;

        // ── Constructor: new filter (used during indexing) ────────────────────────────
        public BloomFilter(int expectedItems, double fpRate)
        {
            if (expectedItems < 1) expectedItems = 1;
            if (fpRate <= 0.0 || fpRate >= 1.0) fpRate = 0.01;

            // Optimal m and k for standard Bloom filter.
            double ln2 = Math.Log(2);
            double rawBits = -(expectedItems * Math.Log(fpRate)) / (ln2 * ln2);
            _m = Math.Max(8, (int)Math.Ceiling(rawBits));
            _k = Math.Max(1, (int)Math.Round(-Math.Log(fpRate) / ln2));

            int byteCount = (_m + 7) / 8;
            _bits = new byte[byteCount];
        }

        // ── Constructor: load from file ───────────────────────────────────────────────
        public BloomFilter(byte[] bytes, int bitCount, int hashFunctions)
        {
            if (bitCount <= 0)
                throw new InvalidOperationException(
                    "Bloom filter file has invalid bit count (" + bitCount + "). Rebuild the index.");
            if (hashFunctions <= 0 || hashFunctions > 20)
                throw new InvalidOperationException(
                    "Bloom filter file has unexpected hash function count (" + hashFunctions +
                    "). Delete the .dat file and rebuild the index.");

            _m = bitCount;
            _k = hashFunctions;

            int expectedBytes = (_m + 7) / 8;
            if (bytes == null || bytes.Length < expectedBytes)
                throw new InvalidOperationException(
                    "Bloom filter data is truncated (expected " + expectedBytes +
                    " bytes, got " + (bytes?.Length ?? 0) + "). Rebuild the index.");

            _bits = new byte[expectedBytes];
            Buffer.BlockCopy(bytes, 0, _bits, 0, expectedBytes);
        }

        // ── Add ───────────────────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(string item)
        {
            ulong hash = XxHash64(item);
            uint h1 = (uint)(hash >> 32);
            uint h2 = (uint)hash | 1u;   // force odd for full-period stepping
            uint m  = (uint)_m;
            for (int i = 0; i < _k; i++)
            {
                uint pos = h1 % m;
                _bits[pos >> 3] |= (byte)(1 << (int)(pos & 7));
                h1 += h2;
            }
        }

        // ── Contains ─────────────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string item)
        {
            ulong hash = XxHash64(item);
            uint h1 = (uint)(hash >> 32);
            uint h2 = (uint)hash | 1u;
            uint m  = (uint)_m;
            for (int i = 0; i < _k; i++)
            {
                uint pos = h1 % m;
                if ((_bits[pos >> 3] & (1 << (int)(pos & 7))) == 0) return false;
                h1 += h2;
            }
            return true;
        }

        public bool ContainsAll(string[] items)
        {
            foreach (var s in items)
                if (!Contains(s)) return false;
            return true;
        }

        // ── Serialization ─────────────────────────────────────────────────────────────
        public byte[] GetBytes()
        {
            var b = new byte[_bits.Length];
            Buffer.BlockCopy(_bits, 0, b, 0, b.Length);
            return b;
        }

        public int GetByteSize() => _bits.Length;

        // ── xxHash64 ──────────────────────────────────────────────────────────────────
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 =  1609587929392839161UL;
        private const ulong Prime4 =  9650029242287828579UL;
        private const ulong Prime5 =  2870177450012600261UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Lane(string s, int i)
            => (ulong)s[i]
             | ((ulong)s[i + 1] << 16)
             | ((ulong)s[i + 2] << 32)
             | ((ulong)s[i + 3] << 48);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong XxHash64(string s)
        {
            int   len = s.Length;
            ulong h;

            if (len >= 16)
            {
                ulong v1 = unchecked(Prime1 + Prime2);
                ulong v2 = Prime2;
                ulong v3 = 0UL;
                ulong v4 = unchecked(0UL - Prime1);

                int i = 0;
                for (; i <= len - 16; i += 16)
                {
                    v1 = Round(v1, Lane(s, i));
                    v2 = Round(v2, Lane(s, i + 4));
                    v3 = Round(v3, Lane(s, i + 8));
                    v4 = Round(v4, Lane(s, i + 12));
                }

                h = unchecked(Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18));
                h = MergeRound(h, v1);
                h = MergeRound(h, v2);
                h = MergeRound(h, v3);
                h = MergeRound(h, v4);
                h += (ulong)(len * 2);

                for (; i <= len - 4; i += 4)
                {
                    h ^= Round(0, Lane(s, i));
                    h  = unchecked(Rotl(h, 27) * Prime1 + Prime4);
                }
                for (; i < len; i++)
                {
                    h ^= unchecked((ulong)s[i] * Prime5);
                    h  = unchecked(Rotl(h, 11) * Prime1);
                }
            }
            else
            {
                h = Prime5 + (ulong)(len * 2);

                int j = 0;
                for (; j <= len - 4; j += 4)
                {
                    h ^= Round(0, Lane(s, j));
                    h  = unchecked(Rotl(h, 27) * Prime1 + Prime4);
                }
                for (; j < len; j++)
                {
                    h ^= unchecked((ulong)s[j] * Prime5);
                    h  = unchecked(Rotl(h, 11) * Prime1);
                }
            }

            h ^= h >> 33;
            h  = unchecked(h * Prime2);
            h ^= h >> 29;
            h  = unchecked(h * Prime3);
            h ^= h >> 32;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Round(ulong acc, ulong lane)
            => unchecked(Rotl(acc + lane * Prime2, 31) * Prime1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound(ulong acc, ulong val)
            => unchecked((acc ^ Round(0, val)) * Prime1 + Prime4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rotl(ulong v, int r) => (v << r) | (v >> (64 - r));
    }
}
