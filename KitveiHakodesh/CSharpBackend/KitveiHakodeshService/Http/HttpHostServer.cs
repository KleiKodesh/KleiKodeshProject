using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Http;

/// <summary>
/// Loopback HTTP host — a SECOND transport in front of the shared <see cref="Dispatcher"/>,
/// added alongside the named pipe (<see cref="PipeServer"/>) without replacing it. It exists so
/// a BROWSER (which cannot speak named pipes) can reach the service directly over
/// http://127.0.0.1:&lt;port&gt; with the exact same MessagePack {op,args} envelope the pipe carries.
///
/// The port is OS-assigned (bind to port 0 → a guaranteed-free ephemeral port; each spawned
/// instance gets its own) and is handed to the spawner ONLY over the private named pipe
/// (the <c>getHttpPort</c> op via <see cref="HttpHostState"/>). It is deliberately never written
/// to a file or printed to stdout — a discovery file would leak the endpoint machine-wide.
///
/// Bound to 127.0.0.1 ONLY (never a routable address): loopback is exempt from the Windows
/// firewall and needs no URL-ACL/admin, so it works where http.sys/HttpListener or an opened
/// port would be blocked. See <see cref="HttpProtocol"/> for why we hand-roll HTTP.
///
///   POST /rpc         → one request → one buffered MessagePack response.
///   POST /rpc-stream  → many pushed frames (FTS results, index progress), each a
///                       [4-byte LE length][envelope] frame inside chunked encoding —
///                       identical framing to the pipe so the client's frame splitter is shared.
///   OPTIONS *         → CORS preflight (the dev app is a cross-origin localhost page).
/// </summary>
public sealed class HttpHostServer(
    Dispatcher dispatcher, HttpHostState state, LocalFileGrants localFileGrants,
    ILogger<HttpHostServer> logger) : BackgroundService
{
    private TcpListener? _listener;

    // The per-instance token's UTF-8 bytes, precomputed for the fixed-time comparison below.
    private readonly byte[] _tokenBytes = Encoding.UTF8.GetBytes(state.Token);

    /// <summary>True when the request carries OUR instance's bearer token. Fixed-time
    /// comparison so the check can't be turned into a byte-at-a-time timing oracle.
    /// This is the security boundary of the loopback host: loopback TCP is reachable by any
    /// local process (and, via CORS-passing requests, by web pages), but only a caller that
    /// received the token over the ACL'd named pipe can use the data endpoints.</summary>
    private bool TokenValid(HttpProtocol.Request req)
    {
        if (string.IsNullOrEmpty(req.Token)) return false;
        byte[] presented = Encoding.UTF8.GetBytes(req.Token);
        return CryptographicOperations.FixedTimeEquals(presented, _tokenBytes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Port 0 → the OS assigns a free ephemeral port (availability-based, no TOCTOU race).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 128);
        _listener = listener;

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        state.SetPort(port); // hand it back over the pipe via getHttpPort — never a file
        logger.LogInformation("KitveiHakodesh HTTP host listening on http://127.0.0.1:{Port}", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Socket socket;
                try { socket = await listener.AcceptSocketAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "HTTP accept error"); continue; }

                // Detach: one connection = one request/response, so the accept loop never blocks.
                _ = HandleAsync(socket, stoppingToken);
            }
        }
        finally
        {
            try { listener.Stop(); } catch { /* stopping */ }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleAsync(Socket socket, CancellationToken ct)
    {
        using (socket)
        await using (var stream = new NetworkStream(socket, ownsSocket: false))
        {
            try
            {
                var req = await HttpProtocol.ReadRequestAsync(stream, ct);
                if (req is null) return;

                if (req.Method == "OPTIONS")
                {
                    await HttpProtocol.WritePreflightAsync(stream, req.Origin, ct);
                    return;
                }

                // GET /file/<handle> is gated by an unguessable CAPABILITY handle (minted only
                // via the token-gated openLocalFile op), not by the bearer token — so pdf.js can
                // range-fetch it with no header. The handle proves prior authentication; an
                // unknown handle is a plain 404. The endpoint NEVER accepts a raw path; the
                // handle rides in the PATH (hex, URL-safe) so it survives the pdf.js viewer's
                // file= param round-trip without query-string re-encoding.
                if (req.Method == "GET" && req.Path.StartsWith("/file/", StringComparison.Ordinal))
                {
                    await ServeFileAsync(stream, req, ct);
                    return;
                }

                if (req.Method != "POST")
                {
                    await HttpProtocol.WriteStatusAsync(stream, 404, "Not Found", req.Origin, ct);
                    return;
                }

                // Bearer-token gate for EVERY data endpoint. Checked before the request body
                // is even looked at, so an unauthenticated caller can neither run an op nor
                // learn which ops exist. 401 leaks nothing (the envelope stays opaque).
                if (!TokenValid(req))
                {
                    await HttpProtocol.WriteStatusAsync(stream, 401, "Unauthorized", req.Origin, ct);
                    return;
                }

                if (req.Path == "/rpc-stream")
                {
                    await HandleStreamAsync(stream, req, ct);
                    return;
                }

                if (req.Path == "/rpc")
                {
                    byte[] resp = await dispatcher.DispatchAsync(req.Body, ct);
                    await HttpProtocol.WriteBufferedAsync(stream, 200, "OK", "application/octet-stream", resp, req.Origin, ct);
                    return;
                }

                await HttpProtocol.WriteStatusAsync(stream, 404, "Not Found", req.Origin, ct);
            }
            catch (OperationCanceledException) { /* service stopping */ }
            catch (Exception ex)
            {
                // A client abort (tab closed, search superseded) surfaces as a broken socket —
                // normal, so log at debug rather than error.
                logger.LogDebug(ex, "HTTP client handler ended");
            }
        }
    }

    /// <summary>Streaming op: the dispatcher pushes many envelope frames; each is wrapped in the
    /// same 4-byte-LE frame the pipe uses, then written as one chunk. The chunked header is sent
    /// lazily on the first frame. A write throwing (client gone) propagates up and cancels the op,
    /// exactly like a broken pipe does on the named-pipe path.</summary>
    private async Task HandleStreamAsync(NetworkStream stream, HttpProtocol.Request req, CancellationToken ct)
    {
        bool headerSent = false;

        async Task WriteFrame(byte[] envelope)
        {
            if (!headerSent)
            {
                await HttpProtocol.WriteChunkedHeaderAsync(stream, "application/octet-stream", req.Origin, ct);
                headerSent = true;
            }
            byte[] framed = new byte[4 + envelope.Length];
            BinaryPrimitives.WriteInt32LittleEndian(framed, envelope.Length);
            envelope.CopyTo(framed, 4);
            await HttpProtocol.WriteChunkAsync(stream, framed, ct);
        }

        bool streamed = await dispatcher.TryDispatchStreamAsync(req.Body, WriteFrame, ct);
        if (streamed)
        {
            if (!headerSent)
                await HttpProtocol.WriteChunkedHeaderAsync(stream, "application/octet-stream", req.Origin, ct);
            await HttpProtocol.WriteFinalChunkAsync(stream, ct);
        }
        else
        {
            // Not a streaming op — answer it as a normal single response (defensive; the client
            // only posts stream ops here).
            byte[] resp = await dispatcher.DispatchAsync(req.Body, ct);
            await HttpProtocol.WriteBufferedAsync(stream, 200, "OK", "application/octet-stream", resp, req.Origin, ct);
        }
    }

    /// <summary>Serve a previously-authorized local file by its capability handle, honoring the
    /// Range header. The file is opened read/shared and streamed in 64 KB slices — the whole
    /// file is never buffered (constant service memory), and Range means pdf.js pulls only the
    /// pages it renders. An unknown handle or a vanished file is a plain 404.</summary>
    private async Task ServeFileAsync(NetworkStream stream, HttpProtocol.Request req, CancellationToken ct)
    {
        // Handle is the path segment after "/file/" (strip any trailing query).
        string tail = req.Path["/file/".Length..];
        int q = tail.IndexOf('?');
        string handle = q >= 0 ? tail[..q] : tail;
        if (!localFileGrants.TryResolve(handle, out string path))
        {
            await HttpProtocol.WriteStatusAsync(stream, 404, "Not Found", req.Origin, ct);
            return;
        }

        FileStream fs;
        long length;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
            length = fs.Length;
        }
        catch
        {
            await HttpProtocol.WriteStatusAsync(stream, 404, "Not Found", req.Origin, ct);
            return;
        }

        await using (fs)
        {
            string contentType = ContentTypeFor(path);
            (long start, long end)? range = ParseRange(req.Range, length);
            if (range is (long s, long e))
            {
                long count = e - s + 1;
                await HttpProtocol.WriteFileHeadAsync(stream, 206, "Partial Content", contentType,
                    count, $"bytes {s}-{e}/{length}", req.Origin, ct);
                fs.Seek(s, SeekOrigin.Begin);
                await CopyExactAsync(fs, stream, count, ct);
            }
            else
            {
                await HttpProtocol.WriteFileHeadAsync(stream, 200, "OK", contentType, length, null, req.Origin, ct);
                await CopyExactAsync(fs, stream, length, ct);
            }
        }
    }

    private static async Task CopyExactAsync(Stream src, Stream dst, long count, CancellationToken ct)
    {
        byte[] buf = new byte[64 * 1024];
        long remaining = count;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buf.Length, remaining);
            int n = await src.ReadAsync(buf.AsMemory(0, want), ct);
            if (n <= 0) break;
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            remaining -= n;
        }
        await dst.FlushAsync(ct);
    }

    /// <summary>Parse a single-range "bytes=start-end" / "bytes=start-" / "bytes=-suffix"
    /// header against the file length. Returns null (→ serve the whole file) if absent or
    /// unsatisfiable.</summary>
    private static (long start, long end)? ParseRange(string? header, long length)
    {
        if (string.IsNullOrEmpty(header) || length <= 0 ||
            !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return null;

        string spec = header["bytes=".Length..].Trim();
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        string a = spec[..dash].Trim(), b = spec[(dash + 1)..].Trim();

        long start, end;
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out long suffix) || suffix <= 0) return null;
            start = Math.Max(0, length - suffix);
            end = length - 1;
        }
        else
        {
            if (!long.TryParse(a, out start) || start < 0 || start >= length) return null;
            if (b.Length == 0) end = length - 1;
            else if (!long.TryParse(b, out end)) return null;
            if (end >= length) end = length - 1;
            if (end < start) return null;
        }
        return (start, end);
    }

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".htm" or ".html" => "text/html; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
}
