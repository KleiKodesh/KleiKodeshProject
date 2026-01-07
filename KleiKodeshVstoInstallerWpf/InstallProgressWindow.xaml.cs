using KleiKodesh.Helpers;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
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
        const string Version = "v1.0.15";
        const string InstallFolderName = "KleiKodesh";
        const string ZipResourceName = "KleiKodesh.zip";
        const string VstoFileName = "KleiKodesh.vsto";
        readonly IProgress<double> _progress;

        static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), InstallFolderName);
        static string AddinRegistryPath => $@"Software\Microsoft\Office\Word\Addins\{AppName}";
        

        public InstallProgressWindow(Window mainWindow, 
            string defaultButton,
            bool Kezayit,
            bool hebrewbooks,
            bool websites,
            bool KleiKodesh)
        {
            InitializeComponent();
            _progress = new Progress<double>(UpdateProgress);
            mainWindow?.Close();
            Install(defaultButton, Kezayit, hebrewbooks, websites, KleiKodesh);
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

        async void Install(string defaultButton,
            bool Kezayit,
            bool hebrewbooks,
            bool websites,
            bool KleiKodesh)
        {
            try
            {
                if (!Directory.Exists(InstallPath))
                    Directory.CreateDirectory(InstallPath);

                await Extract();
                await RegisterAddIn();

                while (ProgressBar.Value < ProgressBar.Maximum)
                {
                    ProgressBar.Value++;
                    await Task.Delay(10);
                }

                MessageBox.Show("ההתקנה הסתיימה");
                
                // Save version to registry after successful installation
                SaveVersionToRegistry(Version);
                
                // Installation completed successfully - exit with code 0
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
                // 64-bit
                using (RegistryKey key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey addinKey = key64.CreateSubKey(AddinRegistryPath))
                    {
                        addinKey.SetValue("FriendlyName", AppDisplayName);
                        _progress.Report(103);
                        addinKey.SetValue("Manifest", $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                        _progress.Report(106);
                        addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                        _progress.Report(109);
                    }
                }
            }
            catch { }

            try
            {
                // 32-bit
                using (RegistryKey key32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                {
                    using (RegistryKey addinKey = key32.CreateSubKey(AddinRegistryPath))
                    {
                        addinKey.SetValue("FriendlyName", AppDisplayName);
                        _progress.Report(112);
                        addinKey.SetValue("Manifest", $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                        _progress.Report(115);
                        addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                        _progress.Report(118);
                    }
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
