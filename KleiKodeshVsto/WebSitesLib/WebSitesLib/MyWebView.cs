using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WebSitesLib
{
    internal class MyWebView : WebView2
    {
        private CoreWebView2Environment Environment;
        private TaskCompletionSource<bool> _coreInitializedTcs;
        private string _defaultUserAgent;  // captured at init time

        private const string IosUserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
            "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        private static readonly string[] IosExceptions = new[]
        {
            "dicta"
        };

        private static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"[MyWebView][{timestamp}] {message}");
        }

        private string GetCurrentUserAgent()
        {
            var ua = CoreWebView2.Settings.UserAgent;
            return string.IsNullOrEmpty(ua) ? "<empty>" : ua;
        }

        private void LogUserAgent(string context)
        {
            //Log($"{context} => Effective UserAgent: '{GetCurrentUserAgent()}'");
        }

        public async Task Navigate(string url)
        {
            //Log($"Navigate called with URL: '{url}'");
            await EnsureCoreAsync();
            //Log($"CoreWebView2 ready. Calling Navigate('{url}')");
            CoreWebView2.Navigate(url);
            //Log($"Navigate dispatched for URL: '{url}'");
        }

        private bool IsIosException(string url)
        {
            //Log($"IsIosException: checking URL '{url}'");
            if (string.IsNullOrWhiteSpace(url))
            {
                //Log("IsIosException: URL is null or whitespace, returning false");
                return false;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                //Log($"IsIosException: parsed host = '{uri.Host}'");
                foreach (var exception in IosExceptions)
                {
                    //Log($"IsIosException: checking if host '{uri.Host}' contains exception '{exception}'");
                    if (uri.Host.Contains(exception))
                    {
                        //Log($"IsIosException: MATCH found for '{exception}', returning true");
                        return true;
                    }
                }
                //Log("IsIosException: no exceptions matched, returning false");
            }
            else
            {
                //Log($"IsIosException: failed to parse '{url}' as absolute URI, returning false");
            }

            return false;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            //Log($"OnNavigationStarting fired. URI: '{e.Uri}', IsUserInitiated: {e.IsUserInitiated}, IsRedirected: {e.IsRedirected}");
            //LogUserAgent("BEFORE change");

            if (IsIosException(e.Uri))
            {
                //Log($"OnNavigationStarting: exception site — restoring default UserAgent: '{_defaultUserAgent}'");
                CoreWebView2.Settings.UserAgent = _defaultUserAgent;
            }
            else
            {
                //Log("OnNavigationStarting: normal site — applying iOS UserAgent.");
                CoreWebView2.Settings.UserAgent = IosUserAgent;
            }

            //LogUserAgent("AFTER change");
        }

        public async Task SetIosMode(bool enable)
        {
            //Log($"SetIosMode called with enable={enable}");
            await EnsureCoreAsync();
            //LogUserAgent("BEFORE SetIosMode");
            CoreWebView2.Settings.UserAgent = enable ? IosUserAgent : _defaultUserAgent;
            //LogUserAgent("AFTER SetIosMode");
        }

        public async Task EnsureCoreAsync()
        {
            //Log("EnsureCoreAsync called.");

            if (_coreInitializedTcs != null && !_coreInitializedTcs.Task.IsCompleted)
            {
                //Log("EnsureCoreAsync: initialization in progress, awaiting existing TCS...");
                await _coreInitializedTcs.Task;
                //Log("EnsureCoreAsync: existing TCS completed.");
            }

            if (Environment == null)
            {
                //Log("EnsureCoreAsync: Environment is null, calling SetEnvironment()...");
                await SetEnvironment();
                //Log("EnsureCoreAsync: SetEnvironment() completed.");
            }
            else
            {
                //Log("EnsureCoreAsync: Environment already initialized, skipping.");
            }

            if (CoreWebView2 == null)
            {
                //Log("EnsureCoreAsync: CoreWebView2 is null, starting initialization...");
                _coreInitializedTcs = new TaskCompletionSource<bool>();

                this.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    //Log($"CoreWebView2InitializationCompleted fired. IsSuccess={e.IsSuccess}");
                    if (e.IsSuccess)
                    {
                        // Capture the real default UA before we ever change it
                        _defaultUserAgent = CoreWebView2.Settings.UserAgent;
                        //Log($"CoreWebView2InitializationCompleted: captured default UserAgent: '{_defaultUserAgent}'");

                        //Log("CoreWebView2InitializationCompleted: subscribing to NavigationStarting.");
                        CoreWebView2.NavigationStarting += OnNavigationStarting;
                        _coreInitializedTcs.TrySetResult(true);
                        //Log("CoreWebView2InitializationCompleted: TCS result set to true.");
                    }
                    else
                    {
                        //Log($"CoreWebView2InitializationCompleted: FAILED. Exception: {e.InitializationException?.Message ?? "null"}");
                        _coreInitializedTcs.TrySetException(
                            e.InitializationException ?? new Exception("CoreWebView2 initialization failed."));
                    }
                };

                //Log("EnsureCoreAsync: calling EnsureCoreWebView2Async...");
                await EnsureCoreWebView2Async(Environment);
                //Log("EnsureCoreAsync: EnsureCoreWebView2Async returned, awaiting TCS...");
                await _coreInitializedTcs.Task;
                //Log("EnsureCoreAsync: CoreWebView2 fully initialized.");
            }
            else
            {
                //Log("EnsureCoreAsync: CoreWebView2 already initialized, skipping.");
            }

            //Log("EnsureCoreAsync completed.");
        }

        private async Task SetEnvironment()
        {
            string tempWebCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webCache");
            //Log($"SetEnvironment: cache directory = '{tempWebCacheDir}' (exists={Directory.Exists(tempWebCacheDir)})");
            //Log("SetEnvironment: calling CoreWebView2Environment.CreateAsync...");
            Environment = await CoreWebView2Environment.CreateAsync(userDataFolder: tempWebCacheDir);
            //Log($"SetEnvironment: environment created. BrowserVersionString='{Environment.BrowserVersionString}'");
        }
    }
}