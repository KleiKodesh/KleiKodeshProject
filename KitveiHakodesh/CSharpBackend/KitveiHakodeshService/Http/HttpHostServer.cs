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
    Dispatcher dispatcher, HttpHostState state, ILogger<HttpHostServer> logger) : BackgroundService
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
}
