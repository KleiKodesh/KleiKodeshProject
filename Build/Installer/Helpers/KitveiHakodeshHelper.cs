using KleiKodeshVstoInstallerWpf.Properties;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Helpers for detecting and closing the כתבי הקודש standalone app before install/repair.
    /// </summary>
    public static class KitveiHakodeshHelper
    {
        private const string ProcessName = "כתבי הקודש";

        /// <summary>True while any כתבי הקודש process is running.</summary>
        public static bool IsKitveiHakodeshRunning() =>
            Process.GetProcessesByName(ProcessName).Length > 0;

        /// <summary>
        /// Blocks up to <paramref name="timeoutMs"/> ms waiting for כתבי הקודש to close.
        /// Returns immediately if it is not running.
        /// </summary>
        public static void WaitForKitveiHakodeshToClose(int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Process.GetProcessesByName(ProcessName).Length == 0) return;
                Thread.Sleep(100);
            }
        }

        /// <summary>Exe path of the instance we closed, so it can be relaunched after install.</summary>
        private static string _closedExePath;

        /// <summary>
        /// Closes כתבי הקודש without asking — the app saves everything as it goes, so nothing
        /// is lost. Remembers its exe path so <see cref="RelaunchIfClosed"/> can reopen it.
        /// Always returns true.
        /// </summary>
        public static bool EnsureKitveiHakodeshClosed()
        {
            foreach (var proc in Process.GetProcessesByName(ProcessName))
            {
                try { _closedExePath = _closedExePath ?? proc.MainModule.FileName; } catch { }
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }
            return true;
        }

        /// <summary>Reopens כתבי הקודש if <see cref="EnsureKitveiHakodeshClosed"/> closed it.</summary>
        public static void RelaunchIfClosed()
        {
            var path = _closedExePath;
            _closedExePath = null;
            if (path == null || !File.Exists(path) || IsKitveiHakodeshRunning()) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }
}
