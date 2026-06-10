using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Handles graceful shutdown of the DocumentLocator Windows service before
    /// the installer overwrites its exe.
    ///
    /// The service grants any authenticated user READ+WRITE pipe access, so no
    /// elevation is required to send the shutdown message.
    ///
    /// Flow:
    ///   1. SendShutdownAsync() — sends {"type":"shutdown"} over the named pipe.
    ///      The service acks and then stops itself via ServiceBase.Stop().
    ///   2. After 1 500 ms the process has exited and the file lock is released.
    ///
    /// The caller fires this at the very start of installation and does NOT await
    /// it — extraction proceeds in parallel so the overall install is not slowed.
    /// When AddinInstaller reaches DocumentLocator.Service.exe it calls
    /// TryCopyServiceExeAsync() which checks whether the 1 500 ms have elapsed
    /// and, if not, waits for the remainder before retrying the copy up to three
    /// times.
    /// </summary>
    public static class DocumentLocatorHelper
    {
        private const string PipeName        = "DocumentLocator";
        private const string ServiceExeName  = "DocumentLocator.Service.exe";
        private const int    ShutdownWaitMs  = 1_500;
        private const int    ConnectTimeoutMs = 2_000;
        private const int    RetryDelayMs    = 700;
        private const int    MaxRetries      = 3;

        private static readonly string ShutdownRequest = "{\"type\":\"shutdown\"}";

        // Timestamp of when SendShutdownAsync() was called, so callers can measure
        // elapsed time and avoid waiting longer than necessary.
        private static DateTime _shutdownRequestedAt = DateTime.MinValue;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a shutdown request to the DocumentLocator service (if running)
        /// and records the request time. Returns immediately after the pipe call
        /// so the installer can proceed with other work while the service stops.
        ///
        /// Call this as a fire-and-forget task at the very start of installation:
        ///   _ = DocumentLocatorHelper.SendShutdownAsync();
        /// </summary>
        public static async Task SendShutdownAsync()
        {
            _shutdownRequestedAt = DateTime.UtcNow;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        using (var pipe = new NamedPipeClientStream(
                            ".", PipeName, PipeDirection.InOut))
                        {
                            pipe.Connect(ConnectTimeoutMs);
                            WriteFrame(pipe, ShutdownRequest);
                            ReadFrame(pipe); // wait for ack
                        }
                    }
                    catch
                    {
                        // Service is not running or pipe unavailable — nothing to shut down.
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                // Non-critical — if anything goes wrong we still proceed with extraction.
            }
        }

        /// <summary>
        /// Attempts to copy <paramref name="sourceStream"/> to <paramref name="destPath"/>
        /// (which is the DocumentLocator.Service.exe on disk).
        ///
        /// Before the first copy attempt, waits until at least <see cref="ShutdownWaitMs"/>
        /// have elapsed since the shutdown request was sent, ensuring the service process
        /// has had time to exit and release its file lock.
        ///
        /// If the file is still locked, retries up to <see cref="MaxRetries"/> times
        /// with increasing delays. On final failure the file is left as-is (silent skip).
        ///
        /// Returns true if the file was copied, false if it was skipped.
        /// </summary>
        public static async Task<bool> TryCopyServiceExeAsync(
            Stream sourceStream, string destPath)
        {
            // Wait for the remainder of the 1 500 ms shutdown window.
            await WaitForShutdownWindowAsync().ConfigureAwait(false);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    // sourceStream may be read-once; seek back to start on retries.
                    if (sourceStream.CanSeek)
                        sourceStream.Seek(0, SeekOrigin.Begin);

                    using (var fs = new FileStream(
                        destPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await sourceStream.CopyToAsync(fs).ConfigureAwait(false);
                    }
                    return true; // success
                }
                catch (IOException) when (attempt < MaxRetries)
                {
                    // File still locked — wait and retry.
                    await Task.Delay(RetryDelayMs * attempt).ConfigureAwait(false);
                }
                catch
                {
                    // On the last attempt or unexpected error — silently skip.
                    break;
                }
            }

            return false; // all retries exhausted — leave the existing file in place
        }

        /// <summary>
        /// Returns true if <paramref name="entryName"/> is the DocumentLocator service exe.
        /// </summary>
        public static bool IsServiceExe(string entryName)
        {
            string normalized = entryName.Replace('/', '\\');
            return normalized.EndsWith(ServiceExeName, StringComparison.OrdinalIgnoreCase);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Waits until at least <see cref="ShutdownWaitMs"/> ms have elapsed since
        /// the shutdown request was sent. If the shutdown was never sent (or the
        /// wait has already passed), returns immediately.
        /// </summary>
        private static async Task WaitForShutdownWindowAsync()
        {
            if (_shutdownRequestedAt == DateTime.MinValue) return;

            int elapsed = (int)(DateTime.UtcNow - _shutdownRequestedAt).TotalMilliseconds;
            int remaining = ShutdownWaitMs - elapsed;
            if (remaining > 0)
                await Task.Delay(remaining).ConfigureAwait(false);
        }

        // ── Length-prefixed frame I/O (matches PipeProtocol on the service side) ─

        private static void WriteFrame(Stream s, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            byte[] len  = BitConverter.GetBytes(body.Length);
            s.Write(len,  0, len.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        private static string ReadFrame(Stream s)
        {
            byte[] lb = ReadExact(s, 4);
            if (lb == null) return null;
            int len = BitConverter.ToInt32(lb, 0);
            if (len <= 0 || len > 64 * 1024 * 1024) return null;
            byte[] body = ReadExact(s, len);
            return body == null ? null : Encoding.UTF8.GetString(body);
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            byte[] buf  = new byte[count];
            int    read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n == 0) return null;
                read += n;
            }
            return buf;
        }
    }
}
