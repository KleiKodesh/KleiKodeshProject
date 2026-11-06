using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Brings the KitveiHakodesh service up when no instance is listening on the pipe - and,
    /// for the installed product, installs or repairs it when that is what stands in the way.
    ///
    /// The service is a hard dependency of every hosted process. The installed version always
    /// wins: no console copy may serve while the product's service is registered. In order:
    ///   1. Registered with Windows (KitveiHakodeshSvc) and not running: start it (no prompt -
    ///      starting is granted to every user), even when a console copy is listening - the
    ///      service ends that copy as it comes up. Running or coming up: wait for its pipe.
    ///   2. Not registered, listening already (a console copy, dev): nothing to do.
    ///   3. Not started, and the product's service exe is installed
    ///      (%LocalAppData%\KleiKodesh\KitveiHakodeshService): install / repair it - its
    ///      <c>--install</c> run elevated (one UAC prompt) re-registers it, enabled - then start
    ///      it. Not asked, declined or failed: run that exe as a console copy for this user
    ///      meanwhile; the service ends that copy once it starts.
    ///   4. Registered but nothing installed to repair it from: fail with the reason.
    ///   5. Neither (a dev checkout, a portable copy): spawn the exe as a console process tied to
    ///      this one (KHS_OWNER_PID), exactly as the dev server does. No prompt ever.
    ///
    /// The UAC prompt is raised unasked ONCE per user: the window's warm-up on the first launch
    /// that finds the product installed but unregistered (AppSettingsRegistry
    /// ServiceInstallPromptShown). Data requests only start or spawn (allowElevation false).
    /// After that the user asks: the settings page's "install the service" row
    /// (<see cref="InstallServiceAsync"/>) or the banner's "try again" (host action retryService
    /// -> <see cref="AllowAnotherRepairAttempt"/>). Never two prompts at once: one gate in this
    /// process, a machine-wide mutex across processes.
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
        // The user pressed the banner's "try again": the next elevation ignores the one-prompt flag.
        private static bool _userAskedForPrompt;

        private readonly string _pipeName;

        public KitveiHakodeshServiceLauncher(string pipeName = null)
        {
            _pipeName = !string.IsNullOrEmpty(pipeName) ? pipeName : KitveiHakodeshServicePipeChannel.ResolvePipeName();
        }

        /// <summary>Forgets that a start failed a moment ago, so the next call really tries
        /// again (the dialog's own retry after a restart) - without allowing an elevation prompt.</summary>
        public static void ForgetRecentStartFailure() => _lastStartFailureUtc = DateTime.MinValue;

        /// <summary>Lets the next failure ask for elevation again (the user pressed "try again").</summary>
        public static void AllowAnotherRepairAttempt()
        {
            _lastStartFailureUtc = DateTime.MinValue;
            _elevationAttempted = false;
            _lastElevationFailure = null;
            _userAskedForPrompt = true;
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
            // Listening is enough only when the listener can be the installed service - or there
            // is none to prefer. A console copy serving beside a stopped registration is replaced.
            bool registered = false;
            if (IsListening())
            {
                registered = _pipeName == KitveiHakodeshServicePipeChannel.DefaultPipeName && IsRegistered();
                if (!registered || IsInstalledServiceRunning()) return;
            }

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

                // A private pipe name means a spawner asked for an instance of its own.
                if (_pipeName != KitveiHakodeshServicePipeChannel.DefaultPipeName)
                {
                    if (IsListening()) return;
                    await SpawnAsync(FindExecutable(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                registered = IsRegistered();
                if (registered)
                {
                    // The installed version first, whatever is listening: SCM status, not the
                    // pipe, says it is up (a console copy's pipe would satisfy a pipe check), then
                    // a pipe. While it ends the copy it replaces (up to ~17 s) that may still be the
                    // copy's pipe: a request caught mid-swap fails once and the page retries it.
                    if (await StartRegisteredAndWaitRunningAsync(cancellationToken).ConfigureAwait(false)
                        && await WaitForPipeAsync(cancellationToken).ConfigureAwait(false)) return;
                    // Still coming up (a long shutdown of the copy it replaces): never "repair" a
                    // service that is on its way - that would stop, delete and recreate it.
                    if (IsInstalledServiceRunning())
                    {
                        if (await WaitForPipeAsync(cancellationToken).ConfigureAwait(false)) return;
                        throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                            "The KitveiHakodesh service is running but did not open its pipe in time.");
                    }
                }
                // Another caller (here or in another process) may have brought a copy up meanwhile.
                else if (IsListening()) return;

                string installedExe = InstalledExecutable();
                if (installedExe != null)
                {
                    // Registered but will not start (disabled, a broken or moved registration),
                    // or never registered: install / repair it from the installed exe, then start
                    // it. Unasked only once per user (the first launch); afterwards only when the
                    // user asks - the banner button or the settings row. A data request never.
                    if (allowElevation && (_userAskedForPrompt || !AppSettingsRegistry.GetServiceInstallPromptShown()))
                    {
                        // Marked BEFORE the prompt: a crash or kill mid-prompt must not ask forever.
                        AppSettingsRegistry.SetServiceInstallPromptShown();
                        try
                        {
                            await RunElevatedInstallAsync(installedExe, cancellationToken).ConfigureAwait(false);
                            if (await StartRegisteredAndWaitRunningAsync(cancellationToken).ConfigureAwait(false)
                                && await WaitForPipeAsync(cancellationToken).ConfigureAwait(false)) return;
                        }
                        catch (KitveiHakodeshServiceUnavailableException) { /* declined or failed: fall back below */ }
                    }

                    // No service: a console copy of the installed exe serves this user meanwhile,
                    // so the app keeps working (without the file index, which needs the service's
                    // MFT access). Once the service runs it ends that copy and takes over.
                    if (IsListening()) return;
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
                if (IsListening()) return;
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
                // The banner's "try again" grants one prompt; it must not outlive this attempt
                // (a later unasked warm-up in this process would otherwise prompt again).
                _userAskedForPrompt = false;
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
                // SCM status, not the pipe: a pipe check is satisfied by ANY listener, and here
                // the console copy is one. Start the service and wait for the SCM to say so.
                if (!await StartRegisteredAndWaitRunningAsync(cancellationToken).ConfigureAwait(false))
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                        "The KitveiHakodesh service was installed but will not start.");
            }
            finally { Gate.Release(); }
        }

        /// <summary>
        /// Starts the registered service and waits for the SCM to report it Running. The status
        /// is re-read while waiting, and a service that becomes Stopped meanwhile is started:
        /// right after it stops itself (last owner gone, database-path change) Windows reports
        /// StopPending for a moment, and a single read would wait out the whole timeout and
        /// then "repair" a perfectly good registration. False only when Windows refuses the
        /// start, or it never reaches Running in time.
        /// </summary>
        private static async Task<bool> StartRegisteredAndWaitRunningAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + StartupTimeout;
            try
            {
                using (var controller = new ServiceController(InstalledServiceName))
                {
                    while (DateTime.UtcNow < deadline)
                    {
                        controller.Refresh();
                        ServiceControllerStatus status = controller.Status;
                        if (status == ServiceControllerStatus.Running) return true;
                        if (status == ServiceControllerStatus.Stopped)
                        {
                            try { controller.Start(); }
                            catch (Exception ex) when (IsTransientStartFailure(ex))
                            {
                                // "An instance of the service is already running" / "cannot accept
                                // control messages at this time": the service is on its way up or
                                // down. Not a refusal - look again instead of giving up at once.
                                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception) { }
            return false;
        }

        // ERROR_SERVICE_ALREADY_RUNNING, ERROR_SERVICE_CANNOT_ACCEPT_CTRL, ERROR_SERVICE_DATABASE_LOCKED,
        // ERROR_SERVICE_NOT_ACTIVE, ERROR_SERVICE_REQUEST_TIMEOUT.
        private static bool IsTransientStartFailure(Exception ex)
        {
            Win32Exception win32 = ex as Win32Exception ?? ex.InnerException as Win32Exception;
            if (win32 == null) return false;
            int code = win32.NativeErrorCode;
            return code == 1056 || code == 1061 || code == 1055 || code == 1062 || code == 1053;
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
        /// Runs <c>KitveiHakodeshService.exe --install</c> elevated: registers (or re-registers)
        /// the service. One UAC prompt, at most once per process until the user asks to retry.
        /// </summary>
        private static async Task RunElevatedInstallAsync(string installedExe, CancellationToken cancellationToken)
        {
            if (_elevationAttempted)
                throw Unavailable(_lastElevationFailure ?? KitveiHakodeshServiceUnavailableReason.StartFailed,
                    "The KitveiHakodesh service could not be installed or repaired in this session.");
            _elevationAttempted = true;
            _userAskedForPrompt = false;

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
