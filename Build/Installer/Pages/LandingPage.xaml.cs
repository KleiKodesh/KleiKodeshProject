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

        private void NextButton_Click(object sender, RoutedEventArgs e)   => _host.NavigateToInstall(true);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Environment.Exit(1);
        private void RepairButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToRepair();
    }
}
