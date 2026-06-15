using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Step 4 of the installer flow — component-specific post-install configuration.
    ///
    /// Reached from SettingsPage "הבא" when KitveiHakodesh or WebSites (or both) are enabled.
    /// Only the relevant sections are shown — sections for disabled components are collapsed.
    ///
    /// KitveiHakodesh section:   lets the user point to a custom SQLite DB file.
    /// WebSites section:  lets the user edit the website whitelist via WhitelistEditorDialog.
    ///                    Whitelist changes are written to disk immediately on dialog OK —
    ///                    no dependency on navigation or install order.
    ///
    /// Navigation:
    ///   "חזור" → SettingsPage (KitveiHakodesh DB path is committed first)
    ///   "סגור" → exit        (KitveiHakodesh DB path is committed first)
    ///
    /// This is the last page in the normal install flow.
    /// </summary>
    public partial class ComponentSettingsPage : Page
    {
        private readonly MainWindow _host;
        private readonly bool _showKitveiHakodesh;
        private readonly bool _showWebsites;
        private string _pendingDbPath;

        public ComponentSettingsPage(MainWindow host, bool showKitveiHakodesh, bool showWebsites)
        {
            InitializeComponent();
            _host        = host;
            _showKitveiHakodesh = showKitveiHakodesh;
            _showWebsites = showWebsites;

            ApplyVisibility();
            LoadDbPath();
            LoadShellPreference();
        }

        private void ApplyVisibility()
        {
            KitveiHakodeshSection.Visibility  = _showKitveiHakodesh  ? Visibility.Visible : Visibility.Collapsed;
            WebsitesSection.Visibility = _showWebsites ? Visibility.Visible : Visibility.Collapsed;
            // Hide divider if either section is hidden
            SectionDivider.Visibility  = (_showKitveiHakodesh && _showWebsites)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadDbPath()
        {
            string existing = Interaction.GetSetting("KitveiHakodesh", "Database", "Path", "");
            if (!string.IsNullOrEmpty(existing))
                DbPathText.Text = existing;
            UpdateDbHint();
        }

        private void UpdateDbHint()
        {
            if (_pendingDbPath != null)
            {
                DbHintText.Text = "לחץ לשינוי הנתיב";
            }
            else
            {
                string existing = Interaction.GetSetting("KitveiHakodesh", "Database", "Path", "");
                if (string.IsNullOrEmpty(existing))
                {
                    string detected = ResolveDefaultDbPath();
                    DbHintText.Text = File.Exists(detected)
                        ? $"ברירת מחדל: {detected}"
                        : "לא נמצאה אפליקציית זית או אוצריה — הגדר נתיב ידנית";
                }
                else
                {
                    DbHintText.Text = "לחץ לשינוי הנתיב";
                }
            }
        }

        /// <summary>
        /// Resolves the default seforim DB path — ZayitApp first, Otzaria as fallback.
        /// Mirrors AppSettings.ResolveDefaultDbPath() in KitveiHakodeshLib.
        /// </summary>
        private static string ResolveDefaultDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string zayit   = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");
            if (File.Exists(zayit))   return zayit;
            if (File.Exists(otzaria)) return otzaria;
            return zayit;
        }

        private void ZayitDbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "בחר קובץ מסד נתונים לכתבי הקודש",
                    Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                    CheckFileExists = true
                };

                string seedPath = _pendingDbPath ?? Interaction.GetSetting("KitveiHakodesh", "Database", "Path", "");
                if (!string.IsNullOrEmpty(seedPath) && File.Exists(seedPath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(seedPath);
                    dialog.FileName = Path.GetFileName(seedPath);
                }

                if (dialog.ShowDialog() != true) return;
                _pendingDbPath = dialog.FileName;
                DbPathText.Text = dialog.FileName;
                UpdateDbHint();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בהגדרת מסד הנתונים: {ex.Message}", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditWhitelistButton_Click(object sender, RoutedEventArgs e)
        {
            // Load the full default catalogue, with IsVisible pre-set from the user's
            // installed file (or from a prior edit this session).
            // - Entries absent from the installed file → unchecked
            // - Entries present but unchecked in the installed file → unchecked
            // - Entries present and checked in the installed file → checked
            // - Fresh install (no installed file) → use IsVisible from the default catalogue
            // On OK → serialize only the checked entries and write to disk immediately.
            // On Cancel → nothing changes.
            var entries = LoadEntries();
            var dialog  = new WhitelistEditorDialog(entries) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                AddinInstaller.ApplyPendingWhitelist(SerializeWhitelistJson(dialog.Entries));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Commit();
            _host.NavigateToSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Commit();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בהתקנה: {ex.Message}", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Commit()
        {
            if (_pendingDbPath != null)
                Interaction.SaveSetting("KitveiHakodesh", "Database", "Path", _pendingDbPath);
        }

        private void LoadShellPreference()
        {
            // Suppress the Changed handler while we set the initial value.
            ShellRegisterCheckBox.Checked   -= ShellRegisterCheckBox_Changed;
            ShellRegisterCheckBox.Unchecked -= ShellRegisterCheckBox_Changed;
            ShellRegisterCheckBox.IsChecked  = ShellRegistrationHelper.LoadPreference();
            ShellRegisterCheckBox.Checked   += ShellRegisterCheckBox_Changed;
            ShellRegisterCheckBox.Unchecked += ShellRegisterCheckBox_Changed;
        }

        private void ShellRegisterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ShellRegistrationHelper.Apply(ShellRegisterCheckBox.IsChecked == true);
        }

        // ── Whitelist helpers ─────────────────────────────────────────────────────
        //
        // These live here rather than in a shared class because the installer cannot
        // use System.Text.Json or System.Web.Extensions — those assemblies are not
        // available via the embedded-DLL resolver at the point this page loads.
        // The hand-rolled regex parser/serializer below handles the flat JSON array
        // in WebSitesWhitelist.json without any external dependencies.

        /// <summary>
        /// Returns the whitelist entries to show in the editor dialog.
        ///
        /// Always starts from the full default catalogue (all entries, with their
        /// default IsVisible values). Then adjusts IsVisible for each entry based on
        /// the user's currently installed file:
        ///   - Entry URL present in installed file  → checked   (it was checked last time)
        ///   - Entry URL absent from installed file → unchecked (it was unchecked or is new)
        ///   - No installed file (fresh install)    → keep default IsVisible from source JSON
        /// </summary>
        private static ObservableCollection<WhitelistEntry> LoadEntries()
        {
            // Start from the full default catalogue
            string defaultJson = LoadDefaultJson();
            var entries = defaultJson != null
                ? Deserialize(defaultJson)
                : new ObservableCollection<WhitelistEntry>();

            // Merge visibility from the user's installed file.
            // The installed file contains only the entries that were checked —
            // presence means checked, absence means unchecked.
            string installedPath = Path.Combine(AddinInstaller.InstallPath, "WebSitesWhitelist.json");
            if (File.Exists(installedPath))
            {
                try
                {
                    var installedEntries = ParseWhitelistJson(File.ReadAllText(installedPath));
                    var installedUrls = new System.Collections.Generic.HashSet<string>(
                        installedEntries.Select(e => e.Url ?? ""),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in entries)
                        entry.IsVisible = installedUrls.Contains(entry.Url ?? "");
                }
                catch { /* ignore — keep default IsVisible values */ }
            }

            return entries;
        }

        /// <summary>
        /// Loads the default whitelist JSON.
        /// Tries the exe directory first (dev/debug), then the embedded resource.
        /// The embedded resource is linked directly from WebSitesLib2/WebSitesWhitelist.json
        /// — do not add a second copy anywhere.
        /// </summary>
        private static string LoadDefaultJson()
        {
            // Dev/debug: file next to the exe (e.g. bin\Debug\WebSitesWhitelist.json)
            string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebSitesWhitelist.json");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);

            // Release: embedded resource compiled from WebSitesLib2/WebSitesWhitelist.json
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("KleiKodeshVstoInstallerWpf.WebSitesWhitelist.json"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static ObservableCollection<WhitelistEntry> Deserialize(string json)
        {
            try { return new ObservableCollection<WhitelistEntry>(ParseWhitelistJson(json)); }
            catch { return new ObservableCollection<WhitelistEntry>(); }
        }

        private static System.Collections.Generic.List<WhitelistEntry> ParseWhitelistJson(string json)
        {
            var result     = new System.Collections.Generic.List<WhitelistEntry>();
            var objPattern = new Regex(@"\{([^{}]*)\}", RegexOptions.Singleline);
            var strProp    = new Regex(@"""(\w+)""\s*:\s*""((?:[^""\\]|\\.)*)""");
            var boolProp   = new Regex(@"""(\w+)""\s*:\s*(true|false)", RegexOptions.IgnoreCase);

            foreach (Match obj in objPattern.Matches(json))
            {
                var entry = new WhitelistEntry();
                string block = obj.Groups[1].Value;
                foreach (Match m in strProp.Matches(block))
                {
                    string key = m.Groups[1].Value;
                    string val = Regex.Unescape(m.Groups[2].Value);
                    if      (key.Equals("Name",        StringComparison.OrdinalIgnoreCase)) entry.Name        = val;
                    else if (key.Equals("Description", StringComparison.OrdinalIgnoreCase)) entry.Description = val;
                    else if (key.Equals("Url",         StringComparison.OrdinalIgnoreCase)) entry.Url         = val;
                }
                foreach (Match m in boolProp.Matches(block))
                    if (m.Groups[1].Value.Equals("IsVisible", StringComparison.OrdinalIgnoreCase))
                        entry.IsVisible = m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(entry.Name) || !string.IsNullOrEmpty(entry.Url))
                    result.Add(entry);
            }
            return result;
        }

        private static string SerializeWhitelistJson(System.Collections.Generic.IEnumerable<WhitelistEntry> entries)
        {
            var list = entries.Where(e => e.IsVisible).ToList();

            // The KleiKodesh website is always included — ensure it's present even if
            // the user unchecked everything.
            const string KleiKodeshUrl = "https://kleikodesh.github.io/";
            if (!list.Any(e => string.Equals(e.Url, KleiKodeshUrl, StringComparison.OrdinalIgnoreCase)))
            {
                var all = entries.ToList();
                var entry = all.FirstOrDefault(e =>
                    string.Equals(e.Url, KleiKodeshUrl, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                    list.Insert(0, entry);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[");
            bool first = true;
            foreach (var e in list)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                sb.AppendLine("  {");
                sb.AppendLine($"    \"Name\": {J(e.Name)},");
                sb.AppendLine($"    \"Description\": {J(e.Description)},");
                sb.Append    ($"    \"Url\": {J(e.Url)}");
                sb.AppendLine();
                sb.Append("  }");
            }
            sb.AppendLine(); sb.Append("]");
            return sb.ToString();
        }

        private static string J(string s) =>
            s == null ? "\"\"" :
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}
