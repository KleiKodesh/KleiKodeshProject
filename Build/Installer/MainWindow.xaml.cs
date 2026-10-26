using System;
using System.Windows;
using System.Windows.Input;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class MainWindow : Window
    {
        public MainWindow(bool startInstallImmediately = false)
        {
            InitializeComponent();

            if (startInstallImmediately)
                NavigateToInstall(false);
            else
                MainFrame.Navigate(new LandingPage(this));
        }

        private System.Windows.Point? _dragStart;

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragStart = null;
                return;
            }

            var pos = e.GetPosition(this);

            if (_dragStart == null)
            {
                _dragStart = pos;
                return;
            }

            var delta = pos - _dragStart.Value;
            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _dragStart = null;
                DragMove();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Environment.Exit(1);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(1);
        }

        public void SetCloseButtonVisible(bool visible)
        {
            CloseBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Navigation ───────────────────────────────────────────────────────────
        //
        // Normal install flow (interactive):
        //   LandingPage → InstallPage → WhatsNewPage → SettingsPage → GeneralSettingsPage → ComponentSettingsPage → exit
        //   (WhatsNewPage is skipped when no release notes are embedded, e.g. local builds)
        //
        // Auto-update (--silent / --install arg) takes the SAME path as a manual install:
        //   LandingPage → InstallPage → WhatsNewPage → SettingsPage → ...
        // The flag is ignored — see App.xaml.cs for why there is no headless flow.
        //
        // Repair flow:
        //   LandingPage "תיקון" → RepairPage
        //   or --repair arg     → RepairPage (autoRun: true, skips confirm dialog)

        public void NavigateToLanding()  => MainFrame.Navigate(new LandingPage(this));
        public void NavigateToSettings(bool fromLanding = false) => MainFrame.Navigate(new SettingsPage(this, fromLanding));
        public void NavigateToGeneralSettings(bool fromLanding = false) => MainFrame.Navigate(new GeneralSettingsPage(this, fromLanding));

        /// <summary>Shows the release notes, or goes straight to SettingsPage when none are embedded.</summary>
        public void NavigateToWhatsNew()
        {
            var notes = ReleaseNotes.Load();
            if (notes == null) NavigateToSettings();
            else MainFrame.Navigate(new WhatsNewPage(this, notes));
        }
        public void NavigateToRepair()   => MainFrame.Navigate(new RepairPage(this));
        public void NavigateToComponentSettings(bool showKitveiHakodesh, bool showWebsites)
            => MainFrame.Navigate(new ComponentSettingsPage(this, showKitveiHakodesh, showWebsites));

        public void NavigateToInstall(bool showSettingsAfter = false)
        {
            // RemoveBackEntry prevents the user navigating back to LandingPage mid-install.
            MainFrame.Navigate(new InstallPage(showSettingsAfter));
            MainFrame.NavigationService.RemoveBackEntry();
        }

        /// <summary>
        /// Used when relaunched as admin via --repair: navigate straight to repair
        /// and auto-trigger the cleanup run (skip the confirm dialog since user already confirmed).
        /// </summary>
        public void NavigateToRepairOnLoad()
        {
            Loaded += (_, __) =>
            {
                var page = new RepairPage(this, autoRun: true);
                MainFrame.Navigate(page);
                MainFrame.NavigationService.RemoveBackEntry();
            };
        }
    }
}
