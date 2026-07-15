using System.Text;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Length-prefixed framing for the named-pipe transport.
///
/// Every message is a single frame:
///   [4-byte little-endian int32 length][UTF-8 payload bytes]
///
/// This is byte-for-byte identical to the DocumentLocator wire framing
/// (see DocumentLocator/PipeProtocol.cs), so the same Node/C# readers apply.
/// The payload itself is the clean RPC envelope — see <see cref="Rpc"/>.
/// </summary>
internal static class FrameProtocol
{
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    public static async Task WriteFrameAsync(Stream stream, string text, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] len = BitConverter.GetBytes(body.Length); // little-endian on Windows
        await stream.WriteAsync(len.AsMemory(0, 4), ct);
        await stream.WriteAsync(body.AsMemory(0, body.Length), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one frame, or null when the peer closed the pipe cleanly.</summary>
    public static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        byte[]? lenBuf = await ReadExactAsync(stream, 4, ct);
        if (lenBuf is null) return null;

        int length = BitConverter.ToInt32(lenBuf, 0);
        if (length <= 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"Bad frame length: {length}");

        byte[]? payload = await ReadExactAsync(stream, length, ct);
        return payload is null ? null : Encoding.UTF8.GetString(payload);
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        byte[] buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) return null; // peer closed
            read += n;
        }
        return buf;
    }
}
