using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
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
        const string Version = "v2.0.0";
        const string InstallFolderName = "KleiKodesh";
        const string ZipResourceName = "KleiKodesh.zip";
        const string VstoFileName = "KleiKodesh.vsto";
        readonly IProgress<double> _progress;
        private bool _installForAllUsers;

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

        private void CheckAdminAndPromptForAllUsers()
        {
            if (IsRunningAsAdministrator())
            {
                var result = MessageBox.Show(
                    "זוהה שהמתקין רץ עם הרשאות מנהל.\n\nהאם ברצונך להתקין את התוסף עבור כל המשתמשים במחשב?\n\n" +
                    "כן - התקנה עבור כל המשתמשים\n" +
                    "לא - התקנה עבור המשתמש הנוכחי בלבד",
                    "התקנה עבור כל המשתמשים?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                _installForAllUsers = (result == MessageBoxResult.Yes);
            }

            Install();
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
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
                // First, clean up any old registry entries that might point to old installation paths
                CleanupOldAddinRegistryEntries();

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
                        // Find the .vsto file in the installation directory
                        string[] vstoFiles = Directory.GetFiles(InstallPath, "*.vsto", SearchOption.AllDirectories);
                        
                        if (vstoFiles.Length > 0)
                        {
                            string vstoPath = vstoFiles[0]; // Use the first .vsto file found
                            
                            // Add to Office inclusion list to trust the solution
                            string inclusionListPath = @"SOFTWARE\Microsoft\VSTO\Security\Inclusion";
                            using (RegistryKey inclusionKey = Registry.CurrentUser.CreateSubKey(inclusionListPath))
                            {
                                string manifestUrl = $"file:///{vstoPath.Replace('\\', '/')}|vstolocal";
                                string keyName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manifestUrl));
                                
                                using (RegistryKey entryKey = inclusionKey.CreateSubKey(keyName))
                                {
                                    entryKey.SetValue("Url", manifestUrl);
                                    // Extract the public key from the actual manifest
                                    string publicKey = ExtractPublicKeyFromManifest();
                                    if (!string.IsNullOrEmpty(publicKey))
                                    {
                                        entryKey.SetValue("PublicKey", publicKey);
                                    }
                                    entryKey.SetValue("AllowsUnsafeCode", false, RegistryValueKind.DWord);
                                }
                            }
                        }

                        // Also add the installation folder to trusted locations for future versions
                        AddFolderToTrustedLocations();
                    }
                    catch
                    {
                        // Ignore inclusion list errors
                    }
                });
            }
            catch
            {
                // Don't fail if inclusion list setup fails
            }
        }

        private string ExtractPublicKeyFromManifest()
        {
            try
            {
                // Search for any .vsto file in the installation directory
                string[] vstoFiles = Directory.GetFiles(InstallPath, "*.vsto", SearchOption.AllDirectories);
                
                if (vstoFiles.Length == 0)
                {
                    // No .vsto file found - return fallback key
                    return "7c40e594188e4b56";
                }

                // Use the first .vsto file found
                string vstoPath = vstoFiles[0];
                string manifestContent = File.ReadAllText(vstoPath);
                
                // Extract publicKeyToken from the manifest XML
                var match = System.Text.RegularExpressions.Regex.Match(
                    manifestContent, 
                    @"publicKeyToken=""([^""]+)""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                {
                    string extractedKey = match.Groups[1].Value;
                    // Validate it looks like a public key token (hex string, typically 16 chars)
                    if (!string.IsNullOrEmpty(extractedKey) && extractedKey.Length >= 8)
                    {
                        return extractedKey;
                    }
                }
            }
            catch (Exception ex)
            {
                // If extraction fails, fall back to known key
            }
            
            // Fallback to current known public key if extraction fails
            return "7c40e594188e4b56";
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

        void CleanupOldAddinRegistryEntries()
        {
            try
            {
                // Delete the entire add-in registry key to remove any old Manifest paths
                // This ensures we start fresh with the new installation path
                Registry.CurrentUser.DeleteSubKey(AddinRegistryPath, throwOnMissingSubKey: false);
                
                // Also cleanup the AddinsData registry path
                Registry.CurrentUser.DeleteSubKey(AddinDataRegistryPath, throwOnMissingSubKey: false);
            }
            catch
            {
                // Ignore cleanup errors - we'll try to register anyway
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
