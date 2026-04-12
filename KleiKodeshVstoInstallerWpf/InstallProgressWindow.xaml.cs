using KleiKodesh.Helpers;
using Microsoft.VisualBasic;
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
        const string Version = "v3.3.0";
        const string InstallFolderName = "KleiKodesh";
        const string ZipResourceName = "KleiKodesh.zip";
        const string VstoFileName = "KleiKodesh.vsto";
        readonly IProgress<double> _progress;

        static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallFolderName);
        static string AddinRegistryPath => $@"Software\Microsoft\Office\Word\Addins\{AppName}";
        static string AddinDataRegistryPath => $@"Software\Microsoft\Office\Word\AddinsData\{AppName}";

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
                await EnsureDbExists();

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

        async Task EnsureDbExists()
        {
            // Check if user enabled ספריית כזית
            bool kezayitEnabled = SettingsManager.GetBool("Ribbon", "Kezayit_Visible", true);

            if (!kezayitEnabled)
                return; // user disabled it → no need to check DB

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultDbPath = Path.Combine(appDataPath, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            string currentDbPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultDbPath);

            if (!File.Exists(currentDbPath))
            {
                var result = MessageBox.Show(
                    "לא נמצאה ספריית זית במחשב.\n\nהאם ברצונך להוריד ולהתקין כעת?",
                    "ספריית זית לא נמצאה",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://zayitapp.com/#/download",
                        UseShellExecute = true
                    });
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
                    addinKey.SetValue("Description", AppDisplayName); // This was missing!
                    addinKey.SetValue("FriendlyName", AppDisplayName);
                    _progress.Report(103);
                    addinKey.SetValue("Manifest", $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                    _progress.Report(106);
                    addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                    _progress.Report(109);
                }

                // Also register in AddinsData registry path (this was missing!)
                using (RegistryKey addinDataKey = Registry.CurrentUser.CreateSubKey(AddinDataRegistryPath))
                {
                    // AddinsData typically stores additional metadata
                    addinDataKey.SetValue("Description", AppDisplayName); // Add Description here too
                    addinDataKey.SetValue("FriendlyName", AppDisplayName);
                    addinDataKey.SetValue("Manifest", $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                    addinDataKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                    _progress.Report(112);
                }

                // Add to Office inclusion list for trust
                await AddToOfficeInclusionList();
            }
            catch { }
        }

        private async Task AddToOfficeInclusionList()
        {
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        string[] vstoFiles = Directory.GetFiles(InstallPath, "*.vsto", SearchOption.AllDirectories);
                        if (vstoFiles.Length == 0) return;

                        string vstoPath = vstoFiles[0];
                        string manifestUrl = $"file:///{vstoPath.Replace('\\', '/')}|vstolocal";

                        // Key name = base64 of the manifest URL (UTF-8)
                        string keyName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manifestUrl));

                        // Full RSA public key XML — extracted from the .vsto manifest's KeyInfo element.
                        // This is what the VSTO runtime checks against the manifest signature.
                        // Using the full key avoids the "מפרסם לא מוכר" trust dialog.
                        string fullPublicKey = ExtractFullPublicKeyFromManifest(vstoPath);

                        string inclusionListPath = @"SOFTWARE\Microsoft\VSTO\Security\Inclusion";
                        using (RegistryKey inclusionKey = Registry.CurrentUser.CreateSubKey(inclusionListPath))
                        using (RegistryKey entryKey = inclusionKey.CreateSubKey(keyName))
                        {
                            entryKey.SetValue("Url", manifestUrl);
                            if (!string.IsNullOrEmpty(fullPublicKey))
                                entryKey.SetValue("PublicKey", fullPublicKey);
                            entryKey.SetValue("AllowsUnsafeCode", false, RegistryValueKind.DWord);
                        }

                        AddFolderToTrustedLocations();
                    }
                    catch { }
                });
            }
            catch { }
        }

        /// <summary>
        /// Extracts the full RSA public key XML string from the .vsto manifest's KeyInfo/KeyValue element.
        /// This is the value the VSTO Inclusion list needs in its PublicKey entry to suppress the
        /// "publisher cannot be verified" trust dialog for self-signed add-ins.
        /// Returns null if the key cannot be extracted — caller must handle null gracefully.
        /// </summary>
        private string ExtractFullPublicKeyFromManifest(string vstoPath)
        {
            try
            {
                string content = File.ReadAllText(vstoPath);

                // Extract the full <RSAKeyValue>...</RSAKeyValue> block from KeyInfo
                var match = System.Text.RegularExpressions.Regex.Match(
                    content,
                    @"<RSAKeyValue>.*?</RSAKeyValue>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (match.Success)
                    return match.Value;
            }
            catch { }

            return null; // Cannot extract — do not write a wrong key
        }

        private void AddFolderToTrustedLocations()
        {
            try
            {
                // Add the installation folder to trusted locations
                // This provides broader trust for future versions
                string trustedLocationsPath = @"SOFTWARE\Microsoft\VSTO\Security\TrustedPaths";
                using (RegistryKey trustedKey = Registry.CurrentUser.CreateSubKey(trustedLocationsPath))
                {
                    string folderKeyName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(InstallPath));
                    using (RegistryKey folderKey = trustedKey.CreateSubKey(folderKeyName))
                    {
                        folderKey.SetValue("Path", InstallPath);
                        folderKey.SetValue("AllowSubfolders", true, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
                // Ignore if trusted locations setup fails
            }
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
