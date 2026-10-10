using System;
using CoreSettings = KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshLib.Settings
{
    /// <summary>
    /// The hosted app's names for its persisted settings. Every one of them is Core's
    /// <see cref="CoreSettings.AppSettingsRegistry"/> underneath.
    ///
    /// This used to be a second implementation: the same registry keys, the same defaults, the
    /// same "True"/"False" casing for the shared VSTO value, reached through
    /// Microsoft.VisualBasic's Interaction helpers instead of Core's raw Registry access. Two
    /// implementations of one store is how the two drift — and one already had: this side read
    /// the popout bounds with int.Parse, which THROWS on a corrupt or empty registry value,
    /// where Core uses TryParse and falls back. That crash is gone with the duplicate.
    ///
    /// What stays is the naming. The app says LoadDarkMode where Core says GetDarkMode, and
    /// roughly forty call sites across this library and the demo app read better for it, so the
    /// facade is kept and the bodies delegate.
    /// </summary>
    public static class AppSettings
    {
        // ── Database path ─────────────────────────────────────────────────────────

        /// <summary>
        /// The default seforim database: the known install locations probed in priority order,
        /// Otzaria first, then Zayit. Returns the first that exists, or the Otzaria path as the
        /// ultimate fallback so the settings UI still shows something meaningful when neither
        /// is installed.
        ///
        /// IGNORES any saved user choice, as the name says. Core's Resolve() would honour one,
        /// which is right for "which database should I open" but wrong here: the caller is
        /// asking what the default WOULD be. Probing directly keeps that true no matter when it
        /// is called, rather than only while the setting happens to be cleared.
        /// </summary>
        public static string ResolveDefaultDbPath()
        {
            string[] defaults = CoreSettings.SeforimDbPathResolver.DefaultLocations();
            foreach (string candidate in defaults)
            {
                try { if (System.IO.File.Exists(candidate)) return candidate; }
                catch { /* unreachable drive — treat as absent and keep probing */ }
            }

            // Nothing installed. Return the first location anyway (Otzaria, the primary
            // supported source) so the settings UI has a meaningful path to show.
            return defaults.Length > 0 ? defaults[0] : "";
        }

        /// <summary>
        /// The database to use: the user's explicit choice when they made one, otherwise the
        /// probe. An empty stored value means "no choice" and falls back, same as a missing
        /// one — which is the state <see cref="ClearDbPath"/> leaves behind.
        /// </summary>
        public static string LoadDbPath()
        {
            string saved = CoreSettings.SeforimDbPathResolver.GetSavedPath();
            return string.IsNullOrWhiteSpace(saved) ? ResolveDefaultDbPath() : saved;
        }

        public static void SaveDbPath(string path) =>
            CoreSettings.SeforimDbPathResolver.SavePath(path);

        /// <summary>
        /// Forgets the user's explicit database choice, so <see cref="LoadDbPath"/> falls back
        /// to the probe and Otzaria-first decides again.
        ///
        /// Resetting must CLEAR this rather than save the probed path back: a stored value reads
        /// as a deliberate choice everywhere else (the resolver returns it without probing, and
        /// the settings UI shows the path as custom), so writing the default into it would pin
        /// whichever library happened to win today and stop any later probe.
        /// </summary>
        public static void ClearDbPath() =>
            CoreSettings.SeforimDbPathResolver.ClearSavedPath();

        // ── Window state ──────────────────────────────────────────────────────────

        public static void SaveMainWindowMaximized(bool isMaximized) =>
            CoreSettings.AppSettingsRegistry.SetMainWindowMaximized(isMaximized);

        public static bool LoadMainWindowMaximized() =>
            CoreSettings.AppSettingsRegistry.GetMainWindowMaximized();

        public static System.Drawing.Rectangle LoadPopoutBounds()
        {
            var (x, y, width, height) = CoreSettings.AppSettingsRegistry.GetPopoutBounds();
            return new System.Drawing.Rectangle(x, y, width, height);
        }

        public static void SavePopoutBounds(System.Drawing.Rectangle bounds) =>
            CoreSettings.AppSettingsRegistry.SetPopoutBounds(
                bounds.X, bounds.Y, bounds.Width, bounds.Height);

        // ── HebrewBooks ───────────────────────────────────────────────────────────

        public static DateTime LoadHbCsvLastUpdated() =>
            CoreSettings.AppSettingsRegistry.GetHebrewBooksCatalogUpdatedUtc();

        public static void SaveHbCsvLastUpdated(DateTime utcDate) =>
            CoreSettings.AppSettingsRegistry.SetHebrewBooksCatalogUpdatedUtc(utcDate);

        public static string LoadHbLocalFolder() =>
            CoreSettings.AppSettingsRegistry.GetHebrewBooksLocalFolder();

        public static void SaveHbLocalFolder(string path) =>
            CoreSettings.AppSettingsRegistry.SetHebrewBooksLocalFolder(path);

        // ── Automatic update check ────────────────────────────────────────────────
        //
        // Shared with the KleiKodesh Word VSTO add-in: both read and write the SAME registry
        // value, so one toggle governs the automatic update check in both. The app name is
        // "KleiKodesh", NOT "KitveiHakodesh", and the stored form is "True"/"False" so the
        // VSTO's bool.TryParse round-trips it. Core holds that detail now; changing it there
        // would fork the setting.

        /// <summary>True when the user has turned OFF the automatic update check.</summary>
        public static bool LoadTurnOffUpdates() =>
            CoreSettings.AppSettingsRegistry.GetTurnOffUpdates();

        public static void SaveTurnOffUpdates(bool turnedOff) =>
            CoreSettings.AppSettingsRegistry.SetTurnOffUpdates(turnedOff);

        // ── Appearance ────────────────────────────────────────────────────────────
        //
        // These mirror the Vue theme so the NATIVE chrome (title bar, tab strip) can be painted
        // correctly at startup, before the frontend has loaded and sent its first setTheme
        // message. Empty when no theme was ever sent.

        public static void SaveDarkMode(bool isDark) =>
            CoreSettings.AppSettingsRegistry.SetDarkMode(isDark);

        public static bool LoadDarkMode() =>
            CoreSettings.AppSettingsRegistry.GetDarkMode();

        /// <summary>The Vue theme's title-bar background colour (hex, e.g. "#2d2d2d").</summary>
        public static void SaveChromeColor(string hex) =>
            CoreSettings.AppSettingsRegistry.SetChromeColor(hex ?? "");

        public static string LoadChromeColor() =>
            CoreSettings.AppSettingsRegistry.GetChromeColor();

        /// <summary>Accent colour (hex) — the native tab-list dropdown's active indicator.</summary>
        public static void SaveAccentColor(string hex) =>
            CoreSettings.AppSettingsRegistry.SetAccentColor(hex ?? "");

        public static string LoadAccentColor() =>
            CoreSettings.AppSettingsRegistry.GetAccentColor();

        /// <summary>Border colour (hex) — the native strip's split divider.</summary>
        public static void SaveBorderColor(string hex) =>
            CoreSettings.AppSettingsRegistry.SetBorderColor(hex ?? "");

        public static string LoadBorderColor() =>
            CoreSettings.AppSettingsRegistry.GetBorderColor();
    }
}
