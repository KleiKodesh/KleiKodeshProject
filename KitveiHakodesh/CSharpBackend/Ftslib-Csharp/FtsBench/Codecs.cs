using System;

namespace FtsBench
{
    /// <summary>
    /// Candidate encoder: byte-identical output to FtsLib.Search.PostingStream, but the
    /// varint is written INLINE into the buffer — no per-byte Action&lt;byte&gt; delegate
    /// (the production Add allocates one delegate per posting) and one capacity check
    /// per posting instead of per byte.
    /// </summary>
    internal sealed class TightPostingStream
    {
        private byte[] _buf = new byte[8];
        private int    _len;
        private int    _count;
        private int    _last;
        private uint   _lastEncoded;
        private bool   _hasLast;

        public int    ByteLength => _len;
        public int    Count      => _count;
        public byte[] Buffer     => _buf;

        public void Add(int entryId)
        {
            if (_hasLast && entryId <= _last)
                throw new ArgumentException("IDs must be strictly ascending.");

            uint encoded = (uint)((long)entryId - int.MinValue);
            uint toWrite = _hasLast ? encoded - _lastEncoded : encoded;

            _last        = entryId;
            _lastEncoded = encoded;
            _hasLast     = true;
            _count++;

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
            _len = 0; _count = 0; _hasLast = false; _lastEncoded = 0;
        }
    }

    /// <summary>Group-varint (4-value groups, 2-bit length codes) over the id delta stream.</summary>
    internal static class GroupVarint
    {
        public static byte[] Encode(int[] ids)
        {
            int n = ids.Length;
            var buf = new byte[(n + 3) / 4 + n * 4 + 4];
            int p = 0, i = 0;
            int prev = 0;
            bool first = true;
            while (i < n)
            {
                int g = Math.Min(4, n - i);
                int cpos = p++;
                int ctrl = 0;
                for (int j = 0; j < g; j++)
                {
                    int id = ids[i + j];
                    uint v = first ? (uint)id : (uint)(id - prev);
                    prev = id; first = false;

                    int b = v < (1u << 8) ? 1 : v < (1u << 16) ? 2 : v < (1u << 24) ? 3 : 4;
                    ctrl |= (b - 1) << (j * 2);
                    for (int t = 0; t < b; t++) buf[p++] = (byte)(v >> (t * 8));
                }
                buf[cpos] = (byte)ctrl;
                i += g;
            }
            Array.Resize(ref buf, p);
            return buf;
        }

        public static void Decode(byte[] buf, int n, int[] outIds)
        {
            int p = 0, i = 0, prev = 0;
            bool first = true;
            while (i < n)
            {
                int g = Math.Min(4, n - i);
                int ctrl = buf[p++];
                for (int j = 0; j < g; j++)
                {
                    int len = ((ctrl >> (j * 2)) & 3) + 1;
                    uint v = 0;
                    for (int t = 0; t < len; t++) v |= (uint)buf[p++] << (t * 8);
                    prev = first ? (int)v : prev + (int)v;
                    first = false;
                    outIds[i + j] = prev;
                }
                i += g;
            }
        }
    }
}
