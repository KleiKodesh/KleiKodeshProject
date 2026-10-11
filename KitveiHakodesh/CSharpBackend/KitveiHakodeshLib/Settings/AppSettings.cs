using Microsoft.Win32;
using System;
using System.Globalization;

namespace KitveiHakodeshLib.Settings
{
    /// <summary>
    /// The HOST's persisted settings: window geometry and the native chrome's theme mirror,
    /// plus two shared values the host still reads (the HebrewBooks folder for "reveal in
    /// Explorer", and the update-check switch for the demo app's startup).
    ///
    /// Everything about the DATA - the seforim database path, the HebrewBooks folder as a
    /// setting, the update switch as a setting - is the KitveiHakodesh service's; the page
    /// reads and writes those through it. This class only touches the registry, under the
    /// same keys the service uses, so the two never disagree on a value they both read.
    /// </summary>
    public static class AppSettings
    {
        private const string AppKitveiHakodesh = "KitveiHakodesh";
        private const string AppKleiKodesh = "KleiKodesh";

        private static string SubKey(string app, string section) =>
            @"Software\VB and VBA Program Settings\" + app + @"\" + section;

        private static string Get(string app, string section, string key, string fallback)
        {
            try
            {
                using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(SubKey(app, section)))
                    return registryKey?.GetValue(key) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void Set(string app, string section, string key, string value)
        {
            using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(SubKey(app, section)))
                registryKey?.SetValue(key, value ?? "");
        }

        private static int ReadInt(string section, string key, int fallback) =>
            int.TryParse(Get(AppKitveiHakodesh, section, key, ""),
                         NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;

        private static void WriteInt(string section, string key, int value) =>
            Set(AppKitveiHakodesh, section, key, value.ToString(CultureInfo.InvariantCulture));

        // ── Window state ──────────────────────────────────────────────────────────

        public static void SaveMainWindowMaximized(bool isMaximized) =>
            Set(AppKitveiHakodesh, "MainWindow", "Maximized", isMaximized ? "1" : "0");

        public static bool LoadMainWindowMaximized() =>
            Get(AppKitveiHakodesh, "MainWindow", "Maximized", "0") == "1";

        public static System.Drawing.Rectangle LoadPopoutBounds() =>
            new System.Drawing.Rectangle(
                ReadInt("Popout", "X", -1), ReadInt("Popout", "Y", -1),
                ReadInt("Popout", "W", 900), ReadInt("Popout", "H", 750));

        public static void SavePopoutBounds(System.Drawing.Rectangle bounds)
        {
            WriteInt("Popout", "X", bounds.X);
            WriteInt("Popout", "Y", bounds.Y);
            WriteInt("Popout", "W", bounds.Width);
            WriteInt("Popout", "H", bounds.Height);
        }

        // ── Shared values the host still reads ────────────────────────────────────

        /// <summary>The HebrewBooks local folder (the service persists it; "reveal in
        /// Explorer" falls back to it when the page sends none).</summary>
        public static string LoadHbLocalFolder() =>
            Get(AppKitveiHakodesh, "HebrewBooks", "LocalFolder", "");

        /// <summary>True when the user has turned OFF the automatic update check. Shared with
        /// the KleiKodesh Word add-in: app name "KleiKodesh", stored as "True"/"False".</summary>
        public static bool LoadTurnOffUpdates() => string.Equals(
            Get(AppKleiKodesh, "UpdateChecker", "TurnOffUpdates", "False"), "True",
            StringComparison.OrdinalIgnoreCase);

        // ── Appearance ────────────────────────────────────────────────────────────
        //
        // These mirror the Vue theme so the NATIVE chrome (title bar, tab strip) can be painted
        // correctly at startup, before the frontend has loaded and sent its first setTheme
        // message. Empty when no theme was ever sent.

        public static void SaveDarkMode(bool isDark) =>
            Set(AppKitveiHakodesh, "Appearance", "DarkMode", isDark ? "1" : "0");

        public static bool LoadDarkMode() =>
            Get(AppKitveiHakodesh, "Appearance", "DarkMode", "0") == "1";

        /// <summary>The Vue theme's title-bar background colour (hex, e.g. "#2d2d2d").</summary>
        public static void SaveChromeColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "ChromeColor", hex ?? "");
        public static string LoadChromeColor() => Get(AppKitveiHakodesh, "Appearance", "ChromeColor", "");

        /// <summary>Accent colour (hex) - the native tab-list dropdown's active indicator.</summary>
        public static void SaveAccentColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "AccentColor", hex ?? "");
        public static string LoadAccentColor() => Get(AppKitveiHakodesh, "Appearance", "AccentColor", "");

        /// <summary>Border colour (hex) - the native strip's split divider.</summary>
        public static void SaveBorderColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "BorderColor", hex ?? "");
        public static string LoadBorderColor() => Get(AppKitveiHakodesh, "Appearance", "BorderColor", "");
    }
}
