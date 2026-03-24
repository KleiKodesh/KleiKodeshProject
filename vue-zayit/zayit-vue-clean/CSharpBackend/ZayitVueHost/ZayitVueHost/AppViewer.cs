using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZayitVueHost
{
    /// <summary>
    /// The main WebView2 host control. Initializes the WebView, injects the JS bridge,
    /// and wires up the three RPC handlers: sql, setDbPath, pickDbPath.
    /// </summary>
    public class AppViewer : UserControl
    {
        private static readonly string AppDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kezayit");

        private readonly WebView2 _webView = new WebView2 { Dock = DockStyle.Fill };
        private DbAccess _db;

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

            // Inject the JS bridge before the Vue app loads
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(JsBridge.Script);

            // Inject globals so the Vue app knows the DB state immediately
            string savedPath = Settings.LoadDbPath();
            bool dbReady = File.Exists(savedPath);

            if (dbReady)
                _db = new DbAccess(savedPath);

            string escapedPath = savedPath.Replace("\\", "\\\\");
            string globalsScript =
                "window.__webviewDbPath = \"" + escapedPath + "\";" +
                "window.__webviewDbReady = " + (dbReady ? "true" : "false") + ";";

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(globalsScript);

            _webView.CoreWebView2.WebMessageReceived += OnMessageReceived;
            _webView.Source = new Uri("http://kezayit-vue-app/index.html");
        }

        // ── Message dispatch ──────────────────────────────────────────────────

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();

                    // Route by "action" property, or "sql" property for query messages
                    string action = root.TryGetProperty("action", out var a)
                        ? a.GetString()
                        : root.TryGetProperty("sql", out _) ? "sql" : null;

                    switch (action)
                    {
                        case "sql":        await HandleSql(root, id);        break;
                        case "setDbPath":  await HandleSetDbPath(root, id);  break;
                        case "pickDbPath": HandlePickDbPath(id);              break;
                        default:
                            Reply(id, new { error = "Unknown action: " + action });
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("OnMessageReceived error: " + ex.Message);
                if (id != null) Reply(id, new { error = ex.Message });
            }
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private async Task HandleSql(JsonElement root, string id)
        {
            string sql = root.GetProperty("sql").GetString();

            if (_db == null)
            {
                Reply(id, new { error = "No database loaded" });
                return;
            }

            try
            {
                var rows = await Task.Run(() => _db.Query(sql, ParseParams(root)));
                Reply(id, new { rows });
            }
            catch (Exception ex)
            {
                Log("SQL error: " + ex.Message);
                Reply(id, new { error = ex.Message });
            }
        }

        private Task HandleSetDbPath(JsonElement root, string id)
        {
            string path = root.GetProperty("path").GetString();

            if (!File.Exists(path))
            {
                Reply(id, new { error = "קובץ לא נמצא" });
                return Task.CompletedTask;
            }

            Settings.SaveDbPath(path);
            _db = new DbAccess(path);
            Reply(id, new { path });
            return Task.CompletedTask;
        }

        private void HandlePickDbPath(string id)
        {
            // Must use BeginInvoke — WebMessageReceived fires on the UI thread,
            // so calling Invoke here would deadlock.
            BeginInvoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title    = "בחר קובץ מסד נתונים";
                    dlg.Filter   = "SQLite Database (*.db)|*.db|All files (*.*)|*.*";
                    dlg.FileName = Settings.LoadDbPath();

                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    Settings.SaveDbPath(dlg.FileName);
                    _db = new DbAccess(dlg.FileName);

                    // Notify JS via event (no pending promise to resolve for pickDbPath)
                    string escaped = dlg.FileName.Replace("\\", "\\\\");
                    _webView.CoreWebView2.PostWebMessageAsJson(
                        "{\"event\":\"dbPathPicked\",\"path\":\"" + escaped + "\"}");
                }
            }));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Reply(string id, object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            // Inject the id as the first property
            string withId = json.Length > 2
                ? "{\"id\":\"" + id + "\"," + json.Substring(1)
                : "{\"id\":\"" + id + "\"}";

            if (InvokeRequired)
                Invoke(new Action(() => _webView.CoreWebView2.PostWebMessageAsJson(withId)));
            else
                _webView.CoreWebView2.PostWebMessageAsJson(withId);
        }

        private static object[] ParseParams(JsonElement root)
        {
            if (!root.TryGetProperty("params", out var el) || el.ValueKind != JsonValueKind.Array)
                return Array.Empty<object>();

            var result = new object[el.GetArrayLength()];
            int i = 0;
            foreach (var item in el.EnumerateArray())
            {
                if      (item.ValueKind == JsonValueKind.String)  result[i] = item.GetString();
                else if (item.ValueKind == JsonValueKind.Number)  result[i] = item.TryGetInt64(out long l) ? (object)l : item.GetDouble();
                else if (item.ValueKind == JsonValueKind.True)    result[i] = true;
                else if (item.ValueKind == JsonValueKind.False)   result[i] = false;
                else                                              result[i] = null;
                i++;
            }
            return result;
        }

        private static void Log(string msg) => Debug.WriteLine("[AppViewer] " + msg);
    }
}
