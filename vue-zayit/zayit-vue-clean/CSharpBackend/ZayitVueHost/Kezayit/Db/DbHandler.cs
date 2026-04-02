using Kezayit.Bridge;
using Kezayit.Settings;
using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kezayit.Db
{
    /// <summary>
    /// Handles SQL queries, setDbPath, and pickDbPath actions.
    /// </summary>
    public class DbHandler
    {
        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private DbAccess _db;

        public Action<string> OnDbPathPicked { get; set; }

        public DbHandler(WebBridge bridge, WebView2 webView, string savedPath)
        {
            _bridge = bridge;
            _webView = webView;
            if (File.Exists(savedPath))
                _db = new DbAccess(savedPath);
        }

        public bool IsReady => _db != null;

        public async Task HandleSql(JsonElement root, string id)
        {
            if (_db == null) { _bridge.Reply(id, new { error = "No database loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _db.Query(sql, ParseParams(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public void HandleResetSettings(string id)
        {
            try { Interaction.DeleteSetting("ZayitApp", "Database"); } catch { }
            _bridge.Reply(id, new { });
        }

        public void HandleSetDbPath(JsonElement root, string id)
        {
            string path = root.GetProperty("path").GetString();
            if (!File.Exists(path)) { _bridge.Reply(id, new { error = "קובץ לא נמצא" }); return; }
            AppSettings.SaveDbPath(path);
            _db = new DbAccess(path);
            _bridge.Reply(id, new { path });
        }

        public void HandlePickDbPath(string id, Control owner)
        {
            owner.BeginInvoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title    = "בחר קובץ מסד נתונים";
                    dlg.Filter   = "SQLite Database (*.db)|*.db|All files (*.*)|*.*";
                    dlg.FileName = AppSettings.LoadDbPath();
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    AppSettings.SaveDbPath(dlg.FileName);
                    _db = new DbAccess(dlg.FileName);
                    string escaped = dlg.FileName.Replace("\\", "\\\\");
                    _webView.CoreWebView2.PostWebMessageAsJson(
                        "{\"event\":\"dbPathPicked\",\"path\":\"" + escaped + "\"}");
                    OnDbPathPicked?.Invoke(dlg.FileName);
                }
            }));
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
    }
}
