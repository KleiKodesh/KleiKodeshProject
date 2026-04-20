using System;
using System.Runtime.CompilerServices;

namespace MinimalIndexer
{
    /// <summary>
    /// Split Block Bloom Filter (SBBF) — same structure used by DuckDB / Apache Parquet.
    ///
    /// Layout
    /// ------
    /// The bit array is divided into 256-bit blocks (8 × uint32 words).
    /// Each Add/Contains touches exactly ONE block, selected by the upper 32 bits of an
    /// xxHash64 digest.  Within that block, 8 independent bit positions are derived from
    /// the lower 32 bits using the 8 Parquet salt multipliers.  Every probe therefore
    /// touches exactly one 32-byte region — one cache line — regardless of filter size.
    ///
    /// Hash function
    /// -------------
    /// xxHash64 (64-bit).  Upper 32 bits → block index.  Lower 32 bits → intra-block
    /// probes via the 8 salt multipliers.  The two halves are statistically independent,
    /// giving a true ~1 % FP rate at ~10.5 bits/item.
    ///
    /// Serialization
    /// -------------
    /// GetBytes() / GetByteSize() return the raw uint[] block array as bytes.
    /// The .dat header stores (bitCount = blockCount * 256, hashFunctions = 8).
    /// The load constructor asserts both invariants so a corrupt or mismatched file
    /// fails loudly rather than silently producing wrong results.
    /// </summary>
    public sealed class BloomFilter
    {
        // Parquet SBBF salt multipliers — one per word in a 256-bit block.
        private static readonly uint[] Salts =
        {
            0x47b6137bU, 0x44974d91U, 0x8824ad5bU, 0xa2b7289dU,
            0x705495c7U, 0x2df1424bU, 0x9efc4947U, 0x5c6bfb31U
        };

        private const int WordsPerBlock    = 8;    // 8 × uint32 = 256 bits = one cache line
        private const int BitsPerBlock     = 256;
        private const int HashFunctionCount = 8;   // always 8 for SBBF

        private readonly uint[] _blocks;           // flat: blockCount × 8 words
        private readonly int    _blockCount;
        private readonly int    _bitCount;         // blockCount * 256  (stored in .dat header)

        // Public surface — writer/reader depend on these two properties.
        public int Size          => _bitCount;
        public int HashFunctions => HashFunctionCount;

        // ── Constructor: new filter (used during indexing) ────────────────────────────
        public BloomFilter(int expectedItems, double fpRate)
        {
            // Guard against degenerate inputs.
            if (expectedItems < 1) expectedItems = 1;
            // Clamp fpRate to a range where Math.Log is finite and negative.
            if (fpRate <= 0.0 || fpRate >= 1.0) fpRate = 0.01;

            double rawBits = -(expectedItems * Math.Log(fpRate)) / (Math.Log(2) * Math.Log(2));

            // Round up to the next whole number of 256-bit blocks; minimum 1 block.
            int blockCount = Math.Max(1, (int)Math.Ceiling(rawBits / BitsPerBlock));

            _blockCount = blockCount;
            _bitCount   = blockCount * BitsPerBlock;
            _blocks     = new uint[blockCount * WordsPerBlock];
        }

        // ── Constructor: load from file ───────────────────────────────────────────────
        public BloomFilter(byte[] bytes, int bitCount, int hashFunctions)
        {
            if (hashFunctions != HashFunctionCount)
                throw new InvalidOperationException(
                    "Bloom filter file was built with a different hash function count (" + hashFunctions +
                    "). Delete the .dat file and rebuild the index.");

            if (bitCount <= 0 || bitCount % BitsPerBlock != 0)
                throw new InvalidOperationException(
                    "Bloom filter file has an unexpected bit count (" + bitCount +
                    "). Delete the .dat file and rebuild the index.");

            _bitCount   = bitCount;
            _blockCount = bitCount / BitsPerBlock;
            _blocks     = new uint[_blockCount * WordsPerBlock];

            int expectedBytes = _blocks.Length * 4;
            if (bytes == null || bytes.Length < expectedBytes)
                throw new InvalidOperationException(
                    "Bloom filter data is truncated (expected " + expectedBytes +
                    " bytes, got " + (bytes?.Length ?? 0) + "). Rebuild the index.");

            Buffer.BlockCopy(bytes, 0, _blocks, 0, expectedBytes);
        }

        // ── Add ───────────────────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(string item)
        {
            ulong h     = XxHash64(item);
            int   block = BlockIndex(h) * WordsPerBlock;
            uint  lower = (uint)h;
            for (int w = 0; w < WordsPerBlock; w++)
                _blocks[block + w] |= 1u << (int)((lower * Salts[w]) >> 27);
        }

        // ── Contains ─────────────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string item)
        {
            ulong h     = XxHash64(item);
            int   block = BlockIndex(h) * WordsPerBlock;
            uint  lower = (uint)h;
            for (int w = 0; w < WordsPerBlock; w++)
                if ((_blocks[block + w] & (1u << (int)((lower * Salts[w]) >> 27))) == 0)
                    return false;
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
            var b = new byte[_blocks.Length * 4];
            Buffer.BlockCopy(_blocks, 0, b, 0, b.Length);
            return b;
        }

        public int GetByteSize() => _blocks.Length * 4;

        // ── Helpers ───────────────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int BlockIndex(ulong h)
            => (int)((uint)(h >> 32) % (uint)_blockCount);

        // ── xxHash64 ──────────────────────────────────────────────────────────────────
        // Hashes the string's UTF-16 code units treated as raw bytes (2 bytes per char).
        // Follows the standard xxHash64 streaming spec:
        //   - Large path (len >= 16 chars): 4 independent accumulators, 16 chars per turn.
        //   - Short path (len < 16 chars):  single accumulator with 4-char and 1-char steps.
        //   - Empty string: returns a valid constant (Prime5 after avalanche).
        // Hebrew words are typically 2–8 chars, so the short path dominates in practice.
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 =  1609587929392839161UL;
        private const ulong Prime4 =  9650029242287828579UL;
        private const ulong Prime5 =  2870177450012600261UL;

        // Pack 4 consecutive UTF-16 chars into one uint64 lane.
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
                // 4 independent accumulators; consume 16 chars (32 bytes) per iteration.
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

                // Remaining full 4-char groups.
                for (; i <= len - 4; i += 4)
                {
                    h ^= Round(0, Lane(s, i));
                    h  = unchecked(Rotl(h, 27) * Prime1 + Prime4);
                }
                // Remaining individual chars.
                for (; i < len; i++)
                {
                    h ^= unchecked((ulong)s[i] * Prime5);
                    h  = unchecked(Rotl(h, 11) * Prime1);
                }
            }
            else
            {
                // Short path — covers empty string, 1-char, 2-char … 15-char inputs.
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

            // Final avalanche mix.
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
