using System;
using System.IO;
using System.Threading;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Lightweight append-only file logger for FtsLib diagnostics.
    ///
    /// Writes to a fixed path alongside the index directory so logs survive
    /// process kills and are easy to find after a crash.  Thread-safe via lock.
    /// Never throws — logging failures are silently swallowed.
    ///
    /// The log path defaults to %TEMP%\FtsLib.log but can be overridden by
    /// setting <see cref="LogPath"/> before the first call.
    /// </summary>
    public static class FtsLog
    {
        private static readonly object _lock = new object();

        /// <summary>
        /// Full path of the log file.  Set this before calling any other member
        /// if you want the log next to the index directory rather than in %TEMP%.
        /// </summary>
        public static string LogPath { get; set; } =
            Path.Combine(Path.GetTempPath(), "FtsLib.log");

        /// <summary>Write a plain message.</summary>
        public static void Write(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") +
                              "  [T" + Thread.CurrentThread.ManagedThreadId + "]" +
                              "  " + message;
                lock (_lock)
                    File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Write a categorised message: [Category] message.</summary>
        public static void Write(string category, string message)
            => Write("[" + category + "] " + message);

        /// <summary>Write a section separator — useful at startup / key lifecycle events.</summary>
        public static void Separator(string label = null)
        {
            string bar = "═══════════════════════════════════════════════════════════════";
            Write(string.IsNullOrEmpty(label) ? bar : "══ " + label + " " + bar.Substring(label.Length + 4));
        }

        /// <summary>Delete the log file so the next run starts with a clean slate.</summary>
        public static void Clear()
        {
            try { lock (_lock) { if (File.Exists(LogPath)) File.Delete(LogPath); } }
            catch { }
        }
    }
}
