using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Db;
using KitveiHakodeshLib.Settings;
using KitveiHakodeshLib.UserSettings;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KitveiHakodeshLib
{
    // WebView2 navigation guard and reload logic for AppViewer.
    // Owns: _allowedNavigationPrefixes, OnNavigationStarting, OnNavigationCompleted, HandleReload.
    public partial class AppViewer
    {
        // Allowlist of URL origins the WebView2 may navigate to.
        // Any navigation to a URL that doesn't match one of these prefixes is cancelled.
        //
        // Allowed origins:
        //   http://KitveiHakodesh-vue-app/   — the main Vue app and the HebrewBooks PDF cache
        //                                       (CacheUrl serves from /cache/hebrewbooks/ on this host)
        //   http://kitvei-localfile-          — per-folder virtual hosts registered by LocalFileHandler
        //                                       for local PDF, HTML, and converted Word files
        //   http://kitvei-hb-local-           — per-folder virtual hosts registered by HebrewBooksHandler
        //                                       for PDFs served from a user-configured local folder
        //   https://download.hebrewbooks.org/ — the HebrewBooks download endpoint; the WebView2
        //                                       browser engine must navigate here so the DownloadStarting
        //                                       event fires and we can intercept the file save path.
        //                                       Direct HTTP fetch cannot be used because HebrewBooks
        //                                       blocks non-browser requests.
        private static readonly string[] _allowedNavigationPrefixes = new[]
        {
            "http://KitveiHakodesh-vue-app/",
            "http://kitvei-localfile-",
            "http://kitvei-hb-local-",
            "https://download.hebrewbooks.org/",
        };

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            // Hide the splash regardless of success — a failed navigation still shows the
            // WebView error page, which is more useful than an infinite splash screen.
            _HideSplash();
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string url = e.Uri ?? "";

            if (url.IndexOf("hebrewbooks.org/message.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                e.Cancel = true;
                _hb?.NotifyBookNotFound();
                return;
            }

            foreach (string prefix in _allowedNavigationPrefixes)
            {
                if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            e.Cancel = true;
            System.Diagnostics.Debug.WriteLine("[AppViewer] Blocked navigation to: " + url);
        }

        private async Task HandleReload()
        {
            // Remove the stale db-path injection script and register a fresh one
            // with the current registry values before navigating.
            if (_dbInjectionScriptId != null)
                _webView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_dbInjectionScriptId);

            string savedPath = AppSettings.LoadDbPath();
            bool dbReady = File.Exists(savedPath);
            string escapedPath = savedPath.Replace("\\", "\\\\");
            string hbLocalFolder = AppSettings.LoadHbLocalFolder();
            string escapedHbFolder = hbLocalFolder.Replace("\\", "\\\\");
            string dbScript =
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";" +
                "window.__webviewShowPopOut=" + (ShowPopOutButton ? "true" : "false") + ";" +
                "window.__webviewHbLocalFolder=\"" + escapedHbFolder + "\";" +
                "window.__webviewIsDark=" + (AppSettings.LoadDarkMode() ? "true" : "false") + ";";
            _dbInjectionScriptId = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                JsBridge.Script + "\n" + dbScript);

            // Re-init the DB handler; keep the existing search handler and its index state.
            _db = new DbHandler(_bridge, _webView, savedPath);
            _db.OnDbPathPicked = path =>
            {
                _search.ResetAndReindex(path);
                _userSettings.UpdateSeforimDbPath(path);
            };
            _db.ResetTitleBarToLight = () =>
            {
                if (InvokeRequired)
                    Invoke(new Action(() => ApplyTitleBarTheme(false)));
                else
                    ApplyTitleBarTheme(false);
                ApplySplashTheme(false);
            };

            // Re-init user settings DB for the (possibly changed) seforim DB path.
            _userSettings?.Dispose();
            _userSettings = new UserSettingsDbHandler(_bridge, this, savedPath);

            // Always call OnDbReady — if the file doesn't exist it pushes ftsDbNotFound
            // to the frontend; if it does exist it starts or resumes indexing.
            _search.OnDbReady(savedPath);

            _webView.CoreWebView2.Navigate("http://KitveiHakodesh-vue-app/index.html");
        }
    }
}
