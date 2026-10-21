using KleiKodeshVstoInstallerWpf.Properties;
using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class RepairPage : Page
    {
        private readonly MainWindow _host;
        private readonly bool _autoRun;
        private bool _isRunning = false;

        public RepairPage(MainWindow host, bool autoRun = false)
        {
            InitializeComponent();
            _host    = host;
            _autoRun = autoRun;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // If relaunched as admin with --repair, skip confirmation and run deep clean
            if (_autoRun)
            {
                BasicCleanButton.IsEnabled = false;
                DeepCleanButton.IsEnabled  = false;
                _ = RunCleanupAsync(skipConfirm: true, deepClean: true);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var confirm = MessageBox.Show(
                    Strings.Repair_StillRunning,
                    Strings.Confirm,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No,
                    UiLanguage.MessageBoxOptions);
                if (confirm != MessageBoxResult.Yes) return;
            }
            _host.NavigateToLanding();
        }

        private void BasicCleanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _ = RunCleanupAsync(skipConfirm: false, deepClean: false);
        }

        private void DeepCleanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            // Only elevate when HKLM actually holds something to delete.
            //
            // deepClean gates HKLM cleanup and nothing else, and those keys only exist on
            // machines that once ran an HKLM-installing version. Elevating regardless used
            // to cost a pointless UAC prompt; since the payload became a sibling file of
            // the exe it costs more than that, because the elevated instance resolves
            // %LOCALAPPDATA% and HKCU against the elevating account — so the reinstall
            // started at the end of RunCleanupAsync would install into the wrong profile
            // whenever the user elevates with different credentials.
            //
            // Reading HKLM needs no elevation, so the common case (nothing there) now runs
            // entirely unelevated as the real user, and the deep clean still does every
            // HKCU/file-system step it did before.
            if (!AdminHelper.IsElevated && FullSystemCleaner.HasHklmLeftovers())
            {
                // Relaunch as admin for deep clean
                AdminHelper.RelaunchAsAdmin("--repair");
                return;
            }

            _ = RunCleanupAsync(skipConfirm: false, deepClean: true);
        }

        private async System.Threading.Tasks.Task RunCleanupAsync(bool skipConfirm, bool deepClean = true)
        {
            if (!skipConfirm)
            {
                var confirm = MessageBox.Show(
                    Strings.Repair_ConfirmText1 +
                    Strings.Repair_ConfirmText2,
                    Strings.Repair_ConfirmTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No,
                    UiLanguage.MessageBoxOptions);

                if (confirm != MessageBoxResult.Yes) return;
            }

            if (!WordHelper.EnsureWordClosedForRepair()) return;
            if (!KitveiHakodeshHelper.EnsureKitveiHakodeshClosedForRepair()) return;

            _isRunning                 = true;
            BasicCleanButton.IsEnabled = false;
            DeepCleanButton.IsEnabled  = false;
            BasicCleanButton.Content   = Strings.Repair_Cleaning;
            CancelButton.IsEnabled     = false;
            ProgressPanel.Visibility = Visibility.Visible;
            LogPanel.Visibility      = Visibility.Visible;
            SummaryText.Visibility   = Visibility.Collapsed;
            LogBox.Text = "";

            var progress  = new Progress<(int percent, string status)>(r =>
            {
                ProgressBar.Value = r.percent;
                StepText.Text     = r.status;
                AppendLog($"── {r.status}");
            });
            var detailLog = new Progress<string>(line => AppendLog(line));

            FullSystemCleaner.CleanupResult result;
            try
            {
                result = await FullSystemCleaner.RunAsync(progress, detailLog, deepClean);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                AppendLog(string.Format(Strings.Repair_ErrorLog, ex.Message));
                BasicCleanButton.IsEnabled = true;
                BasicCleanButton.Content   = Strings.Repair_BasicClean;
                DeepCleanButton.IsEnabled  = true;
                CancelButton.IsEnabled     = true;
                return;
            }

            _isRunning = false;
            CancelButton.IsEnabled = true;
            SummaryText.Visibility = Visibility.Visible;

            if (result.TotalDeleted == 0 && result.Errors.Count == 0)
            {
                SummaryText.Text = Strings.Repair_NothingFound;
                AppendLog(Strings.Repair_NothingFoundLog);
            }
            else
            {
                SummaryText.Text = string.Format(Strings.Repair_Summary, result.DeletedPaths.Count, result.DeletedRegistryKeys.Count);
                if (result.Errors.Count > 0)
                    AppendLog(string.Format(Strings.Repair_ErrorCount, result.Errors.Count));
            }

            StepText.Text = Strings.Repair_DoneReinstalling;
            ProgressBar.Value = 100;
            AppendLog(Strings.Repair_ReinstallLog);

            // A repair re-registers the service even when its registration looks current: a
            // broken one (disabled, wrong permissions) is part of what a repair is for.
            KitveiHakodeshServiceHelper.ForceRegistration = true;
            await System.Threading.Tasks.Task.Delay(800);
            _host.NavigateToInstall(true);
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(line + "\n");
            LogScroller.ScrollToBottom();
        }
    }
}
