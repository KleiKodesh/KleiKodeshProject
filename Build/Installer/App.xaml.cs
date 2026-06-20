using System;
using System.Reflection;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class App : Application
    {
        // Wire up assembly resolver before anything else loads
        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string shortName = new AssemblyName(args.Name).Name + ".dll";
            var asm = Assembly.GetExecutingAssembly();

            string resourceName = Array.Find(
                asm.GetManifestResourceNames(),
                r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null) return null;

            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return Assembly.Load(bytes);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool silentMode  = false;
            // When built with -p:ForceCleanInstall=true, the installer always launches
            // in repair mode — wipes existing files/registry then installs fresh.
            // The "התקן" button on LandingPage therefore behaves exactly like "תיקון".
#if FORCE_CLEAN_INSTALL
            bool repairMode  = true;
#else
            bool repairMode  = false;
#endif
            int  waitForPid  = 0;

            foreach (string arg in e.Args)
            {
                if (arg.Equals("--silent",  StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/silent",   StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--install", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/install",  StringComparison.OrdinalIgnoreCase))
                    silentMode = true;

                if (arg.Equals("--repair", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/repair",  StringComparison.OrdinalIgnoreCase))
                    repairMode = true;
            }

            // --wait-for-pid <PID>: hide until the given process exits, then show normally.
            // Used by the auto-updater: installer is launched from Word's shutdown event
            // while Word is still alive, then waits for Word to fully exit before showing UI.
            for (int i = 0; i < e.Args.Length - 1; i++)
            {
                if (e.Args[i].Equals("--wait-for-pid", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(e.Args[i + 1], out waitForPid);
                    break;
                }
            }

            if (waitForPid > 0)
            {
                // Wait on a background thread — don't block the UI thread
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(waitForPid);
                        proc.WaitForExit();
                    }
                    catch { /* process already gone */ }

                    // Now show the window on the UI thread
                    Dispatcher.Invoke(() => MainWindow?.Show());
                });
            }

            MainWindow mainWindow;
            if (silentMode)
            {
                // Go straight to install progress page — no landing, no UI chrome.
                // Exits with code 0 on success, 1 on failure.
                // Used by: NSIS --silent passthrough.
                mainWindow = new MainWindow(startInstallImmediately: true);
            }
            else
            {
                mainWindow = new MainWindow();
                if (repairMode)
                    mainWindow.NavigateToRepairOnLoad();
            }

            // If waiting for a pid, start hidden — the background task above will show it
            if (waitForPid > 0)
                mainWindow.Visibility = System.Windows.Visibility.Hidden;
            else
                mainWindow.Show();
        }
    }
}
