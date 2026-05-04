using FtsLibDemo.ViewModels;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;

namespace FtsLibDemo
{
    public partial class MainWindow : Window
    {
        // Temp file reused across searches; deleted when the window closes.
        private string _resultsTempFile;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Closed += OnClosed;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel old)
                old.PropertyChanged -= OnViewModelPropertyChanged;

            if (e.NewValue is MainViewModel vm)
                vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private async void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.ResultsHtml)) return;

            var html = ((MainViewModel)sender).ResultsHtml;

            // Ensure the WebView2 runtime is initialised before navigating.
            await ResultsWebView.EnsureCoreWebView2Async();

            if (string.IsNullOrEmpty(html))
            {
                ResultsWebView.NavigateToString("<html><body></body></html>");
                return;
            }

            // NavigateToString has a ~1.5 MB limit. Write to a temp file and
            // navigate by URI instead — no size restriction.
            if (_resultsTempFile == null)
                _resultsTempFile = Path.Combine(Path.GetTempPath(), $"fts_results_{Guid.NewGuid():N}.html");

            File.WriteAllText(_resultsTempFile, html, Encoding.UTF8);
            ResultsWebView.CoreWebView2.Navigate(new Uri(_resultsTempFile).AbsoluteUri);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try { if (_resultsTempFile != null) File.Delete(_resultsTempFile); }
            catch { /* best-effort cleanup */ }
        }
    }
}
