using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Db;
using KitveiHakodeshLib.Diagnostics;
using KitveiHakodeshLib.Dictionary;
using KitveiHakodeshLib.FileSystemSearch;
using KitveiHakodeshLib.Helpers;
using KitveiHakodeshLib.HebrewBooks;
using KitveiHakodeshLib.LocalFile;
using KitveiHakodeshLib.Search;
using KitveiHakodeshLib.Settings;
using KitveiHakodeshLib.UserSettings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    public class AppViewer : UserControl
    {
        private static readonly string AppDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh");

        // Shared across all AppViewer instances in this process — one browser process,
        // one localStorage origin. The folder name is fixed per process (set via
        // WebCacheFolder before the first instance initialises).
        private static Task<CoreWebView2Environment> _sharedEnvTask;

        private static Task<CoreWebView2Environment> GetSharedEnv(string userDataFolder)
        {
            if (_sharedEnvTask == null)
            {
                var options = new CoreWebView2EnvironmentOptions
                {
                    // Disable background features that are irrelevant for a local book reader
                    // and consume memory/CPU unnecessarily.
                    //
                    // --disable-background-networking   — no background sync, prefetch, or
                    //                                     safe-browsing pings
                    // --disable-client-side-phishing-detection — no ML phishing model loaded
                    // --disable-default-apps            — no default app installation checks
                    // --disable-extensions              — no browser extension support
                    // --disable-sync                    — no Edge/Chrome profile sync
                    // --disable-translate               — no translation bar or model download
                    // --no-first-run                    — skip first-run experience dialogs
                    // --no-default-browser-check        — skip default browser prompt
                    //
                    // V8 heap limits:
                    // --js-flags=--max_old_space_size=512
                    //   Caps the V8 old-generation (long-lived objects) heap at 512 MB.
                    //   Default is ~4 GB on 64-bit. A book reader does not need gigabytes
                    //   of JS heap; capping it forces V8 to run GC more aggressively and
                    //   prevents runaway memory growth from accumulated closures, caches,
                    //   and PDF.js decoded page data.
                    // --js-flags=--max_semi_space_size=4
                    //   Caps the young-generation (short-lived objects) semi-space at 4 MB.
                    //   Default is ~8 MB. Smaller semi-space = more frequent minor GCs,
                    //   which keeps short-lived allocations (render frames, event objects)
                    //   from accumulating before collection.
                    //
                    // --disable-features=CalculateNativeWinOcclusion
                    //   Disables Windows occlusion tracking (detecting when the window is
                    //   covered by another window). We handle our own suspension via
                    //   TrySuspendAsync on VisibleChanged; the native occlusion tracker
                    //   adds background thread overhead with no benefit here.
                    AdditionalBrowserArguments =
                        "--disable-background-networking " +
                        "--disable-client-side-phishing-detection " +
                        "--disable-default-apps " +
                        "--disable-extensions " +
                        "--disable-sync " +
                        "--disable-translate " +
                        "--no-first-run " +
                        "--no-default-browser-check " +
                        "--js-flags=\"--max_old_space_size=512 --max_semi_space_size=4\" " +
                        "--disable-features=CalculateNativeWinOcclusion",

                    // Disable tracking prevention — all content is local or from a single
                    // trusted domain; the feature adds overhead with no benefit here.
                    EnableTrackingPrevention = false,
                };

                // Keep the webcache alongside the other cache folders under the app's
                // install directory (AppDomain.CurrentDomain.BaseDirectory), consistent
                // with LocalFileHandler and HebrewBooksHandler.
                _sharedEnvTask = CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder,
                    options: options);
            }
            return _sharedEnvTask;
        }

        private readonly WebView2 _webView = new WebView2 { Dock = DockStyle.Fill };
        private WebBridge _bridge;
        private DbHandler _db;
        private LocalFileHandler _localFile;
        private HebrewBooksHandler _hb;
        private SearchHandler _search;
        private DictionaryHandler _dictionary;
        private HebrewBooksDb _hebrewBooksDb;
        private FileSystemSearchHandler _fileSystemSearch;
        private UserSettingsDbHandler _userSettings;
        private string _dbInjectionScriptId;

        // Reference count for active AppViewer instances — used to know when the
        // last instance is disposed so the shared user settings DB can be closed.
        private static int _instanceCount;
        private bool _instanceCounted;

        private SplashOverlay _splash;

        /// <summary>
        /// Controls whether the "חלון עצמאי / חלונית" (pop-out) button is shown in the
        /// hamburger navigation dropdown.
        /// Set to <c>true</c> when hosting inside the VSTO task pane (where toggling
        /// between task-pane and floating-window makes sense).
        /// Defaults to <c>false</c> so standalone / demo hosts don't show the button.
        /// Must be set before the WebView2 finishes initialising (i.e. before
        /// <see cref="InitAsyncCore"/> injects the startup script).
        /// </summary>
        public bool ShowPopOutButton { get; set; } = false;

        // Subfolder name under KitveiHakodesh\ used as the WebView2 user-data folder.
        // Stored as a readonly field so it is captured before InitAsync runs.
        private readonly string _webCacheFolder;

        public AppViewer(string webCacheFolder = "webcache")
        {
            // Pre-load SQLite.Interop.dll from the install directory's x64\ or x86\ subfolder
            // before any SQLiteConnection is opened. This prevents the VSTO shadow-copy issue
            // where the native DLL cannot be found in the temp shadow-copy directory, causing
            // SQLite to fall back to a wrong-bitness copy on the PATH.
            SqliteNativeLoader.EnsureLoaded(AppDomain.CurrentDomain.BaseDirectory);

            _webCacheFolder = webCacheFolder;
            RightToLeft = RightToLeft.No;
            AutoScaleMode = AutoScaleMode.None;
            BackColorChanged += (_, __) => _SyncSplashBackColor();
            VisibleChanged += OnVisibleChanged;
            Controls.Add(_webView);
            _InitSplash();
            System.Threading.Interlocked.Increment(ref _instanceCount);
            _instanceCounted = true;
            _ = InitAsync();
        }

        // Suspend the WebView2 renderer when the control is hidden (e.g. the host
        // task pane is collapsed or the window is minimised) to free CPU and allow
        // the OS to reclaim the renderer's memory pages.
        // WebView2 resumes automatically when Visible becomes true again, so we
        // only need to handle the hide direction explicitly.
        private async void OnVisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                // Restore the inner WebView2 control visibility so the resumed
                // renderer can paint. WebView2 auto-resumes internally when its
                // control becomes visible, but we hid _webView explicitly on
                // suspend so we must un-hide it here.
                _webView.Visible = true;

                // Restore normal memory target so the renderer can use full resources
                // now that the app is active again.
                if (_webView.CoreWebView2 != null)
                    _webView.CoreWebView2.MemoryUsageTargetLevel =
                        CoreWebView2MemoryUsageTargetLevel.Normal;
                return;
            }

            if (_webView.CoreWebView2 == null) return;   // not yet initialised
            if (_webView.CoreWebView2.IsSuspended) return; // already suspended

            // Signal the browser engine to drop cached data and swap memory to disk
            // before suspending. This is the two-step pattern recommended by the
            // WebView2 performance article: set Low first, then TrySuspendAsync.
            _webView.CoreWebView2.MemoryUsageTargetLevel =
                CoreWebView2MemoryUsageTargetLevel.Low;

            // The API requires the WebView2 control's own Visible to be false.
            // Our _webView fills the UserControl (Dock = Fill), so hiding the
            // UserControl also hides _webView — but we set it explicitly to be safe.
            _webView.Visible = false;
            try
            {
                await _webView.CoreWebView2.TrySuspendAsync();
            }
            catch (Exception ex)
            {
                // Non-fatal — suspension is best-effort. Log and continue.
                System.Diagnostics.Debug.WriteLine("[AppViewer] TrySuspendAsync failed: " + ex.Message);
            }
        }

        private void _InitSplash()
        {
            Image logo = null;
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("KitveiHakodesh.png"))
            {
                if (stream != null)
                    logo = Image.FromStream(stream);
            }

            _splash = new SplashOverlay(logo) { Dock = DockStyle.Fill };
            Controls.Add(_splash);
            _SyncSplashBackColor();
            _splash.BringToFront();
        }

        private void _SyncSplashBackColor()
        {
            if (_splash == null) return;
            _splash.BackColor = BackColor;
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
            try
            {
                await InitAsyncCore();
            }
            catch (Exception ex)
            {
                // InitAsync is fire-and-forget — swallowed exceptions leave the splash up forever.
                // Hide the splash and surface the error so the user isn't stuck on a blank screen.
                _HideSplash();
                if (InvokeRequired)
                    Invoke(new Action(() => MessageBox.Show(
                        "שגיאה באתחול האפליקציה:\n" + ex.Message,
                        "כזית", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                else
                    MessageBox.Show(
                        "שגיאה באתחול האפליקציה:\n" + ex.Message,
                        "כזית", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task InitAsyncCore()
        {
            string udf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", _webCacheFolder);
            var env = await GetSharedEnv(udf);

            await _webView.EnsureCoreWebView2Async(env);

            // ── Disable WebView2 features the app does not use ────────────────────────
            // These are set once after EnsureCoreWebView2Async and before the first
            // navigation so they take effect for the entire session.
            var settings = _webView.CoreWebView2.Settings;

            // The app has its own zoom controls (Ctrl+±, pinch) — block the browser's
            // built-in zoom so the two systems don't fight each other.
            settings.IsZoomControlEnabled = false;

            // No swipe-to-navigate — the app is a single-page reader, not a browser.
            settings.IsSwipeNavigationEnabled = false;

            // No autofill or password saving — the app has no login forms.
            //settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;

            // No status bar — the hover-URL tooltip at the bottom left is irrelevant
            // in a native app and wastes a few pixels.
            settings.IsStatusBarEnabled = false;

            // Disable SmartScreen reputation checks — all content is served from local
            // virtual hosts or trusted origins (HebrewBooks download). SmartScreen adds
            // network round-trips and is meaningless for a local book reader.
            settings.IsReputationCheckingRequired = false;

            // Disable the default right-click context menu — the app provides its own
            // context menus where needed. The browser menu exposes irrelevant items
            // (Save image, Inspect, etc.) and is confusing in a native app context.
            settings.AreDefaultContextMenusEnabled = false;

            // Disable DevTools in production — users cannot open the inspector via
            // F12 or right-click. This also prevents accidental exposure of internals.
            // Remove this line (or set to true) during development if needed.
            //settings.AreDevToolsEnabled = false;

            // Disable browser-specific accelerator keys (Ctrl+F, Ctrl+P, Ctrl+R, F5,
            // F12, etc.). The app intercepts the keys it needs (Ctrl+F, Ctrl+W, etc.)
            // via its own keyboard handling; the browser defaults would interfere.
            //settings.AreBrowserAcceleratorKeysEnabled = false;

            // Show a blank page on navigation failure instead of the browser's styled
            // error page. The app is a local reader — navigation errors are internal
            // bugs, not user-facing web errors, so the browser error page is noise.
            //settings.IsBuiltInErrorPageEnabled = false;

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "KitveiHakodesh-vue-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);

            string savedPath = AppSettings.LoadDbPath();
            bool dbReady = File.Exists(savedPath);
            string escapedPath = savedPath.Replace("\\", "\\\\");

            // Merge both scripts into one AddScriptToExecuteOnDocumentCreatedAsync call —
            // each call is a browser-process round-trip, so one call is faster than two.
            string dbScript =
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";" +
                "window.__webviewShowPopOut=" + (ShowPopOutButton ? "true" : "false") + ";";
            _dbInjectionScriptId = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                JsBridge.Script + "\n" + dbScript);

            // IframeScrollScript runs in every frame including local file iframes.
            // It is registered separately because it must not share the same document-created
            // slot as the bridge script — the bridge script references window.chrome.webview
            // which is only available in the top frame.
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                JsBridge.IframeScrollScript);

            _bridge = new WebBridge(_webView, this);
            _db = new DbHandler(_bridge, _webView, savedPath);

            _localFile = new LocalFileHandler(_bridge, _webView);
            _hb = new HebrewBooksHandler(_bridge, _webView, this);
            _search = new SearchHandler(_bridge, _webView);
            _dictionary = new DictionaryHandler(AppDir);
            _hebrewBooksDb = HebrewBooksDb.Instance;
            _hebrewBooksDb.Initialize();
            _fileSystemSearch = new FileSystemSearchHandler(_bridge);
            _userSettings = new UserSettingsDbHandler(_bridge, this, savedPath);
            _db.OnDbPathPicked = path =>
            {
                _search.ResetAndReindex(path);
                _userSettings.UpdateSeforimDbPath(path);
            };

            _webView.CoreWebView2.WebMessageReceived += OnMessageReceived;
            _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            _webView.Source = new Uri("http://KitveiHakodesh-vue-app/index.html");

            // Safety net: if NavigationCompleted never fires (e.g. WebView2 runtime issue),
            // hide the splash after 8 seconds so the user isn't stuck on a blank screen.
            _ = Task.Delay(8000).ContinueWith(_ => _HideSplash());
            _search.OnDbReady(savedPath);
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            // Hide the splash regardless of success — a failed navigation still shows the
            // WebView error page, which is more useful than an infinite splash screen.
            _HideSplash();
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
            string dbScript =
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";" +
                "window.__webviewShowPopOut=" + (ShowPopOutButton ? "true" : "false") + ";";
            _dbInjectionScriptId = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                JsBridge.Script + "\n" + dbScript);

            // Re-init the DB handler; keep the existing search handler and its index state
            _db = new DbHandler(_bridge, _webView, savedPath);
            _db.OnDbPathPicked = path =>
            {
                _search.ResetAndReindex(path);
                _userSettings.UpdateSeforimDbPath(path);
            };

            // Re-init user settings DB for the (possibly changed) seforim DB path
            _userSettings?.Dispose();
            _userSettings = new UserSettingsDbHandler(_bridge, this, savedPath);

            // Always call OnDbReady — if the file doesn't exist it pushes ftsDbNotFound
            // to the frontend; if it does exist it starts or resumes indexing.
            _search.OnDbReady(savedPath);

            _webView.CoreWebView2.Navigate("http://KitveiHakodesh-vue-app/index.html");
        }

        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
            => _hb.OnDownloadStarting(sender, e);

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                await OnMessageReceivedAsync(e);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AppViewer] Unhandled exception in OnMessageReceived: " + ex);
            }
        }

        private async Task OnMessageReceivedAsync(CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();
                    string action = root.TryGetProperty("action", out var a)
                        ? a.GetString()
                        : root.TryGetProperty("sql", out _) ? "sql" : null;

                    switch (action)
                    {
                        case "sql": await _db.HandleSql(root, id); break;
                        case "dict-sql": await HandleDictSql(root, id); break;
                        case "setDbPath": _db.HandleSetDbPath(root, id); break;
                        case "pickDbPath": _db.HandlePickDbPath(id, this); break;
                        case "resetSettings": _db.HandleResetSettings(id); break;
                        case "reload": _bridge.Reply(id, new { }); await HandleReload(); break;
                        case "pickFile": _localFile.HandlePickFile(id, this); break;
                        case "pickFolder": _localFile.HandlePickFolder(id, this); break;
                        case "restoreLocalFile": await _localFile.HandleRestoreLocalFile(root, id); break;
                        case "disposeLocalFileHost": _localFile.HandleDisposeLocalFileHost(root, id); break;
                        case "appReady": HandleAppReady(id); break;
                        case "restoreHbPdf": _hb.HandleRestoreHbPdf(root, id); break;
                        case "triggerHbDownload": _hb.HandleTriggerHbDownload(root, id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        case "hbSearch": HandleHebrewBooksSearch(root, id); break;
                        case "GetFtsIndexingProgress": _search.HandleGetProgress(id); break;
                        case "FtsSearchStart": _search.HandleSearchStart(root, id); break;
                        case "FtsSearchCancel": _search.HandleSearchCancel(root, id); break;
                        case "DeleteFtsIndex":
                            _search.HandleDeleteIndex(id);
                            break;
                        case "ResetFtsIndex": _search.HandleResetFtsIndex(id); break;
                        case "TogglePopOut": HandleTogglePopOut(id); break;
                        case "toggleFullscreen": HandleToggleFullscreen(id); break;
                        case "getWordSynonyms": HandleGetWordSynonyms(root, id); break;
                        case "getFonts": HandleGetFonts(id); break;
                        case "getDiagnostics": HandleGetDiagnostics(id); break;
                        case "fileSystemSearchPageLoad": _fileSystemSearch.HandlePageLoad(id); break;
                        case "fileSystemSearch": _fileSystemSearch.HandleSearch(root, id); break;
                        case "ResetDocumentLocatorIndex": _fileSystemSearch.HandleReindex(id); break;
                        case "userSettingsQuery": await _userSettings.HandleQuery(root, id); break;
                        case "userSettingsExecute": await _userSettings.HandleExecute(root, id); break;
                        case "exportToWord": HandleExportToWord(root, id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (id != null) _bridge.Reply(id, new { error = ex.Message });
            }
        }

        private void HandleGetWordSynonyms(JsonElement root, string id)
        {
            string word = root.TryGetProperty("word", out var w) ? w.GetString() : null;
            var groups = WordThesaurusProvider.GetSynonyms(word);
            _bridge.Reply(id, new { groups });
        }

        private async Task HandleDictSql(JsonElement root, string id)
        {
            if (!_dictionary.IsReady) { _bridge.Reply(id, new { error = "Dictionary database not available" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _dictionary.Query(sql, DbHandler.ParseParamsStatic(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        private async Task HandleWikiDictSql(JsonElement root, string id)
        {
            if (!_dictionary.IsWikiReady) { _bridge.Reply(id, new { error = "Wikidict database not available" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _dictionary.QueryWiki(sql, DbHandler.ParseParamsStatic(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        private void HandleTogglePopOut(string id)
        {
            _bridge.Reply(id, new { });
            if (InvokeRequired)
                Invoke(new Action(() => TogglePopOut?.Invoke(false)));
            else
                TogglePopOut?.Invoke(false);
        }

        private void HandleToggleFullscreen(string id)
        {
            _bridge.Reply(id, new { });
            if (InvokeRequired)
                Invoke(new Action(() => ToggleFormFullscreen()));
            else
                ToggleFormFullscreen();
        }

        private void ToggleFormFullscreen()
        {
            // AppViewer itself stays in the task pane host even when popped out —
            // TaskPanePopOut moves _webView (the first child) into the floating form.
            // So we must look for the form that contains _webView, not AppViewer itself.
            Form hostForm = _webView.FindForm();

            // If not hosted in a window (e.g., still in the VSTO task pane), pop out first
            if (hostForm == null)
            {
                TogglePopOut?.Invoke(true); // pop out and go fullscreen in one step
                return;
            }

            // Already in a floating window — just toggle fullscreen, never touch popout
            if (hostForm.FormBorderStyle == FormBorderStyle.None && hostForm.WindowState == FormWindowState.Maximized)
            {
                // Exit fullscreen
                hostForm.FormBorderStyle = FormBorderStyle.Sizable;
                hostForm.WindowState = FormWindowState.Normal;
            }
            else
            {
                // Enter fullscreen
                // If window is already maximized, we must restore to Normal first,
                // otherwise setting Maximized again does nothing and chrome doesn't get removed.
                if (hostForm.WindowState == FormWindowState.Maximized)
                {
                    hostForm.WindowState = FormWindowState.Normal;
                }
                hostForm.FormBorderStyle = FormBorderStyle.None;
                hostForm.WindowState = FormWindowState.Maximized;
            }
        }

        private void HandleGetFonts(string id)
        {
            _bridge.Reply(id, new { fonts = FontsProvider.GetHebrewFonts() });
        }

        private void HandleAppReady(string id)
        {
            _bridge.Reply(id, new { });
            _appReady = true;
            if (_pendingFilePath != null)
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                _ = _localFile.OpenFileFromPathAsync(path);
            }
        }

        private void HandleGetDiagnostics(string id)
        {
            var report = EnvironmentDiagnostics.Collect();
            _bridge.Reply(id, new { diagnostics = report });
        }

        private void HandleExportToWord(JsonElement root, string id)
        {
            _bridge.Reply(id, new { ok = true });

            string html = root.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

            _ = WordExporter.ExportAsync(html, title);
        }

        private void HandleHebrewBooksSearch(JsonElement root, string id)
        {
            if (!_hebrewBooksDb.IsInitialized)
            {
                _bridge.Reply(id, new { error = "Hebrew Books database not available" });
                return;
            }

            string query = root.TryGetProperty("query", out var q) ? (q.GetString() ?? "") : "";

            try
            {
                var results = _hebrewBooksDb.Search(query);
                _bridge.Reply(id, new { books = results });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        // A file path queued before the Vue app has finished mounting.
        // Dispatched when Vue sends the 'appReady' message, guaranteeing that all
        // event listeners (localFileStore) are live before the push event fires.
        private string _pendingFilePath;
        // True once Vue has sent 'appReady' — prevents re-queueing after first mount.
        private bool _appReady;

        /// <summary>
        /// Opens a file by path, as if the user had picked it via the file picker.
        /// Safe to call immediately after construction — if the Vue app is not yet
        /// ready the path is queued and opened as soon as 'appReady' is received.
        /// </summary>
        public void OpenFileFromPath(string filePath)
        {
            if (!_appReady)
            {
                // Vue event listeners not live yet — queue until appReady fires.
                _pendingFilePath = filePath;
            }
            else
            {
                _ = _localFile.OpenFileFromPathAsync(filePath);
            }
        }

        /// <summary>
        /// Set by the host to handle the popout toggle.
        /// The bool parameter indicates whether to enter fullscreen mode after popping out.
        /// </summary>
        public Action<bool> TogglePopOut { get; set; }

        /// <summary>
        /// Called by TaskPaneManager via reflection to wire up the popout toggle.
        /// Accepts Action<bool> from the new TaskPanePopOut.Toggle(bool) signature.
        /// </summary>
        public void SetPopOutToggleAction(Action<bool> action)
        {
            TogglePopOut = action;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe all CoreWebView2 event handlers before the control is
                // torn down. Leaving them attached creates reference cycles that prevent
                // the renderer process from being released (per the WebView2 performance
                // article: "Remove native event handlers before disposing WebView2 objects").
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= OnMessageReceived;
                    _webView.CoreWebView2.DownloadStarting -= _hb.OnDownloadStarting;
                }

                // Release all PDF virtual host mappings so WebView2 does not hold
                // folder handles after the process exits.
                _localFile?.DisposeAllHosts();

                _fileSystemSearch?.Dispose();
                _userSettings?.Dispose();

                // Decrement the shared instance count. When the last AppViewer is
                // disposed, close the shared user settings DB connection.
                if (_instanceCounted)
                {
                    _instanceCounted = false;
                    if (System.Threading.Interlocked.Decrement(ref _instanceCount) <= 0)
                        UserSettingsDbAccess.DisposeShared();
                }
            }
            base.Dispose(disposing);
        }
    }
}
