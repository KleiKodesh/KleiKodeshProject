using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace UpdateCheckerLib
{
    public class DownloadProgressWindow : Window
    {
        private bool isCancelled = false;
        private TextBlock versionLabel;
        private ProgressBar progressBar;
        private TextBlock statusLabel;
        private Button cancelButton;

        public bool IsCancelled => isCancelled;
        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public DownloadProgressWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Window properties
            Title = "כלי קודש - הורדת עדכון";
            Height = 180;
            Width = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            FlowDirection = FlowDirection.RightToLeft;
            WindowStyle = WindowStyle.ToolWindow;

            // Create main grid
            var grid = new Grid();
            grid.Margin = new Thickness(20);

            // Define rows
            for (int i = 0; i < 7; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = i % 2 == 0 ? GridLength.Auto : new GridLength(i == 1 || i == 5 ? 20 : 10) });
            }

            // Version Label
            versionLabel = new TextBlock
            {
                Text = "מוריד עדכון...",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            Grid.SetRow(versionLabel, 0);
            grid.Children.Add(versionLabel);

            // Progress Bar
            progressBar = new ProgressBar
            {
                Height = 25,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Grid.SetRow(progressBar, 2);
            grid.Children.Add(progressBar);

            // Status Label
            statusLabel = new TextBlock
            {
                Text = "מתחיל הורדה...",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12
            };
            Grid.SetRow(statusLabel, 4);
            grid.Children.Add(statusLabel);

            // Cancel Button
            cancelButton = new Button
            {
                Content = "ביטול",
                Width = 80,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancelButton.Click += CancelButton_Click;
            Grid.SetRow(cancelButton, 6);
            grid.Children.Add(cancelButton);

            Content = grid;
        }

        public void SetVersion(string version)
        {
            if (Dispatcher.CheckAccess())
            {
                versionLabel.Text = $"מוריד גרסה {version}...";
            }
            else
            {
                Dispatcher.Invoke(() => SetVersion(version));
            }
        }

        public void SetIndeterminate(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                progressBar.IsIndeterminate = true;
                statusLabel.Text = status;
            }
            else
            {
                Dispatcher.Invoke(() => SetIndeterminate(status));
            }
        }

        public void UpdateProgress(int percentage, string status)
        {
            if (Dispatcher.CheckAccess())
            {
                // Switch from indeterminate to normal progress
                if (progressBar.IsIndeterminate)
                {
                    progressBar.IsIndeterminate = false;
                }

                progressBar.Value = Math.Min(100, Math.Max(0, percentage));
                statusLabel.Text = status;
            }
            else
            {
                Dispatcher.Invoke(() => UpdateProgress(percentage, status));
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            isCancelled = true;
            Cancellation.Cancel();
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Prevent closing unless cancelled
            if (!isCancelled)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Cancellation?.Dispose();
            base.OnClosed(e);
        }
    }
}