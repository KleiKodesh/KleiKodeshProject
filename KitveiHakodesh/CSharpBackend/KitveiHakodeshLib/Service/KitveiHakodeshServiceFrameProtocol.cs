using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Length-prefixed framing of the KitveiHakodesh service pipe, byte-for-byte the service's
    /// own Ipc/FrameProtocol.cs: every message is <c>[int32 little-endian length][payload]</c>.
    /// </summary>
    public static class KitveiHakodeshServiceFrameProtocol
    {
        /// <summary>Same ceiling as the service: a frame above it is a protocol error, not data.</summary>
        public const int MaxFrameBytes = 64 * 1024 * 1024;

        public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
        {
            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);
            await stream.WriteAsync(lengthPrefix, 0, 4, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads one frame's payload, or null when the peer closed the stream cleanly.</summary>
        public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] lengthPrefix = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
            if (lengthPrefix == null) return null;

            int length = BitConverter.ToInt32(lengthPrefix, 0);
            if (length < 0 || length > MaxFrameBytes)
                throw new InvalidDataException("Bad frame length: " + length);
            if (length == 0) return Array.Empty<byte>();

            return await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, read, count - read, cancellationToken).ConfigureAwait(false);
                if (n == 0) return null; // peer closed
                read += n;
            }
            return buffer;
        }
    }
}
