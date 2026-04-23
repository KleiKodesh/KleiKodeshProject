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
            // Load entries using priority order:
            //   1. PendingWhitelist — user already edited this session
            //   2. Installed file   — update scenario, preserve user's existing list
            //   3. Embedded default — fresh install
            // If user clicks OK, serialize back into PendingWhitelist so ExtractAsync
            // skips the zip entry and ApplyPendingWhitelist() writes the edited version.
            // If user clicks Cancel, PendingWhitelist is unchanged (null or prior edit).
            var entries = LoadEntries();
            var dialog  = new WhitelistEditorDialog(entries) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                AddinInstaller.PendingWhitelist = SerializeWhitelistJson(dialog.Entries);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToSettings();

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!WordHelper.EnsureWordClosed()) return;
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
        /// Priority:
        ///   1. PendingWhitelist  — user already edited this session (in-memory)
        ///   2. Embedded default + user's visibility from installed file — update scenario
        ///   3. Embedded default  — fresh install
        /// </summary>
        private static ObservableCollection<WhitelistEntry> LoadEntries()
        {
            // 1. Already edited this session
            if (AddinInstaller.PendingWhitelist != null)
                return Deserialize(AddinInstaller.PendingWhitelist);

            // Load the default list
            string defaultJson = LoadDefaultJson();
            var entries = defaultJson != null ? Deserialize(defaultJson) : new ObservableCollection<WhitelistEntry>();

            // 2. Existing install — merge user's visibility choices
            string installedPath = Path.Combine(AddinInstaller.InstallPath, "WebSitesWhitelist.json");
            if (File.Exists(installedPath))
            {
                try
                {
                    string installedJson = File.ReadAllText(installedPath);
                    var installedEntries = ParseWhitelistJson(installedJson);
                    // For each installed entry, find matching default entry by Url and update IsVisible
                    foreach (var installed in installedEntries)
                    {
                        var defaultEntry = entries.FirstOrDefault(e => e.Url == installed.Url);
                        if (defaultEntry != null)
                            defaultEntry.IsVisible = installed.IsVisible;
                    }
                }
                catch { /* ignore errors, keep defaults */ }
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
            foreach (var e in entries)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                sb.AppendLine("  {");
                sb.AppendLine($"    \"Name\": {J(e.Name)},");
                sb.AppendLine($"    \"Description\": {J(e.Description)},");
                sb.AppendLine($"    \"Url\": {J(e.Url)},");
                sb.Append    ($"    \"IsVisible\": {(e.IsVisible ? "true" : "false")}");
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
