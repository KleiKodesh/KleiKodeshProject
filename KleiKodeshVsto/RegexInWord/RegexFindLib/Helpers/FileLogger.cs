using System;
using System.IO;

namespace RegexFindLib.Helpers
{
    /// <summary>
    /// Minimal file logger for diagnosing issues in production (no debugger attached).
    /// Writes to %TEMP%\RegexFindLib.log — open it in any text editor after reproducing the bug.
    /// Call FileLogger.Enabled = false to turn off without recompiling.
    /// </summary>
    public static class FileLogger
    {
        static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "RegexFindLib.log");

        public static bool Enabled { get; set; } = true;

        public static void Log(string message)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { /* never crash the host */ }
        }

        /// <summary>Clears the log file. Call at the start of a search run to keep it readable.</summary>
        public static void Clear()
        {
            try { File.WriteAllText(LogPath, ""); } catch { }
        }

        public static string LogFilePath => LogPath;
    }
}
