using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WebSitesLib.UI
{
    internal class MyWebView : WebView2
    {
        CoreWebView2Environment _environment;
        TaskCompletionSource<bool> _coreInitializedTcs;
        string _defaultUserAgent;

        const string IosUserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
            "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        static readonly string[] IosExceptions = { "dicta" };

        public async Task Navigate(string url)
        {
            await EnsureCoreAsync();
            CoreWebView2.Navigate(url);
        }

        public async Task EnsureCoreAsync()
        {
            if (_coreInitializedTcs != null && !_coreInitializedTcs.Task.IsCompleted)
            {
                await _coreInitializedTcs.Task;
                return;
            }

            if (_environment == null)
                await SetEnvironment();

            if (CoreWebView2 == null)
            {
                _coreInitializedTcs = new TaskCompletionSource<bool>();

                CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _defaultUserAgent = CoreWebView2.Settings.UserAgent;
                        CoreWebView2.NavigationStarting += OnNavigationStarting;
                        _coreInitializedTcs.TrySetResult(true);
                    }
                    else
                    {
                        _coreInitializedTcs.TrySetException(
                            e.InitializationException ?? new Exception("CoreWebView2 initialization failed."));
                    }
                };

                await EnsureCoreWebView2Async(_environment);
                await _coreInitializedTcs.Task;
            }
        }

        bool IsIosException(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                foreach (var ex in IosExceptions)
                    if (uri.Host.Contains(ex)) return true;
            }
            return false;
        }

        void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            CoreWebView2.Settings.UserAgent = IsIosException(e.Uri)
                ? _defaultUserAgent
                : IosUserAgent;
        }

        async Task SetEnvironment()
        {
            string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webCache");
            _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cacheDir);
        }
    }
}
