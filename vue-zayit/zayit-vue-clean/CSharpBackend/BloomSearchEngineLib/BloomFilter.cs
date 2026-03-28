using System;
using System.Runtime.CompilerServices;

namespace MinimalIndexer
{
    public sealed class BloomFilter
    {
        private readonly int _bitCount;
        private readonly uint _bitMask;
        private readonly int[] _data;
        private readonly int _hashFunctions;
        private readonly bool _isPow2;

        public int Size => _bitCount;
        public int HashFunctions => _hashFunctions;

        public BloomFilter(int expectedItems, double fpRate)
        {
            int bits = NextPow2((int)(-(expectedItems * Math.Log(fpRate)) / (Math.Pow(Math.Log(2), 2))));
            _bitCount = bits;
            _isPow2 = true;
            _bitMask = (uint)bits - 1;
            _data = new int[(bits + 31) / 32];
            _hashFunctions = Math.Max(1, (int)Math.Round((bits / (double)expectedItems) * Math.Log(2)));
        }

        public BloomFilter(byte[] bytes, int bitCount, int hashFunctions)
        {
            _bitCount = bitCount;
            _hashFunctions = hashFunctions;
            _isPow2 = (bitCount & (bitCount - 1)) == 0 && bitCount > 0;
            _bitMask = _isPow2 ? (uint)bitCount - 1 : 0;
            _data = new int[(bitCount + 31) / 32];
            Buffer.BlockCopy(bytes, 0, _data, 0, Math.Min(bytes.Length, _data.Length * 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(string item)
        {
            uint h1 = Fnv(item), h2 = (h1 << 16) | (h1 >> 16);
            for (uint i = 0; i < (uint)_hashFunctions; i++)
            {
                int idx = _isPow2 ? (int)((h1 + i * h2) & _bitMask) : (int)((h1 + i * h2) % (uint)_bitCount);
                _data[idx >> 5] |= 1 << (idx & 31);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string item)
        {
            uint h1 = Fnv(item), h2 = (h1 << 16) | (h1 >> 16);
            for (uint i = 0; i < (uint)_hashFunctions; i++)
            {
                int idx = _isPow2 ? (int)((h1 + i * h2) & _bitMask) : (int)((h1 + i * h2) % (uint)_bitCount);
                if ((_data[idx >> 5] & (1 << (idx & 31))) == 0) return false;
            }
            return true;
        }

        public bool ContainsAll(string[] items) { foreach (var s in items) if (!Contains(s)) return false; return true; }

        public byte[] GetBytes() { var b = new byte[(_bitCount + 7) / 8]; Buffer.BlockCopy(_data, 0, b, 0, b.Length); return b; }
        public int GetByteSize() => (_bitCount + 7) / 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Fnv(string s) { uint h = 2166136261u; for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; } return h; }

        private static int NextPow2(int v) { if (v <= 0) return 1; v--; v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16; return v + 1; }
    }
}
