using Microsoft.Win32;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Interaction logic for InstallProgressWindow.xaml
    /// </summary>
    public partial class InstallProgressWindow : Window
    {
        const string AppName = "KleiKodesh";
        const string AppDisplayName = "כלי קודש";
        const string Version = "v1.0.20";
        const string InstallFolderName = "KleiKodesh";
        const string ZipResourceName = "KleiKodesh.zip";
        const string VstoFileName = "KleiKodesh.vsto";
        readonly IProgress<double> _progress;

        static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallFolderName);
        static string AddinRegistryPath => $@"Software\Microsoft\Office\Word\Addins\{AppName}";


        public InstallProgressWindow(Window mainWindow)
        {
            InitializeComponent();
            _progress = new Progress<double>(UpdateProgress);
            mainWindow?.Close();
            Install();
        }

        public void UpdateProgress(double progress)
        {
            ProgressBar.Value = progress;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Window_TouchDown(object sender, TouchEventArgs e)
        {
            this.DragMove();
        }

        async void Install()
        {
            try
            {
                await OldInstallationCleaner.CheckAndRemoveOldInstallations();

                if (!Directory.Exists(InstallPath))
                    Directory.CreateDirectory(InstallPath);

                await Extract();
                await RegisterAddIn();

                while (ProgressBar.Value < ProgressBar.Maximum)
                {
                    ProgressBar.Value++;
                    await Task.Delay(10);
                }

                // Save version to registry after successful installation
                SaveVersionToRegistry(Version);

                // Installation completed successfully - exit with code 0
                await Task.Delay(300);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                // Installation failed - exit with code 1
                Environment.Exit(1);
            }
        }

        // do we need to ensure recursive directory extraction?????
        private async Task Extract()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(ZipResourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("Resource not found: " + ZipResourceName);

                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int current = 0;

                    foreach (var entry in archive.Entries)
                    {
                        string fullPath = Path.Combine(InstallPath, entry.FullName);

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(fullPath);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                        using (var entryStream = entry.Open())
                        using (var fileStream = File.Create(fullPath))
                        {
                            await entryStream.CopyToAsync(fileStream);
                        }

                        current++;
                        double progressValue = (double)current / total * 100;
                        _progress.Report(progressValue);
                    }
                }
            }
        }



        async Task RegisterAddIn()
        {
            try
            {
                // Register add-in for current user only (HKCU)
                using (RegistryKey addinKey = Registry.CurrentUser.CreateSubKey(AddinRegistryPath))
                {
                    addinKey.SetValue("FriendlyName", AppDisplayName);
                    _progress.Report(103);
                    addinKey.SetValue("Manifest", $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                    _progress.Report(106);
                    addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                    _progress.Report(109);
                }
            }
            catch { }
        }

        private void SaveVersionToRegistry(string version)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\KleiKodesh"))
                {
                    if (key != null)
                    {
                        key.SetValue("Version", version);
                    }
                }
            }
            catch (Exception)
            {
                // Ignore registry errors - don't fail installation for this
            }
        }
    }
}
