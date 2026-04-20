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
                    "׳”׳ ׳™׳§׳•׳™ ׳¢׳“׳™׳™׳ ׳₪׳•׳¢׳. ׳”׳׳ ׳׳—׳–׳•׳¨ ׳‘׳›׳ ׳–׳׳×?",
                    "׳׳™׳©׳•׳¨",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
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
            _ = RunCleanupAsync(skipConfirm: false, deepClean: true);
        }

        private async System.Threading.Tasks.Task RunCleanupAsync(bool skipConfirm, bool deepClean = true)
        {
            if (!skipConfirm)
            {
                var confirm = MessageBox.Show(
                    "׳₪׳¢׳•׳׳” ׳–׳• ׳×׳׳—׳§ ׳׳× ׳›׳ ׳§׳‘׳¦׳™ ׳•׳¨׳©׳•׳׳•׳× ׳”׳¨׳’׳™׳¡׳˜׳¨׳™ ׳©׳ ׳›׳׳™ ׳§׳•׳“׳© ׳׳”׳׳—׳©׳‘.\n\n" +
                    "׳׳׳—׳¨ ׳”׳ ׳™׳§׳•׳™ ׳×׳×׳—׳™׳ ׳”׳×׳§׳ ׳” ׳׳—׳“׳© ׳׳•׳˜׳•׳׳˜׳™׳×.\n\n׳׳”׳׳©׳™׳?",
                    "׳׳™׳©׳•׳¨ ׳×׳™׳§׳•׳ ׳׳׳",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

                if (confirm != MessageBoxResult.Yes) return;
            }

            if (!WordHelper.EnsureWordClosedForRepair()) return;

            _isRunning                 = true;
            BasicCleanButton.IsEnabled = false;
            DeepCleanButton.IsEnabled  = false;
            BasicCleanButton.Content   = "׳׳ ׳§׳”...";
            CancelButton.IsEnabled     = false;
            ProgressPanel.Visibility = Visibility.Visible;
            LogPanel.Visibility      = Visibility.Visible;
            SummaryText.Visibility   = Visibility.Collapsed;
            LogBox.Text = "";

            var progress  = new Progress<(int percent, string status)>(r =>
            {
                ProgressBar.Value = r.percent;
                StepText.Text     = r.status;
                AppendLog($"ג”€ג”€ {r.status}");
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
                AppendLog($"\nג ׳©׳’׳™׳׳”: {ex.Message}");
                BasicCleanButton.IsEnabled = true;
                BasicCleanButton.Content   = "׳ ׳™׳§׳•׳™ ׳‘׳¡׳™׳¡׳™";
                DeepCleanButton.IsEnabled  = true;
                CancelButton.IsEnabled     = true;
                return;
            }

            _isRunning = false;
            CancelButton.IsEnabled = true;
            SummaryText.Visibility = Visibility.Visible;

            if (result.TotalDeleted == 0 && result.Errors.Count == 0)
            {
                SummaryText.Text = "ג… ׳׳ ׳ ׳׳¦׳׳• ׳©׳׳¨׳™׳•׳× ג€” ׳”׳׳—׳©׳‘ ׳”׳™׳” ׳ ׳§׳™.";
                AppendLog("\nג… ׳׳ ׳ ׳׳¦׳׳• ׳©׳׳¨׳™׳•׳×.");
            }
            else
            {
                SummaryText.Text = $"ג… ׳”׳ ׳™׳§׳•׳™ ׳”׳•׳©׳׳ ג€” {result.DeletedPaths.Count} ׳×׳™׳§׳™׳•׳×/׳§׳‘׳¦׳™׳, {result.DeletedRegistryKeys.Count} ׳¨׳©׳•׳׳•׳× ׳¨׳’׳™׳¡׳˜׳¨׳™ ׳ ׳׳—׳§׳•.";
                if (result.Errors.Count > 0)
                    AppendLog($"\nג  {result.Errors.Count} ׳©׳’׳™׳׳•׳×.");
            }

            StepText.Text = "׳”׳ ׳™׳§׳•׳™ ׳”׳•׳©׳׳ ג€” ׳׳×׳—׳™׳ ׳”׳×׳§׳ ׳” ׳׳—׳“׳©...";
            ProgressBar.Value = 100;
            AppendLog("\nג–¶ ׳׳×׳—׳™׳ ׳”׳×׳§׳ ׳” ׳׳—׳“׳©...");

            await System.Threading.Tasks.Task.Delay(800);
            _host.NavigateToInstall();
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(line + "\n");
            LogScroller.ScrollToBottom();
        }
    }
}
