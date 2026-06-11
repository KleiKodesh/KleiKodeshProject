using KitveiHakodeshLib.Bridge;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.UserSettings
{
    /// <summary>
    /// Handles all bridge actions for user annotations (highlights and notes).
    ///
    /// Bridge actions handled:
    ///   userSettingsQuery        — SELECT against user_settings.db
    ///   userSettingsExecute      — INSERT / UPDATE / DELETE against user_settings.db
    ///
    /// The underlying database is re-opened whenever the seforim DB path changes
    /// via UpdateSeforimDbPath(). The database file is derived automatically:
    ///   {seforimDbFolder}/Settings/user_settings.db
    ///
    /// DB path change flow (called from DbHandler when the user picks a new path):
    ///   1. Derive old and new user settings paths.
    ///   2. If the new path already has a user_settings.db, ask the user whether to
    ///      copy their existing data over (merge) or keep the existing one.
    ///   3. If the old path had a user_settings.db and the user agrees, copy the file.
    ///   4. Open (or create) the user_settings.db at the new location.
    ///   5. Inform the frontend via a push event.
    /// </summary>
    public class UserSettingsDbHandler
    {
        private readonly WebBridge _bridge;
        private readonly Control _owner;
        private UserSettingsDbAccess _db;

        public UserSettingsDbHandler(WebBridge bridge, Control owner, string seforimDbPath)
        {
            _bridge = bridge;
            _owner = owner;
            if (!string.IsNullOrEmpty(seforimDbPath) && File.Exists(seforimDbPath))
                _db = new UserSettingsDbAccess(seforimDbPath);
        }

        // ── Bridge action handlers ────────────────────────────────────────────────

        public async Task HandleQuery(JsonElement root, string id)
        {
            if (_db == null) { _bridge.Reply(id, new { error = "User settings database not loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _db.Query(sql, _ParseParams(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public async Task HandleExecute(JsonElement root, string id)
        {
            if (_db == null) { _bridge.Reply(id, new { error = "User settings database not loaded" }); return; }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                long lastInsertId = await Task.Run(() => _db.Execute(sql, _ParseParams(root)));
                _bridge.Reply(id, new { lastInsertId });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        // ── DB path change ────────────────────────────────────────────────────────

        /// <summary>
        /// Called when the user changes the seforim DB path.
        /// Shows a confirmation dialog if there is existing annotation data,
        /// optionally copies the data to the new location, then re-opens the DB.
        /// Must be called from the UI thread (or via BeginInvoke) because it may
        /// show a MessageBox.
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
            string oldUserSettingsPath = _db?.Path;
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
                {
                    _CopyUserSettingsDb(oldUserSettingsPath, newUserSettingsPath);
                }
            }

            // Close the old connection before opening the new one.
            _db?.Dispose();
            _db = null;

            try
            {
                _db = new UserSettingsDbAccess(newSeforimDbPath);
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

        public void Dispose()
        {
            _db?.Dispose();
            _db = null;
        }
    }
}
