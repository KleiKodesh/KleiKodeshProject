using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// A channel to the KitveiHakodesh service. Carries request and response envelopes as
    /// opaque bytes: the caller encodes the MessagePack <c>{Op, Args}</c> request and decodes
    /// the <c>{Ok, Result, Error}</c> reply, the channel only moves them.
    ///
    /// The seam between "who asks" and "who answers". <see cref="KitveiHakodeshServicePipeChannel"/>
    /// is the only implementation today; an in-process answerer would be a second one.
    /// </summary>
    public interface IKitveiHakodeshServiceChannel
    {
        /// <summary>One request, one reply: the response envelope bytes.</summary>
        /// <exception cref="KitveiHakodeshServiceUnavailableException">The service is not running
        /// or closed the connection without answering.</exception>
        Task<byte[]> CallAsync(byte[] requestEnvelope, CancellationToken cancellationToken);

        /// <summary>
        /// One request, many replies (the streaming ops, e.g. <c>ftsSearchStream</c>). Returns the
        /// connection as a stream of length-prefixed frames - every frame is
        /// <c>[int32 little-endian length][response envelope bytes]</c>, exactly what the service's
        /// HTTP <c>/rpc-stream</c> route also emits. Disposing the stream is the cancel signal.
        /// </summary>
        /// <exception cref="KitveiHakodeshServiceUnavailableException">The service is not running.</exception>
        Task<Stream> OpenStreamAsync(byte[] requestEnvelope, CancellationToken cancellationToken);
    }

    /// <summary>The service could not be reached. The service is a hard dependency of every
    /// hosted process, so callers surface this to the user rather than working around it.</summary>
    public sealed class KitveiHakodeshServiceUnavailableException : Exception
    {
        public KitveiHakodeshServiceUnavailableException(string message) : base(message) { }
        public KitveiHakodeshServiceUnavailableException(string message, Exception inner) : base(message, inner) { }
    }
}
