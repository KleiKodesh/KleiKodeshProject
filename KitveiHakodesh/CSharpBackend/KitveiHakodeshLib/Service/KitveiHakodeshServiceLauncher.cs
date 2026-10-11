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
    ///      to every user). If it will not start (disabled, a broken or moved registration),
    ///      repair it: run the installed exe's <c>--install</c> elevated (one UAC prompt), which
    ///      re-registers it, then start it again.
    ///   3. Not registered, but the product's service exe is installed beside the app
    ///      (<c>KitveiHakodeshService\KitveiHakodeshService.exe</c>): install it the same way.
    ///   4. Neither (a dev checkout, a portable copy): spawn the exe as a console process tied to
    ///      this one (KHS_OWNER_PID), exactly as the dev server does. No prompt ever.
    ///
    /// UAC is asked at most ONCE per process, and never twice at the same time: every caller
    /// goes through one gate, and after a declined or failed attempt the calls fail fast with
    /// the reason until <see cref="AllowAnotherRepairAttempt"/> (the page's "try again" button).
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
            using (var machineGate = new Mutex(false, @"Global\KitveiHakodeshService-launch-" + _pipeName))
            {
            bool machineGateHeld = false;
            try
            {
                try { machineGateHeld = machineGate.WaitOne(TimeSpan.FromMinutes(3)); }
                catch (AbandonedMutexException) { machineGateHeld = true; }

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

                if (registered)
                {
                    if (await TryStartRegisteredAsync(cancellationToken).ConfigureAwait(false)) return;
                    if (installedExe == null || !allowElevation)
                        throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                            "The KitveiHakodesh service is registered but will not start.");

                    // Will not start: re-register it from the installed exe, then try once more.
                    await RunElevatedInstallAsync(installedExe, cancellationToken).ConfigureAwait(false);
                    if (await TryStartRegisteredAsync(cancellationToken).ConfigureAwait(false)) return;
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                        "The KitveiHakodesh service was repaired but still will not start.");
                }

                if (installedExe != null)
                {
                    // The product, but the service was never registered (or was removed).
                    if (!allowElevation)
                        throw Unavailable(KitveiHakodeshServiceUnavailableReason.NotInstalled,
                            "The KitveiHakodesh service is not installed.");
                    await RunElevatedInstallAsync(installedExe, cancellationToken).ConfigureAwait(false);
                    if (await TryStartRegisteredAsync(cancellationToken).ConfigureAwait(false)) return;
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.StartFailed,
                        "The KitveiHakodesh service was installed but will not start.");
                }

                // A dev checkout or a portable copy: no Windows service, a console instance.
                string executable = FindExecutable();
                if (executable == null)
                    throw Unavailable(KitveiHakodeshServiceUnavailableReason.NotInstalled,
                        "The KitveiHakodesh service is not installed and " + ExecutableName + " was not found.");
                await SpawnAsync(executable, cancellationToken).ConfigureAwait(false);
            }
            // Only a real request records it: the no-prompt warm-up failing must not keep the
            // first real request from offering the install / repair.
            catch (KitveiHakodeshServiceUnavailableException ex) when (allowElevation && ex.Reason == KitveiHakodeshServiceUnavailableReason.StartFailed)
            {
                _lastStartFailureUtc = DateTime.UtcNow;
                throw;
            }
            finally
            {
                if (machineGateHeld) machineGate.ReleaseMutex();
                Gate.Release();
            }
            }
        }

        // ── Installed Windows service ─────────────────────────────────────────────

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
        /// The service exe of the INSTALLED product, or null (dev / portable). Every build of this
        /// library carries the service in a KitveiHakodeshService folder, so the exe being present
        /// proves nothing; what makes a copy the installed product is running from the install
        /// root, %LocalAppData%\KleiKodesh (the installer's AddinInstaller.InstallPath). Only that
        /// copy may register a Windows service - a dev build or an unzipped portable copy must
        /// never ask for admin rights.
        /// </summary>
        private static string InstalledExecutable()
        {
            string installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KleiKodesh");
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.Equals(
                    Path.GetFullPath(baseDirectory).TrimEnd('\\'),
                    Path.GetFullPath(installRoot).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                return null;

            string candidate = Path.Combine(baseDirectory, InstalledServiceFolder, ExecutableName);
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
    }
}
