using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Nuclear cleaner — mirrors a "Forced Uninstall" approach with source-code-verified locations.
    /// Removes every known file-system and registry footprint of KleiKodesh across all versions,
    /// install locations, registry key name variants (Hebrew + English), and hives (HKLM + HKCU).
    /// </summary>
    public static class FullSystemCleaner
    {
        // ── Result types ────────────────────────────────────────────────────────────

        public class CleanupResult
        {
            public List<string> DeletedPaths          { get; } = new List<string>();
            public List<string> DeletedRegistryKeys   { get; } = new List<string>();
            public List<string> Errors                { get; } = new List<string>();

            public int TotalDeleted => DeletedPaths.Count + DeletedRegistryKeys.Count;
        }

        // ── Known name variants ──────────────────────────────────────────────────

        // Every known registry key name ever used for the add-in registration.
        // These are exact key names — used for direct registry path construction.
        private static readonly string[] AppNameVariants =
        {
            "KleiKodesh",   // canonical — current versions
            "Klei Kodesh",  // split English — defensive
            "כלי קודש",     // Hebrew split — first public release
            "כליקודש",      // Hebrew joined — defensive
        };

        // All string tokens that identify KleiKodesh content in arbitrary strings
        // (file paths, registry values, blob data, folder names, shortcut names).
        // OrdinalIgnoreCase is used for all matching, so we only need one casing per token.
        // Hebrew strings are case-invariant by nature.
        private static readonly string[] KleiKodeshTokens = new[]
        {
            "KleiKodesh",   // canonical camelCase — covers kleikodesh, KLEIKODESH etc. via OrdinalIgnoreCase
            "Klei Kodesh",  // split with space    — covers klei kodesh, KLEI KODESH etc.
            "כלי קודש",     // Hebrew split (standard)
            "כליקודש",      // Hebrew joined (no space)
        };

        // ── Entry point ─────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full system cleanup.
        /// <paramref name="progress"/> receives (percent 0-100, step description).
        /// <paramref name="detailLog"/> receives one line per deleted item.
        /// </summary>
        public static async Task<CleanupResult> RunAsync(
            IProgress<(int percent, string status)> progress,
            IProgress<string> detailLog)
        {
            var result = new CleanupResult();

            // Steps and their approximate weight in the 0-100 progress range:
            //  0-10   Kill Word
            // 10-25   File system (install folders + ClickOnce cache + shortcuts)
            // 25-55   Registry (Addins, Uninstall, VSTO, ClickOnce SideBySide)
            // 55-70   Runtime settings (VB Program Settings)
            // 70-80   WebView2 cache
            // 80-95   Word Resiliency
            // 95-100  Done

            progress.Report((0, "בודק תהליכים..."));
            await Task.Run(() => KillWord());

            progress.Report((10, "מוחק תיקיות התקנה..."));
            await CleanFileSystem(result, detailLog);

            progress.Report((25, "מנקה רגיסטרי — Addins..."));
            await CleanAddinRegistry(result, detailLog);

            progress.Report((40, "מנקה רגיסטרי — Uninstall + VSTO Security..."));
            await CleanVstoAndUninstallRegistry(result, detailLog);

            progress.Report((55, "מנקה רגיסטרי — ClickOnce SideBySide..."));
            await CleanClickOnceRegistry(result, detailLog);

            progress.Report((65, "מנקה הגדרות זמן-ריצה..."));
            await CleanRuntimeSettingsRegistry(result, detailLog);

            progress.Report((75, "מוחק מטמון WebView2..."));
            await CleanWebView2Cache(result, detailLog);

            progress.Report((85, "מנקה Word Resiliency + מטא-דאטה תוסף..."));
            await CleanWordResiliency(result, detailLog);
            await CleanWordAddinMetadata(result, detailLog);

            progress.Report((95, "מנקה קיצורי דרך..."));
            await CleanShortcuts(result, detailLog);

            progress.Report((100, "הניקוי הושלם."));
            return result;
        }

        // ── Step implementations ─────────────────────────────────────────────────

        private static void KillWord()
        {
            try
            {
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WINWORD"))
                {
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }

        // ── File system ──────────────────────────────────────────────────────────

        private static async Task CleanFileSystem(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                // All known install folders — English and Hebrew names, all locations
                var foldersToDelete = new[]
                {
                    // Current install location (v1.0.24+)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KleiKodesh"),
                    // Old install locations — English name (v1.0.x through ~v1.0.23)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "KleiKodesh"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "KleiKodesh"),
                    // Old install locations — Hebrew name (very first public release)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "כלי קודש"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "כלי קודש"),
                    // Split-name variants (defensive — unlikely but covered)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Klei Kodesh"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "Klei Kodesh"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klei Kodesh"),
                    // Old addin wrote RibbonSettings.csv here (%AppData%, not %LocalAppData%)
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KleiKodesh"),
                };

                foreach (string folder in foldersToDelete)
                    DeleteFolder(folder, result, log);

                // ClickOnce cache — scan %LocalAppData%\Apps\2.0\ for KleiKodesh content
                CleanClickOnceFolders(result, log);
            });
        }

        private static void CleanClickOnceFolders(CleanupResult result, IProgress<string> log)
        {
            try
            {
                string appsBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Apps", "2.0");

                if (!Directory.Exists(appsBase)) return;

                // ClickOnce uses obfuscated subfolder names; scan all files and match
                // against every known KleiKodesh token (all name variants)
                foreach (string file in Directory.GetFiles(appsBase, "*", SearchOption.AllDirectories))
                {
                    if (!ContainsKleiKodesh(file)) continue;

                    // Delete the entire obfuscated leaf folder that contains this file
                    string leafDir = Path.GetDirectoryName(file);
                    DeleteFolder(leafDir, result, log);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"ClickOnce folders: {ex.Message}");
            }
        }

        // ── WebView2 cache ───────────────────────────────────────────────────────

        /// <summary>
        /// Deletes all WebView2 user-data folders written by KleiKodesh components.
        ///
        /// Current path (v3.x+):  %LocalAppData%\KleiKodesh\WebView2Cache
        ///   Written by: KleiKodeshWebView.cs (Kezayit panel)
        ///   Lives inside the install folder — deleted with it, but cleaned explicitly here.
        ///
        /// Old scattered paths — written by WebViewHost.cs / WebViewControl.cs before the
        /// fix that moved them inside the install folder. WebView2 creates an "EBWebView"
        /// subfolder inside whatever path is passed as userDataFolder:
        ///
        ///   %LocalAppData%\EBWebView          — WebView2 created this when given %LocalAppData% root
        ///   %LocalAppData%\WebView2SharedCache — old explicit name used in early versions
        /// </summary>
        private static async Task CleanWebView2Cache(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Current path — inside the install folder
                DeleteFolder(Path.Combine(localAppData, "KleiKodesh", "WebView2Cache"), result, log);

                // Old ghost: WebView2 creates "EBWebView" when given %LocalAppData% as root.
                // Only delete it if it was ours — check for KleiKodesh content inside.
                // We can't blindly delete EBWebView because other apps may use it too.
                // Instead we check for our specific profile subfolder name pattern.
                CleanEbWebViewFolder(localAppData, result, log);

                // Old explicit standalone path
                DeleteFolder(Path.Combine(localAppData, "WebView2SharedCache"), result, log);
            });
        }

        /// <summary>
        /// Cleans %LocalAppData%\EBWebView subfolders that belong to KleiKodesh.
        ///
        /// WebView2 creates this folder when the host app passes %LocalAppData% as
        /// userDataFolder (the bug that existed before v3.x). The subfolder name is
        /// derived from the userDataFolder path — when the path IS %LocalAppData%,
        /// WebView2 uses the process name as the profile name, so the subfolder is
        /// named after the executable: "KleiKodeshVstoInstallerWpf" or "WINWORD".
        ///
        /// We only delete subfolders whose name contains a KleiKodesh token.
        /// We never delete the EBWebView root or any subfolder not matching our tokens.
        /// </summary>
        private static void CleanEbWebViewFolder(string localAppData, CleanupResult result, IProgress<string> log)
        {
            string ebWebView = Path.Combine(localAppData, "EBWebView");
            if (!Directory.Exists(ebWebView)) return;
            try
            {
                foreach (string sub in Directory.GetDirectories(ebWebView))
                {
                    // Only delete subfolders whose name contains a KleiKodesh token.
                    // Never delete by content scan — too risky.
                    if (ContainsKleiKodesh(Path.GetFileName(sub)))
                        DeleteFolder(sub, result, log);
                }
            }
            catch (Exception ex) { result.Errors.Add($"EBWebView scan: {ex.Message}"); }
        }

        // ── Runtime settings (VB Program Settings) ───────────────────────────────

        /// <summary>
        /// Deletes runtime settings written by SettingsManager (VB Interaction.SaveSetting).
        ///
        /// The current addin uses AppName = "KleiKodesh", so ALL its settings live under:
        ///   HKCU\Software\VB and VBA Program Settings\KleiKodesh\**
        /// That entire subtree is safe to delete.
        ///
        /// ZayitApp is written by our installer and the Kezayit app — entirely ours, safe to delete.
        ///
        /// ⚠ We do NOT touch HKCU\...\WINWORD\** — that key is shared with Word itself
        ///   and potentially with other add-ins. The old GitHub repo (v2.0.1) used
        ///   AppName = "WINWORD", but those settings are indistinguishable from Word's own
        ///   settings and from other add-ins' settings. Deleting them risks breaking Word
        ///   or other installed add-ins. The risk outweighs the benefit of cleaning a
        ///   handful of stale values from a very old version.
        /// </summary>
        private static async Task CleanRuntimeSettingsRegistry(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                const string vbBase = @"Software\VB and VBA Program Settings";

                // KleiKodesh — delete entire subtree (safe: exclusively owned by this addin)
                DeleteRegistrySubtree(
                    Registry.CurrentUser,
                    $@"{vbBase}\KleiKodesh",
                    result, log);

                // ZayitApp — delete the entire subtree.
                // Written by our installer (Database\Path) and by the Kezayit app
                // (HebrewBooks\CsvLastUpdated, etc.). Entirely owned by KleiKodesh.
                DeleteRegistrySubtree(
                    Registry.CurrentUser,
                    $@"{vbBase}\ZayitApp",
                    result, log);

                // ⚠ WINWORD key intentionally NOT touched — shared with Word and other add-ins.
            });
        }

        // ── Addin registry (Addins + AddinsData) ─────────────────────────────────

        private static async Task CleanAddinRegistry(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                foreach (string appName in AppNameVariants)
                {
                    string addinPath     = $@"Software\Microsoft\Office\Word\Addins\{appName}";
                    string addinDataPath = $@"Software\Microsoft\Office\Word\AddinsData\{appName}";

                    // HKCU
                    DeleteRegistrySubtree(Registry.CurrentUser, addinPath,     result, log);
                    DeleteRegistrySubtree(Registry.CurrentUser, addinDataPath, result, log);

                    // HKLM 64-bit
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry64, addinPath,     result, log);
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry64, addinDataPath, result, log);

                    // HKLM 32-bit (Wow6432Node)
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry32, addinPath,     result, log);
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry32, addinDataPath, result, log);
                }

                // Also clean the app version stamp
                DeleteRegistrySubtree(Registry.CurrentUser, @"SOFTWARE\KleiKodesh", result, log);

                // ── Corrupted Hebrew ghost key in HKLM 32-bit ──────────────────────────
                // The very first public release registered under "כלי קודש" (Hebrew) in HKLM.
                // On some machines the key name was written with a broken code page and the
                // subkey name now reads as U+FFFD replacement characters: "\uFFFD\uFFFD\uFFFD \uFFFD\uFFFD\uFFFD\uFFFD"
                // (3 replacement chars, space, 4 replacement chars — matching the shape of "כלי קודש").
                // We scan all subkeys of the Addins parent and delete any whose Manifest value
                // points to a KleiKodesh path, catching both the readable Hebrew and the corrupted form.
                CleanCorruptedHklmAddinKeys(result, log);
            });
        }

        /// <summary>
        /// Scans HKLM\...\Word\Addins (32-bit and 64-bit) for any subkey whose Manifest
        /// value contains a KleiKodesh path. This catches the corrupted Hebrew ghost key
        /// (bytes: FD FF FD FF FD FF 20 00 FD FF FD FF FD FF FD FF) that reg.exe displays
        /// as "��� ����" and that cannot be addressed by name via the normal string API.
        /// </summary>
        private static void CleanCorruptedHklmAddinKeys(CleanupResult result, IProgress<string> log)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey addinsKey = baseKey.OpenSubKey(
                        @"Software\Microsoft\Office\Word\Addins", writable: true))
                    {
                        if (addinsKey == null) continue;

                        foreach (string subName in addinsKey.GetSubKeyNames().ToArray())
                        {
                            // Skip names we already handle via AppNameVariants
                            if (AppNameVariants.Any(v =>
                                    string.Equals(v, subName, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            try
                            {
                                bool isOurs = false;
                                using (RegistryKey sub = addinsKey.OpenSubKey(subName))
                                {
                                    string manifest = sub?.GetValue("Manifest") as string ?? "";
                                    string friendly = sub?.GetValue("FriendlyName") as string ?? "";
                                    isOurs = ContainsKleiKodesh(manifest) || ContainsKleiKodesh(friendly);
                                }
                                if (!isOurs) continue;

                                addinsKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                                string viewLabel = view == RegistryView.Registry32 ? " (32-bit)" : "";
                                string fullPath = $@"HKLM{viewLabel}\Software\Microsoft\Office\Word\Addins\{subName}";
                                result.DeletedRegistryKeys.Add(fullPath);
                                log.Report($"🗝 {fullPath}");
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add($"HKLM Addins scan [{subName}]: {ex.Message}");
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException) { /* no elevation — skip */ }
                catch (Exception ex) { result.Errors.Add($"HKLM Addins scan: {ex.Message}"); }
            }
        }

        // ── VSTO Security + Uninstall entries ────────────────────────────────────

        private static async Task CleanVstoAndUninstallRegistry(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                // Uninstall entries — all name variants, HKLM + HKCU
                // Covers both exact key names AND a scan by DisplayName value,
                // because the old installer used DisplayName = "כלי קודש v2.0.22"
                // (version appended) while the key name was just "כלי קודש".
                foreach (string appName in AppNameVariants)
                {
                    string uninstallPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{appName}";
                    DeleteRegistrySubtree(Registry.CurrentUser, uninstallPath, result, log);
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry64, uninstallPath, result, log);
                    DeleteRegistrySubtreeHive(RegistryHive.LocalMachine, RegistryView.Registry32, uninstallPath, result, log);
                }

                // Scan ALL uninstall entries by DisplayName value — catches entries
                // like "כלי קודש v2.0.22" where the key name doesn't match our tokens
                // but the DisplayName does. This is the root cause of the "2 apps" problem.
                CleanUninstallEntriesByDisplayName(result, log);

                // VSTO Security — Inclusion list: scan for KleiKodesh entries by Url value
                CleanVstoInclusionList(result, log);

                // VSTO Security — TrustedPaths: scan for KleiKodesh entries by Path value
                CleanVstoTrustedPaths(result, log);

                // VSTO SolutionMetadata — scan values by URL key and subkeys by addInName value
                CleanVstoSolutionMetadata(result, log);
            });
        }

        /// <summary>
        /// Scans all Uninstall registry entries across HKCU and HKLM (32+64 bit) and
        /// deletes any whose DisplayName value contains a KleiKodesh token.
        ///
        /// This catches the old GitHub repo's entry "כלי קודש v2.0.22" — the key name
        /// was "כלי קודש" (already covered by AppNameVariants) but also any future
        /// variant where the key name differs from the display name.
        /// </summary>
        private static void CleanUninstallEntriesByDisplayName(CleanupResult result, IProgress<string> log)
        {
            const string uninstallBase = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

            // HKCU
            ScanUninstallHive(Registry.CurrentUser, uninstallBase, "HKCU", result, log);

            // HKLM 64-bit
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    ScanUninstallHive(baseKey, uninstallBase, "HKLM", result, log);
            }
            catch (UnauthorizedAccessException) { }

            // HKLM 32-bit
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    ScanUninstallHive(baseKey, uninstallBase, "HKLM (32-bit)", result, log);
            }
            catch (UnauthorizedAccessException) { }
        }

        private static void ScanUninstallHive(
            RegistryKey hive, string uninstallBase, string hiveName,
            CleanupResult result, IProgress<string> log)
        {
            try
            {
                using (RegistryKey uninstKey = hive.OpenSubKey(uninstallBase, writable: true))
                {
                    if (uninstKey == null) return;
                    foreach (string subName in uninstKey.GetSubKeyNames().ToArray())
                    {
                        // Skip if already handled by name
                        if (ContainsKleiKodesh(subName)) continue;

                        try
                        {
                            string displayName;
                            using (RegistryKey sub = uninstKey.OpenSubKey(subName))
                                displayName = sub?.GetValue("DisplayName") as string ?? "";

                            if (!ContainsKleiKodesh(displayName)) continue;

                            uninstKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                            string fullPath = $@"{hiveName}\{uninstallBase}\{subName} (DisplayName={displayName})";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"Uninstall scan [{subName}]: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { result.Errors.Add($"Uninstall scan {hiveName}: {ex.Message}"); }
        }

        private static void CleanVstoInclusionList(CleanupResult result, IProgress<string> log)
        {
            const string inclusionPath = @"SOFTWARE\Microsoft\VSTO\Security\Inclusion";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(inclusionPath, writable: true))
                {
                    if (key == null) return;
                    foreach (string subName in key.GetSubKeyNames().ToArray())
                    {
                        try
                        {
                            using (RegistryKey entry = key.OpenSubKey(subName))
                            {
                                string url = entry?.GetValue("Url") as string ?? "";
                                if (!ContainsKleiKodesh(url)) continue;
                            }
                            key.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                            string fullPath = $@"HKCU\{inclusionPath}\{subName}";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"VSTO Inclusion {subName}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { result.Errors.Add($"VSTO Inclusion list: {ex.Message}"); }
        }

        private static void CleanVstoTrustedPaths(CleanupResult result, IProgress<string> log)
        {
            const string trustedPath = @"SOFTWARE\Microsoft\VSTO\Security\TrustedPaths";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(trustedPath, writable: true))
                {
                    if (key == null) return;
                    foreach (string subName in key.GetSubKeyNames().ToArray())
                    {
                        try
                        {
                            using (RegistryKey entry = key.OpenSubKey(subName))
                            {
                                string path = entry?.GetValue("Path") as string ?? "";
                                if (!ContainsKleiKodesh(path)) continue;
                            }
                            key.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                            string fullPath = $@"HKCU\{trustedPath}\{subName}";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"VSTO TrustedPaths {subName}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { result.Errors.Add($"VSTO TrustedPaths: {ex.Message}"); }
        }

        // ── ClickOnce SideBySide registry ────────────────────────────────────────

        /// <summary>
        /// Cleans HKCU\Software\Microsoft\VSTO\SolutionMetadata.
        ///
        /// This key has two kinds of entries:
        ///   1. Named values on the key itself — key = manifest URL, value = GUID string.
        ///      We delete values whose name (the URL) contains a KleiKodesh token.
        ///   2. Subkeys named by GUID — contain addInName, friendlyName etc.
        ///      We delete subkeys whose addInName value contains a KleiKodesh token.
        ///
        /// Other VSTO add-ins have their own GUIDs and manifest URLs — no collision possible.
        /// </summary>
        private static void CleanVstoSolutionMetadata(CleanupResult result, IProgress<string> log)
        {
            const string metaPath = @"Software\Microsoft\VSTO\SolutionMetadata";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(metaPath, writable: true))
                {
                    if (key == null) return;

                    // 1. Named values — key is the manifest URL
                    foreach (string valueName in key.GetValueNames().ToArray())
                    {
                        if (!ContainsKleiKodesh(valueName)) continue;
                        try
                        {
                            key.DeleteValue(valueName, throwOnMissingValue: false);
                            string fullPath = $@"HKCU\{metaPath} [{valueName}]";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"SolutionMetadata value {valueName}: {ex.Message}"); }
                    }

                    // 2. GUID subkeys — check addInName value
                    foreach (string subName in key.GetSubKeyNames().ToArray())
                    {
                        try
                        {
                            bool isOurs = false;
                            using (RegistryKey sub = key.OpenSubKey(subName))
                            {
                                string addInName = sub?.GetValue("addInName") as string ?? "";
                                isOurs = ContainsKleiKodesh(addInName);
                            }
                            if (!isOurs) continue;

                            key.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                            string fullPath = $@"HKCU\{metaPath}\{subName}";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"SolutionMetadata {subName}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { result.Errors.Add($"VSTO SolutionMetadata: {ex.Message}"); }
        }

        // ── ClickOnce SideBySide registry ────────────────────────────────────────

        private static async Task CleanClickOnceRegistry(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                const string sideBySideBase =
                    @"Software\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\SideBySide\2.0";

                foreach (string section in new[] { "Marks", "PackageMetadata", "Components", "FamilyMap" })
                {
                    string sectionPath = $@"{sideBySideBase}\{section}";
                    try
                    {
                        using (RegistryKey sectionKey = Registry.CurrentUser.OpenSubKey(sectionPath, writable: true))
                        {
                            if (sectionKey == null) continue;
                            foreach (string subName in sectionKey.GetSubKeyNames().ToArray())
                            {
                                if (!ContainsKleiKodesh(subName)) continue;
                                try
                                {
                                    sectionKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                                    string fullPath = $@"HKCU\{sectionPath}\{subName}";
                                    result.DeletedRegistryKeys.Add(fullPath);
                                    log.Report($"🗝 {fullPath}");
                                }
                                catch (Exception ex) { result.Errors.Add($"ClickOnce {section}\\{subName}: {ex.Message}"); }
                            }
                        }
                    }
                    catch (Exception ex) { result.Errors.Add($"ClickOnce {section}: {ex.Message}"); }
                }
            });
        }

        // ── Word per-addin metadata keys ─────────────────────────────────────────

        /// <summary>
        /// Removes KleiKodesh-named values from Word's per-addin metadata keys.
        ///
        /// Word writes these under HKCU\Software\Microsoft\Office\{ver}\Word\:
        ///   AddInLoadTimes\{addinName}          — binary, load timing stats
        ///   AddinEventTimes\Connect\{addinName} — binary, connect timing
        ///   AddinEventTimes\Shutdown\{addinName}— binary, shutdown timing
        ///   NotifiedAddins\{addinName}          — binary, notification state
        ///   (Common\)CustomUIValidationCache\{addinName}.Microsoft.Word.Document — DWORD, ribbon XML hash
        ///
        /// All of these use the add-in's registry key name as the value name, so we can
        /// safely delete only the KleiKodesh-named values without touching other add-ins.
        /// </summary>
        private static async Task CleanWordAddinMetadata(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                foreach (string officeVersion in new[] { "12.0", "14.0", "15.0", "16.0" })
                {
                    string wordBase = $@"Software\Microsoft\Office\{officeVersion}\Word";

                    // AddInLoadTimes — one value per add-in, named by add-in key name
                    DeleteNamedValuesContaining(
                        $@"{wordBase}\AddInLoadTimes",
                        result, log);

                    // AddinEventTimes\Connect and \Shutdown
                    DeleteNamedValuesContaining(
                        $@"{wordBase}\AddinEventTimes\Connect",
                        result, log);
                    DeleteNamedValuesContaining(
                        $@"{wordBase}\AddinEventTimes\Shutdown",
                        result, log);

                    // NotifiedAddins
                    DeleteNamedValuesContaining(
                        $@"{wordBase}\NotifiedAddins",
                        result, log);
                }

                // CustomUIValidationCache — under Common, not Word
                // Value names are "{addinName}.Microsoft.Word.Document"
                foreach (string officeVersion in new[] { "12.0", "14.0", "15.0", "16.0" })
                {
                    DeleteNamedValuesContaining(
                        $@"Software\Microsoft\Office\{officeVersion}\Common\CustomUIValidationCache",
                        result, log);
                }
            });
        }

        /// <summary>
        /// Opens <paramref name="keyPath"/> under HKCU and deletes any named values
        /// whose name contains a KleiKodesh token. Leaves all other values untouched.
        /// </summary>
        private static void DeleteNamedValuesContaining(
            string keyPath, CleanupResult result, IProgress<string> log)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                {
                    if (key == null) return;
                    foreach (string valueName in key.GetValueNames().ToArray())
                    {
                        if (!ContainsKleiKodesh(valueName)) continue;
                        try
                        {
                            key.DeleteValue(valueName, throwOnMissingValue: false);
                            string fullPath = $@"HKCU\{keyPath} [{valueName}]";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                        catch (Exception ex) { result.Errors.Add($"{keyPath}[{valueName}]: {ex.Message}"); }
                    }
                }
            }
            catch { /* key doesn't exist for this Office version — skip */ }
        }

        // ── Word Resiliency ──────────────────────────────────────────────────────
        /// <summary>
        /// Removes KleiKodesh from Word's DisabledItems list without touching other add-ins.
        ///
        /// Word writes DisabledItems (a binary blob) when an add-in fails to load, which
        /// causes LoadBehavior to flip to 2 and the add-in to appear "disabled".
        ///
        /// The blob is a sequence of variable-length records. We can't reliably parse the
        /// binary format, so we use a safe heuristic: if the blob contains the UTF-16LE
        /// bytes of "KleiKodesh" or "כלי קודש" we delete the whole value. Word recreates
        /// it from scratch on next launch — other add-ins will be re-evaluated normally.
        ///
        /// If the blob does NOT contain a KleiKodesh reference we leave it untouched,
        /// so other add-ins' disabled state is preserved.
        /// </summary>
        private static async Task CleanWordResiliency(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                // Office versions: 12.0=2007, 14.0=2010, 15.0=2013, 16.0=2016/2019/365
                foreach (string officeVersion in new[] { "12.0", "14.0", "15.0", "16.0" })
                {
                    string resiliencyPath = $@"Software\Microsoft\Office\{officeVersion}\Word\Resiliency";
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(resiliencyPath, writable: true))
                        {
                            if (key == null) continue;

                            byte[] blob = key.GetValue("DisabledItems") as byte[];
                            if (blob == null || blob.Length == 0) continue;

                            // Check whether the blob contains a KleiKodesh reference
                            // (UTF-16LE encoding, as used by Windows registry strings)
                            if (!BlobContainsKleiKodesh(blob)) continue;

                            key.DeleteValue("DisabledItems", throwOnMissingValue: false);
                            string fullPath = $@"HKCU\{resiliencyPath}\DisabledItems";
                            result.DeletedRegistryKeys.Add(fullPath);
                            log.Report($"🗝 {fullPath}");
                        }
                    }
                    catch (Exception ex) { result.Errors.Add($"Resiliency {officeVersion}: {ex.Message}"); }
                }
            });
        }

        /// <summary>
        /// Returns true if the DisabledItems binary blob contains any KleiKodesh identifier token
        /// encoded as UTF-16LE (the encoding Windows uses for registry string data).
        /// Covers all name variants: camelCase, split, lowercase, Hebrew joined/split.
        /// </summary>
        private static bool BlobContainsKleiKodesh(byte[] blob)
        {
            foreach (string token in KleiKodeshTokens)
            {
                // UTF-16LE — how Windows stores strings in binary registry values
                byte[] needle = System.Text.Encoding.Unicode.GetBytes(token);
                if (ContainsSequence(blob, needle)) return true;

                // Also check uppercase variant (OrdinalIgnoreCase on strings doesn't help
                // with raw bytes, so we encode both cases explicitly for ASCII tokens)
                byte[] needleUpper = System.Text.Encoding.Unicode.GetBytes(token.ToUpperInvariant());
                if (!needleUpper.SequenceEqual(needle) && ContainsSequence(blob, needleUpper)) return true;

                byte[] needleLower = System.Text.Encoding.Unicode.GetBytes(token.ToLowerInvariant());
                if (!needleLower.SequenceEqual(needle) && ContainsSequence(blob, needleLower)) return true;
            }
            return false;
        }

        private static bool ContainsSequence(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return false;
            int limit = haystack.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }

        // ── Shortcuts ────────────────────────────────────────────────────────────

        private static async Task CleanShortcuts(CleanupResult result, IProgress<string> log)
        {
            await Task.Run(() =>
            {
                // Start Menu
                string startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs");

                // Desktop
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                foreach (string dir in new[] { startMenu, desktop })
                {
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        // Folders named KleiKodesh* or כלי קודש* in Start Menu
                        foreach (string folder in Directory.GetDirectories(dir))
                        {
                            string name = Path.GetFileName(folder);
                            if (ContainsKleiKodesh(name))
                                DeleteFolder(folder, result, log);
                        }

                        // .lnk files on Desktop (and in Start Menu root)
                        foreach (string file in Directory.GetFiles(dir, "*.lnk"))
                        {
                            if (ContainsKleiKodesh(Path.GetFileName(file)))
                                DeleteFile(file, result, log);
                        }
                    }
                    catch (Exception ex) { result.Errors.Add($"Shortcuts in {dir}: {ex.Message}"); }
                }
            });
        }

        // ── Low-level helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="s"/> contains any known KleiKodesh identifier token.
        /// Covers all name variants: camelCase, split, lowercase, Hebrew joined/split.
        /// Matching is case-insensitive.
        /// </summary>
        private static bool ContainsKleiKodesh(string s)
        {
            if (s == null) return false;
            foreach (string token in KleiKodeshTokens)
            {
                if (s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void DeleteFolder(string path, CleanupResult result, IProgress<string> log)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                // Clear read-only attributes before deleting
                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
                result.DeletedPaths.Add(path);
                log.Report($"📁 {path}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{path}: {ex.Message}");
                // Fallback: delete file by file
                TryDeleteFilesIndividually(path, result, log);
            }
        }

        private static void TryDeleteFilesIndividually(string path, CleanupResult result, IProgress<string> log)
        {
            try
            {
                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        result.DeletedPaths.Add(f);
                        log.Report($"📄 {f}");
                    }
                    catch (Exception ex) { result.Errors.Add($"{f}: {ex.Message}"); }
                }
            }
            catch { }
        }

        private static void DeleteFile(string path, CleanupResult result, IProgress<string> log)
        {
            if (!File.Exists(path)) return;
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                result.DeletedPaths.Add(path);
                log.Report($"📄 {path}");
            }
            catch (Exception ex) { result.Errors.Add($"{path}: {ex.Message}"); }
        }

        private static void DeleteRegistrySubtree(
            RegistryKey hive, string subKeyPath,
            CleanupResult result, IProgress<string> log)
        {
            try
            {
                // Check existence before attempting delete (avoids spurious error entries)
                using (RegistryKey check = hive.OpenSubKey(subKeyPath))
                {
                    if (check == null) return;
                }
                hive.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                string hiveName = hive == Registry.CurrentUser ? "HKCU" : "HKLM";
                string fullPath = $@"{hiveName}\{subKeyPath}";
                result.DeletedRegistryKeys.Add(fullPath);
                log.Report($"🗝 {fullPath}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($@"{subKeyPath}: {ex.Message}");
            }
        }

        private static void DeleteRegistrySubtreeHive(
            RegistryHive hive, RegistryView view, string subKeyPath,
            CleanupResult result, IProgress<string> log)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                {
                    using (RegistryKey check = baseKey.OpenSubKey(subKeyPath))
                    {
                        if (check == null) return;
                    }
                    baseKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                    string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                    string viewName = view == RegistryView.Registry32 ? " (32-bit)" : "";
                    string fullPath = $@"{hiveName}{viewName}\{subKeyPath}";
                    result.DeletedRegistryKeys.Add(fullPath);
                    log.Report($"🗝 {fullPath}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                // HKLM without elevation — silently skip, not an error
            }
            catch (Exception ex)
            {
                result.Errors.Add($@"{subKeyPath}: {ex.Message}");
            }
        }
    }
}
