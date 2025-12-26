using System.Configuration;
using System.Data;
using System.Windows;

namespace KleiKodeshInstallerWpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
                var progressWindow = new InstallProgressWindow(null, "Install", true, true, true, true);
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
