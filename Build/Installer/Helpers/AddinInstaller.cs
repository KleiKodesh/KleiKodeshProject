using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using Microsoft.Win32;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using System;
using System.IO;
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
        public const string Version         = "v10.0.0";
        public const string InstallFolderName = "KleiKodesh";
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
#pragma warning disable CS0162 // Unreachable code — intentional compile-time constant
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
#pragma warning restore CS0162

            // Every path delivered by this payload (extracted OR skip-preserved),
            // collected so the post-extract purge can tell current files from stale
            // leftovers of previous versions.
            var payloadPaths = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            // The payload is a solid-LZMA archive written next to this exe by the NSIS
            // wrapper (see PayloadArchive for why it is not a zip). It is strictly
            // sequential: every entry must be consumed in order, and skipped entries
            // still have to be drained out of the stream rather than seeked past.
            using (var stream = OpenPayloadStream())
            {
                int total = PayloadArchive.ReadHeader(stream);
                int current = 0;

                using (var body = new LZipStream(stream, CompressionMode.Decompress))
                {
                    for (int i = 0; i < total; i++)
                    {
                        var entry = PayloadArchive.ReadEntryHeader(body);
                        payloadPaths.Add(entry.Path.Replace('/', '\\'));
                        string fullPath = Path.Combine(InstallPath, entry.Path);

                        // Skip files that should be preserved across updates:
                        // 1. WebSitesWhitelist.json — user's website list customization
                        // 2. Cache folders — user's cached PDFs, conversions, downloads
                        // 3. BloomFilters — search index (rebuilt on version mismatch)
                        //
                        // The bytes still have to come out of the LZMA stream to reach the
                        // next entry, so a "skip" drains them to Stream.Null instead of
                        // seeking — the zip version could simply not open the entry.
                        if (ShouldSkipOnUpdate(entry.Path) && File.Exists(fullPath))
                        {
                            PayloadArchive.CopyExactly(body, Stream.Null, entry.Length);
                            current++;
                            progress?.Report((double)current / total * 100);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                        // DocumentLocator.Service.exe may be locked by the running Windows
                        // service. DocumentLocatorHelper.SendShutdownAsync() was fired at the
                        // start of installation; TryCopyServiceExeAsync() waits for the
                        // remainder of the 1 500 ms exit window, then retries up to 3 times.
                        // On permanent failure the existing file is left in place (silent skip).
                        if (DocumentLocatorHelper.IsServiceExe(entry.Path))
                        {
                            // Buffer this entry in memory: TryCopyServiceExeAsync seeks back
                            // to the start between retries, and the LZMA body stream cannot
                            // seek. The buffer must be filled here regardless of whether the
                            // copy succeeds, so the stream stays aligned for the next entry.
                            using (var buffer = new MemoryStream())
                            {
                                PayloadArchive.CopyExactly(body, buffer, entry.Length);
                                buffer.Seek(0, SeekOrigin.Begin);
                                await DocumentLocatorHelper.TryCopyServiceExeAsync(buffer, fullPath)
                                    .ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            using (var fileStream = File.Create(fullPath))
                                PayloadArchive.CopyExactly(body, fileStream, entry.Length);
                        }

                        current++;
                        progress?.Report((double)current / total * 100);
                    }
                }
            }

            // Updates historically never removed files that dropped out of the
            // payload, so machines upgraded across versions accumulated stale
            // binaries (e.g. old SQLite-era DLLs) that Word could still load.
            PurgeStaleFiles(payloadPaths);
        }

        // ── Stale-file purge ─────────────────────────────────────────────────────

        /// <summary>
        /// File extensions the installer owns outright. The purge only ever touches
        /// these; data files (.db, .json, indexes, caches) are never deleted even
        /// when absent from the payload, because several features generate files
        /// under the install folder at runtime (FtsIndex, filesystemindex,
        /// webcache…) and the seforim DB — plus its Settings\user_settings.db —
        /// may be user-placed anywhere, including inside the install folder.
        /// </summary>
        private static readonly string[] PurgeableExtensions =
            { ".dll", ".exe", ".pdb", ".xml", ".config", ".manifest", ".vsto" };

        /// <summary>
        /// Relative paths never purged even when the extension is purgeable.
        /// Matched as loose case-insensitive prefixes — deliberately loose, since
        /// over-preserving is harmless while over-deleting is not (e.g.
        /// "KitveiHakodesh\webcache" also covers webcache-standalone).
        /// </summary>
        private static readonly string[] PreservedPrefixes =
        {
            "uninstall.exe",                    // created by the NSIS wrapper, never in the payload
            "WebSitesWhitelist.json",
            "KitveiHakodesh\\cache",
            "KitveiHakodesh\\webcache",
            "KitveiHakodesh\\word-cache",
            "KitveiHakodesh\\hebrewbooks-cache",
            "BloomFilters",
            "FtsIndex",
            "filesystemindex",
            "SearchExpansion",
            "Settings",                          // user_settings.db when the seforim DB sits in the install root
        };

        /// <summary>
        /// Deletes installer-owned binaries under InstallPath that this payload did
        /// not deliver — leftovers from previous versions. Runs after a successful
        /// extraction only; every failure is non-fatal (a locked stale file simply
        /// survives until the next update).
        /// </summary>
        private static void PurgeStaleFiles(System.Collections.Generic.HashSet<string> payloadPaths)
        {
            try
            {
                foreach (string fullPath in Directory.GetFiles(InstallPath, "*", SearchOption.AllDirectories))
                {
                    string relative = fullPath.Substring(InstallPath.Length).TrimStart('\\');

                    if (payloadPaths.Contains(relative)) continue;
                    if (!HasPurgeableExtension(relative)) continue;
                    if (IsPreserved(relative)) continue;

                    try
                    {
                        File.Delete(fullPath);
                        Console.WriteLine("[AddinInstaller] Purged stale file: " + relative);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[AddinInstaller] Could not purge " + relative + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AddinInstaller] Stale-file purge skipped: " + ex.Message);
            }
        }

        private static bool HasPurgeableExtension(string relativePath)
        {
            string ext = Path.GetExtension(relativePath);
            foreach (string purgeable in PurgeableExtensions)
                if (string.Equals(ext, purgeable, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsPreserved(string relativePath)
        {
            foreach (string prefix in PreservedPrefixes)
                if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Explicit payload location, set from the <c>--payload</c> switch at startup
        /// (see App.OnStartup). Survives the elevated relaunch that ניקוי עמוק performs,
        /// where %TEMP% resolves to a different profile and the sibling file is not
        /// visible. Null when the switch was not passed.
        /// </summary>
        public static string PayloadPathOverride { get; set; }

        /// <summary>
        /// Resolves the payload archive, in order:
        ///   1. --payload path, when the process was relaunched with one.
        ///   2. Next to the exe — where the NSIS wrapper stages it.
        ///   3. The staging folder under the *invoking* user's TEMP, recovered from
        ///      the exe's own path, for the elevated-relaunch case.
        ///   4. An embedded resource, so a self-contained exe still installs.
        /// </summary>
        private static Stream OpenPayloadStream()
        {
            var tried = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(PayloadPathOverride))
            {
                if (File.Exists(PayloadPathOverride))
                    return File.OpenRead(PayloadPathOverride);
                tried.Add(PayloadPathOverride);
            }

            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string sideBySide = Path.Combine(exeDir, PayloadArchive.FileName);
            if (File.Exists(sideBySide))
                return File.OpenRead(sideBySide);
            tried.Add(sideBySide);

            var embedded = Assembly.GetExecutingAssembly()
                                   .GetManifestResourceStream(PayloadArchive.FileName);
            if (embedded != null)
                return embedded;

            throw new FileNotFoundException(
                "Payload archive not found. Looked for:" + Environment.NewLine +
                "  " + string.Join(Environment.NewLine + "  ", tried.ToArray()) + Environment.NewLine +
                "and for an embedded resource named '" + PayloadArchive.FileName + "'.");
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

            // DocumentLocator NTFS index — runtime-generated, never overwrite on update.
            if (normalized.StartsWith("filesystemindex\\", StringComparison.OrdinalIgnoreCase))
                return true;

            // The user's excluded-folders list. It sits BESIDE filesystemindex rather than
            // inside it (a reindex deletes that directory wholesale), so the prefix above
            // does not cover it. Belt-and-braces: a runtime-generated user file is never in
            // the payload, and .json is not purgeable either — the fix for the list being
            // wiped on upgrade is its location, not this rule. Keep it so a payload that
            // ever did ship this name could not clobber the user's list.
            if (string.Equals(normalized, "excluded_folders.json", StringComparison.OrdinalIgnoreCase))
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

        // ── Start Menu Shortcut ───────────────────────────────────────────────────

        /// <summary>
        /// Creates (or overwrites) a Start Menu shortcut for כתבי הקודש.exe.
        /// Placed in %AppData%\Microsoft\Windows\Start Menu\Programs\כלי קודש\כתבי הקודש.lnk
        /// Safe to call on every install/update — always overwrites to keep the
        /// target path and icon up to date.
        /// </summary>
        public static void CreateKitveiHakodeshShortcut()
        {
            try
            {
                string exeName  = "כתבי הקודש.exe";
                string exePath  = Path.Combine(InstallPath, exeName);

                // Place the shortcut under a KleiKodesh subfolder in Programs so
                // it groups neatly alongside any future shortcuts.
                string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string shortcutFolder = Path.Combine(programsFolder, AppDisplayName);
                Directory.CreateDirectory(shortcutFolder);

                string shortcutPath = Path.Combine(shortcutFolder, "כתבי הקודש.lnk");

                // Use WScript.Shell COM object — available on every Windows machine,
                // no extra reference or NuGet package required.
                Type   shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell     = Activator.CreateInstance(shellType);

                object shortcut  = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null, shell,
                    new object[] { shortcutPath });

                Type scType = shortcut.GetType();

                // Target exe
                scType.InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { exePath });

                // Working directory = install folder
                scType.InvokeMember("WorkingDirectory",
                    System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { InstallPath });

                // Description shown on hover
                scType.InvokeMember("Description",
                    System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { "כתבי הקודש — מאגר ספרי קודש" });

                // Icon: use the exe itself (index 0)
                scType.InvokeMember("IconLocation",
                    System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { exePath + ",0" });

                scType.InvokeMember("Save",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch { }
        }

    }
}
