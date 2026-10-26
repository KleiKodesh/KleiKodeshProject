using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Step 3 of the installer flow — post-install ribbon configuration.
    ///
    /// Reached from InstallPage after a successful install (showSettingsAfter: true).
    /// NOT shown during silent installs.
    ///
    /// Lets the user choose:
    ///   - Which components are visible in the Word ribbon (KitveiHakodesh, WebSites, DocDesign, etc.)
    ///   - Which component is the default/primary ribbon button
    ///
    /// Navigation:
    ///   "next"  → GeneralSettingsPage (language, macros), then ComponentSettingsPage / exit
    ///   "back"  → LandingPage (only when opened straight from the landing page)
    ///   "close" → exit
    ///
    /// NOTE: control x:Names in the XAML (e.g. "KitveiHakodesh_Visible", "WebSites_Option") are
    /// used directly as registry setting keys — do not rename them to follow UI conventions.
    /// </summary>
    public partial class SettingsPage : Page
    {
        private readonly MainWindow _host;

        private readonly Dictionary<string, bool> _visibleFlags = new Dictionary<string, bool>();
        private string _defaultButton;
        private readonly bool _fromLanding;

        // fromLanding: opened straight from the landing page, so Back returns there.
        public SettingsPage(MainWindow host, bool fromLanding = false)
        {
            InitializeComponent();
            _host = host;
            _fromLanding = fromLanding;
            BackButton.Visibility = fromLanding ? Visibility.Visible : Visibility.Collapsed;
            LoadSettings();
        }

        private void LoadSettings()
        {
            // cb.Name IS the registry key (e.g. "KitveiHakodesh_Visible") — do not rename the controls.
            foreach (System.Windows.Controls.CheckBox cb in VisibleSettingsPanel.Children)
            {
                bool val = SettingsManager.GetBool("Ribbon", cb.Name, true);
                _visibleFlags[cb.Name] = val;
                cb.IsChecked = val;
                cb.Checked   += (s, e) => _visibleFlags[cb.Name] = true;
                cb.Unchecked += (s, e) => _visibleFlags[cb.Name] = false;
            }

            _defaultButton = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            // rb.Name stripped of "_Option" IS the saved DefaultButton value (e.g. "KitveiHakodesh_Option" → "KitveiHakodesh").
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
            _host.NavigateToGeneralSettings(_fromLanding);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSettings();
            _host.NavigateToLanding();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSettings();
            WordHelper.ExitAfterInstall();
        }
    }
}
