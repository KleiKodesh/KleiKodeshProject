using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;

namespace WebViewLib
{
    public class WebViewHost : ContentControl, INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public static readonly DependencyProperty SourceProperty =
              DependencyProperty.Register(
               "Source",
               typeof(string),
               typeof(WebViewHost),
               new PropertyMetadata(null, OnSourceChanged));

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebViewHost host && e.NewValue is string url)
                host.Navigate(url);
        }

        public WebView2 WebView { get; private set; }
        protected CoreWebView2Environment _environment;
        bool _isIPhoneMode = true;

        public WebViewHost()
        {
            WebView = new WebView2 { AllowExternalDrop = false };
            WebView.NavigationStarting += WebView_NavigationStarting;
            WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            var host = new WindowsFormsHost { Child = WebView };
            this.Content = host;
            SetCore();
        }

        public WebViewHost(string uri)
        {
            WebView = new WebView2 { AllowExternalDrop = false };
            WebView.NavigationStarting += WebView_NavigationStarting;
            WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            var host = new WindowsFormsHost { Child = WebView };
            this.Content = host;
            SetCore();
            Navigate(uri);
        }

        public WebViewHost(bool isIPhoneMode = true)
        {
            _isIPhoneMode = isIPhoneMode;

            WebView = new WebView2 { AllowExternalDrop = false };
            WebView.NavigationStarting += WebView_NavigationStarting;
            WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            var host = new WindowsFormsHost { Child = WebView };
            this.Content = host;
            SetCore(isIPhoneMode);
        }

        private void WebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        }

        private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Uri uri = new Uri(e.Uri);
            if (uri.Scheme == Uri.UriSchemeFile)
            {
                string filePath = uri.LocalPath;
                if (!File.Exists(filePath))
                    e.Cancel = true;
            }

            if (_isIPhoneMode)
            {
                if (e.Uri.Contains("dicta.org") == true)
                    WebView.CoreWebView2.Settings.UserAgent = null;
                else
                    WebView.CoreWebView2.Settings.UserAgent =
                //"Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
                "Mozilla/5.0 (Linux; Android 12; Pixel 6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.100 Mobile Safari/537.36";
            }
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            WebView.CoreWebView2.Navigate(e.Uri);
            e.Handled = true;
        }

        async void SetCore(bool iPhoneMode = true)
        {
            string tempWebCacheDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: tempWebCacheDir);
        }

        public async Task EnsurCoreAsync()
        {
            if (WebView.CoreWebView2 == null)
                await WebView.EnsureCoreWebView2Async(_environment);
        }
        
        public async void Navigate(string url)
        {
            try
            {
                await EnsurCoreAsync();
                WebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex){ MessageBox.Show(ex.Message, "WebViewHostError"); }
        }

        public async void DocumentWrite(string html)
        {
            await EnsurCoreAsync();
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"Otzarnik_Temp_File{Guid.NewGuid()}.html");
            File.WriteAllText(tempFilePath, html);
            WebView.CoreWebView2.DOMContentLoaded +=  (s, _) => File.Delete(tempFilePath);
            Navigate(tempFilePath);
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            await EnsurCoreAsync();
            return await WebView.ExecuteScriptAsync(script) ;
        }

        public async Task Sleep() =>
             await WebView?.CoreWebView2.TrySuspendAsync();

        public void Dispose()
        {
            WebView.Dispose();
        }
    }
}
