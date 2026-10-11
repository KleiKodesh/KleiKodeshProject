using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.ServiceClient
{
    /// <summary>
    /// A channel that starts the service when it is not there. Wraps the pipe channel: a call
    /// that finds no service running asks <see cref="KitveiHakodeshServiceLauncher"/> to bring
    /// one up and is retried once. Everything else - including a second failure - passes
    /// through unchanged, so the caller still sees
    /// <see cref="KitveiHakodeshServiceUnavailableException"/> when the service truly cannot run.
    /// </summary>
    public sealed class KitveiHakodeshServiceSelfStartingChannel : IKitveiHakodeshServiceChannel
    {
        private readonly IKitveiHakodeshServiceChannel _inner;
        private readonly KitveiHakodeshServiceLauncher _launcher;

        public KitveiHakodeshServiceSelfStartingChannel(IKitveiHakodeshServiceChannel inner, KitveiHakodeshServiceLauncher launcher)
        {
            _inner = inner;
            _launcher = launcher;
        }

        public async Task<byte[]> CallAsync(byte[] requestEnvelope, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.CallAsync(requestEnvelope, cancellationToken).ConfigureAwait(false);
            }
            catch (KitveiHakodeshServiceUnavailableException)
            {
                await _launcher.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
                return await _inner.CallAsync(requestEnvelope, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<Stream> OpenStreamAsync(byte[] requestEnvelope, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.OpenStreamAsync(requestEnvelope, cancellationToken).ConfigureAwait(false);
            }
            catch (KitveiHakodeshServiceUnavailableException)
            {
                await _launcher.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
                return await _inner.OpenStreamAsync(requestEnvelope, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
