using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZayitVueHost.services;

namespace ZayitVueHost.viewer
{
    internal class ZayitViewer : UserControl
    {
        private readonly WebView2 _wv = new WebView2 { Dock = DockStyle.Fill };
        private DbService _db;
        private WebViewRpc _rpc;

        private static readonly string AppDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kezayit");

        public ZayitViewer()
        {
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(_wv);
            _ = InitAsync();
        }

        private async Task InitAsync()
        {
            Log("InitAsync started");

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(AppDir, "webcache"));

            await _wv.EnsureCoreWebView2Async(env);
            Log("WebView2 ready");

            _wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kezayit-vue-app", AppDir, CoreWebView2HostResourceAccessKind.Allow);
            Log("Virtual host mapped: kezayit-vue-app -> " + AppDir);

            await _wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WebViewBridge.Script);
            Log("Bridge script injected");

            string savedPath = DbService.LoadPath();
            bool dbExists = File.Exists(savedPath);
            Log("DB path: " + savedPath + " | exists: " + dbExists);

            if (dbExists)
            {
                _db = new DbService(savedPath);
                Log("DbService created");
            }
            else
            {
                Log("WARNING: DB file not found — queries will fail until user picks a file");
            }

            string escaped = savedPath.Replace("\\", "\\\\");
            string globalsScript = "window.__webviewDbPath = \"" + escaped + "\"; window.__webviewDbReady = " + (dbExists ? "true" : "false") + ";";
            await _wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(globalsScript);
            Log("Globals injected: " + globalsScript);

            _rpc = new WebViewRpc(_wv.CoreWebView2, this);
            _rpc.On("sql",        HandleQuery);
            _rpc.On("setDbPath",  HandleSetDbPath);
            _rpc.On("pickDbPath", HandlePickDbPath);
            _rpc.Attach();
            Log("RPC attached");

            _wv.Source = new Uri("http://kezayit-vue-app/index.html");
            Log("Navigation started -> http://kezayit-vue-app/index.html");
        }

        // ── RPC handlers ──────────────────────────────────────────────────────

        private async Task HandleQuery(JsonElement root, Action<object> reply)
        {
            string sql = root.GetProperty("sql").GetString();
            Log("SQL query: " + sql?.Substring(0, Math.Min(80, sql?.Length ?? 0)));

            if (_db == null)
            {
                Log("ERROR: _db is null — no database loaded");
                reply(new { error = "No database loaded" });
                return;
            }

            try
            {
                var parameters = ParseParams(root);
                var rows = await Task.Run(() => _db.Query(sql, parameters));
                Log("SQL OK — row count: " + System.Linq.Enumerable.Count(rows));
                reply(new { rows });
            }
            catch (Exception ex)
            {
                Log("SQL ERROR: " + ex.Message);
                reply(new { error = ex.Message });
            }
        }

        private Task HandleSetDbPath(JsonElement root, Action<object> reply)
        {
            string path = root.GetProperty("path").GetString();
            Log("setDbPath: " + path);
            if (!File.Exists(path))
            {
                Log("setDbPath ERROR: file not found");
                reply(new { error = "קובץ לא נמצא" });
                return Task.CompletedTask;
            }
            DbService.SavePath(path);
            _db = new DbService(path);
            Log("setDbPath OK — DbService updated");
            reply(new { path });
            return Task.CompletedTask;
        }

        private Task HandlePickDbPath(JsonElement root, Action<object> reply)
        {
            Log("pickDbPath received — opening dialog via BeginInvoke");
            BeginInvoke(new Action(() =>
            {
                Log("BeginInvoke fired — showing OpenFileDialog");
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title    = "בחר קובץ מסד נתונים";
                    dlg.Filter   = "SQLite Database (*.db)|*.db|All files (*.*)|*.*";
                    dlg.FileName = DbService.LoadPath();
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        Log("pickDbPath: user cancelled");
                        return;
                    }

                    Log("pickDbPath: user picked " + dlg.FileName);
                    DbService.SavePath(dlg.FileName);
                    _db = new DbService(dlg.FileName);
                    Log("DbService updated with new path");

                    string escaped = dlg.FileName.Replace("\\", "\\\\");
                    string msg = "{\"event\":\"dbPathPicked\",\"path\":\"" + escaped + "\"}";
                    Log("Posting to JS: " + msg);
                    _wv.CoreWebView2.PostWebMessageAsJson(msg);
                    Log("PostWebMessageAsJson called");
                }
            }));
            return Task.CompletedTask;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void Log(string msg)
        {
            Debug.WriteLine("[ZayitViewer] " + msg);
        }

        private static object[] ParseParams(JsonElement root)
        {
            if (!root.TryGetProperty("params", out var el) || el.ValueKind != JsonValueKind.Array)
                return Array.Empty<object>();

            var result = new object[el.GetArrayLength()];
            int i = 0;
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)      result[i] = item.GetString();
                else if (item.ValueKind == JsonValueKind.Number) result[i] = item.TryGetInt64(out long l) ? (object)l : item.GetDouble();
                else if (item.ValueKind == JsonValueKind.True)   result[i] = true;
                else if (item.ValueKind == JsonValueKind.False)  result[i] = false;
                else                                             result[i] = null;
                i++;
            }
            return result;
        }
    }
}
