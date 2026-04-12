using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class RepairWindow : Window
    {
        private bool _isRunning = false;

        public RepairWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void Window_TouchDown(object sender, TouchEventArgs e)
        {
            this.DragMove();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var confirm = MessageBox.Show(
                    "הניקוי עדיין פועל. האם לסגור בכל זאת?",
                    "אישור",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (confirm != MessageBoxResult.Yes) return;
            }
            Close();
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            // Confirm
            var confirm = MessageBox.Show(
                "פעולה זו תמחק את כל קבצי ורשומות הרגיסטרי של כלי קודש מהמחשב.\n\n" +
                "לאחר הניקוי תתחיל התקנה מחדש אוטומטית.\n\n" +
                "להמשיך?",
                "אישור תיקון מלא",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (confirm != MessageBoxResult.Yes) return;

            // Close Word if open
            var wordProcs = Process.GetProcessesByName("WINWORD");
            if (wordProcs.Length > 0)
            {
                var closeWord = MessageBox.Show(
                    "וורד פתוח כעת. יש לסגור אותו לפני הניקוי.\nהאם לסגור את וורד כעת?",
                    "וורד פתוח",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

                if (closeWord == MessageBoxResult.Yes)
                {
                    foreach (var proc in wordProcs)
                    {
                        try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                }
                else return;
            }

            // ── UI: show progress + log ──
            _isRunning = true;
            RunButton.IsEnabled = false;
            RunButton.Content = "מנקה...";
            ProgressPanel.Visibility = Visibility.Visible;
            LogPanel.Visibility = Visibility.Visible;
            SummaryText.Visibility = Visibility.Collapsed;
            LogBox.Text = "";

            // Progress reports step text + percent
            var progress = new Progress<(int percent, string status)>(report =>
            {
                ProgressBar.Value = report.percent;
                StepText.Text = report.status;
                AppendLog($"── {report.status}");
            });

            // Live detail log — each deleted item is appended in real time
            var detailLog = new Progress<string>(line =>
            {
                AppendLog(line);
            });

            FullSystemCleaner.CleanupResult cleanupResult;
            try
            {
                cleanupResult = await FullSystemCleaner.RunAsync(progress, detailLog);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                AppendLog($"\n❌ שגיאה: {ex.Message}");
                RunButton.IsEnabled = true;
                RunButton.Content = "נסה שוב";
                return;
            }

            _isRunning = false;

            // ── Summary line ──
            SummaryText.Visibility = Visibility.Visible;
            if (cleanupResult.TotalDeleted == 0 && cleanupResult.Errors.Count == 0)
            {
                SummaryText.Text = "✅ לא נמצאו שאריות — המחשב היה נקי.";
                AppendLog("\n✅ לא נמצאו שאריות.");
            }
            else
            {
                SummaryText.Text =
                    $"✅ הניקוי הושלם — " +
                    $"{cleanupResult.DeletedPaths.Count} תיקיות/קבצים, " +
                    $"{cleanupResult.DeletedRegistryKeys.Count} רשומות רגיסטרי נמחקו.";

                if (cleanupResult.Errors.Count > 0)
                    AppendLog($"\n⚠ {cleanupResult.Errors.Count} שגיאות (ייתכן שחלק מהרשומות דורשות הרשאות מנהל).");
            }

            StepText.Text = "הניקוי הושלם — מתחיל התקנה מחדש...";
            ProgressBar.Value = 100;

            AppendLog("\n▶ מתחיל התקנה מחדש...");

            // ── Auto-reinstall ──
            await System.Threading.Tasks.Task.Delay(800); // brief pause so user can read
            Close();
            new InstallProgressWindow(null).Show();
        }

        /// <summary>
        /// Appends a line to the live log textbox and auto-scrolls to the bottom.
        /// </summary>
        private void AppendLog(string line)
        {
            LogBox.AppendText(line + "\n");
            LogScroller.ScrollToBottom();
        }
    }
}
