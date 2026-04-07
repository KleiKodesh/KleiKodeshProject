using KezayitLib.Bridge;
using KezayitLib.Db;
using KezayitLib.HebrewBooks;
using KezayitLib.Pdf;
using KezayitLib.Search;
using KezayitLib.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KezayitLib
{
    public class AppViewer : UserControl
    {
        private static readonly string AppDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kezayit");

        private readonly WebView2 _webView = new WebView2 { Dock = DockStyle.Fill };
        private WebBridge _bridge;
        private DbHandler _db;
        private PdfHandler _pdf;
        private HebrewBooksHandler _hb;
        private HebrewBooksCsvUpdater _hbCsvUpdater;
        private SearchHandler _search;
        private string _dbInjectionScriptId;

        private SplashOverlay _splash;

        public AppViewer()
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
                .GetManifestResourceStream("KleiKodesh_Main.png"))
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
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(AppDir, "webcache"));

            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kezayit-vue-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(JsBridge.Script);

            string savedPath = AppSettings.LoadDbPath();
            bool dbReady = File.Exists(savedPath);
            string escapedPath = savedPath.Replace("\\", "\\\\");
            _dbInjectionScriptId = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";");

            _bridge = new WebBridge(_webView, this);
            _db = new DbHandler(_bridge, _webView, savedPath);
            _pdf = new PdfHandler(_bridge, _webView);
            _hb = new HebrewBooksHandler(_bridge, _webView, this);
            _hbCsvUpdater = new HebrewBooksCsvUpdater();
            _search = new SearchHandler(_bridge, _webView);
            _db.OnDbPathPicked = path => _search.ResetAndReindex(path);

            _webView.CoreWebView2.WebMessageReceived += OnMessageReceived;
            _webView.CoreWebView2.DownloadStarting += (s, e) => _hb.OnDownloadStarting(s, e);
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            _webView.Source = new Uri("http://kezayit-vue-app/index.html");

            Console.WriteLine("[AppViewer] InitAsync: savedPath=" + savedPath + " dbReady=" + dbReady);
            if (dbReady)
            {
                Console.WriteLine("[AppViewer] Calling _search.OnDbReady");
                _search.OnDbReady(savedPath);
                _hbCsvUpdater.RunIfDue();
            }
            else
            {
                Console.WriteLine("[AppViewer] Skipping OnDbReady — no DB file at path");
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
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
            _dbInjectionScriptId = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";");

            // Re-init the DB handler; keep the existing search handler and its index state
            _db = new DbHandler(_bridge, _webView, savedPath);
            _db.OnDbPathPicked = path => _search.ResetAndReindex(path);

            // Only kick off indexing if the DB changed or bloom is missing/stale
            if (dbReady)
                _search.OnDbReady(savedPath);

            _webView.CoreWebView2.Navigate("http://kezayit-vue-app/index.html");
        }

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                        case "setDbPath": _db.HandleSetDbPath(root, id); break;
                        case "pickDbPath": _db.HandlePickDbPath(id, this); break;
                        case "resetSettings": _db.HandleResetSettings(id); break;
                        case "reload": _bridge.Reply(id, new { }); await HandleReload(); break;
                        case "pickFile": _pdf.HandlePickFile(id, this); break;
                        case "restoreLocalPdf": await _pdf.HandleRestoreLocalPdf(root, id); break;
                        case "disposePdfHost": _pdf.HandleDisposePdfHost(root, id); break;
                        case "restoreHbPdf": _hb.HandleRestoreHbPdf(root, id); break;
                        case "triggerHbDownload": _hb.HandleTriggerHbDownload(root, id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        case "GetBloomIndexingProgress": _search.HandleGetProgress(id); break;
                        case "BloomSearchStart": _search.HandleSearchStart(root, id); break;
                        case "BloomSearchCancel": _search.HandleSearchCancel(root, id); break;
                        case "DeleteBloomIndex": _search.HandleDeleteIndex(id); break;
                        case "TogglePopOut": HandleTogglePopOut(id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (id != null) _bridge.Reply(id, new { error = ex.Message });
            }
        }

        private void HandleTogglePopOut(string id)
        {
            _bridge.Reply(id, new { });
            if (InvokeRequired)
                Invoke(new Action(() => TogglePopOut?.Invoke()));
            else
                TogglePopOut?.Invoke();
        }

        /// <summary>
        /// Set by the host to handle the popout toggle.
        /// </summary>
        public Action TogglePopOut { get; set; }

        /// <summary>
        /// Called by TaskPaneManager via reflection to wire up the popout toggle.
        /// </summary>
        public void SetPopOutToggleAction(Action action) => TogglePopOut = action;
    }
}
