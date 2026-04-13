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
            foreach (string arg in e.Args)
            {
                if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                {
                    silentMode = true;
                    break;
                }
            }

            if (silentMode)
            {
                var mainWindow = new MainWindow(startInstallImmediately: true);
                mainWindow.Show();
            }
            else
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }
    }
}
