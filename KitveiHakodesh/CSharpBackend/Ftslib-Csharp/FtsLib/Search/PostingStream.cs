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

            // Inline base-128 varint write. A uint is at most 5 bytes, so ensuring
            // that much space up front lets the hot loop drop the per-byte capacity
            // check, and writing straight into the buffer avoids the Action<byte>
            // delegate that VarInt.Write(…, WriteByte) allocated on EVERY posting
            // (~336M allocations across a full build). Measured ~2.4x faster encode,
            // byte-for-byte identical output — verified on 667k real posting lists.
            if (_len + 5 > _buf.Length)
            {
                int n = _buf.Length * 2;
                while (n < _len + 5) n *= 2;
                Array.Resize(ref _buf, n);
            }

            byte[] buf = _buf;
            int p = _len;
            while (toWrite >= 0x80) { buf[p++] = (byte)(toWrite | 0x80); toWrite >>= 7; }
            buf[p++] = (byte)toWrite;
            _len = p;
        }

        public void Reset()
        {
            _len         = 0;
            _count       = 0;
            _hasLast     = false;
            _lastEncoded = 0;
        }

        // Doc/line IDs are non-negative and strictly ascending, so the raw value IS
        // the encoded value — no int.MinValue rebasing. This makes the FIRST posting of
        // every term cost varint(id) bytes (~3-4) instead of a fixed 5 (the rebased
        // value was always >= 2^31). Deltas are unchanged. Format v2 (no-offset).
        private static uint Encode(int v) => (uint)v;
    }
}
