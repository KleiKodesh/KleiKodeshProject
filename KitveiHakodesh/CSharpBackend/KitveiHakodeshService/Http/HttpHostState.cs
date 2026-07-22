using System.Security.Cryptography;

namespace KitveiHakodeshService.Http;

/// <summary>
/// Holds the loopback HTTP host's endpoint secrets — the OS-assigned port and a per-instance
/// bearer token — so the RPC dispatcher can hand BOTH to the spawner over the PRIVATE named
/// pipe (the <c>getHttpPort</c> op). Neither is ever written to a file or printed to stdout:
/// a world-readable discovery file would leak the endpoint to every process/user on the
/// machine. The pipe is ACL'd to the spawning user, so passing them through it keeps the
/// endpoint private.
///
/// The token is the actual security boundary (the ephemeral port is only obscurity): loopback
/// TCP is reachable by ANY local process and — absent the token check — by malicious web pages
/// via localhost CSRF. Every /rpc and /rpc-stream request must present the token; only callers
/// who obtained it over the ACL'd pipe can talk to the host. A fresh 256-bit random token is
/// generated per service instance, so a token from one instance is useless against another
/// (and against a restarted instance).
///
/// The port is set once the <see cref="HttpHostServer"/> has actually bound, so a
/// <c>getHttpPort</c> that races startup awaits the bind instead of seeing 0.
/// </summary>
public sealed class HttpHostState
{
    private readonly TaskCompletionSource<int> _port =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Per-instance bearer token, generated eagerly at construction (256-bit CSPRNG,
    /// hex). Required on every HTTP data request via the <c>X-KHS-Token</c> header.</summary>
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Called by the HTTP host once its listener is bound.</summary>
    public void SetPort(int port) => _port.TrySetResult(port);

    /// <summary>Awaits the bound port (resolves immediately once known).</summary>
    public Task<int> GetPortAsync(CancellationToken ct) => _port.Task.WaitAsync(ct);
}
