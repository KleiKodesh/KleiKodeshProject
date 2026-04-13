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

            bool silentMode = false;
            bool repairMode = false;
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

            MainWindow mainWindow;
            if (silentMode)
            {
                // Go straight to install progress page — no landing, no UI chrome.
                // Exits with code 0 on success, 1 on failure.
                // Used by: auto-updater (DownloadManager), NSIS --silent passthrough.
                mainWindow = new MainWindow(startInstallImmediately: true);
            }
            else
            {
                mainWindow = new MainWindow();
                if (repairMode)
                    mainWindow.NavigateToRepairOnLoad();
            }
            mainWindow.Show();
        }
    }
}
