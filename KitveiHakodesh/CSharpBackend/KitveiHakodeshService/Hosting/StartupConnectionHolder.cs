using System.IO.Pipes;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// Keeps the pipe LISTENING while the data host is being built.
///
/// <see cref="UserSessionBinding"/> accepts exactly one connection to learn which user it
/// serves, then builds and starts the data host (resolving the database, opening it, starting
/// index builders) - which can take seconds. During that window nothing was listening: every
/// further client got PIPE_BUSY / "all pipe instances are busy" even though the service was up.
/// This holder keeps several instances armed meanwhile; each connection's request is READ and
/// then held until <see cref="Ready"/> supplies the real handler (or <see cref="Abandon"/>
/// closes them, so the client sees a closed pipe and retries instead of waiting forever).
///
/// Once the data host's own PipeServer is armed, <see cref="StopAccepting"/> ends the
/// listening here; connections already accepted are still answered.
/// </summary>
internal sealed class StartupConnectionHolder
{
    private readonly Func<NamedPipeServerStream> _createPipe;
    private readonly int _acceptors;
    private readonly Action<string, Exception?> _log;
    private readonly CancellationTokenSource _stopAccepting;
    private readonly TaskCompletionSource<Func<NamedPipeServerStream, byte[], CancellationToken, Task>> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _lifetime;
    private readonly List<Task> _loops = new();
    private int _held;

    public StartupConnectionHolder(
        Func<NamedPipeServerStream> createPipe, int acceptors, CancellationToken lifetime,
        Action<string, Exception?>? log = null)
    {
        _createPipe = createPipe;
        _acceptors = acceptors;
        _lifetime = lifetime;
        _log = log ?? ((_, _) => { });
        _stopAccepting = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    }

    /// <summary>Connections accepted and read so far (for tests/diagnostics).</summary>
    public int HeldCount => Volatile.Read(ref _held);

    public void Start()
    {
        for (int i = 0; i < _acceptors; i++)
            _loops.Add(Task.Run(() => AcceptLoopAsync(_stopAccepting.Token)));
    }

    /// <summary>The data host is up: answer everything held (and anything still being read)
    /// with <paramref name="answer"/>.</summary>
    public void Ready(Func<NamedPipeServerStream, byte[], CancellationToken, Task> answer) => _ready.TrySetResult(answer);

    /// <summary>The data host could not be built: close every held connection.</summary>
    public void Abandon() => _ready.TrySetCanceled();

    /// <summary>Stops creating new listener instances and waits for the accept loops to end.</summary>
    public async Task StopAcceptingAsync()
    {
        _stopAccepting.Cancel();
        try { await Task.WhenAll(_loops); } catch { /* loops handle their own errors */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = _createPipe();
                await pipe.WaitForConnectionAsync(ct);
                var connected = pipe;
                pipe = null;
                _ = HoldAsync(connected);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); break; }
            catch (Exception ex)
            {
                _log("startup pipe accept error", ex);
                pipe?.Dispose();
                try { await Task.Delay(200, CancellationToken.None); } catch { }
            }
        }
    }

    private async Task HoldAsync(NamedPipeServerStream pipe)
    {
        try
        {
            byte[]? request = await FrameProtocol.ReadFrameAsync(pipe, _lifetime);
            if (request is null) { pipe.Dispose(); return; } // probe that left without a request
            Interlocked.Increment(ref _held);
            var answer = await _ready.Task.WaitAsync(_lifetime);
            await answer(pipe, request, _lifetime); // owns and disposes the pipe
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException) _log("held startup connection failed", ex);
            pipe.Dispose();
        }
    }
}
