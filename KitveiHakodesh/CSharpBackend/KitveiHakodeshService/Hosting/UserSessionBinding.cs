using System.IO.Pipes;
using System.Security.Principal;
using KitveiHakodesh.Core.Settings;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// The Windows-service bootstrap: wait for the first pipe client, bind the process to THAT
/// user's profile, then run the real data host inside this one.
///
/// Why a two-stage host. Running as LocalSystem, this process has no user of its own, but
/// everything the data host does at construction - resolve the seforim DB from the user's
/// registry, open it, start the index builders - needs one. The SCM must also see the
/// service "running" within seconds, so the wait cannot happen before the service reports
/// started. So the service starts with only this listener; the first connection tells it
/// who it serves (<see cref="UserProfile.BindToUser"/>), and only then is the data host built
/// and started. That first request is held and answered by the data host once it is up, so
/// the first caller sees an ordinary (slightly slower) reply.
///
/// When the data host stops itself (it restarts after a database-path change), the service
/// stops with it; the SCM shows it stopped and the next host to need it starts it again.
/// </summary>
public sealed class UserSessionBinding(
    IHostApplicationLifetime serviceLifetime,
    ILogger<UserSessionBinding> logger) : BackgroundService
{
    private IHost? _dataHost;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string pipeName = PipeServer.ResolvePipeName();
        NamedPipeServerStream? firstConnection = null;
        byte[]? firstRequest;
        try
        {
            while (true)
            {
                firstConnection = WindowsServiceMode.CreatePipe(pipeName);
                await firstConnection.WaitForConnectionAsync(stoppingToken);

                // The request must be READ before the client can be impersonated: Windows refuses
                // ImpersonateNamedPipeClient until data has arrived on the pipe (error 1368).
                firstRequest = await FrameProtocol.ReadFrameAsync(firstConnection, stoppingToken);
                if (firstRequest is not null) break;

                // A probe that connected and left without a request (an existence check, a
                // cancelled client): not a user to bind to. Wait for the next one.
                firstConnection.Dispose();
                firstConnection = null;
            }

            WindowsIdentity user = CaptureClient(firstConnection);
            UserSessionProcess.ServedSessionId = UserSessionProcess.SessionOfPipeClient(firstConnection);
            UserProfile.BindToUser(user.AccessToken);
            logger.LogInformation("Serving {User}; profile bound (AppData {AppData})", user.Name, UserProfile.RoamingAppData);
        }
        catch (OperationCanceledException) { firstConnection?.Dispose(); return; }
        catch (Exception ex)
        {
            firstConnection?.Dispose();
            logger.LogError(ex, "Could not bind to the connecting user; stopping");
            serviceLifetime.StopApplication();
            return;
        }

        _dataHost = Program.BuildDataHost(Array.Empty<string>());
        await _dataHost.StartAsync(stoppingToken);
        // The request that told us who we serve is answered like any other, once the data host
        // is up - the first caller sees nothing special.
        _ = AnswerAsync(_dataHost.Services.GetRequiredService<Dispatcher>(), firstConnection, firstRequest, stoppingToken);
        try
        {
            // Ends when the data host stops itself or the SCM stops us.
            await _dataHost.WaitForShutdownAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await _dataHost.StopAsync(CancellationToken.None); } catch { }
            _dataHost.Dispose();
            _dataHost = null;
            // The data host restarted itself: let the SCM see us stopped so the next host
            // start brings a fresh instance up against the new database.
            if (!stoppingToken.IsCancellationRequested) serviceLifetime.StopApplication();
        }
    }

    /// <summary>The identity behind a connected pipe. RunAsClient impersonates the client on
    /// this thread; the identity captured inside opens its own handle on that token, which
    /// outlives the impersonation and the pipe - what the profile binding needs.</summary>
    private static WindowsIdentity CaptureClient(NamedPipeServerStream pipe)
    {
        WindowsIdentity? identity = null;
        pipe.RunAsClient(() =>
        {
            identity = WindowsIdentity.GetCurrent(
                TokenAccessLevels.Query | TokenAccessLevels.Impersonate | TokenAccessLevels.Duplicate);
        });
        return identity ?? throw new InvalidOperationException("the pipe client could not be identified");
    }

    /// <summary>Serves the first request exactly as PipeServer.HandleClientAsync would.</summary>
    private async Task AnswerAsync(Dispatcher dispatcher, NamedPipeServerStream pipe, byte[] request, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                if (await dispatcher.TryDispatchStreamAsync(request, resp => FrameProtocol.WriteFrameAsync(pipe, resp, ct), ct))
                    return;
                byte[] response = await dispatcher.DispatchAsync(request, ct);
                await FrameProtocol.WriteFrameAsync(pipe, response, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "First request after binding failed");
            }
        }
    }
}
