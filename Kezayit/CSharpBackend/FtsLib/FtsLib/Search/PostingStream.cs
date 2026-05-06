using System;

namespace FtsLib.Search
{
    /// <summary>
    /// Compressed posting list for a single term.
    /// Stores delta+varint encoded doc IDs in a raw byte[].
    /// IDs must be added in strictly ascending order.
    /// </summary>
    internal sealed class PostingStream
    {
        private byte[] _buf = new byte[8];
        private int    _len;
        private int    _count;
        private int    _last;
        private uint   _lastEncoded;
        private bool   _hasLast;

        public int    ByteLength  => _len;
        public int    Count       => _count;
        public uint   LastEncoded => _lastEncoded;
        public byte[] Buffer      => _buf;

        /// <summary>Byte offset at which the next Add will write — used by skip list.</summary>
        public int NextByteOffset => _len;

        public void Add(int entryId)
        {
            if (_hasLast && entryId <= _last)
                throw new ArgumentException(
                    $"IDs must be strictly ascending. Got {entryId} after {_last}.",
                    nameof(entryId));

            uint encoded = Encode(entryId);
            uint toWrite = _hasLast ? encoded - _lastEncoded : encoded;

            _last        = entryId;
            _lastEncoded = encoded;
            _hasLast     = true;
            _count++;

            VarInt.Write(toWrite, WriteByte);
        }

        public void Reset()
        {
            _len         = 0;
            _count       = 0;
            _hasLast     = false;
            _lastEncoded = 0;
        }

        internal void WriteByte(byte b)
        {
            if (_len == _buf.Length)
                Array.Resize(ref _buf, _buf.Length * 2);
            _buf[_len++] = b;
        }

        private static uint Encode(int v) => (uint)((long)v - int.MinValue);
    }
}
