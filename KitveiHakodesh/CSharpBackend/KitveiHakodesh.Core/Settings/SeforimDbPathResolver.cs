using System;
using System.IO;

namespace KitveiHakodesh.Core.Settings
{
    /// <summary>
    /// Answers where the seforim library database is, and remembers the user's choice.
    ///
    /// Order, and every step VERIFIES THE FILE IS ACTUALLY THERE:
    ///
    ///   1. the registry value the user picked — if that file still exists
    ///   2. the known install locations, Otzaria then Zayit — first one that exists
    ///   3. nothing found; say so
    ///
    /// Both checks in steps 1 and 2 are the point. The old code returned the registry value
    /// without testing it, so a moved or deleted library produced a stale path that failed
    /// much later somewhere less obvious; and its default probe returned the Zayit path even
    /// when neither app was installed — handing back a path known not to exist. A resolver
    /// that reports success for a missing file is worse than one that reports nothing.
    ///
    /// Step 3 returns <see cref="SeforimDbLocation.NotFound"/>. It does not throw and does
    /// not invent a path: Core reports, the host decides what it means — the hosted app opens
    /// its setup wizard, dev surfaces an error.
    ///
    /// There is no DB_PATH environment variable. It used to sit between steps 1 and 2, but the
    /// registry is checked first and returns on a hit, so it only ever fired on a machine that
    /// had never been configured — where it fell back to an empty placeholder file anyway.
    /// A developer changes the setting in the app, exactly like a user.
    /// </summary>
    public static class SeforimDbPathResolver
    {
        /// <summary>
        /// The registry key holding the user's choice. Public because the database-change
        /// watcher subscribes to it: the hosted app writes this value DIRECTLY rather than
        /// through the service, so the service has to notice it changing underneath itself.
        /// </summary>
        public const string RegistryKeyPath =
            @"Software\VB and VBA Program Settings\KitveiHakodesh\Database";

        private const string RegistryApp = "KitveiHakodesh";
        private const string RegistrySection = "Database";
        private const string RegistryValueName = "Path";

        /// <summary>Where a seforim.db was found, and how.</summary>
        public readonly struct SeforimDbLocation
        {
            /// <summary>Nothing usable was found anywhere.</summary>
            public static readonly SeforimDbLocation NotFound = default;

            private SeforimDbLocation(string path, bool isUserChoice)
            {
                Path = path;
                IsUserChoice = isUserChoice;
            }

            /// <summary>Full path to an existing seforim.db, or null when nothing was found.</summary>
            public string? Path { get; }

            /// <summary>True when this came from the user's saved setting rather than a probe.</summary>
            public bool IsUserChoice { get; }

            /// <summary>True when a usable database was found.</summary>
            public bool Found => !string.IsNullOrEmpty(Path);

            internal static SeforimDbLocation FromUserChoice(string path) => new(path, true);
            internal static SeforimDbLocation FromProbe(string path) => new(path, false);
        }

        /// <summary>
        /// Finds the database. See the class remarks for the order and why each step verifies.
        /// </summary>
        public static SeforimDbLocation Resolve()
        {
            string? saved = GetSavedPath();
            if (!string.IsNullOrWhiteSpace(saved) && FileExists(saved!))
                return SeforimDbLocation.FromUserChoice(saved!);

            foreach (string candidate in DefaultLocations())
            {
                if (FileExists(candidate))
                    return SeforimDbLocation.FromProbe(candidate);
            }

            return SeforimDbLocation.NotFound;
        }

        /// <summary>
        /// True when the user has explicitly chosen a database AND that file is still there.
        /// A saved path pointing at a file that has since gone is NOT a custom choice — the
        /// caller would otherwise trust a setting that cannot be honoured.
        /// </summary>
        public static bool HasUserChoice()
        {
            string? saved = GetSavedPath();
            return !string.IsNullOrWhiteSpace(saved) && FileExists(saved!);
        }

        /// <summary>The saved path exactly as stored — no existence check, no fallback.
        /// For code that needs to show or clear the setting rather than use it.</summary>
        public static string? GetSavedPath()
        {
            string value = AppSettingsRegistry.Get(RegistryApp, RegistrySection, RegistryValueName, "");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>Records the user's choice.</summary>
        public static void SavePath(string path) =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, path);

        /// <summary>Forgets the user's choice, so resolution falls back to probing.</summary>
        public static void ClearSavedPath() =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, "");

        /// <summary>
        /// The install locations worth probing, in order. Otzaria first, then Zayit —
        /// Otzaria is the primary supported source, so it wins when both are installed.
        /// This is the order the hosted app (AppSettings.ResolveDefaultDbPath) and the
        /// service (SeforimDbLocator.ResolveDefaultDbPath) already probe in; Core had it
        /// reversed, which made a settings reset settle on Zayit when both were present.
        /// </summary>
        public static string[] DefaultLocations()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new[]
            {
                Path.Combine(appData, "otzaria", "books", "seforim.db"),
                Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db"),
            };
        }

        private static bool FileExists(string path)
        {
            // A saved path can point at a disconnected network drive or a removed USB stick,
            // where File.Exists throws rather than returning false.
            try { return File.Exists(path); }
            catch { return false; }
        }
    }
}
