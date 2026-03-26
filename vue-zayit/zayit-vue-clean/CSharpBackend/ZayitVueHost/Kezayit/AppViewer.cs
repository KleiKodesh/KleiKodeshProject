using Kezayit.Bridge;
using Kezayit.Db;
using Kezayit.HebrewBooks;
using Kezayit.Pdf;
using Kezayit.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
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
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__webviewDbPath=\"" + escapedPath + "\";" +
                "window.__webviewDbReady=" + (dbReady ? "true" : "false") + ";");

            _bridge = new WebBridge(_webView, this);
            _db = new DbHandler(_bridge, _webView, savedPath);
            _pdf = new PdfHandler(_bridge, _webView);
            _hb = new HebrewBooksHandler(_bridge, _webView, this);

            _webView.CoreWebView2.WebMessageReceived += OnMessageReceived;
            _webView.CoreWebView2.DownloadStarting += (s, e) => _hb.OnDownloadStarting(s, e);
            _webView.Source = new Uri("http://kezayit-vue-app/index.html");
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
                        case "pickFile": _pdf.HandlePickFile(id, this); break;
                        case "restoreLocalPdf": await _pdf.HandleRestoreLocalPdf(root, id); break;
                        case "disposePdfHost": _pdf.HandleDisposePdfHost(root, id); break;
                        case "restoreHbPdf": _hb.HandleRestoreHbPdf(root, id); break;
                        case "triggerHbDownload": _hb.HandleTriggerHbDownload(root, id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (id != null) _bridge.Reply(id, new { error = ex.Message });
            }
        }
    }
}
