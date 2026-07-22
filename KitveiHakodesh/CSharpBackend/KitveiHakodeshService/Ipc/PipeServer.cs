using System.IO.Pipes;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Hosts the KitveiHakodesh named pipe and serves one RPC request per connection.
///
/// Accept loop / one-handler-per-connection model mirrors
/// DocumentLocator.Service/IndexService.cs. The pipe carries the clean
/// <see cref="Rpc"/> envelope, framed by <see cref="FrameProtocol"/>.
///
/// In dev the service runs as a plain console process (spawned by the Vite dev
/// plugin) under the developer's own account, so the default pipe ACL — which
/// grants the creating user access — is sufficient. When this later runs as an
/// installed Windows service under LocalSystem, an authenticated-user ACL will
/// be added via NamedPipeServerStreamAcl.
/// </summary>
public sealed class PipeServer(Dispatcher dispatcher, ILogger<PipeServer> logger) : BackgroundService
{
    public const string DefaultPipeName = "KitveiHakodesh";

    // Per-spawn pipe name: a spawner (the Vite dev plugin, or any app) passes a UNIQUE name via
    // KHS_PIPE_NAME so each spawned instance has its own private pipe — that's how a client
    // reaches ITS instance's HTTP port (getHttpPort) instead of some other instance sharing a
    // fixed name. Falls back to the fixed default for the installed-service / standalone case.
    private readonly string _pipeName =
        Environment.GetEnvironmentVariable("KHS_PIPE_NAME") is { Length: > 0 } n ? n : DefaultPipeName;

    // Keep several listener instances armed at once. A single serial acceptor has a
    // brief window with NO listening instance — between accepting one client and
    // creating the next — during which a concurrent connect gets ENOENT ("all pipe
    // instances are busy"). The frontend fires a burst of requests on load (warmup +
    // catalog + page data), so run a small pool of independent acceptor loops: there
    // is always a ready instance and bursts don't race into ENOENT.
    private const int AcceptorCount = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            @"KitveiHakodesh pipe server listening on \\.\pipe\{Pipe} ({N} acceptors)", _pipeName, AcceptorCount);

        var acceptors = new Task[AcceptorCount];
        for (int i = 0; i < AcceptorCount; i++)
            acceptors[i] = AcceptLoopAsync(stoppingToken);
        await Task.WhenAll(acceptors);
    }

    /// <summary>One independent accept loop: wait for a client, hand it to a detached
    /// handler, immediately loop to wait for the next. N of these run concurrently.</summary>
    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe(_pipeName);
                await pipe.WaitForConnectionAsync(stoppingToken);

                // Hand off to a detached task so this loop can immediately accept the
                // next client. Each connection serves exactly one request/response.
                var connected = pipe;
                pipe = null;
                _ = HandleClientAsync(connected, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pipe accept error");
                pipe?.Dispose();
                await Task.Delay(200, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName) =>
        new(pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096);

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                byte[]? request = await FrameProtocol.ReadFrameAsync(pipe, ct);
                if (request is null) return; // client closed without sending

                // Streaming ops push MANY frames over this one connection (the connection
                // IS the stream); everything else stays one request → one response.
                if (await dispatcher.TryDispatchStreamAsync(
                        request, resp => FrameProtocol.WriteFrameAsync(pipe, resp, ct), ct))
                    return;

                byte[] response = await dispatcher.DispatchAsync(request, ct);
                await FrameProtocol.WriteFrameAsync(pipe, response, ct);
            }
            catch (OperationCanceledException)
            {
                // service stopping — ignore
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pipe client handler error");
            }
        }
    }
}
