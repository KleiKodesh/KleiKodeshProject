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
                MainFrame.Navigate(new InstallPage());
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

        public void NavigateToLanding()  => MainFrame.Navigate(new LandingPage(this));
        public void NavigateToSettings() => MainFrame.Navigate(new SettingsPage(this));
        public void NavigateToRepair()   => MainFrame.Navigate(new RepairPage(this));

        public void NavigateToInstall()
        {
            MainFrame.Navigate(new InstallPage());
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
