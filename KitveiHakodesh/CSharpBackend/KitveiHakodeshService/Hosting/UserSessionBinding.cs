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
/// When the data host asks to be reloaded (a database-path or additional-library change) it is
/// rebuilt IN THIS PROCESS (<see cref="DataHostSupervisor"/>): the service keeps running, so
/// nothing has to start it again. Only a stop that is not a reload ends the service.
/// </summary>
public sealed class UserSessionBinding(
    IHostApplicationLifetime serviceLifetime,
    ILogger<UserSessionBinding> logger) : BackgroundService
{
    /// <summary>A service started but never contacted (a host that died before its window,
    /// a start nobody followed up) has no OwnerWatcher yet - that lives in the data host. Bound
    /// the wait so it does not sit as a SYSTEM process forever; same limit as the data host's
    /// ownerless idle (OwnerWatcher.OwnerlessIdleLimit).</summary>
    private static readonly TimeSpan UnboundWaitLimit = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string pipeName = PipeServer.ResolvePipeName();
        NamedPipeServerStream? firstConnection = null;
        byte[]? firstRequest;
        try
        {
            using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            waitLimit.CancelAfter(UnboundWaitLimit);
            while (true)
            {
                firstConnection = WindowsServiceMode.CreatePipe(pipeName);
                try { await firstConnection.WaitForConnectionAsync(waitLimit.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("No program connected for {Minutes:N0} minutes; stopping", UnboundWaitLimit.TotalMinutes);
                    firstConnection.Dispose();
                    serviceLifetime.StopApplication();
                    return;
                }

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

        // The data host runs - and is rebuilt in this process on a reload request (an added or
        // removed library, a switched database) - until the SCM stops us or something stops it
        // without asking for a reload (the installer's "shutdown", a host that cannot be built).
        logger.LogWarning("data host: supervisor starting");
        var supervisor = new DataHostSupervisor(
            () => WindowsServiceMode.CreatePipe(pipeName),
            () => Program.BuildDataHost(Array.Empty<string>()),
            (host, pipe, request, ct) => AnswerAsync(host.Services.GetRequiredService<Dispatcher>(), pipe, request, ct),
            DataHostReload.Consume,
            logger);
        try { await supervisor.RunAsync(firstConnection, firstRequest!, stoppingToken); }
        finally
        {
            // The data host stopped for good (or never started): the service stops with it, and
            // the next client starts a fresh one. Not when the SCM itself is stopping us.
            if (!stoppingToken.IsCancellationRequested)
            {
                serviceLifetime.StopApplication();
                // A stop that never completes leaves a service the SCM reports as running with no
                // pipe behind it, and nothing can start it again. End the process instead, so the
                // next client starts a fresh one.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    logger.LogError("The service did not stop within 60s; ending the process");
                    Environment.Exit(1);
                });
            }
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
