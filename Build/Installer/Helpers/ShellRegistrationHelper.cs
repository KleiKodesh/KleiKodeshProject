using KleiKodesh.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Registers / unregisters כתבי הקודש as an "Open With" shell handler for its
    /// supported file types. All writes go to HKCU\Software\Classes — no elevation needed.
    ///
    /// Registry layout (mirrors ShellRegistration.cs in KitveiHakodeshLib):
    ///
    ///   HKCU\Software\Classes\KitveiHakodesh.Document.1
    ///     (Default)  = "כתבי הקודש"
    ///     shell\open\command
    ///       (Default) = "\"<exe>\" \"%1\""
    ///
    ///   HKCU\Software\Classes\Applications\כתבי הקודש.exe
    ///     FriendlyAppName = "כתבי הקודש"
    ///     SupportedTypes\.<ext>  (REG_SZ, empty) for each supported extension
    ///     shell\open\command
    ///       (Default) = "\"<exe>\" \"%1\""
    ///
    ///   For each supported extension:
    ///   HKCU\Software\Classes\.<ext>\OpenWithProgids
    ///     KitveiHakodesh.Document.1  (REG_BINARY, empty)
    ///
    /// The preference (checked/unchecked) is persisted via SettingsManager so that
    /// re-running the installer restores the user's previous choice.
    /// </summary>
    public static class ShellRegistrationHelper
    {
        private const string ProgId       = "KitveiHakodesh.Document.1";
        private const string FriendlyName = "כתבי הקודש";
        private const string ExeName      = "כתבי הקודש.exe";
        private const string SettingSection = "KitveiHakodesh";
        private const string SettingKey     = "ShellRegistered";

        private static readonly string[] SupportedExtensions =
            { ".pdf", ".doc", ".docx", ".rtf", ".txt", ".htm", ".html" };

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
        private const int  SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        /// <summary>
        /// Returns the saved preference (defaults to true on fresh install so the
        /// checkbox starts checked, matching the previous auto-register behaviour).
        /// </summary>
        public static bool LoadPreference() =>
            SettingsManager.GetBool(SettingSection, SettingKey, defaultValue: true);

        /// <summary>
        /// Persists the user's choice and applies it immediately (register or unregister).
        /// </summary>
        public static void Apply(bool register)
        {
            SettingsManager.Save(SettingSection, SettingKey, register);

            string exePath = Path.Combine(AddinInstaller.InstallPath, ExeName);

            if (register)
                Register(exePath);
            else
                Unregister();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static void Register(string exePath)
        {
            try
            {
                string command = "\"" + exePath + "\" \"%1\"";

                using (var classesRoot = OpenClassesRoot(writable: true))
                {
                    RegisterProgId(classesRoot, command);
                    RegisterApplicationsEntry(classesRoot, command);
                    RegisterExtensions(classesRoot);
                }

                NotifyShell();
            }
            catch { /* non-fatal */ }
        }

        private static void Unregister()
        {
            try
            {
                using (var classesRoot = OpenClassesRoot(writable: true))
                {
                    if (classesRoot == null) return;
                    TryDelete(classesRoot, ProgId);
                    TryDelete(classesRoot, @"Applications\" + ExeName);
                    RemoveFromExtensions(classesRoot);
                }

                NotifyShell();
            }
            catch { /* non-fatal */ }
        }

        private static RegistryKey OpenClassesRoot(bool writable) =>
            Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        private static void RegisterProgId(RegistryKey classesRoot, string command)
        {
            using (var key = classesRoot.CreateSubKey(ProgId))
            {
                key.SetValue("", FriendlyName);
                using (var cmd = key.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue("", command);
            }
        }

        private static void RegisterApplicationsEntry(RegistryKey classesRoot, string command)
        {
            using (var appKey = classesRoot.CreateSubKey(@"Applications\" + ExeName))
            {
                appKey.SetValue("FriendlyAppName", FriendlyName);
                using (var types = appKey.CreateSubKey("SupportedTypes"))
                    foreach (var ext in SupportedExtensions)
                        types.SetValue(ext, "");
                using (var cmd = appKey.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue("", command);
            }
        }

        private static void RegisterExtensions(RegistryKey classesRoot)
        {
            foreach (var ext in SupportedExtensions)
                using (var progids = classesRoot.CreateSubKey(ext + @"\OpenWithProgids"))
                    progids.SetValue(ProgId, new byte[0], RegistryValueKind.Binary);
        }

        private static void RemoveFromExtensions(RegistryKey classesRoot)
        {
            foreach (var ext in SupportedExtensions)
                using (var progids = classesRoot.OpenSubKey(ext + @"\OpenWithProgids", writable: true))
                    try { progids?.DeleteValue(ProgId, throwOnMissingValue: false); } catch { }
        }

        private static void TryDelete(RegistryKey parent, string subKey)
        {
            try { parent.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); } catch { }
        }

        private static void NotifyShell()
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
            catch { }
        }
    }
}
