using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Shared helpers for interacting with the Word process.
    /// </summary>
    public static class WordHelper
    {
        /// <summary>
        /// Blocks up to <paramref name="timeoutMs"/> ms waiting for Word to close.
        /// Returns immediately if Word is not running.
        /// </summary>
        public static void WaitForWordToClose(int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Process.GetProcessesByName("WINWORD").Length == 0) return;
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// If Word is running, asks the user whether to close it.
        /// Returns true if it is safe to proceed (Word not running, or user approved and it was killed).
        /// Returns false if the user declined or Word could not be closed.
        /// </summary>
        public static bool EnsureWordClosed()
        {
            var wordProcs = Process.GetProcessesByName("WINWORD");
            if (wordProcs.Length == 0) return true;

            var r = MessageBox.Show(
                "וורד פתוח כעת.\nהאם לסגור את וורד כדי להמשיך בהתקנה?",
                "וורד פתוח",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r == MessageBoxResult.No)
            {
                MessageBox.Show(
                    "ההתקנה בוטלה. אנא סגור את וורד ונסה שוב.",
                    "התקנה בוטלה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            foreach (var proc in wordProcs)
                try { proc.Kill(); proc.WaitForExit(); } catch { }

            return true;
        }

        /// <summary>
        /// Variant used by RepairPage — asks to close Word before cleanup (not install).
        /// Returns true if safe to proceed.
        /// </summary>
        public static bool EnsureWordClosedForRepair()
        {
            var wordProcs = Process.GetProcessesByName("WINWORD");
            if (wordProcs.Length == 0) return true;

            var r = MessageBox.Show(
                "וורד פתוח כעת. יש לסגור אותו לפני הניקוי.\nהאם לסגור את וורד כעת?",
                "וורד פתוח",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (r != MessageBoxResult.Yes) return false;

            foreach (var proc in wordProcs)
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }

            return true;
        }
    }
}
