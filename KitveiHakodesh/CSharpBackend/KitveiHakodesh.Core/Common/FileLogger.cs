using System;
using System.IO;
using System.Reflection;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Append-only text log, for the times something has to be written down before the app is
    /// in a state to report it — early startup, add-in load, a host that has no logger yet.
    ///
    /// A DOCUMENTED EXCEPTION to the rule that Core never swallows an error. Everything here
    /// catches and discards, because a logger that throws takes down the thing it was meant to
    /// explain: a full disk or a locked file must not turn a working add-in into a crashing
    /// one. Nothing else in Core may reason this way — this is the only place where losing
    /// information is the lesser harm.
    ///
    /// This is NOT the general way Core reports anything. Core returns data or throws, and the
    /// orchestrator decides what the user sees.
    /// </summary>
    public sealed class FileLogger
    {
        private readonly string _logPath;
        private readonly object _gate = new object();

        /// <param name="logPath">Where to append. Its folder is created on first write.</param>
        public FileLogger(string logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath))
                throw new ArgumentException("logPath is required", nameof(logPath));

            _logPath = logPath;
        }

        /// <summary>A log named after the app, in the temp folder. Temp because this exists for
        /// problems that happen before a real location is known to be writable.</summary>
        public static FileLogger InTempFolder(string fileName) =>
            new FileLogger(Path.Combine(Path.GetTempPath(), fileName));

        public string LogPath => _logPath;

        public void Log(string message)
        {
            try
            {
                lock (_gate)
                {
                    string? folder = Path.GetDirectoryName(_logPath);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        Directory.CreateDirectory(folder!);

                    File.AppendAllText(
                        _logPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // See the class remarks: a logger that throws hides the thing it was logging.
            }
        }

        public void Log(string category, string message) => Log("[" + category + "] " + message);

        /// <summary>
        /// An exception across several lines: type and message, then the inner one, then the
        /// stack. Separate lines rather than one, because the interesting part is usually the
        /// inner exception and a single long line buries it.
        /// </summary>
        public void LogException(string category, Exception exception)
        {
            if (exception == null) return;

            Log(category, "EXCEPTION: " + exception.GetType().Name + " — " + exception.Message);

            if (exception.InnerException != null)
                Log(category, "INNER: " + exception.InnerException.GetType().Name
                            + " — " + exception.InnerException.Message);

            Log(category, "STACK: " + exception.StackTrace);
        }

        /// <summary>
        /// A separator and the facts that identify this run. Worth writing once at startup:
        /// almost every "it works on my machine" question is answered by the executable path,
        /// the runtime version, or the bitness, and none of them can be recovered from a later
        /// line in the log.
        /// </summary>
        public void LogStartup(string appName)
        {
            Log("========================================");
            Log("STARTUP  " + appName + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("EXE: " + (Assembly.GetEntryAssembly()?.Location ?? "<unknown>"));
            Log("CWD: " + Directory.GetCurrentDirectory());
            Log("OS:  " + Environment.OSVersion);
            Log("CLR: " + Environment.Version);
            Log("BIT: " + (IntPtr.Size * 8) + "-bit process");
            Log("========================================");
        }
    }
}
