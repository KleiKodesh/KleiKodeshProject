using KleiKodeshVstoInstallerWpf.Properties;
using System;
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
        /// <summary>True while any WINWORD process is running.</summary>
        public static bool IsWordRunning() =>
            Process.GetProcessesByName("WINWORD").Length > 0;

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
                Strings.Word_OpenContinue,
                Strings.Word_OpenTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r == MessageBoxResult.No)
            {
                MessageBox.Show(
                    Strings.Word_Cancelled,
                    Strings.InstallCancelledTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            foreach (var proc in wordProcs)
                try { proc.Kill(); proc.WaitForExit(); } catch { }

            return true;
        }

        /// <summary>
        /// Exits the installer after a successful install. On a clean install (not an
        /// update) the add-in only finishes setting up the first time Word loads it, so
        /// offer to open Word first.
        /// </summary>
        public static void ExitAfterInstall()
        {
            if (InstallRunner.IsCleanInstall && !IsWordRunning())
            {
                var r = MessageBox.Show(
                    Strings.Word_OpenToFinish,
                    Strings.Word_OpenToFinishTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes,
                    UiLanguage.MessageBoxOptions);

                if (r == MessageBoxResult.Yes)
                    try { Process.Start(new ProcessStartInfo("winword.exe") { UseShellExecute = true }); } catch { }
            }
            Environment.Exit(0);
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
                Strings.Word_OpenClean,
                Strings.Word_OpenTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                UiLanguage.MessageBoxOptions);

            if (r != MessageBoxResult.Yes) return false;

            foreach (var proc in wordProcs)
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }

            return true;
        }
    }
}
