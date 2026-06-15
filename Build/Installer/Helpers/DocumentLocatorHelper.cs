using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Handles graceful shutdown of the DocumentLocator Windows service before
    /// the installer overwrites its exe, and triggers a reindex after install.
    ///
    /// The service grants any authenticated user READ+WRITE pipe access, so no
    /// elevation is required to send the shutdown or reindex message.
    ///
    /// Shutdown flow (start of install):
    ///   1. SendShutdownAsync() — sends {"type":"shutdown"} over the named pipe.
    ///      The service acks and then stops itself via ServiceBase.Stop().
    ///   2. After 1 500 ms the process has exited and the file lock is released.
    ///
    /// Reindex flow (end of install):
    ///   1. EnsureServiceRunningAndReindexAsync() — starts the service via SCM
    ///      if it is not running, waits for the pipe to become available, then
    ///      sends {"type":"reindex"}. The service acks and rebuilds in the
    ///      background; the installer does not wait for the rebuild to finish.
    /// </summary>
    public static class DocumentLocatorHelper
    {
        private const string PipeName         = "DocumentLocator";
        private const string ServiceExeName   = "DocumentLocator.Service.exe";
        private const string SvcName          = "DocumentLocatorSvc";
        private const int    ShutdownWaitMs   = 1_500;
        private const int    ConnectTimeoutMs = 2_000;
        private const int    RetryDelayMs     = 700;
        private const int    MaxRetries       = 3;
        private const int    StartupPollMs    = 300;
        private const int    StartupTimeoutMs = 15_000;

        private static readonly string ShutdownRequest = "{\"type\":\"shutdown\"}";
        private static readonly string ReindexRequest  = "{\"type\":\"reindex\"}";

        // Timestamp of when SendShutdownAsync() was called, so callers can measure
        // elapsed time and avoid waiting longer than necessary.
        private static DateTime _shutdownRequestedAt = DateTime.MinValue;

        // ── Win32 SCM ─────────────────────────────────────────────────────────────

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(
            IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        private const uint SC_MANAGER_CONNECT         = 0x0001;
        private const uint SERVICE_START              = 0x0010;
        private const int  ERROR_SERVICE_ALREADY_RUNNING = 1056;

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
        /// Ensures the DocumentLocator Windows Service is registered in the SCM with
        /// an ImagePath pointing to the freshly extracted exe in the install folder.
        ///
        /// Called by the installer after extraction, while the installer is still a
        /// visible foreground process that can properly surface a UAC prompt.
        /// This avoids having the VSTO (running inside Word) attempt the elevation,
        /// which often fails silently because Word runs under a restricted desktop.
        ///
        /// Flow:
        ///   - If not installed at all → run --install elevated (UAC prompt once).
        ///   - If installed but ImagePath points elsewhere (e.g. stale dev registration)
        ///     → run --install elevated which uninstalls-then-reinstalls automatically.
        ///   - If already installed and current → no-op.
        ///
        /// Returns true if the service is registered and current after the call.
        /// Returns false if the user declined the UAC prompt.
        /// </summary>
        public static async Task<bool> EnsureServiceInstalledAsync()
        {
            string serviceExe = Path.Combine(AddinInstaller.InstallPath, ServiceExeName);
            if (!File.Exists(serviceExe)) return false; // exe not deployed yet — skip

            if (IsServiceInstalledAndCurrent(serviceExe)) return true;

            // Launch --install elevated.  The service exe handles the case where it is
            // already registered at a different path by uninstalling first.
            var psi = new ProcessStartInfo
            {
                FileName        = serviceExe,
                Arguments       = "--install",
                Verb            = "runas",
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
            };

            try
            {
                using (var proc = Process.Start(psi))
                    proc?.WaitForExit(30_000);
            }
            catch (System.ComponentModel.Win32Exception ex)
                when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — user clicked No
            {
                return false;
            }

            // Poll until SCM reflects the registration (up to 10 s).
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (IsServiceInstalledAndCurrent(serviceExe)) return true;
                await Task.Delay(300).ConfigureAwait(false);
            }

            return IsServiceInstalled(); // last-chance check
        }

        /// <summary>
        /// If the DocumentLocator service is installed, starts it (if stopped) and
        /// sends a reindex command so the file-system index is rebuilt against the
        /// freshly installed binaries.
        ///
        /// The service acks immediately and rebuilds in the background — this method
        /// returns as soon as the ack is received (or on any error). It is safe to
        /// call fire-and-forget from the installer.
        ///
        /// No-ops immediately and silently if the service is not installed
        /// (e.g. first-run before the VSTO add-in has ever registered it).
        /// </summary>
        public static async Task EnsureServiceRunningAndReindexAsync()
        {
            try
            {
                // Check installation before doing anything else — avoids the
                // 15-second pipe-wait on machines where the service doesn't exist yet.
                bool installed = await Task.Run(() => IsServiceInstalled())
                    .ConfigureAwait(false);
                if (!installed) return;

                await Task.Run(() => StartServiceIfStopped()).ConfigureAwait(false);

                // Wait for the pipe to become ready after start.
                await WaitForPipeAsync().ConfigureAwait(false);

                // Send reindex — fire-and-forget from the service's perspective.
                await Task.Run(() =>
                {
                    try
                    {
                        using (var pipe = new NamedPipeClientStream(
                            ".", PipeName, PipeDirection.InOut))
                        {
                            pipe.Connect(ConnectTimeoutMs);
                            WriteFrame(pipe, ReindexRequest);
                            ReadFrame(pipe); // wait for ack (returns immediately)
                        }
                    }
                    catch { /* non-critical */ }
                }).ConfigureAwait(false);
            }
            catch
            {
                // Non-critical — reindex failure does not affect the install result.
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
        /// Returns true if the DocumentLocator service is registered in the SCM.
        /// Reads HKLM — no elevation required.
        /// </summary>
        private static bool IsServiceInstalled()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\DocumentLocatorSvc"))
                return key != null;
        }

        /// <summary>
        /// Returns true when the service is registered AND its ImagePath matches
        /// <paramref name="expectedExe"/>. A stale registration (e.g. from a dev
        /// build at a different path) returns false so we reinstall.
        /// </summary>
        private static bool IsServiceInstalledAndCurrent(string expectedExe)
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\DocumentLocatorSvc"))
            {
                if (key == null) return false;
                string imagePath = (key.GetValue("ImagePath") as string ?? "").Trim('"');
                return string.Equals(imagePath, expectedExe, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(imagePath);
            }
        }

        /// <summary>
        /// Opens the SCM and issues StartService. Ignores ERROR_SERVICE_ALREADY_RUNNING.
        /// Returns silently if the service is not installed.
        /// </summary>
        private static void StartServiceIfStopped()
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return; // SCM unavailable — skip

            try
            {
                IntPtr svc = OpenService(scm, SvcName, SERVICE_START);
                if (svc == IntPtr.Zero) return; // service not installed — skip

                try
                {
                    bool started = StartService(svc, 0, IntPtr.Zero);
                    if (!started)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != ERROR_SERVICE_ALREADY_RUNNING)
                            return; // unexpected error — fall through, pipe wait will handle it
                    }
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }
        }

        /// <summary>
        /// Polls until the DocumentLocator pipe is accepting connections or the
        /// timeout elapses. Does not throw on timeout — callers handle gracefully.
        /// </summary>
        private static async Task WaitForPipeAsync()
        {
            int elapsed = 0;
            while (elapsed < StartupTimeoutMs)
            {
                await Task.Delay(StartupPollMs).ConfigureAwait(false);
                elapsed += StartupPollMs;
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                    {
                        pipe.Connect(300);
                        return; // pipe is up
                    }
                }
                catch { /* not ready yet */ }
            }
        }

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
