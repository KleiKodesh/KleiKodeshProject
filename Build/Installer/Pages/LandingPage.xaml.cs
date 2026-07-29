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
            if (!KitveiHakodeshHelper.EnsureKitveiHakodeshClosed()) return;
            // showSettingsAfter: true — after install completes, navigate to SettingsPage
            // so the user can configure ribbon visibility and the default button.
            _host.NavigateToInstall(showSettingsAfter: true);
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Environment.Exit(1);
        private void RepairButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToRepair();
    }
}
