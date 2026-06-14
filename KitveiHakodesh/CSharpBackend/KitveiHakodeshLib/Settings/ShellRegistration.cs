using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KitveiHakodeshLib.Settings
{
    /// <summary>
    /// Registers and unregisters the app as an "Open With" handler for all file types it
    /// supports. All writes go to HKCU\Software\Classes — no admin elevation required.
    ///
    /// Registry layout written (per-user, no admin):
    ///
    ///   HKCU\Software\Classes\KitveiHakodesh.Document.1
    ///     (Default)     = "כתבי הקודש"
    ///     shell\open\command
    ///       (Default)   = "\"<exe>\" \"%1\""
    ///
    ///   HKCU\Software\Classes\Applications\<exefilename>
    ///     FriendlyAppName = "כתבי הקודש"
    ///     SupportedTypes\
    ///       .pdf  (REG_SZ, empty)
    ///       .doc  ...
    ///       ...
    ///     shell\open\command
    ///       (Default) = "\"<exe>\" \"%1\""
    ///
    ///   For each supported extension:
    ///   HKCU\Software\Classes\<.ext>\OpenWithProgids
    ///     KitveiHakodesh.Document.1  (REG_NONE, empty)
    ///
    /// After writing, SHChangeNotify is called so Explorer picks up the change immediately.
    /// </summary>
    public static class ShellRegistration
    {
        private const string ProgId        = "KitveiHakodesh.Document.1";
        private const string FriendlyName  = "כתבי הקודש";

        private static readonly string[] SupportedExtensions =
        {
            ".pdf", ".doc", ".docx", ".rtf", ".txt", ".htm", ".html"
        };

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private const int    SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint   SHCNF_IDLIST       = 0x0000;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers the app as an "Open With" option for all supported file types.
        /// Idempotent — safe to call on every launch.
        /// </summary>
        public static void Register(string executablePath)
        {
            string exeFileName = Path.GetFileName(executablePath);
            string command     = "\"" + executablePath + "\" \"%1\"";

            using (RegistryKey classesRoot = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
            {
                RegisterProgId(classesRoot, command);
                RegisterApplicationsEntry(classesRoot, exeFileName, command);
                RegisterExtensions(classesRoot);
            }

            NotifyShell();
        }

        /// <summary>
        /// Removes all registry entries written by Register(). Safe to call even if
        /// Register() was never called (missing keys are silently ignored).
        /// </summary>
        public static void Unregister(string executablePath)
        {
            string exeFileName = Path.GetFileName(executablePath);

            using (RegistryKey classesRoot = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
            {
                if (classesRoot == null) return;

                TryDeleteSubKeyTree(classesRoot, ProgId);
                TryDeleteSubKeyTree(classesRoot, @"Applications\" + exeFileName);
                RemoveFromExtensions(classesRoot);
            }

            NotifyShell();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static void RegisterProgId(RegistryKey classesRoot, string command)
        {
            using (RegistryKey progKey = classesRoot.CreateSubKey(ProgId))
            {
                progKey.SetValue("", FriendlyName);
                using (RegistryKey openCmd = progKey.CreateSubKey(@"shell\open\command"))
                    openCmd.SetValue("", command);
            }
        }

        private static void RegisterApplicationsEntry(RegistryKey classesRoot, string exeFileName, string command)
        {
            using (RegistryKey appKey = classesRoot.CreateSubKey(@"Applications\" + exeFileName))
            {
                appKey.SetValue("FriendlyAppName", FriendlyName);

                using (RegistryKey types = appKey.CreateSubKey("SupportedTypes"))
                {
                    foreach (string ext in SupportedExtensions)
                        types.SetValue(ext, "");
                }

                using (RegistryKey openCmd = appKey.CreateSubKey(@"shell\open\command"))
                    openCmd.SetValue("", command);
            }
        }

        private static void RegisterExtensions(RegistryKey classesRoot)
        {
            foreach (string ext in SupportedExtensions)
            {
                using (RegistryKey progids = classesRoot.CreateSubKey(ext + @"\OpenWithProgids"))
                    progids.SetValue(ProgId, new byte[0], RegistryValueKind.Binary);
            }
        }

        private static void RemoveFromExtensions(RegistryKey classesRoot)
        {
            foreach (string ext in SupportedExtensions)
            {
                using (RegistryKey progids = classesRoot.OpenSubKey(ext + @"\OpenWithProgids", writable: true))
                {
                    if (progids == null) continue;
                    try { progids.DeleteValue(ProgId, throwOnMissingValue: false); } catch { }
                }
            }
        }

        private static void TryDeleteSubKeyTree(RegistryKey parent, string subKey)
        {
            try { parent.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); } catch { }
        }

        private static void NotifyShell()
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
            catch { /* non-fatal */ }
        }
    }
}
