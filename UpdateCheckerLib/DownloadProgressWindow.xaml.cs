using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace UpdateCheckerLib
{
    internal class DownloadProgressWindow : Window
    {
        private readonly TextBlock versionLabel;
        private readonly ProgressBar progressBar;
        private readonly TextBlock statusLabel;

        public bool IsCancelled { get; private set; }
        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public DownloadProgressWindow()
        {
            Title = "כלי קודש - הורדת עדכון";
            Height = 180;
            Width = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            FlowDirection = FlowDirection.RightToLeft;
            WindowStyle = WindowStyle.ToolWindow;

            var grid = new Grid { Margin = new Thickness(20) };
            for (int i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = i % 2 == 0 ? GridLength.Auto : new GridLength(i == 1 || i == 5 ? 20 : 10) });

            versionLabel = new TextBlock { Text = "מוריד עדכון...", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14, FontWeight = FontWeights.Bold };
            Grid.SetRow(versionLabel, 0);
            grid.Children.Add(versionLabel);

            progressBar = new ProgressBar { Height = 25, Minimum = 0, Maximum = 100, Value = 0 };
            Grid.SetRow(progressBar, 2);
            grid.Children.Add(progressBar);

            statusLabel = new TextBlock { Text = "מתחיל הורדה...", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            Grid.SetRow(statusLabel, 4);
            grid.Children.Add(statusLabel);

            var cancelButton = new Button { Content = "ביטול", Width = 80, Height = 30, HorizontalAlignment = HorizontalAlignment.Center };
            cancelButton.Click += (s, e) => { IsCancelled = true; Cancellation.Cancel(); DialogResult = false; Close(); };
            Grid.SetRow(cancelButton, 6);
            grid.Children.Add(cancelButton);

            Content = grid;
        }

        public void SetVersion(string version) =>
            Invoke(() => versionLabel.Text = $"מוריד גרסה {version}...");

        public void SetIndeterminate(string status) =>
            Invoke(() => { progressBar.IsIndeterminate = true; statusLabel.Text = status; });

        public void UpdateProgress(int percentage, string status) =>
            Invoke(() =>
            {
                if (progressBar.IsIndeterminate) progressBar.IsIndeterminate = false;
                progressBar.Value = Math.Min(100, Math.Max(0, percentage));
                statusLabel.Text = status;
            });

        private void Invoke(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!IsCancelled) e.Cancel = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Cancellation?.Dispose();
            base.OnClosed(e);
        }
    }
}
