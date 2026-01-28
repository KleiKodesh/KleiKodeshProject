using System;

namespace MinimalIndexer
{
    internal class BloomFilter
    {
        private readonly int _bitCount;  // Renamed for clarity - this is in BITS
        private readonly BitArray _bits;
        private readonly int _hashFunctions;

        internal int Size => _bitCount;  // Size in BITS
        internal int HashFunctions => _hashFunctions;

        // Constructor for creating new filter
        internal BloomFilter(int expectedItems, double falsePositiveRate)
        {
            if (expectedItems <= 0) throw new ArgumentException("Expected items must be positive.");
            if (falsePositiveRate <= 0 || falsePositiveRate >= 1) throw new ArgumentException("False positive rate must be between 0 and 1.");

            _bitCount = (int)(-(expectedItems * Math.Log(falsePositiveRate)) / (Math.Pow(Math.Log(2), 2)));
            _bits = new BitArray(_bitCount);
            _hashFunctions = (int)Math.Round((_bitCount / (double)expectedItems) * Math.Log(2));
        }

        // Constructor for loading from disk
        // CRITICAL: serializedData is BYTE array, but bitCount is in BITS
        internal BloomFilter(byte[] serializedData, int bitCount, int hashFunctions)
        {
            _bitCount = bitCount;
            _hashFunctions = hashFunctions;
            _bits = new BitArray(serializedData, bitCount);
        }

        internal void Add(string item)
        {
            uint hash1 = (uint)item.GetHashCode();
            uint hash2 = (hash1 << 16) | (hash1 >> 16);

            for (uint i = 0; i < (uint)_hashFunctions; i++)
            {
                uint hash = hash1 + i * hash2;
                int index = (int)(hash % (uint)_bitCount);
                _bits.SetBit(index);
            }
        }

        internal bool Contains(string item)
        {
            uint hash1 = (uint)item.GetHashCode();
            uint hash2 = (hash1 << 16) | (hash1 >> 16);

            for (uint i = 0; i < (uint)_hashFunctions; i++)
            {
                uint hash = hash1 + i * hash2;
                int index = (int)(hash % (uint)_bitCount);
                if (!_bits.GetBit(index))
                    return false;
            }
            return true;
        }

        internal bool ContainsAll(string[] items)
        {
            foreach (var item in items)
                if (!Contains(item))
                    return false;
            return true;
        }

        internal byte[] GetBytes()
        {
            return _bits.ToByteArray();
        }

        // Helper method to get the actual byte size for metadata storage
        internal int GetByteSize()
        {
            return (_bitCount + 7) / 8;
        }

        private class BitArray
        {
            private readonly int[] _data;
            private readonly int _bitCount;

            internal BitArray(int bitCount)
            {
                _bitCount = bitCount;
                _data = new int[(bitCount + 31) / 32];
            }

            // Constructor for loading from byte array
            // bytes: the serialized byte array
            // bitCount: number of BITS (not bytes!)
            internal BitArray(byte[] bytes, int bitCount)
            {
                _bitCount = bitCount;
                _data = new int[(bitCount + 31) / 32];

                // Convert bytes directly to ints
                Buffer.BlockCopy(bytes, 0, _data, 0, Math.Min(bytes.Length, _data.Length * 4));
            }

            internal void SetBit(int index)
            {
                _data[index >> 5] |= 1 << (index & 31);
            }

            internal bool GetBit(int index)
            {
                return (_data[index >> 5] & (1 << (index & 31))) != 0;
            }

            internal byte[] ToByteArray()
            {
                int byteCount = (_bitCount + 7) / 8;
                byte[] bytes = new byte[byteCount];
                Buffer.BlockCopy(_data, 0, bytes, 0, byteCount);
                return bytes;
            }
        }
    }
}