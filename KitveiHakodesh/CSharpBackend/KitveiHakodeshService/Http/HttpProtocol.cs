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
        /// <summary>The Range request header (e.g. "bytes=0-1023"), or null. Used by GET /file
        /// so pdf.js loads a PDF progressively instead of fetching the whole file.</summary>
        public string? Range { get; init; }
        /// <summary>Empty until <see cref="ReadBodyAsync"/> runs. The host reads the body only
        /// AFTER the bearer token checks out, so an unauthenticated caller cannot make us
        /// allocate up to <see cref="MaxBodyBytes"/>.</summary>
        public byte[] Body { get; internal set; } = [];
        /// <summary>Declared Content-Length, already range-checked against MaxBodyBytes.</summary>
        public int ContentLength { get; init; }
        /// <summary>Body bytes that already arrived in the same TCP segment as the headers.</summary>
        internal byte[] Prebuffered { get; init; } = [];
    }

    /// <summary>Reads the request line and headers only, or null when the peer closed before
    /// sending anything. The body is left on the wire for <see cref="ReadBodyAsync"/> so the
    /// caller can authenticate first.</summary>
    public static async Task<Request?> ReadHeadAsync(Stream stream, CancellationToken ct)
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
        string? range = null;
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
            else if (name.Equals("Range", StringComparison.OrdinalIgnoreCase))
                range = value;
        }
        if (contentLength < 0 || contentLength > MaxBodyBytes)
            throw new InvalidDataException($"Bad Content-Length: {contentLength}");

        // The body may partly (or fully) already sit in the accumulation buffer past the header.
        // Keep just those bytes; the rest stays on the wire until the caller asks for it.
        int bodyStart = headerEnd + HeaderTerminator.Length;
        int have = Math.Max(0, Math.Min(total - bodyStart, contentLength));
        byte[] prebuffered = new byte[have];
        if (have > 0) Array.Copy(all, bodyStart, prebuffered, 0, have);

        return new Request
        {
            Method = reqLine[0], Path = reqLine[1], Origin = origin, Token = token, Range = range,
            ContentLength = contentLength, Prebuffered = prebuffered,
        };
    }

    /// <summary>Reads the declared body into <see cref="Request.Body"/>. Call only for a request
    /// that has been authorized — this is where the up-to-64 MB allocation happens. Idempotent.</summary>
    public static async Task ReadBodyAsync(Request req, Stream stream, CancellationToken ct)
    {
        if (req.ContentLength == 0 || req.Body.Length == req.ContentLength) return;

        byte[] body = new byte[req.ContentLength];
        int read = req.Prebuffered.Length;
        if (read > 0) req.Prebuffered.CopyTo(body, 0);
        while (read < req.ContentLength)
        {
            int n = await stream.ReadAsync(body.AsMemory(read, req.ContentLength - read), ct);
            if (n == 0) throw new InvalidDataException("Unexpected EOF in HTTP body");
            read += n;
        }
        req.Body = body;
    }

    /// <summary>Writes the headers for a file response (200 or 206), advertising byte-range
    /// support so pdf.js loads progressively. The caller then streams the body from disk in
    /// small buffers — the whole file is never held in memory on either side.</summary>
    public static async Task WriteFileHeadAsync(
        Stream stream, int status, string reason, string contentType,
        long contentLength, string? contentRange, string? origin, CancellationToken ct)
    {
        string head =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {contentLength}\r\n" +
            "Accept-Ranges: bytes\r\n" +
            (contentRange is null ? "" : $"Content-Range: {contentRange}\r\n") +
            "Cache-Control: no-store\r\n" +
            Cors(origin) +
            "Access-Control-Expose-Headers: Content-Range, Accept-Ranges, Content-Length\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.FlushAsync(ct);
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
            "Access-Control-Allow-Headers: Content-Type, X-KHS-Token, Range\r\n" +
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
