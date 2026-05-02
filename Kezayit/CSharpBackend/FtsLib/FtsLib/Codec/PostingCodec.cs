using System.Collections.Generic;
using System.IO;

namespace FtsLib.Codec
{
    /// <summary>
    /// Delta + varint codec for posting lists.
    /// Encodes a sorted sequence of entry IDs (any int value) as delta-compressed varints.
    ///
    /// Negative IDs are supported by remapping the full int range to uint before encoding:
    ///   encoded = (uint)(value - int.MinValue)
    /// This preserves sort order, so deltas are always non-negative as long as IDs are
    /// added in ascending order.
    /// </summary>
    internal static class PostingCodec
    {
        // Shift the signed int range onto [0, uint.MaxValue] to keep deltas non-negative.
        private static uint Encode(int value) => (uint)(value - (long)int.MinValue);
        private static int  Decode(uint value) => (int)(value + (long)int.MinValue);

        public static void Write(Stream stream, int value, ref int last, ref bool hasLast)
        {
            uint encoded = Encode(value);

            if (!hasLast)
            {
                WriteVarInt(stream, encoded);
                last    = value;
                hasLast = true;
                return;
            }

            uint delta = encoded - Encode(last);
            WriteVarInt(stream, delta);
            last = value;
        }

        public static IEnumerable<int> Read(Stream stream)
        {
            stream.Position = 0;

            if (stream.Length == 0)
                yield break;

            uint encoded = ReadVarInt(stream);
            int  last    = Decode(encoded);
            yield return last;

            while (stream.Position < stream.Length)
            {
                uint delta = ReadVarInt(stream);
                encoded += delta;
                last     = Decode(encoded);
                yield return last;
            }
        }

        private static void WriteVarInt(Stream stream, uint value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        private static uint ReadVarInt(Stream stream)
        {
            int  shift  = 0;
            uint result = 0;

            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException();

                result |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    break;

                shift += 7;
            }

            return result;
        }
    }
}
