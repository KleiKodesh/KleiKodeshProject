using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Starts the KitveiHakodesh service when no instance is listening on the pipe.
    ///
    /// The service is a hard dependency of every hosted process. The installed product
    /// registers it as a Windows service (KitveiHakodeshSvc, LocalSystem, demand start), and
    /// starting that needs no prompt. Without a registration - a dev checkout, a portable
    /// copy - the executable is spawned instead, exactly as the dev server does; that instance
    /// is tied to this process (KHS_OWNER_PID) so it exits with it, and a second host that is
    /// still running starts a fresh one on its next call. Either way a service that stopped
    /// itself (it restarts after a database-path change) is brought back by whoever calls next.
    ///
    /// A machine-wide mutex serialises the spawn so two hosts starting together do not race
    /// into two instances.
    /// </summary>
    public sealed class KitveiHakodeshServiceLauncher
    {
        /// <summary>Points at the service executable explicitly (installer, dev rig).</summary>
        public const string ExecutableEnvironmentVariable = "KHS_SERVICE_EXE";

        private const string ExecutableName = "KitveiHakodeshService.exe";
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

        private readonly string _pipeName;

        public KitveiHakodeshServiceLauncher(string pipeName = null)
        {
            _pipeName = !string.IsNullOrEmpty(pipeName) ? pipeName : KitveiHakodeshServicePipeChannel.ResolvePipeName();
        }

        /// <summary>True when a service instance is listening on the pipe right now.</summary>
        public bool IsListening()
        {
            try { return File.Exists(@"\\.\pipe\" + _pipeName); }
            catch { return false; }
        }

        /// <summary>
        /// Makes sure a service instance is listening: returns at once when one is, otherwise
        /// spawns the executable and waits for its pipe. Throws
        /// <see cref="KitveiHakodeshServiceUnavailableException"/> when the executable cannot
        /// be found or the pipe never appears.
        /// </summary>
        public async Task EnsureRunningAsync(CancellationToken cancellationToken)
        {
            if (IsListening()) return;

            // The product: the installer registered the exe as a Windows service (LocalSystem,
            // demand start, start granted to every user), and that is what owns the file index.
            // Only the default pipe can be the installed service's; a private pipe name means a
            // spawner asked for an instance of its own.
            if (_pipeName == KitveiHakodeshServicePipeChannel.DefaultPipeName && TryStartInstalledService())
            {
                await WaitForPipeAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            string executable = FindExecutable();
            if (executable == null)
                throw new KitveiHakodeshServiceUnavailableException(
                    "The KitveiHakodesh service is not running and " + ExecutableName + " was not found. " +
                    "Set " + ExecutableEnvironmentVariable + " or place it beside the application.");

            using (var launchGate = new Mutex(false, @"Global\KitveiHakodeshService-launch-" + _pipeName))
            {
                bool held = false;
                try
                {
                    try { held = launchGate.WaitOne(StartupTimeout); }
                    catch (AbandonedMutexException) { held = true; }

                    // Another host may have started it while we waited for the gate.
                    if (IsListening()) return;

                    Spawn(executable);
                    await WaitForPipeAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (held) launchGate.ReleaseMutex();
                }
            }
        }

        /// <summary>The name the installer registers the service under (ServiceRegistration in the service).</summary>
        public const string InstalledServiceName = "KitveiHakodeshSvc";

        /// <summary>
        /// Starts the installed Windows service when there is one. False when it is not
        /// registered on this machine (dev checkout, portable copy) - the caller then spawns
        /// the exe. Starting needs no elevation: the registration grants SERVICE_START to
        /// authenticated users.
        /// </summary>
        private static bool TryStartInstalledService()
        {
            ServiceController controller;
            ServiceControllerStatus status;
            try
            {
                controller = new ServiceController(InstalledServiceName);
                status = controller.Status; // throws when not registered
            }
            catch (InvalidOperationException)
            {
                return false; // not registered: a dev checkout or portable copy - spawn instead
            }

            // Registered: from here on the installed service is THE instance. Never fall back
            // to spawning a console copy on the same pipe name - the two would end up serving
            // one pipe. A start that cannot happen right now is "unavailable", and the caller
            // retries.
            using (controller)
            {
                try
                {
                    if (status == ServiceControllerStatus.Running || status == ServiceControllerStatus.StartPending)
                        return true;
                    if (status == ServiceControllerStatus.StopPending)
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    controller.Start();
                    return true;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is System.ServiceProcess.TimeoutException ||
                    ex is System.ComponentModel.Win32Exception)
                {
                    throw new KitveiHakodeshServiceUnavailableException(
                        "The installed KitveiHakodesh service could not be started.", ex);
                }
            }
        }

        private void Spawn(string executable)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            };
            startInfo.EnvironmentVariables[KitveiHakodeshServicePipeChannel.PipeNameEnvironmentVariable] = _pipeName;
            startInfo.EnvironmentVariables["KHS_OWNER_PID"] = Process.GetCurrentProcess().Id.ToString();
            Process.Start(startInfo);
        }

        private async Task WaitForPipeAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + StartupTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) return;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            throw new KitveiHakodeshServiceUnavailableException(
                "The KitveiHakodesh service was started but did not open pipe '" + _pipeName + "' in time.");
        }

        /// <summary>
        /// Where the executable is looked for, in order: the environment variable; beside the
        /// application; in a KitveiHakodeshService folder beside it; and, for a source checkout,
        /// the service project's own build output (walking up to the CSharpBackend folder).
        /// </summary>
        public static string FindExecutable()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string candidate in new[]
            {
                Path.Combine(baseDirectory, ExecutableName),
                Path.Combine(baseDirectory, "KitveiHakodeshService", ExecutableName),
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
