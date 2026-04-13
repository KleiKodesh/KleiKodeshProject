using System;
using System.Diagnostics;
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
        /// </summary>
        public static bool RelaunchAsAdmin(string arguments = "")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = Assembly.GetExecutingAssembly().Location,
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
