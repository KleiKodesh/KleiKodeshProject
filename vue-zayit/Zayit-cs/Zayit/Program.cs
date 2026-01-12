using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zayit
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);

        private enum DpiAwareness
        {
            Unaware = 0,
            SystemAware = 1,
            PerMonitorAware = 2
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Enable high DPI support with fallback for different Windows versions
            EnableDpiAwareness();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form
            {
                Height = 1000,
                Width = 700,
                StartPosition = FormStartPosition.CenterScreen,
                AutoScaleMode = AutoScaleMode.Dpi
            };

            //var viewer = new ZayitViewer();
            //var uc = new UserControl { Dock = DockStyle.Fill };
            //uc.Controls.Add(viewer);
            form.Controls.Add(new Zayit.Viewer.ZayitViewerHost());
            //form.Controls.Add(viewer);

            Application.Run(form);
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                // Try Windows 8.1+ method first (shcore.dll)
                if (Environment.OSVersion.Version.Major > 6 ||
                    (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor >= 3))
                {
                    SetProcessDpiAwareness((int)DpiAwareness.PerMonitorAware);
                    return;
                }
            }
            catch (DllNotFoundException)
            {
                // shcore.dll not available, fall back to user32.dll
            }
            catch (EntryPointNotFoundException)
            {
                // Function not available, fall back to user32.dll
            }

            try
            {
                // Fallback to Windows Vista+ method (user32.dll)
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    SetProcessDPIAware();
                }
            }
            catch (DllNotFoundException)
            {
                // user32.dll not available (shouldn't happen)
            }
            catch (EntryPointNotFoundException)
            {
                // Function not available on this Windows version
                // Application will run without DPI awareness
            }
        }
    }
}
