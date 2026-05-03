using System;

namespace FtsEngine.Core
{
    /// <summary>
    /// Holds the compressed posting list for a single term.
    /// Uses a raw byte[] to avoid MemoryStream overhead.
    /// Entry IDs must be added in strictly ascending order.
    /// Identical to FtsLib.Codec.PostingStream — self-contained copy.
    /// </summary>
    public sealed class PostingStream
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

        public void Add(int entryId)
        {
            if (_hasLast && entryId <= _last)
                throw new ArgumentException(
                    $"Entry IDs must be added in strictly ascending order. Got {entryId} after {_last}.",
                    nameof(entryId));

            uint encoded = Encode(entryId);
            uint toWrite = _hasLast ? encoded - _lastEncoded : encoded;

            _last        = entryId;
            _lastEncoded = encoded;
            _hasLast     = true;
            _count++;

            WriteVarInt(toWrite);
        }

        public int NextByteOffset => _len;

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

        private void WriteVarInt(uint v)
        {
            while (v >= 0x80)
            {
                WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            WriteByte((byte)v);
        }

        private static uint Encode(int v) => (uint)((long)v - int.MinValue);
    }
}
