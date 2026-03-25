using Kezayit.Bridge;
using Kezayit.Db;
using Kezayit.HebrewBooks;
using Kezayit.Pdf;
using Kezayit.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kezayit
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

        public AppViewer()
        {
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(_webView);
            _ = InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                Log("InitAsync start");

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: Path.Combine(AppDir, "webcache"));
                Log("CoreWebView2Environment created");

                await _webView.EnsureCoreWebView2Async(env);
                Log("EnsureCoreWebView2Async done");

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "kezayit-vue-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);
                Log("VirtualHostMapping set");

                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(JsBridge.Script);
                Log("JsBridge script injected");

                string savedPath = AppSettings.LoadDbPath();
                bool dbReady = File.Exists(savedPath);
                Log("DB path: " + savedPath + " | exists: " + dbReady);

                string escapedPath = savedPath.Replace("\\", "\\\\");
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "window.__webviewDbPath=\"" + escapedPath + "\";" +
                    "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";");
                Log("Globals script injected");

                _bridge = new WebBridge(_webView, this);
                _db = new DbHandler(_bridge, _webView, savedPath);
                _pdf = new PdfHandler(_bridge, _webView);
                _hb = new HebrewBooksHandler(_bridge, _webView, this);
                Log("Handlers created");

                _webView.CoreWebView2.WebMessageReceived += OnMessageReceived;
                _webView.CoreWebView2.DownloadStarting += (s, e) => _hb.OnDownloadStarting(s, e);
                Log("Events hooked");

                _webView.Source = new Uri("http://kezayit-vue-app/index.html");
                Log("Source set — init complete");
            }
            catch (Exception ex)
            {
                Log("InitAsync EXCEPTION: " + ex);
            }
        }

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                Log("Message received: " + e.WebMessageAsJson);
                using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();
                    string action = root.TryGetProperty("action", out var a)
                        ? a.GetString()
                        : root.TryGetProperty("sql", out _) ? "sql" : null;

                    Log("Dispatching id=" + id + " action=" + action);

                    switch (action)
                    {
                        case "sql": await _db.HandleSql(root, id); break;
                        case "setDbPath": _db.HandleSetDbPath(root, id); break;
                        case "pickDbPath": _db.HandlePickDbPath(id, this); break;
                        case "pickFile": _pdf.HandlePickFile(id, this); break;
                        case "restoreLocalPdf": await _pdf.HandleRestoreLocalPdf(root, id); break;
                        case "disposePdfHost": _pdf.HandleDisposePdfHost(root, id); break;
                        case "restoreHbPdf": _hb.HandleRestoreHbPdf(root, id); break;
                        case "triggerHbDownload": _hb.HandleTriggerHbDownload(root, id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }

                    Log("Dispatched id=" + id);
                }
            }
            catch (Exception ex)
            {
                Log("OnMessageReceived EXCEPTION id=" + id + ": " + ex);
                if (id != null) _bridge.Reply(id, new { error = ex.Message });
            }
        }

        private static void Log(string msg) =>
            Debug.WriteLine("[AppViewer] " + msg);
    }
}
