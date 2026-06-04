using KleiKodesh.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        public const string Version         = "v7.1.6";
        public const string InstallFolderName = "KleiKodesh";
        public const string ZipResourceName = "KleiKodesh.zip";
        public const string VstoFileName    = "KleiKodesh.vsto";

        /// <summary>
        /// Which installer variant this binary is — baked in at build time via
        /// -p:InstallerVariant=x64|x86|AnyCPU (DefineConstants in the csproj).
        /// Saved to registry by SaveVersion() so the update checker can download
        /// the same variant on the next update.
        /// </summary>
#if INSTALLER_VARIANT_X64
        public const string InstallerVariant = "x64";
#elif INSTALLER_VARIANT_X86
        public const string InstallerVariant = "x86";
#else
        public const string InstallerVariant = "AnyCPU";
#endif

        public static string InstallPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallFolderName);

        public static string AddinRegistryPath     => $@"Software\Microsoft\Office\Word\Addins\{AppName}";
        public static string AddinDataRegistryPath => $@"Software\Microsoft\Office\Word\AddinsData\{AppName}";

        /// <summary>
        /// Whether this build was compiled with the DELETE_FTS_INDEX flag.
        /// When true, ExtractAsync deletes the FTS index directory before extraction,
        /// forcing a full reindex on the user's machine.
        /// Baked in at build time via -p:DeleteFtsIndex=true (DefineConstants in the csproj).
        /// </summary>
#if DELETE_FTS_INDEX
        public const bool DeleteFtsIndexOnInstall = true;
#else
        public const bool DeleteFtsIndexOnInstall = false;
#endif

        // ── Extract ──────────────────────────────────────────────────────────────

        public static async Task ExtractAsync(IProgress<double> progress)
        {
            // Delete the FTS index before extracting so the app rebuilds it fresh.
            // Only done when the installer was built with -p:DeleteFtsIndex=true.
            if (DeleteFtsIndexOnInstall)
            {
                try
                {
                    string ftsPath = Path.Combine(InstallPath, "FtsIndex");
                    if (Directory.Exists(ftsPath))
                    {
                        Directory.Delete(ftsPath, recursive: true);
                        Console.WriteLine("[AddinInstaller] Deleted FTS index directory (forced reindex)");
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal — if deletion fails, the app will still run;
                    // the existing index will be used until it detects a mismatch.
                    Console.WriteLine("[AddinInstaller] Failed to delete FTS index: " + ex.Message);
                }
            }

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

                        // Skip files that should be preserved across updates:
                        // 1. WebSitesWhitelist.json — user's website list customization
                        // 2. Cache folders — user's cached PDFs, conversions, downloads
                        // 3. BloomFilters — search index (rebuilt on version mismatch)
                        if (ShouldSkipOnUpdate(entry.FullName) && File.Exists(fullPath))
                        {
                            current++;
                            progress?.Report((double)current / total * 100);
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

        /// <summary>
        /// Returns true if this entry should be skipped during extraction on update.
        /// Preserves user data and caches across installer updates.
        /// </summary>
        private static bool ShouldSkipOnUpdate(string entryPath)
        {
            // Normalize path separators
            string normalized = entryPath.Replace('/', '\\');

            // User's website list customization
            if (string.Equals(normalized, "WebSitesWhitelist.json", StringComparison.OrdinalIgnoreCase))
                return true;

            // Cache folders: Word→PDF conversions, HebrewBooks downloads, WebView2 webcache
            if (normalized.StartsWith("KitveiHakodesh\\cache\\", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith("KitveiHakodesh\\webcache\\", StringComparison.OrdinalIgnoreCase))
                return true;

            // Bloom filter search index (rebuilt on version mismatch)
            if (normalized.StartsWith("BloomFilters\\", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
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

        // ── Whitelist ────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the given whitelist JSON to disk immediately.
        /// Called directly from ComponentSettingsPage when the user clicks OK in
        /// WhitelistEditorDialog — self-contained, no dependency on install order.
        /// </summary>
        public static void ApplyPendingWhitelist(string json)
        {
            try
            {
                string dest = Path.Combine(InstallPath, "WebSitesWhitelist.json");
                File.WriteAllText(dest, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ── Version + DB ─────────────────────────────────────────────────────────

        public static void SaveVersion()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\KleiKodesh"))
                {
                    key?.SetValue("Version",          Version);
                    key?.SetValue("InstallerVariant", InstallerVariant);
                }
            }
            catch { }
        }

    }
}
