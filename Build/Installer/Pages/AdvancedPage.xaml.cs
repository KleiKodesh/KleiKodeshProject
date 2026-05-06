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
    public partial class AdvancedPage : Page
    {
        private readonly MainWindow _host;
        private readonly bool _showKezayit;
        private readonly bool _showWebsites;
        private string _pendingDbPath;

        public AdvancedPage(MainWindow host, bool showKezayit, bool showWebsites)
        {
            InitializeComponent();
            _host        = host;
            _showKezayit = showKezayit;
            _showWebsites = showWebsites;

            ApplyVisibility();
            LoadDbPath();
        }

        private void ApplyVisibility()
        {
            KezayitSection.Visibility  = _showKezayit  ? Visibility.Visible : Visibility.Collapsed;
            WebsitesSection.Visibility = _showWebsites ? Visibility.Visible : Visibility.Collapsed;
            // Hide divider if either section is hidden
            SectionDivider.Visibility  = (_showKezayit && _showWebsites)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadDbPath()
        {
            string existing = Interaction.GetSetting("ZayitApp", "Database", "Path", "");
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
                string existing = Interaction.GetSetting("ZayitApp", "Database", "Path", "");
                DbHintText.Text = string.IsNullOrEmpty(existing)
                    ? "לא הוגדר נתיב — ישתמש בברירת המחדל של אפליקציית זית"
                    : "לחץ לשינוי הנתיב";
            }
        }

        private void ZayitDbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "בחר קובץ מסד נתונים לכזית",
                    Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                    CheckFileExists = true
                };

                string seedPath = _pendingDbPath ?? Interaction.GetSetting("ZayitApp", "Database", "Path", "");
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
            // On OK → serialize only the checked entries into PendingWhitelist.
            // On Cancel → PendingWhitelist unchanged (null or prior edit).
            var entries = LoadEntries();
            var dialog  = new WhitelistEditorDialog(entries) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                AddinInstaller.PendingWhitelist = SerializeWhitelistJson(dialog.Entries);
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
                Interaction.SaveSetting("ZayitApp", "Database", "Path", _pendingDbPath);
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
        ///
        /// If the user already edited the list this session (PendingWhitelist != null),
        /// that is used instead so re-opening the dialog shows their prior choices.
        /// </summary>
        private static ObservableCollection<WhitelistEntry> LoadEntries()
        {
            // Already edited this session — show their prior choices
            if (AddinInstaller.PendingWhitelist != null)
                return Deserialize(AddinInstaller.PendingWhitelist);

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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[");
            bool first = true;
            foreach (var e in entries.Where(e => e.IsVisible))
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
