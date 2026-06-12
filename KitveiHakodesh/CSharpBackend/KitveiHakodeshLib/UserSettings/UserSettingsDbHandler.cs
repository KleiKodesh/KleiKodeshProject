using KitveiHakodeshLib.Bridge;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.UserSettings
{
    /// <summary>
    /// Per-AppViewer routing layer for user annotation bridge actions.
    ///
    /// Bridge actions handled:
    ///   userSettingsQuery        — SELECT against user_settings.db
    ///   userSettingsExecute      — INSERT / UPDATE / DELETE against user_settings.db
    ///
    /// The actual SQLite connection lives in UserSettingsDbAccess.Current — a
    /// process-wide singleton shared by all AppViewer instances. This handler does
    /// NOT own the connection and must never dispose it.
    ///
    /// DB path change flow (called from AppViewer when the user picks a new DB path):
    ///   1. Derive the old and new user settings paths.
    ///   2. If the old path had annotation data, show a dialog asking the user whether
    ///      to carry the data over to the new location.
    ///   3. If the user agrees, copy the file.
    ///   4. Call UserSettingsDbAccess.Open() with the new seforim DB path — this
    ///      replaces the shared connection process-wide so all AppViewer instances
    ///      immediately start using the new database.
    ///   5. Push a "userSettingsDbReady" event to the frontend.
    ///
    /// Only the first AppViewer to open (or the one that triggers a path change)
    /// drives the dialog; all others share the resulting connection transparently.
    /// </summary>
    public class UserSettingsDbHandler
    {
        private readonly WebBridge _bridge;
        private readonly Control _owner;

        public UserSettingsDbHandler(WebBridge bridge, Control owner, string seforimDbPath)
        {
            _bridge = bridge;
            _owner = owner;

            // Open (or re-use) the shared singleton connection.
            // If another AppViewer already opened it at the same path this is a no-op.
            UserSettingsDbAccess.Open(seforimDbPath);
        }

        // ── Bridge action handlers ────────────────────────────────────────────────

        public async Task HandleQuery(JsonElement root, string id)
        {
            var db = UserSettingsDbAccess.Current;
            if (db == null) { _bridge.Reply(id, new { error = "User settings database not loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => db.Query(sql, _ParseParams(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public async Task HandleExecute(JsonElement root, string id)
        {
            var db = UserSettingsDbAccess.Current;
            if (db == null) { _bridge.Reply(id, new { error = "User settings database not loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                long lastInsertId = await Task.Run(() => db.Execute(sql, _ParseParams(root)));
                _bridge.Reply(id, new { lastInsertId });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        // ── DB path change ────────────────────────────────────────────────────────

        /// <summary>
        /// Called when the user changes the seforim DB path.
        /// Shows a confirmation dialog if there is existing annotation data,
        /// optionally copies the data to the new location, then re-points the shared
        /// connection to the new database. Safe to call from any AppViewer instance —
        /// all instances share the connection so they all pick up the change.
        /// Must be dispatched via BeginInvoke because it may show a MessageBox.
        /// </summary>
        public void UpdateSeforimDbPath(string newSeforimDbPath)
        {
            _owner.BeginInvoke(new Action(() =>
            {
                _HandleDbPathChange(newSeforimDbPath);
            }));
        }

        private void _HandleDbPathChange(string newSeforimDbPath)
        {
            string oldUserSettingsPath = UserSettingsDbAccess.Current?.Path;
            string newUserSettingsPath = UserSettingsDbAccess.DeriveUserSettingsDbPath(newSeforimDbPath);

            // Nothing to do if the path has not actually changed.
            if (string.Equals(oldUserSettingsPath, newUserSettingsPath, StringComparison.OrdinalIgnoreCase))
                return;

            bool oldHasData = !string.IsNullOrEmpty(oldUserSettingsPath) && File.Exists(oldUserSettingsPath);
            bool newAlreadyExists = File.Exists(newUserSettingsPath);

            if (oldHasData)
            {
                string message = newAlreadyExists
                    ? "שינית את נתיב מסד הנתונים.\n\n" +
                      "בנתיב החדש כבר קיים קובץ הערות ופנקס גישה (user_settings.db).\n" +
                      "האם לייבא את ההערות והסימונים מהנתיב הישן לנתיב החדש?\n\n" +
                      "⚠️ הנתונים הקיימים בנתיב החדש יוחלפו."
                    : "שינית את נתיב מסד הנתונים.\n\n" +
                      "יש לך הערות וסימונים שמורים בנתיב הישן.\n" +
                      "האם להעביר אותם לנתיב החדש?\n\n" +
                      "אם תבחר 'לא', ההערות הישנות לא יהיו זמינות.";

                var result = MessageBox.Show(
                    message,
                    "העברת הערות וסימונים",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);

                if (result == DialogResult.Yes)
                    _CopyUserSettingsDb(oldUserSettingsPath, newUserSettingsPath);
            }

            // Re-open the shared connection at the new path. This closes the old
            // connection and opens a new one, visible to all AppViewer instances.
            try
            {
                UserSettingsDbAccess.Open(newSeforimDbPath);
                _bridge.PushEvent(new { @event = "userSettingsDbReady", path = newUserSettingsPath });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "שגיאה בפתיחת מסד נתוני ההגדרות:\n" + ex.Message,
                    "שגיאה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void _CopyUserSettingsDb(string sourcePath, string destinationPath)
        {
            try
            {
                string destinationDir = System.IO.Path.GetDirectoryName(destinationPath);
                if (!Directory.Exists(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "שגיאה בהעתקת קובץ ההגדרות:\n" + ex.Message + "\n\n" +
                    "יש להעתיק ידנית:\n" + sourcePath + "\nאל:\n" + destinationPath,
                    "שגיאה בהעתקה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static object[] _ParseParams(JsonElement root)
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

        /// <summary>
        /// Per-instance dispose — does NOT touch the shared connection.
        /// The shared connection lives until process teardown via UserSettingsDbAccess.DisposeShared().
        /// </summary>
        public void Dispose()
        {
            // Nothing to dispose per-instance — the connection is shared.
        }
    }
}
