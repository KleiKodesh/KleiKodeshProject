using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.Db
{
    /// <summary>
    /// Handles SQL queries, setDbPath, and pickDbPath actions.
    /// </summary>
    public class DbHandler
    {
        private readonly WebBridge _bridge;
        private DbAccess _db;

        public Action<string> OnDbPathPicked { get; set; }

        public DbHandler(WebBridge bridge, WebView2 webView, string savedPath)
        {
            _bridge = bridge;
            if (File.Exists(savedPath))
            {
                try { _db = new DbAccess(savedPath); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[DbHandler] Failed to open DB: " + ex.Message);
                }
            }
        }

        public bool IsReady => _db != null;

        public async Task HandleSql(JsonElement root, string id)
        {
            if (_db == null) { _bridge.Reply(id, new { error = "No database loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                // ConfigureAwait(false): keep the continuation (JSON-serializing a
                // potentially huge row set in Reply) OFF the WinForms UI thread —
                // that thread also services WebView2 accelerator keys, and stalls
                // there directly delay Ctrl+Tab / Ctrl+W / etc. reaching the page.
                var rows = await Task.Run(() => _db.Query(sql, ParseParamsStatic(root))).ConfigureAwait(false);
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public void HandleResetSettings(string id)
        {
            // Wipe the entire KitveiHakodesh VB settings subtree (Database, Popout, HebrewBooks, etc.)
            // This also clears the persisted dark mode (Appearance/DarkMode), so the app
            // resets to light theme. Apply light to the title bar immediately so it matches
            // the Vue theme that will be applied after the reload that follows this call.
            try { Interaction.DeleteSetting("KitveiHakodesh"); } catch { }
            ResetTitleBarToLight?.Invoke();
            _bridge.Reply(id, new { });
        }

        /// <summary>
        /// Invoked after a settings reset to immediately apply light theme to the title bar.
        /// Set by AppViewer after construction.
        /// </summary>
        public Action ResetTitleBarToLight { get; set; }

        /// <summary>
        /// Resets the database path to the auto-resolved default and reopens the DB.
        /// Replies with { path } so the frontend can update its displayed path.
        /// </summary>
        public void HandleClearDbPath(string id)
        {
            string defaultPath = AppSettings.ResolveDefaultDbPath();
            AppSettings.SaveDbPath(defaultPath);
            if (_db != null) _db.Dispose();
            if (File.Exists(defaultPath))
            {
                try { _db = new DbAccess(defaultPath); }
                catch { _db = null; }
            }
            else
            {
                _db = null;
            }
            _bridge.Reply(id, new { path = defaultPath });
            OnDbPathPicked?.Invoke(defaultPath);
        }

        /// <summary>
        /// Clears the persisted HebrewBooks local folder setting (saves an empty string).
        /// </summary>
        public void HandleClearHbLocalFolder(string id)
        {
            AppSettings.SaveHbLocalFolder("");
            _bridge.Reply(id, new { });
        }

        /// <summary>
        /// Reads the shared "turn off automatic updates" flag (same registry key as the
        /// KleiKodesh Word add-in). Replies with { value: bool }.
        /// </summary>
        public void HandleGetTurnOffUpdates(string id)
        {
            _bridge.Reply(id, new { value = AppSettings.LoadTurnOffUpdates() });
        }

        /// <summary>
        /// Persists the shared "turn off automatic updates" flag. Expects { value: bool }.
        /// </summary>
        public void HandleSetTurnOffUpdates(JsonElement root, string id)
        {
            bool value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
            AppSettings.SaveTurnOffUpdates(value);
            _bridge.Reply(id, new { });
        }

        public void HandleSetDbPath(JsonElement root, string id)
        {
            string path = root.GetProperty("path").GetString();
            if (!File.Exists(path)) { _bridge.Reply(id, new { error = "קובץ לא נמצא" }); return; }
            AppSettings.SaveDbPath(path);
            if (_db != null) _db.Dispose();
            try
            {
                _db = new DbAccess(path);
            }
            catch (Exception ex)
            {
                _db = null;
                _bridge.Reply(id, new { error = ex.Message });
                return;
            }
            _bridge.Reply(id, new { path });
            OnDbPathPicked?.Invoke(path);
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
                    if (_db != null) _db.Dispose();
                    try
                    {
                        _db = new DbAccess(dlg.FileName);
                    }
                    catch (Exception ex)
                    {
                        _db = null;
                        _bridge.PushEvent(new { @event = "dbOpenError", error = ex.Message });
                        return;
                    }
                    string escaped = dlg.FileName.Replace("\\", "\\\\");
                    _bridge.PushEvent(new { @event = "dbPathPicked", path = dlg.FileName });
                    OnDbPathPicked?.Invoke(dlg.FileName);
                }
            }));
        }

        /// <summary>
        /// Parses the "params" array from a JSON message into a typed object array.
        /// Public so AppViewer can reuse it for dict SQL handlers.
        /// </summary>
        public static object[] ParseParamsStatic(JsonElement root)
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
