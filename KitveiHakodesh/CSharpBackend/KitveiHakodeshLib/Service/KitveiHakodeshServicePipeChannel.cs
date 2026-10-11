using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// The named-pipe channel to a running KitveiHakodesh service. Mirrors the service's
    /// connection model exactly (Ipc/PipeServer.cs): one connection per request; an ordinary op
    /// answers with one frame and the server closes, a streaming op answers with many frames
    /// until the server closes or the client disconnects.
    ///
    /// There is no reconnect ladder and no fallback here on purpose. If the pipe is not there
    /// the service is not running, and the hosted app cannot work without it.
    /// </summary>
    public sealed class KitveiHakodeshServicePipeChannel : IKitveiHakodeshServiceChannel
    {
        /// <summary>The installed service's pipe (PipeServer.DefaultPipeName).</summary>
        public const string DefaultPipeName = "KitveiHakodesh";

        /// <summary>A spawner that gave its service instance a private pipe name passes it on
        /// through this variable - the same one the service itself reads.</summary>
        public const string PipeNameEnvironmentVariable = "KHS_PIPE_NAME";

        private readonly string _pipeName;
        private readonly int _connectTimeoutMilliseconds;

        public KitveiHakodeshServicePipeChannel(string pipeName = null, int connectTimeoutMilliseconds = 5000)
        {
            _pipeName = !string.IsNullOrEmpty(pipeName) ? pipeName : ResolvePipeName();
            _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
        }

        public string PipeName => _pipeName;

        /// <summary>Environment override first, the installed service's fixed name otherwise.</summary>
        public static string ResolvePipeName()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable);
            return !string.IsNullOrEmpty(fromEnvironment) ? fromEnvironment : DefaultPipeName;
        }

        public async Task<byte[]> CallAsync(byte[] requestEnvelope, CancellationToken cancellationToken)
        {
            using (NamedPipeClientStream pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                await KitveiHakodeshServiceFrameProtocol.WriteFrameAsync(pipe, requestEnvelope, cancellationToken).ConfigureAwait(false);
                byte[] response = await KitveiHakodeshServiceFrameProtocol.ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (response == null)
                    throw new KitveiHakodeshServiceUnavailableException(
                        "The KitveiHakodesh service closed the connection without answering.");
                return response;
            }
        }

        public async Task<Stream> OpenStreamAsync(byte[] requestEnvelope, CancellationToken cancellationToken)
        {
            NamedPipeClientStream pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await KitveiHakodeshServiceFrameProtocol.WriteFrameAsync(pipe, requestEnvelope, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken cancellationToken)
        {
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                // ConnectAsync keeps retrying inside the timeout, which also rides out the brief
                // moment when every listener instance of the service is busy with another client.
                await pipe.ConnectAsync(_connectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is UnauthorizedAccessException)
            {
                pipe.Dispose();
                throw new KitveiHakodeshServiceUnavailableException(
                    "The KitveiHakodesh service is not reachable on pipe '" + _pipeName + "'.", ex);
            }
        }
    }
}
