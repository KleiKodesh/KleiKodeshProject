// Verification of "a library change reloads the data host IN the running service" (branch
// feature/multi-library-restart).
//
// Run: dotnet run -c Release -- --reload
//
// Real pipes (a unique private pipe name, never the installed service's), the real
// DataHostSupervisor / StartupConnectionHolder / DataHostReload, and the real host-side client
// channel (KitveiHakodeshServicePipeChannel, the one the Word add-in uses). Only the data host
// itself is a small stand-in (it answers ping / reload / shutdown), because the real one needs a
// seforim database. Touches no registry and no installed service.
//
//   R1. First request binds, is answered; a reload request makes the SAME supervisor build a
//       second host; clients hammering the pipe all the while get a correct answer every time
//       (instance id changes from A to B) and the supervisor never returns (= service stays up).
//   R2. Three reloads in a row, still no loss, three distinct instance ids.
//   R3. A stop WITHOUT a reload mark ("shutdown", the installer's op) ends the supervisor, so
//       the service stops exactly as before.
//   R4. A reload whose rebuild fails ends the supervisor (service stops, the next client starts
//       a fresh one) and does not hang held clients forever.
//   R5. The reload mark is single-use and the SCM cancel path (stoppingToken) returns promptly.
using System.IO.Pipes;
using KitveiHakodeshLib.Service;
using KitveiHakodeshService.Hosting;
using KitveiHakodeshService.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KitveiHakodeshService.Tests;

internal static class ReloadVerify
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) _failures++;
    }

    private sealed class FakeHostState(string pipeName)
    {
        public string PipeName { get; } = pipeName;
        public string Instance { get; } = Guid.NewGuid().ToString("N");
    }

    /// <summary>What the real PipeServer does, answering from the stand-in dispatcher.</summary>
    private sealed class FakePipeServer(FakeHostState state, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var loops = new Task[4];
            for (int i = 0; i < loops.Length; i++) loops[i] = LoopAsync(stoppingToken);
            await Task.WhenAll(loops);
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = WindowsServiceMode.CreatePipe(state.PipeName);
                    await pipe.WaitForConnectionAsync(ct);
                    var connected = pipe;
                    pipe = null;
                    _ = Task.Run(async () =>
                    {
                        using (connected)
                        {
                            try
                            {
                                byte[]? request = await FrameProtocol.ReadFrameAsync(connected, ct);
                                if (request is null) return;
                                await FrameProtocol.WriteFrameAsync(connected, Answer(state, lifetime, request), ct);
                            }
                            catch { }
                        }
                    });
                }
                catch (OperationCanceledException) { pipe?.Dispose(); break; }
                catch { pipe?.Dispose(); try { await Task.Delay(50, CancellationToken.None); } catch { } }
            }
        }
    }

    /// <summary>The stand-in dispatcher (same ops the real one has for this purpose).</summary>
    public static byte[] Answer(object stateObj, IHostApplicationLifetime lifetime, byte[] request)
    {
        var state = (FakeHostState)stateObj;
        var req = MsgPack.De<RpcRequest>(request);
        switch (req.Op)
        {
            case "ping":
                return RpcResponse.Ok(MsgPack.Ser(new PongResult { Instance = state.Instance }));
            case "reload": // what addAdditionalSeforimLibrary's RestartSoon does
                _ = Task.Run(async () => { await Task.Delay(100); DataHostReload.RequestAndStop(lifetime); });
                return RpcResponse.Ok(MsgPack.Ser(new PongResult { Instance = state.Instance }));
            case "shutdown": // what the installer's op does: stops WITHOUT the reload mark
                _ = Task.Run(async () => { await Task.Delay(100); lifetime.StopApplication(); });
                return RpcResponse.Ok(MsgPack.Ser(new ShuttingDownResult()));
            default:
                return RpcResponse.Err("unknown op " + req.Op);
        }
    }

    private static IHost BuildFakeHost(string pipeName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new FakeHostState(pipeName));
        builder.Services.AddHostedService<FakePipeServer>();
        return builder.Build();
    }

    private static async Task<string?> PingAsync(string pipeName, int connectTimeoutMs = 4000)
    {
        var channel = new KitveiHakodeshServicePipeChannel(pipeName, connectTimeoutMs, 8000);
        byte[] reply = await channel.CallAsync(MsgPack.Ser(new RpcRequest { Op = "ping" }), CancellationToken.None);
        var env = MsgPack.De<RpcEnvelope>(reply);
        return env.Ok ? MsgPack.De<PongResult>(env.Result).Instance : null;
    }

    private static async Task<byte[]> CallAsync(string pipeName, string op)
    {
        var channel = new KitveiHakodeshServicePipeChannel(pipeName, 4000, 8000);
        return await channel.CallAsync(MsgPack.Ser(new RpcRequest { Op = op }), CancellationToken.None);
    }

    /// <summary>Starts a supervisor the way UserSessionBinding does: the first connection is
    /// taken, its request read, and handed over.</summary>
    private static async Task<(DataHostSupervisor supervisor, Task run, CancellationTokenSource cts, string firstInstance)>
        StartAsync(string pipeName, Func<IHost> buildHost)
    {
        var cts = new CancellationTokenSource();
        var first = WindowsServiceMode.CreatePipe(pipeName);
        Task<string?> firstClient = Task.Run(() => PingAsync(pipeName));
        await first.WaitForConnectionAsync(cts.Token);
        byte[]? firstRequest = await FrameProtocol.ReadFrameAsync(first, cts.Token);

        DataHostSupervisor supervisor = null!;
        supervisor = new DataHostSupervisor(
            () => WindowsServiceMode.CreatePipe(pipeName),
            buildHost,
            async (host, pipe, request, ct) =>
            {
                using (pipe)
                {
                    var state = host.Services.GetRequiredService<FakeHostState>();
                    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                    await FrameProtocol.WriteFrameAsync(pipe, Answer(state, lifetime, request), ct);
                }
            },
            DataHostReload.Consume,
            NullLogger.Instance);

        Task run = Task.Run(() => supervisor.RunAsync(first, firstRequest!, cts.Token));
        string? firstInstance = await firstClient;
        return (supervisor, run, cts, firstInstance ?? "");
    }

    public static int Run()
    {
        RunAsync().GetAwaiter().GetResult();
        Console.WriteLine(_failures == 0 ? "\nALL PASS" : $"\n{_failures} FAILED");
        return _failures;
    }

    private static async Task RunAsync()
    {
        // ── R1 + R2 ───────────────────────────────────────────────────────────────
        string pipe1 = "KHS-reload-test-" + Guid.NewGuid().ToString("N");
        var (sup, run, cts, instanceA) = await StartAsync(pipe1, () => BuildFakeHost(pipe1));
        Check(!string.IsNullOrEmpty(instanceA), "R1 first request (the binding one) answered by host A");
        Check(sup.HostsStarted == 1, "R1 one host started");

        var seen = new HashSet<string> { instanceA };
        int rawHiccups = 0, pings = 0, failedPings = 0, consecutiveFailures = 0, maxConsecutive = 0;
        using var hammerStop = new CancellationTokenSource();
        var hammer = Task.Run(async () =>
        {
            while (!hammerStop.IsCancellationRequested)
            {
                try
                {
                    string? id;
                    try { id = await PingAsync(pipe1, 6000); }
                    catch (KitveiHakodeshServiceUnavailableException)
                    {
                        // The product's SelfStartingChannel retries a call that found the service
                        // "not there" exactly once (the page's own ladder retries more). A call that
                        // hit the OLD host at the instant it went down is such a case.
                        rawHiccups++;
                        id = await PingAsync(pipe1, 6000);
                    }
                    lock (seen) { if (id != null) seen.Add(id); }
                    Interlocked.Increment(ref pings);
                    consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      ping failed even after the retry at {DateTime.Now:HH:mm:ss.fff}: {ex.GetType().Name}: {ex.Message}");
                    failedPings++;
                    maxConsecutive = Math.Max(maxConsecutive, ++consecutiveFailures);
                }
                await Task.Delay(15);
            }
        });

        for (int reload = 1; reload <= 3; reload++)
        {
            int before; lock (seen) before = seen.Count;
            await CallAsync(pipe1, "reload");
            bool newHost = await WaitUntilAsync(() => { lock (seen) return seen.Count > before; }, 20_000);
            Check(newHost, $"R{(reload == 1 ? 1 : 2)} reload #{reload}: a NEW host answers (instance ids seen: {SeenCount(seen)})");
            Check(!run.IsCompleted, $"R{(reload == 1 ? 1 : 2)} reload #{reload}: the supervisor (= the service) is still running");
        }
        await Task.Delay(300);
        hammerStop.Cancel();
        await hammer;
        Check(sup.HostsStarted == 4, $"R2 four hosts started in total (got {sup.HostsStarted})");
        Check(SeenCount(seen) == 4, $"R2 four distinct instance ids answered (got {SeenCount(seen)})");
        Console.WriteLine($"      pings answered: {pings}; calls that hit the old host as it went down and needed the one retry: {rawHiccups}; failed even after the retry: {failedPings}");
        Check(failedPings == 0, "R1/R2 no client call failed across three reloads, given the product's one retry (the pipe never went dark)");

        // ── R3: stop without a reload mark ends the supervisor ───────────────────
        await CallAsync(pipe1, "shutdown");
        bool ended = await Task.WhenAny(run, Task.Delay(15_000)) == run;
        Check(ended, "R3 'shutdown' (no reload mark) ends the supervisor, so the service stops as before");
        cts.Dispose();

        // ── R4: a failing rebuild ends it instead of hanging ─────────────────────
        string pipe4 = "KHS-reload-test-" + Guid.NewGuid().ToString("N");
        int builds = 0;
        var (sup4, run4, cts4, a4) = await StartAsync(pipe4, () =>
        {
            if (++builds > 1) throw new InvalidOperationException("simulated: database unreadable");
            return BuildFakeHost(pipe4);
        });
        Check(!string.IsNullOrEmpty(a4), "R4 first host up");
        await CallAsync(pipe4, "reload");
        // A client that arrives while the rebuild fails must not wait forever.
        Task<string?> held = Task.Run(async () => { try { return await PingAsync(pipe4, 3000); } catch { return null; } });
        bool ended4 = await Task.WhenAny(run4, Task.Delay(15_000)) == run4;
        Check(ended4, "R4 a failed rebuild ends the supervisor");
        bool heldDone = await Task.WhenAny(held, Task.Delay(15_000)) == held;
        Check(heldDone, "R4 a client held during the failed rebuild is released, not hung");
        cts4.Dispose();

        // ── R5: single-use mark, and SCM stop returns promptly ───────────────────
        DataHostReload.Request();
        Check(DataHostReload.Consume() && !DataHostReload.Consume(), "R5 the reload mark is single-use");
        string pipe5 = "KHS-reload-test-" + Guid.NewGuid().ToString("N");
        var (sup5, run5, cts5, a5) = await StartAsync(pipe5, () => BuildFakeHost(pipe5));
        cts5.Cancel();
        bool ended5 = await Task.WhenAny(run5, Task.Delay(15_000)) == run5;
        Check(ended5, "R5 cancelling (the SCM stopping the service) returns promptly");
        cts5.Dispose();
    }

    private static int SeenCount(HashSet<string> seen) { lock (seen) return seen.Count; }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }
}
