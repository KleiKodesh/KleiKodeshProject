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
        // No connection is held here any more. This class kept a Dapper DbAccess open only to
        // serve the raw-SQL passthrough the page no longer uses — seforim reads go through
        // SeforimQueryHandler and Core. What is still needed is the CHECK that a chosen file
        // is a usable database before its path is saved, and Core answers that without
        // holding anything open.
        // Full detail of a startup DB-open failure. The constructor runs before the page
        // loads (no bridge listener yet), so the error is held here and attached to the
        // first "No database loaded" reply — that puts the real cause in the F12 console
        // instead of leaving it in Debug output nobody sees.
        private string _startupOpenError;

        public Action<string> OnDbPathPicked { get; set; }

        public DbHandler(WebBridge bridge, WebView2 webView, string savedPath)
        {
            _bridge = bridge;
            if (File.Exists(savedPath))
            {
                if (!CanOpen(savedPath, out string startupError))
                {
                    _startupOpenError = "Opening the seforim database failed at startup for \""
                        + savedPath + "\":\n" + startupError;
                    System.Diagnostics.Debug.WriteLine("[DbHandler] " + _startupOpenError);
                }
            }
        }

        /// <summary>True when a readable seforim database is configured.</summary>
        public bool IsReady => _startupOpenError == null;

        /// <summary>
        /// Whether this file opens as a seforim database. Used to refuse a path BEFORE it is
        /// saved, so a mistyped or corrupt file is reported when the user picks it rather than
        /// as a broken app on the next launch. Opens read-only and closes immediately.
        /// </summary>
        private static bool CanOpen(string path, out string error)
        {
            error = null;
            try
            {
                new KitveiHakodesh.Core.SeforimDb.SeforimDbQueries(path).GetAllCategories();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }
        // HandleSql is gone with the raw-SQL passthrough it served. The page names typed
        // operations now (SeforimQueryHandler), so nothing sends SQL text across the bridge.


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
            // CLEAR the setting rather than saving the resolved default into it. Saving turned
            // "no choice, use the default" into an explicit choice, which pinned whichever
            // library the probe happened to pick and stopped it ever probing again.
            AppSettings.ClearDbPath();
            string defaultPath = AppSettings.ResolveDefaultDbPath();
            if (File.Exists(defaultPath) && !CanOpen(defaultPath, out string defaultError))
            {
                // Full detail to the F12 console — this catch used to swallow silently.
                _bridge.PushEvent(new { @event = "dbOpenError",
                    error = "Opening the default seforim database failed for \""
                            + defaultPath + "\":\n" + defaultError });
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
            // Diagnostics for the "path won't change in the VSTO" reports: every failure
            // in this flow must reach the frontend with the FULL exception (type, message,
            // stack, inner exceptions — ex.ToString()), so an affected user's F12 console
            // shows exactly which step failed. ex.Message alone hid the failing frame.
            string path = root.GetProperty("path").GetString();
            if (!File.Exists(path))
            {
                _bridge.Reply(id, new { error = "קובץ לא נמצא: File.Exists returned false for \"" + path + "\"" });
                return;
            }
            AppSettings.SaveDbPath(path);
            // Checked BEFORE saving: a file that cannot be opened must not become the
            // persisted choice, or the app starts broken on the next launch.
            if (!CanOpen(path, out string chosenError))
            {
                _bridge.Reply(id, new { error = "Opening the seforim database failed for \""
                                                + path + "\":\n" + chosenError });
                return;
            }
            _bridge.Reply(id, new { path });
            OnDbPathPicked?.Invoke(path);
        }

        public void HandlePickDbPath(string id, Control owner)
        {
            owner.BeginInvoke(new Action(() =>
            {
                // Nothing awaits pickDbPath (fire-and-forget), so without this catch an
                // exception anywhere here — ShowDialog, SaveDbPath — is an unhandled
                // crash on the host UI thread and the user just sees a dead button.
                // Push the full exception so it lands in the F12 console instead.
                try
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Title    = "בחר קובץ מסד נתונים";
                        dlg.Filter   = "SQLite Database (*.db)|*.db|All files (*.*)|*.*";
                        dlg.FileName = AppSettings.LoadDbPath();
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        AppSettings.SaveDbPath(dlg.FileName);
                        if (!CanOpen(dlg.FileName, out string pickedError))
                        {
                            _bridge.PushEvent(new { @event = "dbOpenError",
                                error = "Opening the seforim database failed for \""
                                        + dlg.FileName + "\":\n" + pickedError });
                            return;
                        }
                        _bridge.PushEvent(new { @event = "dbPathPicked", path = dlg.FileName });
                        OnDbPathPicked?.Invoke(dlg.FileName);
                    }
                }
                catch (Exception ex)
                {
                    _bridge.PushEvent(new { @event = "dbOpenError", error = "pickDbPath failed:\n" + ex });
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
