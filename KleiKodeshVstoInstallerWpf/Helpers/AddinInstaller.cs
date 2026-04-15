using KleiKodesh.Helpers;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Handles extracting the VSTO package and registering the add-in.
    /// All install constants live here so InstallPage.xaml.cs stays thin.
    /// </summary>
    public static class AddinInstaller
    {
        public const string AppName         = "KleiKodesh";
        public const string AppDisplayName  = "כלי קודש";
        public const string Version         = "v3.2.0";
        public const string InstallFolderName = "KleiKodesh";
        public const string ZipResourceName = "KleiKodesh.zip";
        public const string VstoFileName    = "KleiKodesh.vsto";

        public static string InstallPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallFolderName);

        public static string AddinRegistryPath     => $@"Software\Microsoft\Office\Word\Addins\{AppName}";
        public static string AddinDataRegistryPath => $@"Software\Microsoft\Office\Word\AddinsData\{AppName}";

        // ── Extract ──────────────────────────────────────────────────────────────

        public static async Task ExtractAsync(IProgress<double> progress)
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
                            await entryStream.CopyToAsync(fileStream);

                        current++;
                        progress?.Report((double)current / total * 100);
                    }
                }
            }
        }

        // ── Register ─────────────────────────────────────────────────────────────

        public static async Task RegisterAddInAsync(IProgress<double> progress)
        {
            try
            {
                using (RegistryKey addinKey = Registry.CurrentUser.CreateSubKey(AddinRegistryPath))
                {
                    addinKey.SetValue("Description",  AppDisplayName);
                    addinKey.SetValue("FriendlyName", AppDisplayName);
                    progress?.Report(103);
                    addinKey.SetValue("Manifest",     $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                    progress?.Report(106);
                    addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                    progress?.Report(109);
                }

                using (RegistryKey addinDataKey = Registry.CurrentUser.CreateSubKey(AddinDataRegistryPath))
                {
                    addinDataKey.SetValue("Description",  AppDisplayName);
                    addinDataKey.SetValue("FriendlyName", AppDisplayName);
                    addinDataKey.SetValue("Manifest",     $"file:///{InstallPath}\\{VstoFileName}|vstolocal");
                    addinDataKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                    progress?.Report(112);
                }

                await AddToOfficeInclusionListAsync();
            }
            catch { }
        }

        // ── VSTO trust ───────────────────────────────────────────────────────────

        private static async Task AddToOfficeInclusionListAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string[] vstoFiles = Directory.GetFiles(InstallPath, "*.vsto", SearchOption.AllDirectories);
                    if (vstoFiles.Length == 0) return;

                    string vstoPath    = vstoFiles[0];
                    string manifestUrl = $"file:///{vstoPath.Replace('\\', '/')}|vstolocal";
                    string keyName     = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestUrl));
                    string publicKey   = ExtractPublicKeyFromManifest(vstoPath);

                    const string inclusionPath = @"SOFTWARE\Microsoft\VSTO\Security\Inclusion";
                    using (RegistryKey inclusionKey = Registry.CurrentUser.CreateSubKey(inclusionPath))
                    using (RegistryKey entryKey     = inclusionKey.CreateSubKey(keyName))
                    {
                        entryKey.SetValue("Url", manifestUrl);
                        if (!string.IsNullOrEmpty(publicKey))
                            entryKey.SetValue("PublicKey", publicKey);
                        entryKey.SetValue("AllowsUnsafeCode", false, RegistryValueKind.DWord);
                    }

                    AddFolderToTrustedLocations();
                }
                catch { }
            });
        }

        private static void AddFolderToTrustedLocations()
        {
            try
            {
                const string trustedPath = @"SOFTWARE\Microsoft\VSTO\Security\TrustedPaths";
                using (RegistryKey trustedKey = Registry.CurrentUser.CreateSubKey(trustedPath))
                {
                    string folderKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(InstallPath));
                    using (RegistryKey fk = trustedKey.CreateSubKey(folderKey))
                    {
                        fk.SetValue("Path",            InstallPath);
                        fk.SetValue("AllowSubfolders", true, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private static string ExtractPublicKeyFromManifest(string vstoPath)
        {
            try
            {
                string content = File.ReadAllText(vstoPath);
                var match = Regex.Match(content, @"<RSAKeyValue>.*?</RSAKeyValue>", RegexOptions.Singleline);
                if (match.Success) return match.Value;
            }
            catch { }
            return null;
        }

        // ── Version + DB ─────────────────────────────────────────────────────────

        public static void SaveVersion()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\KleiKodesh"))
                    key?.SetValue("Version", Version);
            }
            catch { }
        }

        public static async Task EnsureDbExistsAsync()
        {
            if (!SettingsManager.GetBool("Ribbon", "Kezayit_Visible", true)) return;

            string appData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultDb    = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            string currentDb    = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultDb);

            if (!File.Exists(currentDb))
            {
                var result = MessageBox.Show(
                    "לא נמצאה ספריית זית במחשב.\n\nהאם ברצונך להוריד ולהתקין כעת?",
                    "ספריית זית לא נמצאה",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo { FileName = "https://zayitapp.com/#/download", UseShellExecute = true });
            }
        }
    }
}
