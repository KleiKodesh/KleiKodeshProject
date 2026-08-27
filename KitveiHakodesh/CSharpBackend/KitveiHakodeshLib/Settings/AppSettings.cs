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
        ///   1. Otzaria   — %AppData%\otzaria\books\seforim.db
        ///   2. ZayitApp  — %AppData%\io.github.kdroidfilter.seforimapp\databases\seforim.db
        /// Returns the first path that exists on disk, or the Otzaria path as the
        /// ultimate fallback (so the UI shows a meaningful default even if neither is installed).
        /// </summary>
        public static string ResolveDefaultDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");
            string zayit  = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");

            if (File.Exists(otzaria)) return otzaria;
            if (File.Exists(zayit))   return zayit;

            return otzaria; // fallback — Otzaria is the primary supported source
        }

        // ── Persisted settings ────────────────────────────────────────────────────

        public static string LoadDbPath()
        {
            // An EMPTY stored value means "no choice" and must fall back to the probe, same as
            // a missing one. GetSetting only substitutes its default when the value is absent,
            // so a cleared setting comes back as "" and would otherwise be handed to callers as
            // a database path — which is how ClearDbPath leaves it.
            string saved = Interaction.GetSetting("KitveiHakodesh", "Database", "Path", "");
            return string.IsNullOrWhiteSpace(saved) ? ResolveDefaultDbPath() : saved;
        }

        public static void SaveDbPath(string path)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Database", "Path", path);
        }

        /// <summary>
        /// Forgets the user's explicit database choice, so LoadDbPath falls back to
        /// ResolveDefaultDbPath and the Otzaria-first probe decides again.
        ///
        /// Resetting must CLEAR this rather than save the probed path back: a stored value
        /// reads as a deliberate choice everywhere else (the resolver returns it without
        /// probing, and the settings UI shows the path as custom), so writing the default
        /// into it pins whichever library happened to win today and stops any later probe.
        /// </summary>
        public static void ClearDbPath()
        {
            Interaction.SaveSetting("KitveiHakodesh", "Database", "Path", "");
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

        // ── Automatic update check ─────────────────────────────────────────────────
        //
        // Shared with the KleiKodesh Word VSTO add-in: both apps read/write the SAME
        // registry value so a single toggle governs the automatic update check in both.
        // The VSTO writes it via SettingsManager.Save("UpdateChecker","TurnOffUpdates",...)
        // which resolves to app name "KleiKodesh" (NOT "KitveiHakodesh"). We deliberately
        // pass "KleiKodesh" here so we hit the exact same key:
        //   HKCU\Software\VB and VBA Program Settings\KleiKodesh\UpdateChecker\TurnOffUpdates
        // Do not change the app name / section / key — that would fork the setting.
        private const string UpdateCheckerAppName = "KleiKodesh";

        /// <summary>
        /// True when the user has turned OFF the automatic update check.
        /// Reads the shared VSTO key so the Word add-in and the standalone app agree.
        /// </summary>
        public static bool LoadTurnOffUpdates()
        {
            return string.Equals(
                Interaction.GetSetting(UpdateCheckerAppName, "UpdateChecker", "TurnOffUpdates", "False"),
                "True",
                StringComparison.OrdinalIgnoreCase);
        }

        public static void SaveTurnOffUpdates(bool turnedOff)
        {
            // Store the same "True"/"False" string the VSTO's bool.ToString() produces,
            // so SettingsManager.GetBool()'s bool.TryParse round-trips it correctly.
            Interaction.SaveSetting(UpdateCheckerAppName, "UpdateChecker", "TurnOffUpdates", turnedOff ? "True" : "False");
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

        /// <summary>Vue theme border color (hex) — used by the native strip's split divider.</summary>
        public static void SaveBorderColor(string hex)
        {
            Interaction.SaveSetting("KitveiHakodesh", "Appearance", "BorderColor", hex ?? "");
        }

        public static string LoadBorderColor()
        {
            return Interaction.GetSetting("KitveiHakodesh", "Appearance", "BorderColor", "");
        }
    }
}

