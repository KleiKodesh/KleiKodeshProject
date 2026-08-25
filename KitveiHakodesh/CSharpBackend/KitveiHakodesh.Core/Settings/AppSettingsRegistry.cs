using System;
using System.Globalization;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.Settings
{
    /// <summary>
    /// The app's own preferences, in the Windows registry.
    ///
    /// Layout is fixed by history: the net4.8 code wrote these through VB's
    /// <c>Interaction.GetSetting/SaveSetting</c>, which store under
    /// <c>HKCU\Software\VB and VBA Program Settings\{app}\{section}</c> with the key as the
    /// value name. This mirrors that byte for byte, so the hosted app, the add-in, the demo
    /// app and the service all read one source of truth. Raw <see cref="Registry"/> rather
    /// than VB Interaction, which is net48-only and would not compile on the modern leg.
    ///
    /// Do NOT change an (app, section, key) tuple: an old install keeps writing the old
    /// location and the setting silently forks.
    ///
    /// NOT here: the seforim database path — that belongs to SeforimDbPathResolver, which
    /// has to verify the file exists and fall back to probing, neither of which is a plain
    /// get/set. It uses Get/Set below for the storage half.
    /// </summary>
    public static class AppSettingsRegistry
    {
        private const string AppKitveiHakodesh = "KitveiHakodesh";

        /// <summary>
        /// The automatic-update toggle is shared with the KleiKodesh Word add-in, whose
        /// SettingsManager writes it under app name "KleiKodesh" — NOT "KitveiHakodesh".
        /// One toggle governs both, so this deliberate mismatch must stay.
        /// </summary>
        private const string AppKleiKodesh = "KleiKodesh";

        private static string SubKey(string app, string section) =>
            $@"Software\VB and VBA Program Settings\{app}\{section}";

        // ── Primitives ────────────────────────────────────────────────────────────────

        /// <summary>Reads a value, returning <paramref name="fallback"/> when the key is
        /// missing or unreadable. Never throws — a settings read must not take the app down.</summary>
        public static string Get(string app, string section, string key, string fallback)
        {
            try
            {
                using var registryKey = Registry.CurrentUser.OpenSubKey(SubKey(app, section));
                return registryKey?.GetValue(key) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>Writes a value, creating the key path if needed.</summary>
        public static void Set(string app, string section, string key, string value)
        {
            using var registryKey = Registry.CurrentUser.CreateSubKey(SubKey(app, section));
            registryKey?.SetValue(key, value ?? "");
        }

        // ── HebrewBooks ───────────────────────────────────────────────────────────────

        public static string GetHebrewBooksLocalFolder() =>
            Get(AppKitveiHakodesh, "HebrewBooks", "LocalFolder", "");

        public static void SetHebrewBooksLocalFolder(string path) =>
            Set(AppKitveiHakodesh, "HebrewBooks", "LocalFolder", path);

        /// <summary>
        /// When the bundled HebrewBooks catalog was last refreshed, in UTC.
        /// <see cref="DateTime.MinValue"/> when it never has been.
        ///
        /// Parsed with InvariantCulture + RoundtripKind deliberately. The value is written
        /// with "o" (ISO-8601, UTC); a plain DateTime.Parse converts it to LOCAL time and
        /// drops Kind, so a UTC value read back would be wrong by the machine's offset and
        /// every "is it stale yet" comparison against UtcNow would be skewed.
        /// </summary>
        public static DateTime GetHebrewBooksCatalogUpdatedUtc()
        {
            string raw = Get(AppKitveiHakodesh, "HebrewBooks", "CsvLastUpdated", "");
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
        }

        /// <summary>
        /// Records a catalog refresh. The registry VALUE NAME stays "CsvLastUpdated" even
        /// though the catalog is SQLite now, not CSV — renaming it would silently reset the
        /// clock on every existing install and trigger an immediate re-scrape for everyone.
        /// </summary>
        public static void SetHebrewBooksCatalogUpdatedUtc(DateTime utc) =>
            Set(AppKitveiHakodesh, "HebrewBooks", "CsvLastUpdated",
                utc.ToString("o", CultureInfo.InvariantCulture));

        // ── Automatic update check (shared with the Word add-in) ──────────────────────

        /// <summary>True when the user has turned the automatic update check OFF.</summary>
        public static bool GetTurnOffUpdates() => string.Equals(
            Get(AppKleiKodesh, "UpdateChecker", "TurnOffUpdates", "False"),
            "True",
            StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Stores the exact "True"/"False" text bool.ToString() produces, because the add-in
        /// reads it back with bool.TryParse. Any other casing or wording breaks that.
        /// </summary>
        public static void SetTurnOffUpdates(bool turnedOff) =>
            Set(AppKleiKodesh, "UpdateChecker", "TurnOffUpdates", turnedOff ? "True" : "False");

        // ── Window state ──────────────────────────────────────────────────────────────

        public static bool GetMainWindowMaximized() =>
            Get(AppKitveiHakodesh, "MainWindow", "Maximized", "0") == "1";

        public static void SetMainWindowMaximized(bool maximized) =>
            Set(AppKitveiHakodesh, "MainWindow", "Maximized", maximized ? "1" : "0");

        /// <summary>
        /// Pop-out window bounds as four separate values. Core deals in ints; the host turns
        /// them into whatever rectangle type it uses (System.Drawing on the WinForms side) —
        /// Core owns no UI types.
        ///
        /// Read with TryParse, not Parse. The registry fallback only applies when the value
        /// is MISSING; a value that exists but is empty or corrupt reaches the parser, and
        /// Parse would throw right through the caller. The defaults differ per field
        /// (-1 means "not positioned yet", the size defaults are a usable window).
        /// </summary>
        public static (int X, int Y, int Width, int Height) GetPopoutBounds() => (
            ReadInt("Popout", "X", -1),
            ReadInt("Popout", "Y", -1),
            ReadInt("Popout", "W", 900),
            ReadInt("Popout", "H", 750));

        public static void SetPopoutBounds(int x, int y, int width, int height)
        {
            WriteInt("Popout", "X", x);
            WriteInt("Popout", "Y", y);
            WriteInt("Popout", "W", width);
            WriteInt("Popout", "H", height);
        }

        // ── Appearance ────────────────────────────────────────────────────────────────
        //
        // These mirror the Vue theme so the NATIVE chrome (title bar, tab strip) can be
        // painted correctly at startup, before the frontend has loaded and sent its first
        // setTheme message. Core only stores and returns them; it never interprets a colour.

        public static bool GetDarkMode() =>
            Get(AppKitveiHakodesh, "Appearance", "DarkMode", "0") == "1";

        public static void SetDarkMode(bool isDark) =>
            Set(AppKitveiHakodesh, "Appearance", "DarkMode", isDark ? "1" : "0");

        public static string GetChromeColor() => Get(AppKitveiHakodesh, "Appearance", "ChromeColor", "");
        public static void SetChromeColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "ChromeColor", hex);

        public static string GetAccentColor() => Get(AppKitveiHakodesh, "Appearance", "AccentColor", "");
        public static void SetAccentColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "AccentColor", hex);

        public static string GetBorderColor() => Get(AppKitveiHakodesh, "Appearance", "BorderColor", "");
        public static void SetBorderColor(string hex) => Set(AppKitveiHakodesh, "Appearance", "BorderColor", hex);

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private static int ReadInt(string section, string key, int fallback) =>
            int.TryParse(Get(AppKitveiHakodesh, section, key, ""),
                         NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;

        private static void WriteInt(string section, string key, int value) =>
            Set(AppKitveiHakodesh, section, key, value.ToString(CultureInfo.InvariantCulture));
    }
}
