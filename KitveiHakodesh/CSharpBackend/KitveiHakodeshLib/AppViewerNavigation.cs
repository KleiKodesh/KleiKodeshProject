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
        //   http://KitveiHakodesh-vue-app/   — the main Vue app, the HebrewBooks PDF cache
        //                                       (/hebrewbooks-cache/), and converted Word files
        //                                       (/word-cache/) — all served from KitveiHakodesh\
        //   http://kitvei-localfile-          — per-folder virtual hosts registered by LocalFileHandler
        //                                       for local PDF, HTML, and converted Word files
        //   http://kitvei-hb-local-           — per-folder virtual hosts registered by HebrewBooksHandler
        //                                       for PDFs served from a user-configured local folder
        //   https://download.hebrewbooks.org/ — the HebrewBooks download endpoint, for Save As ONLY:
        //                                       navigating here is what raises DownloadStarting, which
        //                                       is where the native Save dialog gets its file path.
        //                                       Opening a book to READ does not come through here — it
        //                                       is fetched over HttpClient in HebrewBooksHandler. The
        //                                       endpoint wants a UA header, not a real browser; it was
        //                                       navigation, not the server, that forced the old design.
        private static readonly string[] _allowedNavigationPrefixes = new[]
        {
            "http://KitveiHakodesh-vue-app/",
            "http://kitvei-localfile-",
            "http://kitvei-hb-local-",
            "https://download.hebrewbooks.org/",
        };

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // A failed navigation off the app shell leaves this WebView2 — the one rendering the
            // whole app — sitting on the WebView error page: no tabs, no close button, no way to
            // open a window, wedged until the process is killed. Book downloads no longer
            // navigate at all, so the only thing that can still take us off the shell is a Save
            // As; this stays as the backstop that puts the app back either way.
            if (!e.IsSuccess && !IsAppShellUrl(_webView.Source))
            {
                Log("Navigation failed (" + e.WebErrorStatus + ") off the app shell — restoring " + AppShellUrl);
                _webView.CoreWebView2.Navigate(AppShellUrl);
                return;
            }

            // First successful load only: the splash and the input plumbing are one-time setup.
            if (!_firstNavigationSettled)
            {
                _firstNavigationSettled = true;
                // Hide the splash regardless of success — a failed navigation still shows the
                // WebView error page, which is more useful than an infinite splash screen.
                _HideSplash();
                // Put OS focus on the web content on first load so trackpad/keyboard gestures
                // (e.g. swipe-to-switch-tab) work immediately without the user clicking the page.
                FocusWebContent();
                // Guarantee horizontal trackpad swipes reach the page even when WinForms (not the
                // web content) holds focus — Windows routes WM_MOUSEHWHEEL to the focused window.
                InstallHorizontalWheelFilter();
            }
        }

        /// <summary>The Vue app's own URL — the one navigation must always be able to fall back to.</summary>
        internal const string AppShellUrl = "http://KitveiHakodesh-vue-app/index.html";

        /// <summary>
        /// True while the WebView2 is showing the Vue app rather than an outside page (the
        /// HebrewBooks download endpoint). A download navigation replaces the app in this same
        /// WebView2, so "are we still on the app?" is what decides whether a failure is fatal.
        /// </summary>
        private static bool IsAppShellUrl(Uri source) =>
            source != null &&
            source.ToString().StartsWith("http://KitveiHakodesh-vue-app/", StringComparison.OrdinalIgnoreCase);

        // Set once the first navigation settles, so the one-time splash/focus/input setup in
        // OnNavigationCompleted does not re-run on later navigations. The handler itself must
        // stay subscribed for the lifetime of the WebView2 — unsubscribing after the first
        // navigation is what left later failures unhandled.
        private bool _firstNavigationSettled;

        private static void Log(string msg) => System.Diagnostics.Debug.WriteLine("[AppViewer] " + msg);

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

        /// <summary>
        /// Clears the WebView2 profile's browsing data (cache, storage) for the live webview.
        ///
        /// This replaces the webcache folder that the app-reset cache wipe (now
        /// FtsIndexState.DeletePdfCachesInBackground) used to try to Directory.Delete.
        /// That could never work — the folder is this webview's own mounted user-data
        /// directory — so the profile API is the only route that actually clears it.
        /// Best-effort: a failure here must not block the reload that follows, because a reset
        /// that leaves a stale HTTP cache behind is still far better than one that never
        /// finishes and strands the user on a dead page.
        /// </summary>
        private async Task ClearWebViewBrowsingDataAsync()
        {
            try
            {
                var profile = _webView?.CoreWebView2?.Profile;
                if (profile != null)
                    await profile.ClearBrowsingDataAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AppViewer] ClearBrowsingData failed: " + ex.Message);
            }
        }

        /// <param name="clearBrowsingData">
        /// True only for the app reset. The reload action is NOT reset-only — seforimDb.onDbReady
        /// sends the same action after a DB-path change and at the end of the setup wizard — and
        /// clearing unconditionally there would wipe open tabs, last-read positions, recents and
        /// HebrewBooks history that the user never asked to lose. The reset path is the one case
        /// where the frontend has already wiped that storage itself, so clearing is a no-op for
        /// data it cares about and only finishes the job on the HTTP cache.
        /// </param>
        private async Task HandleReload(bool clearBrowsingData = false)
        {
            // The webcache can only be cleared through the profile API while the webview is
            // live, and it must happen before the navigate below or the fresh page immediately
            // repopulates it.
            if (clearBrowsingData)
                await ClearWebViewBrowsingDataAsync();

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

            _webView.CoreWebView2.Navigate(AppShellUrl);
        }
    }
}
