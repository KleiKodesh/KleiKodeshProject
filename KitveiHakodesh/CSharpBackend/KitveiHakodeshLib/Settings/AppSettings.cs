using Microsoft.VisualBasic;
using System;
using System.IO;

namespace KitveiHakodeshLib.Settings
{
    /// <summary>
    /// Persists app settings to the Windows registry via VB Interaction helpers.
    /// </summary>
    public static class AppSettings
    {
        // ── Default DB path resolution ────────────────────────────────────────────

        /// <summary>
        /// Resolves the default seforim database path by probing known app locations
        /// in priority order:
        ///   1. ZayitApp  — %AppData%\io.github.kdroidfilter.seforimapp\databases\seforim.db
        ///   2. Otzaria   — %AppData%\otzaria\books\seforim.db
        /// Returns the first path that exists on disk, or the ZayitApp path as the
        /// ultimate fallback (so the UI shows a meaningful default even if neither is installed).
        /// </summary>
        public static string ResolveDefaultDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string zayit  = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");

            if (File.Exists(zayit))   return zayit;
            if (File.Exists(otzaria)) return otzaria;

            return zayit; // fallback — ZayitApp is the primary supported source
        }

        // ── Persisted settings ────────────────────────────────────────────────────

        public static string LoadDbPath()
        {
            return Interaction.GetSetting("KitveiHakodesh", "Database", "Path", ResolveDefaultDbPath());
        }

        public static void SaveDbPath(string path)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Database", "Path", path);
        }

        public static System.Drawing.Rectangle LoadPopoutBounds()
        {
            int x = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "X", "-1"));
            int y = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "Y", "-1"));
            int w = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "W", "900"));
            int h = int.Parse(Interaction.GetSetting("KitveiHakodesh", "Popout", "H", "750"));
            return new System.Drawing.Rectangle(x, y, w, h);
        }

        public static void SavePopoutBounds(System.Drawing.Rectangle bounds)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "X", bounds.X.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "Y", bounds.Y.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "W", bounds.Width.ToString());
            Interaction.SaveSetting("KitveiHakodesh", "Popout", "H", bounds.Height.ToString());
        }

        public static DateTime LoadHbCsvLastUpdated()
        {
            string raw = Interaction.GetSetting("KitveiHakodesh", "HebrewBooks", "CsvLastUpdated", "");
            if (DateTime.TryParse(raw, out DateTime dt)) return dt;
            return DateTime.MinValue;
        }

        public static void SaveHbCsvLastUpdated(DateTime utcDate)
        {
            Interaction.SaveSetting("KitveiHakodesh", "HebrewBooks", "CsvLastUpdated", utcDate.ToString("o"));
        }
    }
}
