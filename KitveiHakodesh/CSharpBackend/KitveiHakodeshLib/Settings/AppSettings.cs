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

        public static void SaveMainWindowMaximized(bool isMaximized)
        {
            Interaction.SaveSetting("KitveiHakodesh", "MainWindow", "Maximized", isMaximized ? "1" : "0");
        }

        public static bool LoadMainWindowMaximized()
        {
            return Interaction.GetSetting("KitveiHakodesh", "MainWindow", "Maximized", "0") == "1";
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


        public static string LoadHbLocalFolder()
        {
            return Interaction.GetSetting("KitveiHakodesh", "HebrewBooks", "LocalFolder", "");
        }

        public static void SaveHbLocalFolder(string path)
        {
            Interaction.SaveSetting("KitveiHakodesh", "HebrewBooks", "LocalFolder", path);
        }

        public static void SaveHbCsvLastUpdated(DateTime utcDate)
        {
            Interaction.SaveSetting("KitveiHakodesh", "HebrewBooks", "CsvLastUpdated", utcDate.ToString("o"));
        }

        // ── Dark mode ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Persists whether the app is currently in dark mode.
        /// Stored as "1" (dark) or "0" (light) so the host window title bar can be
        /// themed correctly before the Vue app loads and sends its first setTheme message.
        /// </summary>
        public static void SaveDarkMode(bool isDark)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Appearance", "DarkMode", isDark ? "1" : "0");
        }

        public static bool LoadDarkMode()
        {
            return Interaction.GetSetting("KitveiHakodesh", "Appearance", "DarkMode", "0") == "1";
        }

        /// <summary>
        /// Persists the Vue theme's title-bar background color (hex, e.g. "#2d2d2d") so the
        /// native chrome tab strip can be themed correctly before the Vue app loads and
        /// sends its first setTheme message. Empty when no theme was ever sent.
        /// </summary>
        public static void SaveChromeColor(string hex)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Appearance", "ChromeColor", hex ?? "");
        }

        public static string LoadChromeColor()
        {
            return Interaction.GetSetting("KitveiHakodesh", "Appearance", "ChromeColor", "");
        }

        /// <summary>Vue theme accent color (hex) — used by the native tab-list dropdown's active indicator.</summary>
        public static void SaveAccentColor(string hex)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Appearance", "AccentColor", hex ?? "");
        }

        public static string LoadAccentColor()
        {
            return Interaction.GetSetting("KitveiHakodesh", "Appearance", "AccentColor", "");
        }
    }
}

