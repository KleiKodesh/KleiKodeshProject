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
    public const string PipeName = "KitveiHakodesh";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(@"KitveiHakodesh pipe server listening on \\.\pipe\{Pipe}", PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);

                // Hand off to a detached task so we can immediately accept the next
                // client. Each connection serves exactly one request/response.
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

    private static NamedPipeServerStream CreatePipe() =>
        new(PipeName,
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
