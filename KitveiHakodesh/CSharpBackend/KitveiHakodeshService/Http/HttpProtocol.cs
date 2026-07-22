using System.Globalization;
using System.Text;

namespace KitveiHakodeshService.Http;

/// <summary>
/// A deliberately tiny HTTP/1.1 codec for the loopback host. We control BOTH ends (the
/// Vite dev client and this server), so only the needed subset is implemented: read a
/// request (method, path, headers, Content-Length body) and write either a buffered
/// response (Content-Length) or a chunked stream. No keep-alive — every connection
/// serves one request then closes (<c>Connection: close</c>), mirroring the
/// one-request-per-connection model of <see cref="Ipc.PipeServer"/>.
///
/// Only System.Net.Sockets + System.Text are used, so this stays fully native-AOT safe
/// (no reflection, no ASP.NET/Kestrel, and NOT System.Net.HttpListener — http.sys would
/// need a <c>netsh http add urlacl</c> reservation or admin and is unreliable in
/// locked-down environments). A raw loopback TCP socket needs no reservation and is
/// exempt from the Windows firewall.
/// </summary>
internal static class HttpProtocol
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 64 * 1024 * 1024;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    /// <summary>The parsed subset of an inbound request the host cares about.</summary>
    public sealed class Request
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public string? Origin { get; init; }
        /// <summary>The X-KHS-Token bearer header, or null when absent. Verified by the host
        /// against the per-instance token before any data op runs.</summary>
        public string? Token { get; init; }
        public byte[] Body { get; init; } = [];
    }

    /// <summary>Reads one HTTP request, or null when the peer closed before sending anything.</summary>
    public static async Task<Request?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        // Accumulate raw bytes until the end-of-headers marker (\r\n\r\n) appears.
        byte[] buf = new byte[8192];
        using var acc = new MemoryStream();
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buf, ct);
            if (n == 0)
                return acc.Length == 0 ? null : throw new InvalidDataException("Unexpected EOF in HTTP headers");
            acc.Write(buf, 0, n);
            if (acc.Length > MaxHeaderBytes) throw new InvalidDataException("HTTP headers too large");
            headerEnd = IndexOf(acc.GetBuffer(), (int)acc.Length, HeaderTerminator);
        }

        byte[] all = acc.GetBuffer();
        int total = (int)acc.Length;

        string headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        string[] lines = headerText.Split("\r\n");
        string[] reqLine = lines[0].Split(' ');
        if (reqLine.Length < 2) throw new InvalidDataException("Malformed HTTP request line");

        int contentLength = 0;
        string? origin = null;
        string? token = null;
        for (int i = 1; i < lines.Length; i++)
        {
            int c = lines[i].IndexOf(':');
            if (c <= 0) continue;
            string name = lines[i][..c].Trim();
            string value = lines[i][(c + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
            else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                origin = value;
            else if (name.Equals("X-KHS-Token", StringComparison.OrdinalIgnoreCase))
                token = value;
        }
        if (contentLength < 0 || contentLength > MaxBodyBytes)
            throw new InvalidDataException($"Bad Content-Length: {contentLength}");

        // The body may partly (or fully) already sit in the accumulation buffer past the header.
        int bodyStart = headerEnd + HeaderTerminator.Length;
        byte[] body = new byte[contentLength];
        int have = Math.Min(total - bodyStart, contentLength);
        if (have > 0) Array.Copy(all, bodyStart, body, 0, have);
        int read = have;
        while (read < contentLength)
        {
            int n = await stream.ReadAsync(body.AsMemory(read, contentLength - read), ct);
            if (n == 0) throw new InvalidDataException("Unexpected EOF in HTTP body");
            read += n;
        }

        return new Request { Method = reqLine[0], Path = reqLine[1], Origin = origin, Token = token, Body = body };
    }

    /// <summary>Writes a complete buffered response (status + headers + Content-Length body).</summary>
    public static async Task WriteBufferedAsync(
        Stream stream, int status, string reason, string contentType,
        ReadOnlyMemory<byte> body, string? origin, CancellationToken ct)
    {
        string head =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            Cors(origin) +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        if (body.Length > 0) await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Writes a bodyless response (used for 404 and errors).</summary>
    public static async Task WriteStatusAsync(Stream stream, int status, string reason, string? origin, CancellationToken ct)
    {
        string head =
            $"HTTP/1.1 {status} {reason}\r\n" +
            Cors(origin) +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Answers a CORS preflight. The dev app is a cross-origin localhost page, and a
    /// POST with an application/octet-stream body is not a "simple" request, so the browser
    /// sends OPTIONS first. Also answers Chromium's Private Network Access preflight
    /// (Access-Control-Allow-Private-Network) so newer WebView2/Chromium runtimes — which
    /// gate page→loopback requests behind it — keep working when the hosted app's page talks
    /// to this host directly.</summary>
    public static async Task WritePreflightAsync(Stream stream, string? origin, CancellationToken ct)
    {
        string head =
            "HTTP/1.1 204 No Content\r\n" +
            Cors(origin) +
            "Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type, X-KHS-Token\r\n" +
            "Access-Control-Allow-Private-Network: true\r\n" +
            "Access-Control-Max-Age: 86400\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Opens a chunked (Transfer-Encoding: chunked) streaming response.</summary>
    public static async Task WriteChunkedHeaderAsync(Stream stream, string contentType, string? origin, CancellationToken ct)
    {
        string head =
            "HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Cache-Control: no-cache\r\n" +
            Cors(origin) +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Writes one HTTP chunk. The browser dechunks transparently, so the caller's
    /// payload bytes (already carrying their own 4-byte LE frame prefix) arrive intact.</summary>
    public static async Task WriteChunkAsync(Stream stream, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (data.Length == 0) return;
        string size = data.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(size), ct);
        await stream.WriteAsync(data, ct);
        await stream.WriteAsync("\r\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Closes a chunked response (the terminating zero-length chunk).</summary>
    public static async Task WriteFinalChunkAsync(Stream stream, CancellationToken ct)
    {
        await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    // Loopback dev is same-machine and unauthenticated; a wildcard (or echoed) origin is fine
    // and no credentials are sent, so this needs no allow-credentials handling.
    private static string Cors(string? origin) =>
        "Access-Control-Allow-Origin: " + (string.IsNullOrEmpty(origin) ? "*" : origin) + "\r\n";

    private static int IndexOf(byte[] hay, int hayLen, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hayLen; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
