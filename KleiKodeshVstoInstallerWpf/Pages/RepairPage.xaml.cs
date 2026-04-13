using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class RepairPage : Page
    {
        private readonly MainWindow _host;
        private bool _isRunning = false;

        public RepairPage(MainWindow host)
        {
            InitializeComponent();
            _host = host;
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

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            var confirm = MessageBox.Show(
                "פעולה זו תמחק את כל קבצי ורשומות הרגיסטרי של כלי קודש מהמחשב.\n\n" +
                "לאחר הניקוי תתחיל התקנה מחדש אוטומטית.\n\nלהמשיך?",
                "אישור תיקון מלא",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (confirm != MessageBoxResult.Yes) return;

            if (!WordHelper.EnsureWordClosedForRepair()) return;

            _isRunning = true;
            RunButton.IsEnabled    = false;
            RunButton.Content      = "מנקה...";
            CancelButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            LogPanel.Visibility      = Visibility.Visible;
            SummaryText.Visibility   = Visibility.Collapsed;
            LogBox.Text = "";

            var progress  = new Progress<(int percent, string status)>(r => { ProgressBar.Value = r.percent; StepText.Text = r.status; AppendLog($"── {r.status}"); });
            var detailLog = new Progress<string>(line => AppendLog(line));

            FullSystemCleaner.CleanupResult result;
            try
            {
                result = await FullSystemCleaner.RunAsync(progress, detailLog);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                AppendLog($"\n❌ שגיאה: {ex.Message}");
                RunButton.IsEnabled    = true;
                RunButton.Content      = "נסה שוב";
                CancelButton.IsEnabled = true;
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
                    AppendLog($"\n⚠ {result.Errors.Count} שגיאות (ייתכן שחלק מהרשומות דורשות הרשאות מנהל).");
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
