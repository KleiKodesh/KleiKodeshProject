using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Starts the KitveiHakodesh service when no instance is listening on the pipe.
    ///
    /// The service is a hard dependency of every hosted process, and today nothing installs
    /// it as a Windows service: it is a native executable that the first host to need it
    /// spawns, exactly as the dev server does. The spawned instance is tied to this process
    /// (KHS_OWNER_PID) so it exits with it; a second host that is still running simply starts
    /// a fresh one on its next call. Hosts in one session therefore share one instance on the
    /// default pipe name, and a service that restarts itself (after a database-path change) is
    /// brought back by whoever calls next.
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
