using KleiKodesh.Helpers;
using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Second settings page — general (non-ribbon) options: interface language and the
    /// optional VBA macros template. Follows SettingsPage.
    ///
    /// Navigation:
    ///   "back" → SettingsPage
    ///   "next" → ComponentSettingsPage (only if KitveiHakodesh or WebSites is enabled — those have
    ///            component-specific settings like DB path and website whitelist), otherwise exit
    ///   "close" → exit
    /// </summary>
    public partial class GeneralSettingsPage : Page
    {
        private readonly MainWindow _host;
        private readonly bool _fromLanding;

        public GeneralSettingsPage(MainWindow host, bool fromLanding)
        {
            InitializeComponent();
            _host = host;
            _fromLanding = fromLanding;
            LoadSettings();
        }

        private void LoadSettings()
        {
            bool english = UiLanguage.Current == UiLanguage.English;
            EnglishLanguageRadio.IsChecked = english;
            HebrewLanguageRadio.IsChecked  = !english;
            EnglishLanguageRadio.Checked += (s, e) => SwitchLanguage(UiLanguage.English);
            HebrewLanguageRadio.Checked  += (s, e) => SwitchLanguage(UiLanguage.Hebrew);

            LoadMacros();
            LoadShellPreference();
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

        // ── VBA macros template (opt-in; shared logic in VbaMacrosTemplate) ─────

        private void LoadMacros()
        {
            bool english = UiLanguage.Current == UiLanguage.English;
            MacrosHeader.Text          = VbaMacrosTemplate.HeaderText(english);
            MacrosNoneRadio.Content    = VbaMacrosTemplate.NoneText(english);
            MacrosHebrewRadio.Content  = VbaMacrosTemplate.HebrewNamesText(english);
            MacrosEnglishRadio.Content = VbaMacrosTemplate.EnglishNamesText(english);
            MacrosHint.Text            = VbaMacrosTemplate.HintText(english);

            string installed = VbaMacrosTemplate.Installed;
            MacrosNoneRadio.IsChecked    = installed == null;
            MacrosHebrewRadio.IsChecked  = installed == VbaMacrosTemplate.Hebrew;
            MacrosEnglishRadio.IsChecked = installed == VbaMacrosTemplate.English;
            MacrosNoneRadio.Checked    += (s, e) => InstallMacros(null);
            MacrosHebrewRadio.Checked  += (s, e) => InstallMacros(VbaMacrosTemplate.Hebrew);
            MacrosEnglishRadio.Checked += (s, e) => InstallMacros(VbaMacrosTemplate.English);
        }

        // Word loads the template from STARTUP on its next start; a running Word has to
        // be reopened (and may hold the old one open, which makes the copy fail).
        private void InstallMacros(string lang)
        {
            bool english = UiLanguage.Current == UiLanguage.English;
            try
            {
                VbaMacrosTemplate.Install(lang, AddinInstaller.MacrosResourcesFolder);
                if (WordHelper.IsWordRunning())
                    MessageBox.Show(VbaMacrosTemplate.ReopenWordText(english), Properties.Strings.Settings,
                        MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, UiLanguage.MessageBoxOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show(VbaMacrosTemplate.FailedText(english) + "\n" + ex.Message, Properties.Strings.Settings,
                    MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, UiLanguage.MessageBoxOptions);
            }
        }

        // Saves the language (General/UiLanguage, shared with the add-in), tells the user
        // to restart Word if it is running, and reloads this page in the new language.
        private void SwitchLanguage(string lang)
        {
            if (UiLanguage.Current == lang) return;
            UiLanguage.Set(lang);
            if (WordHelper.IsWordRunning())
                MessageBox.Show(Properties.Strings.Settings_RestartWordNotice, Properties.Strings.Settings,
                    MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, UiLanguage.MessageBoxOptions);
            _host.NavigateToGeneralSettings(_fromLanding);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            bool KitveiHakodesh = SettingsManager.GetBool("Ribbon", "KitveiHakodesh_Visible", true);
            bool webSites       = SettingsManager.GetBool("Ribbon", "WebSites_Visible", true);

            // Only KitveiHakodesh and WebSites have component-specific settings (DB path, whitelist).
            // If neither is enabled there's nothing more to configure — exit immediately.
            if (KitveiHakodesh || webSites)
                _host.NavigateToComponentSettings(KitveiHakodesh, webSites);
            else
                WordHelper.ExitAfterInstall();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToSettings(_fromLanding);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => WordHelper.ExitAfterInstall();
    }
}
