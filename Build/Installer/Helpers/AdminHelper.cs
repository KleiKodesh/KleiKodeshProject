using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    public static class AdminHelper
    {
        public static bool IsElevated =>
            new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>
        /// Relaunches the current exe with "runas" (UAC prompt) and the given arguments,
        /// then exits the current (non-elevated) instance.
        /// Returns false if the user cancelled the UAC prompt.
        ///
        /// The payload-archive path is appended automatically. The archive is a sibling
        /// file of the exe in the NSIS staging folder under %TEMP%, and an elevated
        /// process resolves %TEMP% to a different profile — so the new instance cannot
        /// find it by looking next to itself unless it is told where it is. This was
        /// invisible while the payload was an embedded resource, because it travelled
        /// inside the exe. Symptom without it: ניקוי עמוק (which is the only caller,
        /// via RepairPage) fails with "Payload archive not found".
        /// </summary>
        public static bool RelaunchAsAdmin(string arguments = "")
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;

                // Forward the resolved payload location, whether it came from our own
                // --payload switch or from sitting next to the exe.
                string payload = AddinInstaller.PayloadPathOverride;
                if (string.IsNullOrEmpty(payload))
                {
                    string sibling = Path.Combine(
                        Path.GetDirectoryName(exePath), PayloadArchive.FileName);
                    if (File.Exists(sibling)) payload = sibling;
                }
                if (!string.IsNullOrEmpty(payload))
                {
                    arguments = (arguments + " " + PayloadArchive.PathSwitch +
                                 " \"" + payload + "\"").Trim();
                }

                var psi = new ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = arguments,
                    Verb            = "runas",
                    UseShellExecute = true,
                };
                Process.Start(psi);
                Environment.Exit(0);
                return true; // never reached
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User clicked "No" on the UAC prompt
                return false;
            }
        }
    }
}
