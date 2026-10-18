using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Brings the KitveiHakodesh service up when no instance is listening on the pipe - and,
    /// for the installed product, installs or repairs it when that is what stands in the way.
    ///
    /// The service is a hard dependency of every hosted process. In order:
    ///   1. Listening already: nothing to do.
    ///   2. Registered with Windows (KitveiHakodeshSvc): start it (no prompt - starting is granted
    ///      to every user).
    ///   3. Not started, and the product's service exe is installed
    ///      (%LocalAppData%\KleiKodesh\KitveiHakodeshService): install / repair it - its
    ///      <c>--install</c> run elevated (one UAC prompt) re-registers it, enabled - then start
    ///      it. Not asked, declined or failed: run that exe as a console copy for this user
    ///      meanwhile; the service ends that copy once it starts.
    ///   4. Registered but nothing installed to repair it from: fail with the reason.
    ///   5. Neither (a dev checkout, a portable copy): spawn the exe as a console process tied to
    ///      this one (KHS_OWNER_PID), exactly as the dev server does. No prompt ever.
    ///
    /// The UAC prompt is never raised unasked: data requests and the startup warm-up call with
    /// allowElevation false and only start or spawn; the page's banner says the service needs
    /// installing, and its button (host action retryService ->
    /// <see cref="AllowAnotherRepairAttempt"/>) runs the elevated step. Never two prompts at
    /// once: one gate in this process, a machine-wide mutex across processes.
    /// The failure carries a <see cref="KitveiHakodeshServiceUnavailableReason"/> so the page can
    /// say what is wrong.
    /// </summary>
    public sealed class KitveiHakodeshServiceLauncher
    {
        /// <summary>Points at the service executable explicitly (installer, dev rig).</summary>
        public const string ExecutableEnvironmentVariable = "KHS_SERVICE_EXE";

        /// <summary>The name the installer registers the service under (ServiceRegistration in the service).</summary>
        public const string InstalledServiceName = "KitveiHakodeshSvc";

        private const string ExecutableName = "KitveiHakodeshService.exe";
        private const string InstalledServiceFolder = "KitveiHakodeshService";
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

        // Process-wide: several AppViewers (Word task panes) share one service and one prompt.
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan StartFailureMemory = TimeSpan.FromSeconds(10);
        private static DateTime _lastStartFailureUtc = DateTime.MinValue;
        private static bool _elevationAttempted;
        private static KitveiHakodeshServiceUnavailableReason? _lastElevationFailure;

        private readonly string _pipeName;

        public KitveiHakodeshServiceLauncher(string pipeName = null)
        {
            _pipeName = !string.IsNullOrEmpty(pipeName) ? pipeName : KitveiHakodeshServicePipeChannel.ResolvePipeName();
        }

        /// <summary>Lets the next failure ask for elevation again (the user pressed "try again").</summary>
        public static void AllowAnotherRepairAttempt()
        {
            _lastStartFailureUtc = DateTime.MinValue;
            _elevationAttempted = false;
            _lastElevationFailure = null;
        }

        /// <summary>
        /// True when a service instance is listening on the pipe right now - checked WITHOUT
        /// connecting. File.Exists on a pipe path opens the pipe, and the installed service
        /// takes its very first connection as the user it serves; a probe that connects and
        /// leaves must never be that connection. Listing the pipe directory does not connect.
        /// </summary>
        public bool IsListening()
        {
            try
            {
                foreach (string pipe in Directory.EnumerateFiles(@"\\.\pipe\"))
                    if (string.Equals(Path.GetFileName(pipe), _pipeName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* listing refused - treat as not listening */ }
            return false;
        }

        /// <summary>
        /// Makes sure a service instance is listening (see the class remarks for the order).
        /// Throws <see cref="KitveiHakodeshServiceUnavailableException"/> with the reason when it
        /// cannot.
        /// </summary>
        public async Task EnsureRunningAsync(CancellationToken cancellationToken, bool allowElevation = true)
        {
            if (IsListening()) return;

            // A start that just failed is not retried by every request queued behind it, each
            // waiting the full startup timeout: they fail fast for a few seconds instead.
            if (DateTime.UtcNow - _lastStartFailureUtc < StartFailureMemory)
                throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                    "The KitveiHakodesh service failed to start a moment ago.");

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            // Machine-wide too: Word and the standalone app must not both prompt or both spawn.
            // Disposing it releases the mutex on the thread that took it (see ThreadOwnedMutex).
            using (var machineGate = new ThreadOwnedMutex(@"Global\KitveiHakodeshService-launch-" + _pipeName, TimeSpan.FromMinutes(3)))
            {
            try
            {
                await machineGate.Acquired.ConfigureAwait(false);

                // Another caller (here or in another process) may have brought it up meanwhile.
                if (IsListening()) return;

                // A private pipe name means a spawner asked for an instance of its own.
                if (_pipeName != KitveiHakodeshServicePipeChannel.DefaultPipeName)
                {
                    await SpawnAsync(FindExecutable(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                string installedExe = InstalledExecutable();
                bool registered = IsRegistered();

                if (registered && await TryStartRegisteredAsync(cancellationToken).ConfigureAwait(false)) return;

                if (installedExe != null)
                {
                    // Registered but will not start (disabled, a broken or moved registration),
                    // or never registered: install / repair it from the installed exe, then start
                    // it. Only when asked (the banner button): a data request or the startup
                    // warm-up must never raise a UAC prompt by itself.
                    if (allowElevation)
                    {
                        try
                        {
                            await RunElevatedInstallAsync(installedExe, cancellationToken).ConfigureAwait(false);
                            if (await TryStartRegisteredAsync(cancellationToken).ConfigureAwait(false)) return;
                        }
                        catch (KitveiHakodeshServiceUnavailableException) { /* declined or failed: fall back below */ }
                    }

                    // No service: a console copy of the installed exe serves this user meanwhile,
                    // so the app keeps working (without the file index, which needs the service's
                    // MFT access). Once the service runs it ends that copy and takes over.
                    await SpawnAsync(installedExe, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (registered)
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                        "The KitveiHakodesh service is registered but will not start.");

                // A dev checkout or a portable copy: no Windows service, a console instance.
                string executable = FindExecutable();
                if (executable == null)
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.NotInstalled,
                        "The KitveiHakodesh service is not installed and " + ExecutableName + " was not found.");
                await SpawnAsync(executable, cancellationToken).ConfigureAwait(false);
            }
            // Every failed start is remembered, so queued data requests fail fast instead of each
            // waiting out the start timeout. The banner's repair button clears it
            // (AllowAnotherRepairAttempt), so it never blocks the repair.
            catch (KitveiHakodeshServiceUnavailableException ex) when (ex.Reason == KitveiHakodeshServiceUnavailableReason.StartFailed)
            {
                _lastStartFailureUtc = DateTime.UtcNow;
                throw;
            }
            finally
            {
                Gate.Release();
            }
            }
        }

        // ── Installed Windows service ─────────────────────────────────────────────

        /// <summary>
        /// The product is installed but its Windows service is not the one serving right now
        /// (never registered, the prompt declined, disabled...): a console copy serves instead,
        /// without the file index. The settings page shows an "install the service" row then.
        /// </summary>
        public static bool CanInstallService() =>
            InstalledExecutable() != null && !IsInstalledServiceRunning();

        /// <summary>
        /// The settings page's "install the service": one UAC prompt, re-register from the
        /// installed exe, start it. The service ends the console copy as it comes up. Throws
        /// with the reason when declined or failed.
        /// </summary>
        public async Task InstallServiceAsync(CancellationToken cancellationToken)
        {
            string installedExe = InstalledExecutable();
            if (installedExe == null)
                throw Unavailable(KitveiHakodeshServiceUnavailableReason.NotInstalled,
                    "The product is not installed on this machine.");

            AllowAnotherRepairAttempt();
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RunElevatedInstallAsync(installedExe, cancellationToken).ConfigureAwait(false);
                // Not TryStartRegisteredAsync: that is satisfied by ANY listener on the pipe, and
                // here the console copy is one. Start the service and wait for the SCM to say so.
                if (!await StartRegisteredAndWaitRunningAsync(cancellationToken).ConfigureAwait(false))
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                        "The KitveiHakodesh service was installed but will not start.");
            }
            finally { Gate.Release(); }
        }

        private static async Task<bool> StartRegisteredAndWaitRunningAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + StartupTimeout;
            try
            {
                using (var controller = new ServiceController(InstalledServiceName))
                {
                    if (controller.Status == ServiceControllerStatus.Stopped) controller.Start();
                    while (DateTime.UtcNow < deadline)
                    {
                        controller.Refresh();
                        if (controller.Status == ServiceControllerStatus.Running) return true;
                        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception) { }
            return false;
        }

        private static bool IsInstalledServiceRunning()
        {
            try
            {
                using (var controller = new ServiceController(InstalledServiceName))
                    return controller.Status == ServiceControllerStatus.Running
                        || controller.Status == ServiceControllerStatus.StartPending;
            }
            catch { return false; } // not registered, or the SCM refused the query
        }

        private static bool IsRegistered()
        {
            try
            {
                using (var controller = new ServiceController(InstalledServiceName))
                {
                    _ = controller.Status; // throws when not registered
                    return true;
                }
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>
        /// Starts the registered service and waits for its pipe. False only when Windows refuses
        /// the start, or it is running and never opens the pipe - the caller decides whether to
        /// repair.
        ///
        /// The status is re-read while waiting, and a stopped service is started again: right
        /// after the service restarts itself (database-path change) Windows can still report it
        /// running while it is on its way down, and a single read would wait out the whole timeout
        /// and then "repair" a perfectly good registration.
        /// </summary>
        private async Task<bool> TryStartRegisteredAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + StartupTimeout;
            try
            {
                using (var controller = new ServiceController(InstalledServiceName))
                {
                    while (DateTime.UtcNow < deadline)
                    {
                        if (IsListening()) return true;

                        controller.Refresh();
                        ServiceControllerStatus status = controller.Status;
                        if (status == ServiceControllerStatus.Stopped)
                            controller.Start();
                        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is System.ServiceProcess.TimeoutException ||
                ex is Win32Exception)
            {
                return false;
            }
            return IsListening();
        }

        /// <summary>
        /// Runs <c>KitveiHakodeshService.exe --install</c> elevated: registers (or re-registers)
        /// the service. One UAC prompt, at most once per process until the user asks to retry.
        /// </summary>
        private static async Task RunElevatedInstallAsync(string installedExe, CancellationToken cancellationToken)
        {
            if (_elevationAttempted)
                throw Unavailable(_lastElevationFailure ?? KitveiHakodeshServiceUnavailableReason.StartFailed,
                    "The KitveiHakodesh service could not be installed or repaired in this session.");
            _elevationAttempted = true;

            var startInfo = new ProcessStartInfo(installedExe, "--install")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(installedExe),
                // .NET Framework passes an owner window to ShellExecuteEx only with ErrorDialog
                // set. Without one Windows opens the prompt in the background, as a flashing
                // taskbar button - users saw "nothing happens" on the repair button.
                ErrorDialog = true,
                ErrorDialogParentHandle = Process.GetCurrentProcess().MainWindowHandle,
            };
            try
            {
                int exitCode = await Task.Run(() =>
                {
                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null) return -1;
                        // Generous: re-registering waits for the old instance to stop (up to 30 s)
                        // and retires DocumentLocatorSvc (up to 30 s more). A timeout is not
                        // treated as failure - the caller still tries to start the service.
                        return process.WaitForExit(120_000) ? process.ExitCode : 0;
                    }
                }, cancellationToken).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    _lastElevationFailure = KitveiHakodeshServiceUnavailableReason.InstallFailed;
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.InstallFailed,
                        "Installing the KitveiHakodesh service failed (exit code " + exitCode + ").");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the user said no
            {
                _lastElevationFailure = KitveiHakodeshServiceUnavailableReason.InstallDeclined;
                throw Unavailable(KitveiHakodeshServiceUnavailableReason.InstallDeclined,
                    "Installing the KitveiHakodesh service was declined.");
            }
            catch (Win32Exception ex)
            {
                _lastElevationFailure = KitveiHakodeshServiceUnavailableReason.InstallFailed;
                throw new KitveiHakodeshServiceUnavailableException(KitveiHakodeshServiceUnavailableReason.InstallFailed,
                    "Could not run the KitveiHakodesh service installer.", ex);
            }
        }

        /// <summary>
        /// The service exe of the INSTALLED product, or null when the product is not installed on
        /// this machine. Looked up at the install root, %LocalAppData%\KleiKodesh (the installer's
        /// AddinInstaller.InstallPath), whatever folder THIS host runs from: a host that lives in
        /// a subfolder, a portable copy or a dev build on a machine with the product installed must
        /// still install / repair that service, never run beside it. Only that exe is ever
        /// registered; a portable or dev copy's own exe never asks for admin rights.
        /// </summary>
        private static string InstalledExecutable()
        {
            string candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KleiKodesh", InstalledServiceFolder, ExecutableName);
            return File.Exists(candidate) ? candidate : null;
        }

        // ── Console instance (dev / portable) ─────────────────────────────────────

        private async Task SpawnAsync(string executable, CancellationToken cancellationToken)
        {
            if (executable == null)
                throw Unavailable(KitveiHakodeshServiceUnavailableReason.NotInstalled,
                    ExecutableName + " was not found. Set " + ExecutableEnvironmentVariable + " or place it beside the application.");

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            };
            startInfo.EnvironmentVariables[KitveiHakodeshServicePipeChannel.PipeNameEnvironmentVariable] = _pipeName;
            startInfo.EnvironmentVariables["KHS_OWNER_PID"] = Process.GetCurrentProcess().Id.ToString();
            try { Process.Start(startInfo)?.Dispose(); }
            catch (Win32Exception ex)
            {
                throw new KitveiHakodeshServiceUnavailableException(
                    KitveiHakodeshServiceUnavailableReason.StartFailed, "Could not start " + executable + ".", ex);
            }

            if (!await WaitForPipeAsync(cancellationToken).ConfigureAwait(false))
                throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                    "The KitveiHakodesh service was started but did not open pipe '" + _pipeName + "' in time.");
        }

        private async Task<bool> WaitForPipeAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + StartupTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) return true;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            return false;
        }

        private static KitveiHakodeshServiceUnavailableException Unavailable(
            KitveiHakodeshServiceUnavailableReason reason, string message) =>
            new KitveiHakodeshServiceUnavailableException(reason, message);

        /// <summary>
        /// Where a console instance's executable is looked for, in order: the environment
        /// variable; beside the application; in a KitveiHakodeshService folder beside it; and,
        /// for a source checkout, the service project's own build output.
        /// </summary>
        public static string FindExecutable()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string candidate in new[]
            {
                Path.Combine(baseDirectory, ExecutableName),
                Path.Combine(baseDirectory, InstalledServiceFolder, ExecutableName),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }

            // Source checkout: <...>\CSharpBackend\<SomeApp>\bin\<cfg>\ -> the service's bin.
            var directory = new DirectoryInfo(baseDirectory);
            for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
            {
                if (!directory.Name.Equals("CSharpBackend", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string configuration in new[] { "Debug", "Release" })
                {
                    string candidate = Path.Combine(directory.FullName, "KitveiHakodeshService", "bin",
                        configuration, "net10.0-windows", ExecutableName);
                    if (File.Exists(candidate)) return candidate;
                }
                break;
            }
            return null;
        }

        /// <summary>
        /// A named Mutex taken and released on its own dedicated thread. A Mutex must be released
        /// by the thread that took it, and the launch gate is held across awaits that resume on
        /// another thread ("Object synchronization method was called from an unsynchronized block
        /// of code"). A Mutex, not a Semaphore, so a holder that crashed is seen at once
        /// (AbandonedMutexException) instead of blocking the next caller until the timeout.
        /// </summary>
        private sealed class ThreadOwnedMutex : IDisposable
        {
            private readonly Mutex _mutex;
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
            private readonly TaskCompletionSource<bool> _acquired =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Thread _owner;

            public ThreadOwnedMutex(string name, TimeSpan timeout)
            {
                _mutex = new Mutex(false, name);
                _owner = new Thread(() =>
                {
                    bool held;
                    try { held = _mutex.WaitOne(timeout); }
                    catch (AbandonedMutexException) { held = true; }
                    catch (Exception ex) { _acquired.TrySetException(ex); return; }
                    _acquired.TrySetResult(held);
                    _release.Wait();
                    if (held) _mutex.ReleaseMutex();
                }) { IsBackground = true, Name = "KHS launch gate" };
                _owner.Start();
            }

            /// <summary>True once held; false when the timeout passed (the caller goes ahead anyway).</summary>
            public Task<bool> Acquired => _acquired.Task;

            public void Dispose()
            {
                _release.Set();
                _owner.Join();
                _mutex.Dispose();
                _release.Dispose();
            }
        }
    }
}
