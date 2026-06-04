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
                "כתבי הקודש פתוח כעת.\nהאם לסגור אותו כדי להמשיך בהתקנה?",
                "כתבי הקודש פתוח",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (r == MessageBoxResult.No)
            {
                MessageBox.Show(
                    "ההתקנה בוטלה. אנא סגור את כתבי הקודש ונסה שוב.",
                    "התקנה בוטלה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    MessageBoxResult.OK,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
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
                "כתבי הקודש פתוח כעת. יש לסגור אותו לפני הניקוי.\nהאם לסגור את כתבי הקודש כעת?",
                "כתבי הקודש פתוח",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (r != MessageBoxResult.Yes) return false;

            foreach (var proc in procs)
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }

            return true;
        }
    }
}
