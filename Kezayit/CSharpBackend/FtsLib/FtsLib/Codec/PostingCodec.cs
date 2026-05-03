using System.Collections.Generic;

namespace FtsLib.Codec
{
    /// <summary>
    /// Delta + varint codec for posting lists. Algorithm unchanged —
    /// only the write target changed from Stream to PostingStream's byte[].
    /// </summary>
    internal static class PostingCodec
    {
        // Shift signed int range onto [0, uint.MaxValue] so deltas are always non-negative.
        private static uint Encode(int value) => (uint)((long)value - int.MinValue);
        private static int  Decode(uint value) => (int)((long)value + int.MinValue);

        // ---- Write ----

        public static void Write(PostingStream ps, int value, ref int last, ref bool hasLast)
        {
            uint encoded = Encode(value);

            if (!hasLast)
            {
                WriteVarInt(ps, encoded);
                last    = value;
                hasLast = true;
                return;
            }

            WriteVarInt(ps, encoded - Encode(last));
            last = value;
        }

        private static void WriteVarInt(PostingStream ps, uint value)
        {
            while (value >= 0x80)
            {
                ps.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            ps.WriteByte((byte)value);
        }

        // ---- Read ----

        public static IEnumerable<int> Read(byte[] buf, int len)
        {
            if (len == 0) yield break;

            int  pos     = 0;
            uint encoded = ReadVarInt(buf, ref pos, len);
            int  last    = Decode(encoded);
            yield return last;

            while (pos < len)
            {
                uint delta = ReadVarInt(buf, ref pos, len);
                encoded += delta;
                last     = Decode(encoded);
                yield return last;
            }
        }

        private static uint ReadVarInt(byte[] buf, ref int pos, int len)
        {
            int  shift  = 0;
            uint result = 0;
            while (pos < len)
            {
                byte b = buf[pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }
    }
}
