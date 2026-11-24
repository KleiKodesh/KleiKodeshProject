using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// "The user asked for the library and it is not here yet." Set by the app's download
    /// button or the installer's checkbox; cleared only when seforim.db is in place.
    ///
    /// It lives in the registry, not in memory, so a download survives the service stopping,
    /// a reboot, or the installer finishing before the service ever ran: whoever starts next
    /// sees the flag and resumes. The installer (net48, no Core reference) writes this exact
    /// value — Database\DownloadPending = "1" — so keep the two in step.
    /// </summary>
    public static class SeforimLibraryDownloadPendingFlag
    {
        private const string RegistryApp = "KitveiHakodesh";
        private const string RegistrySection = "Database";
        private const string RegistryValueName = "DownloadPending";

        public static bool IsSet =>
            AppSettingsRegistry.Get(RegistryApp, RegistrySection, RegistryValueName, "") == "1";

        public static void Set() =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, "1");

        public static void Clear() =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, "");
    }
}
