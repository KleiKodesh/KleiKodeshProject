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
                    "הניקוי עדיין פועל. האם לחזור בכל זאת?",
                    "אישור",
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
                    "פעולה זו תמחק את כל קבצי ורשומות הרגיסטרי של כלי קודש מהמחשב.\n\n" +
                    "לאחר הניקוי תתחיל התקנה מחדש אוטומטית.\n\nלהמשיך?",
                    "אישור תיקון מלא",
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
            BasicCleanButton.Content   = "מנקה...";
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
                AppendLog($"\n❌ שגיאה: {ex.Message}");
                BasicCleanButton.IsEnabled = true;
                BasicCleanButton.Content   = "ניקוי בסיסי";
                DeepCleanButton.IsEnabled  = true;
                CancelButton.IsEnabled     = true;
                return;
            }

            _isRunning = false;
            CancelButton.IsEnabled = true;
            SummaryText.Visibility = Visibility.Visible;

            if (result.TotalDeleted == 0 && result.Errors.Count == 0)
            {
                SummaryText.Text = "✅ לא נמצאו שאריות — המחשב היה נקי.";
                AppendLog("\n✅ לא נמצאו שאריות.");
            }
            else
            {
                SummaryText.Text = $"✅ הניקוי הושלם — {result.DeletedPaths.Count} תיקיות/קבצים, {result.DeletedRegistryKeys.Count} רשומות רגיסטרי נמחקו.";
                if (result.Errors.Count > 0)
                    AppendLog($"\n⚠ {result.Errors.Count} שגיאות.");
            }

            StepText.Text = "הניקוי הושלם — מתחיל התקנה מחדש...";
            ProgressBar.Value = 100;
            AppendLog("\n▶ מתחיל התקנה מחדש...");

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
