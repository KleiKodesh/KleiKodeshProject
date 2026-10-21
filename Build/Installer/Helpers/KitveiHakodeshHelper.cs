using KleiKodeshVstoInstallerWpf.Properties;
using System.Diagnostics;
using System.Threading;
using System.Windows;

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

        /// <summary>
        /// If כתבי הקודש is running, asks the user whether to close it.
        /// Returns true if it is safe to proceed (not running, or user approved and it was killed).
        /// Returns false if the user declined or it could not be closed.
        /// </summary>
        public static bool EnsureKitveiHakodeshClosed()
        {
            var procs = Process.GetProcessesByName(ProcessName);
            if (procs.Length == 0) return true;

            var r = MessageBox.Show(
                Strings.KH_OpenContinue,
                Strings.KH_OpenTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                UiLanguage.MessageBoxOptions);

            if (r == MessageBoxResult.No)
            {
                MessageBox.Show(
                    Strings.KH_Cancelled,
                    Strings.InstallCancelledTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    MessageBoxResult.OK,
                    UiLanguage.MessageBoxOptions);
                return false;
            }

            foreach (var proc in procs)
                try { proc.Kill(); proc.WaitForExit(); } catch { }

            return true;
        }

        /// <summary>
        /// Variant used by RepairPage — asks to close כתבי הקודש before cleanup (not install).
        /// Returns true if safe to proceed.
        /// </summary>
        public static bool EnsureKitveiHakodeshClosedForRepair()
        {
            var procs = Process.GetProcessesByName(ProcessName);
            if (procs.Length == 0) return true;

            var r = MessageBox.Show(
                Strings.KH_OpenClean,
                Strings.KH_OpenTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                UiLanguage.MessageBoxOptions);

            if (r != MessageBoxResult.Yes) return false;

            foreach (var proc in procs)
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }

            return true;
        }
    }
}
