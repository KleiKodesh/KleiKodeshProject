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

        // ── Shared WebView2 browser process + profiles ──────────────────────────────
        //
        // Both Kitvei Hakodesh hosts — the standalone app and the Word (VSTO) add-in —
        // render through AppViewer, which creates its WebView2 environment with an
        // identical set of CoreWebView2EnvironmentOptions. They ALWAYS point at ONE
        // shared user-data folder (see SharedUserDataFolder), so they run in a SINGLE
        // shared browser process. Per Microsoft's WebView2 docs this "optimizes system
        // resources by running in one browser process" — one browser/GPU/utility process
        // instead of a separate set per host — and lets both run at the same time.
        //
        // What the user CAN choose is whether the two hosts share the same *profile*
        // (CoreWebView2ControllerOptions.ProfileName) under that shared folder:
        //   ShareProfile = true  → both use one profile → shared cookies, localStorage,
        //                          browser cache, and login state.
        //   ShareProfile = false → each host uses its own profile → browser data is
        //                          isolated, but they still share the one browser process.
        // Multiple profiles under one user-data folder is the documented, supported model
        // (and the memory saving is identical either way — it comes from the shared
        // browser process, not from sharing a profile).
        //
        // Stored under the SHARED "KleiKodesh" app name (NOT "KitveiHakodesh"), exactly
        // like TurnOffUpdates, so the standalone app and the Word add-in read/write the
        // SAME value and one toggle governs both:
        //   HKCU\Software\VB and VBA Program Settings\KleiKodesh\WebView\ShareProfile
        private const string WebViewAppName = "KleiKodesh";

        /// <summary>
        /// True when the two hosts should share ONE WebView2 profile (shared browser data).
        /// False (default) = each host uses its own profile (isolated data). Either way the
        /// hosts share one browser process. Reads the shared VSTO key so both apps agree.
        /// </summary>
        public static bool LoadShareProfile()
        {
            return string.Equals(
                Interaction.GetSetting(WebViewAppName, "WebView", "ShareProfile", "False"),
                "True",
                StringComparison.OrdinalIgnoreCase);
        }

        public static void SaveShareProfile(bool shared)
        {
            Interaction.SaveSetting(WebViewAppName, "WebView", "ShareProfile", shared ? "True" : "False");
        }

        /// <summary>
        /// The WebView2 user-data folder shared by ALL Kitvei Hakodesh hosts (standalone
        /// app + Word add-in) so they run in one browser process.
        ///
        /// Resolved RELATIVE TO THE RUNNING APP (AppDomain.CurrentDomain.BaseDirectory), so
        /// it ALWAYS lives inside the install folder and is removed in one sweep when the
        /// install folder is deleted or the app is uninstalled — no matter where the app is
        /// installed. Sharing works because the installed product is a FLAT layout: the
        /// standalone exe (כתבי הקודש.exe) and the VSTO add-in assembly both sit directly in
        /// %LocalAppData%\KleiKodesh, so BaseDirectory is the SAME folder in both processes
        /// and they resolve this to the identical path. (In dev they run from separate
        /// bin\Debug folders and simply get separate caches — harmless.) BaseDirectory is
        /// per-user writable here, which WebView2 requires — never point a UDF at a
        /// write-protected install dir such as %ProgramFiles%.
        ///
        /// The "KitveiHakodesh" subfolder matches AppViewer.AppDir (where the Vue frontend
        /// lives) and, in the Word process, keeps this distinct from the add-in's OTHER
        /// webview KleiKodeshWebView (…\KleiKodesh\WebView2Cache), which uses different
        /// environment options — sharing one folder across mismatched options would clash
        /// (ERROR_INVALID_STATE). Keep them in separate subfolders.
        /// </summary>
        public static string SharedUserDataFolder()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "WebView2Cache");
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

