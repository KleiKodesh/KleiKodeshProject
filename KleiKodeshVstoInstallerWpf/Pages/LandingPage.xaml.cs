using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class LandingPage : Page
    {
        private readonly MainWindow _host;

        public LandingPage(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            WordHelper.WaitForWordToClose();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e) => Install();
        private void CancelButton_Click(object sender, RoutedEventArgs e)  => Environment.Exit(1);
        private void RepairButton_Click(object sender, RoutedEventArgs e)  => _host.NavigateToRepair();
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToSettings();

        private void Install()
        {
            try
            {
                if (!WordHelper.EnsureWordClosed()) return;
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
