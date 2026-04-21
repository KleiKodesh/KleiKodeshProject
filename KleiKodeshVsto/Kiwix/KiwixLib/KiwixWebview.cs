using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KiwixLib
{
    public class KiwixWebview : UserControl
    {
        private static readonly string AppDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kiwix.js");

        // Shared environment — same UDF means same browser process across instances.
        private static Task<CoreWebView2Environment> _sharedEnvTask;

        private static Task<CoreWebView2Environment> GetSharedEnv()
        {
            if (_sharedEnvTask == null)
                _sharedEnvTask = CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiwix-webcache"));
            return _sharedEnvTask;
        }

        private readonly WebView2 _webView = new WebView2 { Dock = DockStyle.Fill };
        private SplashOverlay _splash;

        public KiwixWebview()
        {
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(_webView);
            _InitSplash();
            _ = InitAsync();
        }

        private void _InitSplash()
        {
            Image logo = null;
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("kiwix-256.png"))
            {
                if (stream != null)
                    logo = Image.FromStream(stream);
            }

            _splash = new SplashOverlay(logo) { Dock = DockStyle.Fill };
            Controls.Add(_splash);
            _splash.BringToFront();
        }

        private void _HideSplash()
        {
            if (_splash == null) return;
            if (InvokeRequired) { Invoke(new Action(_HideSplash)); return; }
            _splash.FadeOut();
            _splash = null;
        }

        private async Task InitAsync()
        {
            var env = await GetSharedEnv();

            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kiwix-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);

            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            _webView.Source = new Uri("https://kiwix-app/index.html");
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            _HideSplash();
        }
    }
}
