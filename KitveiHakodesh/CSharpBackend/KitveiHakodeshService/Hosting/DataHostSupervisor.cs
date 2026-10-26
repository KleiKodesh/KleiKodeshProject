using System.IO.Pipes;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// Runs the data host for the installed service and REBUILDS it in this process when it asks
/// to be reloaded (<see cref="DataHostReload"/>) - an added or removed additional library, a
/// switched database. The Windows service itself never stops for that, so nothing has to start
/// it again afterwards: the SCM keeps showing it running, the pipe keeps listening, and clients
/// only see a short pause.
///
/// While one host goes down and the next is built, a <see cref="StartupConnectionHolder"/> keeps
/// the pipe listening and holds incoming requests, which the new host then answers. Anything
/// that stops the data host without a reload mark (the installer's "shutdown", a host that
/// could not be built) returns from <see cref="RunAsync"/>, and the caller stops the service.
/// </summary>
internal sealed class DataHostSupervisor
{
    private readonly Func<NamedPipeServerStream> _createPipe;
    private readonly Func<IHost> _buildHost;
    private readonly Func<IHost, NamedPipeServerStream, byte[], CancellationToken, Task> _answer;
    private readonly Func<bool> _consumeReload;
    private readonly ILogger _logger;

    /// <summary>How long a data host gets to stop before it is abandoned.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(40);

    /// <summary>Number of data hosts started so far (diagnostics and tests).</summary>
    public int HostsStarted { get; private set; }

    public DataHostSupervisor(
        Func<NamedPipeServerStream> createPipe,
        Func<IHost> buildHost,
        Func<IHost, NamedPipeServerStream, byte[], CancellationToken, Task> answer,
        Func<bool> consumeReload,
        ILogger logger)
    {
        _createPipe = createPipe;
        _buildHost = buildHost;
        _answer = answer;
        _consumeReload = consumeReload;
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="firstConnection"/> / <paramref name="firstRequest"/> are the request that
    /// told the service who it serves; it is answered by the first host once that is up.
    /// Returns when the service should stop (see the class remarks).
    /// </summary>
    public async Task RunAsync(NamedPipeServerStream firstConnection, byte[] firstRequest, CancellationToken stoppingToken)
    {
        NamedPipeServerStream? pending = firstConnection;
        byte[]? pendingRequest = firstRequest;

        // Keep the pipe LISTENING while the data host is built: with only the single held
        // connection nothing was armed, and every further client got PIPE_BUSY for as long as
        // the build took. Their requests are read and held, then answered once it is up.
        StartupConnectionHolder holder = StartHolder(stoppingToken);

        while (true)
        {
            IHost host;
            try
            {
                host = _buildHost();
                try
                {
                    await host.StartAsync(stoppingToken);
                }
                catch
                {
                    try { host.Dispose(); } catch { }
                    throw;
                }
                HostsStarted++;
            }
            catch (OperationCanceledException)
            {
                holder.Abandon();
                await holder.StopAcceptingAsync();
                pending?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                // A data host that cannot be built (database unreadable, a store failing at
                // construction) used to leave the held first request unanswered forever. Answer it
                // with the error, close everything else, and stop so the next client starts a
                // fresh attempt.
                _logger.LogError(ex, "Could not build or start the data host; stopping");
                holder.Abandon();
                await holder.StopAcceptingAsync();
                if (pending is not null)
                {
                    try
                    {
                        await FrameProtocol.WriteFrameAsync(
                            pending, RpcResponse.Err("service failed to start: " + ex.Message), CancellationToken.None);
                    }
                    catch { /* the client may already be gone */ }
                    pending.Dispose();
                }
                return;
            }

            IHost current = host;
            holder.Ready((pipe, request, ct) => _answer(current, pipe, request, ct));
            // The data host's own PipeServer is armed now (StartAsync ran its accept loops up to
            // their first await), so the holder can stop listening.
            await holder.StopAcceptingAsync();
            if (pending is not null && pendingRequest is not null)
            {
                // The request that told us who we serve is answered like any other, once the
                // data host is up - the first caller sees nothing special.
                _ = _answer(current, pending, pendingRequest, stoppingToken);
                pending = null;
                pendingRequest = null;
            }

            try
            {
                // Ends when the data host stops itself or the SCM stops us.
                await current.WaitForShutdownAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }

            bool reload = !stoppingToken.IsCancellationRequested && _consumeReload();
            // Listening again BEFORE the old host finishes stopping: no moment without a pipe.
            if (reload) holder = StartHolder(stoppingToken);

            // Bounded: a hosted service whose stop never returns used to leave this process alive
            // with no pipe - the SCM still said "Running", so nothing could start it again and
            // every client waited on a dead service. Past the limit the host is abandoned (a
            // reload rebuilds beside it; a real stop ends the process in UserSessionBinding).
            Task stopping = current.StopAsync(CancellationToken.None);
            bool stoppedInTime = false;
            try { stoppedInTime = await Task.WhenAny(stopping, Task.Delay(StopTimeout)) == stopping; }
            catch { stoppedInTime = true; }
            if (stoppedInTime) { try { await stopping; } catch { } }
            else _logger.LogError("The data host did not stop within {Seconds:N0}s; abandoning it", StopTimeout.TotalSeconds);
            if (stoppedInTime) { try { current.Dispose(); } catch { } }

            if (!reload) return;
            _logger.LogInformation("Data host stopped for a reload; rebuilding it in this process (the service keeps running)");
        }
    }

    private StartupConnectionHolder StartHolder(CancellationToken stoppingToken)
    {
        var holder = new StartupConnectionHolder(
            _createPipe, 3, stoppingToken,
            (message, ex) => _logger.LogWarning(ex, "{Message}", message));
        holder.Start();
        return holder;
    }
}
