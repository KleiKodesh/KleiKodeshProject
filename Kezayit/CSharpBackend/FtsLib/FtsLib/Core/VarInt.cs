namespace FtsLib.Core
{
    /// <summary>
    /// Little-endian base-128 varint codec.
    /// Each byte holds 7 data bits; the high bit signals "more bytes follow".
    /// A uint encodes in at most 5 bytes.
    /// All methods are static and allocation-free.
    /// </summary>
    internal static class VarInt
    {
        // ── Write ─────────────────────────────────────────────────────

        /// <summary>
        /// Writes a varint by invoking <paramref name="writeByte"/> for each byte.
        /// Used by PostingStream which manages its own growable buffer.
        /// </summary>
        public static void Write(uint v, System.Action<byte> writeByte)
        {
            while (v >= 0x80) { writeByte((byte)(v | 0x80)); v >>= 7; }
            writeByte((byte)v);
        }

        /// <summary>
        /// Encodes a varint into <paramref name="buf"/> starting at index 0.
        /// Returns the number of bytes written (1–5).
        /// </summary>
        public static int Encode(uint v, byte[] buf)
        {
            int i = 0;
            while (v >= 0x80) { buf[i++] = (byte)(v | 0x80); v >>= 7; }
            buf[i++] = (byte)v;
            return i;
        }

        // ── Read ──────────────────────────────────────────────────────

        /// <summary>
        /// Decodes a varint from <paramref name="buf"/> at <paramref name="pos"/>,
        /// advancing <paramref name="pos"/> past the bytes consumed.
        /// </summary>
        public static uint Read(byte[] buf, ref int pos, int len)
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
