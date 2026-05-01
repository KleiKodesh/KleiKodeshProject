using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class SettingsPage : Page
    {
        private readonly MainWindow _host;

        private readonly Dictionary<string, bool> _visibleFlags = new Dictionary<string, bool>();
        private string _defaultButton;

        public SettingsPage(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            LoadSettings();
        }

        private void LoadSettings()
        {
            // cb.Name IS the registry key (e.g. "Kezayit_Visible") — do not rename the controls.
            foreach (System.Windows.Controls.CheckBox cb in VisibleSettingsPanel.Children)
            {
                bool val = SettingsManager.GetBool("Ribbon", cb.Name, true);
                _visibleFlags[cb.Name] = val;
                cb.IsChecked = val;
                cb.Checked   += (s, e) => _visibleFlags[cb.Name] = true;
                cb.Unchecked += (s, e) => _visibleFlags[cb.Name] = false;
            }

            _defaultButton = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            // rb.Name stripped of "_Option" IS the saved DefaultButton value (e.g. "Kezayit_Option" → "Kezayit").
            // Do not rename the controls.
            foreach (System.Windows.Controls.RadioButton rb in OptionsSettingsPanel.Children)
            {
                if (rb.Name.Contains(_defaultButton))
                    rb.IsChecked = true;
                rb.Checked += (s, e) =>
                    _defaultButton = rb.Name.Replace("_Option", "");
            }
        }

        private void CommitSettings()
        {
            foreach (var kv in _visibleFlags)
                SettingsManager.Save("Ribbon", kv.Key, kv.Value);

            SettingsManager.Save("Ribbon", "DefaultButton", _defaultButton);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSettings();
            bool kezayit  = _visibleFlags.TryGetValue("Kezayit_Visible",  out bool k) && k;
            bool webSites = _visibleFlags.TryGetValue("WebSites_Visible", out bool w) && w;

            if (kezayit || webSites)
                _host.NavigateToAdvanced(kezayit, webSites);
            else
                Environment.Exit(0);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSettings();
            Environment.Exit(0);
        }
    }
}
