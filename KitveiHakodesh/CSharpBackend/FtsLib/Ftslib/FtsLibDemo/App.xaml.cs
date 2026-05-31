using FtsLibDemo.Services;
using FtsLibDemo.ViewModels;
using System.Windows;

namespace FtsLibDemo
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings      = new SettingsService();
            var indexService  = new IndexService();
            var searchService = new SearchService();
            var viewModel     = new MainViewModel(settings, indexService, searchService);

            var window = new MainWindow { DataContext = viewModel };

            RestoreWindowGeometry(window, settings);

            window.Closing += (sender, args) => SaveWindowGeometry(window, settings);

            window.Show();
        }

        private static void RestoreWindowGeometry(Window window, ISettingsService settings)
        {
            if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
            {
                window.Width  = settings.WindowWidth.Value;
                window.Height = settings.WindowHeight.Value;
            }

            if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
            {
                double left = settings.WindowLeft.Value;
                double top  = settings.WindowTop.Value;

                // Only restore position if the window would land on a visible virtual screen area.
                double screenLeft   = SystemParameters.VirtualScreenLeft;
                double screenTop    = SystemParameters.VirtualScreenTop;
                double screenRight  = screenLeft + SystemParameters.VirtualScreenWidth;
                double screenBottom = screenTop  + SystemParameters.VirtualScreenHeight;

                bool isOnScreen =
                    left + window.Width  > screenLeft  &&
                    left                 < screenRight  &&
                    top  + window.Height > screenTop    &&
                    top                  < screenBottom;

                if (isOnScreen)
                {
                    window.Left = left;
                    window.Top  = top;
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }

            if (settings.WindowMaximized)
                window.WindowState = WindowState.Maximized;
        }

        private static void SaveWindowGeometry(Window window, ISettingsService settings)
        {
            settings.WindowMaximized = window.WindowState == WindowState.Maximized;

            // Always save the restored (non-maximized) bounds so the window
            // comes back at a sensible size when un-maximized next launch.
            settings.WindowLeft   = window.RestoreBounds.Left;
            settings.WindowTop    = window.RestoreBounds.Top;
            settings.WindowWidth  = window.RestoreBounds.Width;
            settings.WindowHeight = window.RestoreBounds.Height;

            settings.Save();
        }
    }
}
