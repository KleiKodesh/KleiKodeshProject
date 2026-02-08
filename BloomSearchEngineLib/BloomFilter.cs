using System;
using System.Runtime.CompilerServices;

namespace MinimalIndexer
{
    public sealed class BloomFilter
    {
        private readonly int _bitCount;
        private readonly uint _bitMask;
        private readonly BitArray _bits;
        private readonly int _hashFunctions;
        private readonly bool _isPowerOfTwo;

        public int Size => _bitCount;  // Size in BITS
        public int HashFunctions => _hashFunctions;

        // Constructor for creating new filter
        public BloomFilter(int expectedItems, double falsePositiveRate)
        {
            if (expectedItems <= 0) throw new ArgumentException("Expected items must be positive.");
            if (falsePositiveRate <= 0 || falsePositiveRate >= 1) throw new ArgumentException("False positive rate must be between 0 and 1.");

            int calculatedBits = (int)(-(expectedItems * Math.Log(falsePositiveRate)) / (Math.Pow(Math.Log(2), 2)));

            // Round to next power of 2 for faster modulo via bitmasking
            _bitCount = NextPowerOfTwo(calculatedBits);
            _isPowerOfTwo = true;
            _bitMask = (uint)_bitCount - 1;

            _bits = new BitArray(_bitCount);
            _hashFunctions = (int)Math.Round((_bitCount / (double)expectedItems) * Math.Log(2));

            // Ensure at least 1 hash function
            if (_hashFunctions < 1) _hashFunctions = 1;
        }

        // Constructor for loading from disk
        // CRITICAL: serializedData is BYTE array, but bitCount is in BITS
        public BloomFilter(byte[] serializedData, int bitCount, int hashFunctions)
        {
            _bitCount = bitCount;
            _hashFunctions = hashFunctions;
            _isPowerOfTwo = IsPowerOfTwo(bitCount);
            _bitMask = _isPowerOfTwo ? (uint)bitCount - 1 : 0;
            _bits = new BitArray(serializedData, bitCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(string item)
        {
            uint hash1 = GetStableHash(item);
            uint hash2 = RotateLeft(hash1, 16); // Faster than shift/or combination

            if (_isPowerOfTwo)
            {
                for (uint i = 0; i < (uint)_hashFunctions; i++)
                {
                    uint hash = hash1 + i * hash2;
                    int index = (int)(hash & _bitMask); // Bitwise AND instead of modulo
                    _bits.SetBit(index);
                }
            }
            else
            {
                for (uint i = 0; i < (uint)_hashFunctions; i++)
                {
                    uint hash = hash1 + i * hash2;
                    int index = (int)(hash % (uint)_bitCount);
                    _bits.SetBit(index);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string item)
        {
            uint hash1 = GetStableHash(item);
            uint hash2 = RotateLeft(hash1, 16);

            if (_isPowerOfTwo)
            {
                for (uint i = 0; i < (uint)_hashFunctions; i++)
                {
                    uint hash = hash1 + i * hash2;
                    int index = (int)(hash & _bitMask);
                    if (!_bits.GetBit(index))
                        return false;
                }
            }
            else
            {
                for (uint i = 0; i < (uint)_hashFunctions; i++)
                {
                    uint hash = hash1 + i * hash2;
                    int index = (int)(hash % (uint)_bitCount);
                    if (!_bits.GetBit(index))
                        return false;
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(string[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (!Contains(items[i]))
                    return false;
            }
            return true;
        }

        public byte[] GetBytes()
        {
            return _bits.ToByteArray();
        }

        // Helper method to get the actual byte size for metadata storage
        public int GetByteSize()
        {
            return (_bitCount + 7) / 8;
        }

        // FNV-1a hash - much faster than MD5 and sufficient for Bloom filters
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetStableHash(string str)
        {
            uint hash = 2166136261u; // FNV offset basis

            for (int i = 0; i < str.Length; i++)
            {
                hash ^= str[i];
                hash *= 16777619u; // FNV prime
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;

            return value;
        }

        private sealed class BitArray
        {
            private readonly int[] _data;
            private readonly int _bitCount;

            public BitArray(int bitCount)
            {
                _bitCount = bitCount;
                _data = new int[(bitCount + 31) / 32];
            }

            // Constructor for loading from byte array
            // bytes: the serialized byte array
            // bitCount: number of BITS (not bytes!)
            public BitArray(byte[] bytes, int bitCount)
            {
                _bitCount = bitCount;
                _data = new int[(bitCount + 31) / 32];

                // Convert bytes directly to int
                Buffer.BlockCopy(bytes, 0, _data, 0, Math.Min(bytes.Length, _data.Length * 4));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBit(int index)
            {
                _data[index >> 5] |= 1 << (index & 31);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool GetBit(int index)
            {
                return (_data[index >> 5] & (1 << (index & 31))) != 0;
            }

            public byte[] ToByteArray()
            {
                int byteCount = (_bitCount + 7) / 8;
                byte[] bytes = new byte[byteCount];
                Buffer.BlockCopy(_data, 0, bytes, 0, byteCount);
                return bytes;
            }
        }
    }
}