using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class SettingsPage : Page
    {
        private readonly MainWindow _host;

        private readonly Dictionary<string, bool> _visibleFlags = new Dictionary<string, bool>();
        private string _defaultButton;
        private string _pendingDbPath;

        public SettingsPage(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            LoadSettings();
        }

        private void LoadSettings()
        {
            foreach (System.Windows.Controls.CheckBox cb in VisibleSettingsPanel.Children)
            {
                bool val = SettingsManager.GetBool("Ribbon", cb.Name, true);
                _visibleFlags[cb.Name] = val;
                cb.IsChecked = val;
                cb.Checked   += (s, e) => _visibleFlags[cb.Name] = true;
                cb.Unchecked += (s, e) => _visibleFlags[cb.Name] = false;
            }

            _defaultButton = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            foreach (System.Windows.Controls.RadioButton rb in OptionsSettingsPanel.Children)
            {
                if (rb.Name.Contains(_defaultButton))
                    rb.IsChecked = true;
                rb.Checked += (s, e) =>
                    _defaultButton = rb.Name.Replace("_Option", "");
            }

            string currentPath = Interaction.GetSetting("ZayitApp", "Database", "Path", "");
            if (!string.IsNullOrEmpty(currentPath))
                DbPathText.Text = currentPath;

            UpdateDbHint();
        }

        private void CommitSettings()
        {
            foreach (var kv in _visibleFlags)
                SettingsManager.Save("Ribbon", kv.Key, kv.Value);

            SettingsManager.Save("Ribbon", "DefaultButton", _defaultButton);

            if (_pendingDbPath != null)
                Interaction.SaveSetting("ZayitApp", "Database", "Path", _pendingDbPath);
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

        private void BackButton_Click(object sender, RoutedEventArgs e)    => _host.NavigateToLanding();
        private void InstallButton_Click(object sender, RoutedEventArgs e) => Install();

        private void ZayitDbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
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

        private void Install()
        {
            try
            {
                if (!WordHelper.EnsureWordClosed()) return;
                CommitSettings();
                _host.NavigateToInstall();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בהתקנה: {ex.Message}", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
