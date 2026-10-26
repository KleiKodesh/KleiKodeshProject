using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Step 1 of the installer flow — the welcome screen.
    ///
    /// Navigation flow:
    ///   "התקן"  → InstallPage (runs the actual install, then proceeds to SettingsPage)
    ///   "תיקון" → RepairPage
    ///   "הגדרות" → SettingsPage directly (Back returns here)
    ///   "ביטול" → exit
    ///
    /// Auto-updates land here too — --silent no longer bypasses this page. That is
    /// deliberate: this is the one place that can require Word and כתבי הקודש to be closed
    /// and tell the user which one to close, which the install depends on. See App.xaml.cs.
    /// </summary>
    public partial class LandingPage : Page
    {
        private readonly MainWindow _host;

        public LandingPage(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            WordHelper.WaitForWordToClose();
            KitveiHakodeshHelper.WaitForKitveiHakodeshToClose();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (!WordHelper.EnsureWordClosed()) return;
            KitveiHakodeshHelper.EnsureKitveiHakodeshClosed();
            // showSettingsAfter: true — after install completes, navigate to SettingsPage
            // so the user can configure ribbon visibility and the default button.
            _host.NavigateToInstall(showSettingsAfter: true);
        }
        // Switches the installer language and reloads the window so every page
        // picks up the new texts and flow direction.
        private void LanguageButton_Click(object sender, RoutedEventArgs e)
        {
            UiLanguage.Set(UiLanguage.Current == UiLanguage.English ? UiLanguage.Hebrew : UiLanguage.English);
            var window = new MainWindow();
            Application.Current.MainWindow = window;
            window.Show();
            _host.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Environment.Exit(1);
        private void RepairButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToRepair();
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToSettings(fromLanding: true);
    }
}
