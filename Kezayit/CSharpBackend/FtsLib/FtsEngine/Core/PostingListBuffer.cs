using System.IO;

namespace FtsEngine.Core
{
    /// <summary>
    /// Accumulates doc IDs for a single term during the merge phase.
    /// Encodes them as delta+varint directly into a raw byte[] buffer.
    /// Reused across terms — call Begin() to reset for the next term.
    /// </summary>
    internal sealed class PostingListBuffer
    {
        private byte[] _buf = new byte[64];
        private int    _len;
        private int    _count;
        private uint   _lastEncoded;
        private bool   _hasLast;

        public string CurrentTerm { get; private set; }
        public int    Count       => _count;
        public int    ByteLength  => _len;

        public void Begin(string term)
        {
            CurrentTerm  = term;
            _len         = 0;
            _count       = 0;
            _hasLast     = false;
            _lastEncoded = 0;
        }

        public void Add(int docId)
        {
            uint encoded = Encode(docId);
            uint toWrite = _hasLast ? encoded - _lastEncoded : encoded;
            _lastEncoded = encoded;
            _hasLast     = true;
            _count++;
            WriteVarInt(toWrite);
        }

        /// <summary>Appends bytes to postings.bin. Returns (offset, length, count).</summary>
        public (long offset, int length, int count) Flush(Stream postings)
        {
            long offset = postings.Position;
            postings.Write(_buf, 0, _len);
            return (offset, _len, _count);
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

        private void WriteByte(byte b)
        {
            if (_len == _buf.Length)
                System.Array.Resize(ref _buf, _buf.Length * 2);
            _buf[_len++] = b;
        }

        private static uint Encode(int v) => (uint)((long)v - int.MinValue);
    }
}
