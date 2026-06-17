using System;
using System.IO;

namespace KitveiHakodeshLib.Diagnostics
{
    /// <summary>
    /// Simple append-only file logger. Writes to %TEMP%\KitveiHakodesh.log.
    /// Thread-safe via lock. Never throws — logging failures are silently swallowed
    /// so they can never affect the host application.
    /// </summary>
    public static class AppLogger
    {
        private static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "KitveiHakodesh.log");

        private static readonly object _lock = new object();

        public static string LogFilePath => LogPath;

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Log(string category, string message)
            => Log("[" + category + "] " + message);

        public static void LogException(string category, Exception ex)
        {
            Log("[" + category + "] EXCEPTION: " + ex.GetType().Name + " — " + ex.Message);
            if (ex.InnerException != null)
                Log("[" + category + "] INNER: " + ex.InnerException.GetType().Name + " — " + ex.InnerException.Message);
            Log("[" + category + "] STACK: " + ex.StackTrace);
        }

        /// <summary>
        /// Writes a separator + timestamp header. Call once at app startup.
        /// </summary>
        public static void LogStartup(string appName)
        {
            Log("========================================");
            Log("STARTUP  " + appName + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("EXE: " + System.Reflection.Assembly.GetEntryAssembly()?.Location);
            Log("CWD: " + Directory.GetCurrentDirectory());
            Log("OS:  " + Environment.OSVersion);
            Log("CLR: " + Environment.Version);
            Log("========================================");
        }
    }
}
