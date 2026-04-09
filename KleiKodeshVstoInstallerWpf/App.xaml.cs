using System;
using System.Threading.Tasks;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Kick off old installation cleanup immediately in the background
            _ = Task.Run(() => OldInstallationCleaner.CheckAndRemoveOldInstallations());

            // Check for silent installation argument
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
                // Silent mode: go directly to installation
                var progressWindow = new InstallProgressWindow(null);
                progressWindow.Show();
            }
            else
            {
                // Normal mode: show main window
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }
    }
}
