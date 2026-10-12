using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// The one Windows service this product installs: KitveiHakodeshService, registered as
    /// <c>KitveiHakodeshSvc</c> (LocalSystem, demand start). It owns the seforim data, the
    /// search indexes and the NTFS file index that DocumentLocatorSvc used to own; that older
    /// service is retired by <see cref="LegacyServiceRetirement"/>.
    ///
    /// The installer itself stays asInvoker (per-user install). Registration is the one step
    /// that needs admin, so the service exe is run elevated with <c>--install</c> (one UAC
    /// prompt) - normally a no-op on updates, when the registration already points at the
    /// installed exe. Everything else here talks to the running service over its pipe, which
    /// needs no elevation.
    /// </summary>
    public static class KitveiHakodeshServiceHelper
    {
        public const string ServiceName   = "KitveiHakodeshSvc";
        public const string PipeName      = "KitveiHakodesh";
        /// <summary>The service lives in its own folder under the install root, as the hosts expect
        /// (KitveiHakodeshLib Service\KitveiHakodeshServiceLauncher looks there).</summary>
        public const string ServiceFolder = "KitveiHakodeshService";
        public const string ServiceExeName = "KitveiHakodeshService.exe";

        private const int ShutdownWaitMs   = 1_500;
        private const int ConnectTimeoutMs = 2_000;
        private const int RetryDelayMs     = 700;
        private const int MaxRetries       = 3;
        private const int StartupPollMs    = 300;
        private const int StartupTimeoutMs = 20_000;

        private static DateTime _shutdownRequestedAt = DateTime.MinValue;

        public static string ServiceExePath =>
            Path.Combine(AddinInstaller.InstallPath, ServiceFolder, ServiceExeName);

        public static bool IsServiceDeployed => File.Exists(ServiceExePath);

        // ── Shutdown before extraction ────────────────────────────────────────────

        /// <summary>Asks a running service to exit (pipe op <c>shutdown</c>) so the extraction
        /// can overwrite its files. The service stops within a moment; <see cref="TryCopyServiceExeAsync"/>
        /// waits out the rest of the window.</summary>
        public static async Task SendShutdownAsync()
        {
            _shutdownRequestedAt = DateTime.UtcNow;
            await Task.Run(() => TrySendOp("shutdown")).ConfigureAwait(false);
        }

        // ── Registration ─────────────────────────────────────────────────────────

        /// <summary>
        /// Set by the repair flow: re-register the service even when the registration looks
        /// current, because "looks current" is exactly what a broken install can fake (a
        /// registration that is disabled, or whose permissions were changed). Cleared only by a
        /// successful registration, so declining the prompt and retrying still re-registers.
        /// </summary>
        public static bool ForceRegistration { get; set; }

        /// <summary>
        /// Stops whatever runs the service from this install and waits until it is gone, so the
        /// files under KitveiHakodeshService\ (its exe and the databases it holds open) can be
        /// overwritten or deleted. Used before extraction and before the repair's clean-up.
        ///   1. the "shutdown" pipe op - the installed service stops itself (no admin needed);
        ///   2. wait, bounded, until no instance listens on the pipe;
        ///   3. end any console copy still running from the install folder (a portable-style
        ///      instance this user started; a SYSTEM one cannot be ended here and has already
        ///      stopped in step 1).
        /// Best effort: a stuck service never blocks the install; the copy step retries.
        /// </summary>
        public static async Task StopServiceAsync()
        {
            await SendShutdownAsync().ConfigureAwait(false);

            // Gone means the pipe is closed AND, for the registered service, Windows reports it
            // stopped: the process can still hold its exe and databases for a moment after it
            // closes the pipe. Reading the status needs no elevation.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && (IsPipeListening() || IsRegisteredServiceStillUp()))
                await Task.Delay(StartupPollMs).ConfigureAwait(false);

            string serviceFolder = Path.Combine(AddinInstaller.InstallPath, ServiceFolder);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ServiceExeName)))
            {
                try
                {
                    string path = process.MainModule?.FileName ?? "";
                    if (path.StartsWith(serviceFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(5_000);
                    }
                }
                catch { /* not ours to end (SYSTEM), or already gone */ }
                finally { process.Dispose(); }
            }
        }

        /// <summary>
        /// Registers the service when the package deployed it and the registration is missing,
        /// points elsewhere, is disabled, or a repair asked for it. False when the user declined
        /// the prompt or registration failed; also false when the package does not ship the
        /// service at all.
        /// </summary>
        public static async Task<bool> EnsureServiceInstalledAsync(IProgress<string> status = null)
        {
            string serviceExe = ServiceExePath;
            if (!File.Exists(serviceExe)) return false;
            // The elevated --install run does BOTH halves of the switch: registers this service
            // and retires DocumentLocatorSvc. So it is needed when either is outstanding - one
            // prompt, never two. A registration that points here but is disabled is not current,
            // and the repair flow re-registers regardless.
            bool force = ForceRegistration || IsServiceDisabled();
            if (!force && IsServiceInstalledAndCurrent(serviceExe) && !IsLegacyServiceRegistered()) return true;

            // Windows can take several seconds to show the prompt (it checks the freshly written
            // exe first); without a word here the user takes the installer for finished or stuck.
            status?.Report(ApprovalPromptStatus);

            var psi = new ProcessStartInfo
            {
                FileName        = serviceExe,
                Arguments       = "--install",
                Verb            = "runas",
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
                // .NET Framework passes this window to ShellExecuteEx only when ErrorDialog is
                // set. With an owner the prompt comes up in front of the installer; without one
                // Windows opens it in the background, as a flashing taskbar button.
                ErrorDialog              = true,
                ErrorDialogParentHandle  = InstallerWindowHandle(),
            };

            try
            {
                // Off the UI thread: a "runas" start blocks until the prompt is answered, and on
                // the UI thread that froze the window ("not responding") the whole time.
                // Generous: re-registering waits for a running instance to stop and retires
                // DocumentLocatorSvc, each bounded at 30 s inside the service.
                await Task.Run(() =>
                {
                    using (var proc = Process.Start(psi))
                        proc?.WaitForExit(120_000);
                }).ConfigureAwait(false);
            }
            catch (System.ComponentModel.Win32Exception ex)
                when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the user clicked No
            {
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (IsServiceInstalledAndCurrent(serviceExe) && !IsServiceDisabled())
                {
                    ForceRegistration = false;
                    return true;
                }
                await Task.Delay(300).ConfigureAwait(false);
            }
            return IsServiceInstalled();
        }

        /// <summary>Status line shown while Windows prepares the admin prompt.</summary>
        private const string ApprovalPromptStatus = "Windows תבקש אישור בעוד רגע - נא ללחוץ 'כן'...";

        /// <summary>The installer's main window, as the owner of the admin prompt; zero when
        /// there is none (the prompt then still appears, only possibly in the background).</summary>
        private static IntPtr InstallerWindowHandle()
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return IntPtr.Zero;
                return app.Dispatcher.Invoke(() =>
                    app.MainWindow == null
                        ? IntPtr.Zero
                        : new System.Windows.Interop.WindowInteropHelper(app.MainWindow).Handle);
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>The registered service has not reached Stopped yet (false when it is not
        /// registered, or its state cannot be read).</summary>
        private static bool IsRegisteredServiceStillUp()
        {
            try
            {
                using (var controller = new System.ServiceProcess.ServiceController(ServiceName))
                    return controller.Status != System.ServiceProcess.ServiceControllerStatus.Stopped;
            }
            catch { return false; }
        }

        /// <summary>The registration exists but is disabled (Start = 4): Windows will refuse to
        /// start it, so it must be registered again.</summary>
        private static bool IsServiceDisabled()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + ServiceName))
                return key?.GetValue("Start") is int start && start == 4;
        }

        /// <summary>DocumentLocatorSvc, the service this one replaces, is still registered.
        /// Reads HKLM only; no elevation needed.</summary>
        private static bool IsLegacyServiceRegistered()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\DocumentLocatorSvc"))
                return key != null;
        }

        /// <summary>Starts the registered service and asks it to rebuild the file index so it
        /// reflects the files this install just wrote. Best effort; never blocks the install.</summary>
        public static async Task EnsureServiceRunningAndReindexAsync()
        {
            try
            {
                bool installed = await Task.Run(() => IsServiceInstalled()).ConfigureAwait(false);
                if (!installed) return;
                await Task.Run(() => StartServiceIfStopped()).ConfigureAwait(false);
                if (!await WaitForPipeAsync().ConfigureAwait(false)) return;
                await Task.Run(() => TrySendOp("resetDocumentLocatorIndex")).ConfigureAwait(false);
            }
            catch
            {
                // Non-critical: the index rebuilds itself on the service's own schedule.
            }
        }

        // ── Extraction helpers ────────────────────────────────────────────────────

        public static bool IsServiceExe(string entryName)
        {
            string normalized = entryName.Replace('/', '\\');
            return normalized.EndsWith(ServiceFolder + "\\" + ServiceExeName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The service exe may still be locked by the exiting service; wait out the
        /// shutdown window and retry a few times. On permanent failure the existing file stays.</summary>
        public static async Task<bool> TryCopyServiceExeAsync(Stream sourceStream, string destPath)
        {
            await WaitForShutdownWindowAsync().ConfigureAwait(false);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    if (sourceStream.CanSeek) sourceStream.Seek(0, SeekOrigin.Begin);
                    using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        await sourceStream.CopyToAsync(fs).ConfigureAwait(false);
                    return true;
                }
                catch (IOException) when (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelayMs * attempt).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
            return false;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static bool IsServiceInstalled()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + ServiceName))
                return key != null;
        }

        private static bool IsServiceInstalledAndCurrent(string expectedExe)
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + ServiceName))
            {
                if (key == null) return false;
                string imagePath = (key.GetValue("ImagePath") as string ?? "").Trim().Trim('"');
                return string.Equals(imagePath, expectedExe, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(imagePath);
            }
        }

        private static void StartServiceIfStopped()
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return;
            try
            {
                IntPtr svc = OpenService(scm, ServiceName, SERVICE_START);
                if (svc == IntPtr.Zero) return;
                try { StartService(svc, 0, IntPtr.Zero); }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }
        }

        /// <summary>
        /// Whether the service pipe exists, WITHOUT connecting to it. File.Exists on a pipe
        /// path opens the pipe, and the service treats its very first connection as the user
        /// it serves - a probe that connects and leaves must never be that connection.
        /// Listing the pipe directory does not connect.
        /// </summary>
        private static bool IsPipeListening()
        {
            try
            {
                foreach (string pipe in Directory.EnumerateFiles(@"\\.\pipe\"))
                    if (string.Equals(Path.GetFileName(pipe), PipeName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* listing refused - treat as not listening */ }
            return false;
        }

        private static async Task<bool> WaitForPipeAsync()
        {
            int elapsed = 0;
            while (elapsed < StartupTimeoutMs)
            {
                await Task.Delay(StartupPollMs).ConfigureAwait(false);
                elapsed += StartupPollMs;
                if (IsPipeListening()) return true;
            }
            return false;
        }

        /// <summary>
        /// Waits for the service asked to shut down to be gone: a registered service until the
        /// SCM reports it stopped (its data host may take seconds to wind down an index build),
        /// otherwise the fixed exit window. Bounded, so a stuck service never stalls the install.
        /// </summary>
        private static async Task WaitForShutdownWindowAsync()
        {
            if (_shutdownRequestedAt == DateTime.MinValue) return;

            if (IsServiceInstalled())
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline && IsPipeListening())
                    await Task.Delay(StartupPollMs).ConfigureAwait(false);
                return;
            }

            int elapsed = (int)(DateTime.UtcNow - _shutdownRequestedAt).TotalMilliseconds;
            int remaining = ShutdownWaitMs - elapsed;
            if (remaining > 0) await Task.Delay(remaining).ConfigureAwait(false);
        }

        /// <summary>
        /// One request on the service pipe. The wire is the service's MessagePack envelope,
        /// <c>{Op: name}</c> with no args, built by hand here because the installer carries no
        /// MessagePack library: a 1-entry map (0x81), fixstr key "Op" (0xA2 'O' 'p'), fixstr
        /// value (0xA0 | length, then the bytes). Framed as [int32 LE length][payload].
        /// </summary>
        private static void TrySendOp(string op)
        {
            try
            {
                byte[] opBytes = Encoding.UTF8.GetBytes(op);
                if (opBytes.Length > 31) throw new ArgumentException("op too long for a fixstr", nameof(op));
                var frame = new byte[1 + 3 + 1 + opBytes.Length];
                frame[0] = 0x81; frame[1] = 0xA2; frame[2] = (byte)'O'; frame[3] = (byte)'p';
                frame[4] = (byte)(0xA0 | opBytes.Length);
                Buffer.BlockCopy(opBytes, 0, frame, 5, opBytes.Length);

                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(ConnectTimeoutMs);
                    byte[] length = BitConverter.GetBytes(frame.Length);
                    pipe.Write(length, 0, 4);
                    pipe.Write(frame, 0, frame.Length);
                    pipe.Flush();
                    ReadFrame(pipe); // reply, or EOF once the service has acted (shutdown closes first)
                }
            }
            catch
            {
                // Service not running or pipe unavailable: nothing to do.
            }
        }

        private static byte[] ReadFrame(Stream s)
        {
            byte[] lengthBytes = ReadExact(s, 4);
            if (lengthBytes == null) return null;
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > 64 * 1024 * 1024) return null;
            return ReadExact(s, length);
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n == 0) return null;
                read += n;
            }
            return buffer;
        }

        // ── Win32 SCM ─────────────────────────────────────────────────────────────

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_START      = 0x0010;
    }
}
