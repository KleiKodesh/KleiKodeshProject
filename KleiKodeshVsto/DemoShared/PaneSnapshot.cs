using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KleiKodesh.DemoShared
{
    /// <summary>
    /// Renders a demo window across the four Office themes, straight to PNG.
    ///
    /// The panes are only reachable through their demo apps, and until now
    /// checking one against a dark theme meant editing a commented-out
    /// Background line and rebuilding - the DocDesign demo says exactly that in
    /// its own markup. That is too much friction to do routinely, so it was not
    /// done routinely, which is how thirteen pane styles ended up with text that
    /// goes near-black on a dark Office theme.
    ///
    /// This is the same approach the WpfLib gallery uses: draw the visual tree
    /// with RenderTargetBitmap rather than photograph the screen. It is
    /// deterministic, needs no focus, and does not take over the desktop.
    ///
    /// Shared by link rather than by assembly, so the demos gain the capability
    /// without WpfLib having to ship a test utility to production.
    ///
    /// Usage from a demo's App:
    ///     protected override void OnStartup(StartupEventArgs e)
    ///     {
    ///         if (PaneSnapshot.TryRender(e.Args, () => new MainWindow())) { Shutdown(0); return; }
    ///         base.OnStartup(e);
    ///     }
    /// </summary>
    internal static class PaneSnapshot
    {
        /// <summary>The Office themes, as the host applies them: background, foreground.</summary>
        private static readonly Tuple<string, string, string>[] Themes =
        {
            Tuple.Create("office-white",      "#FFFFFFFF", "#FF262626"),
            Tuple.Create("office-light-gray", "#FFF3F3F3", "#FF262626"),
            Tuple.Create("office-dark-gray",  "#FF666666", "#FFE6E6E6"),
            Tuple.Create("office-black",      "#FF262626", "#FFD4D4D4"),
        };

        /// <summary>
        /// Applies a theme to a window that is about to be shown normally.
        ///
        /// For "--theme office-black" and friends. The renderer cannot help
        /// with popups: RenderTargetBitmap draws the visual tree, and a Popup
        /// is a separate HwndSource that is not in it. So checking a drop-down
        /// against a dark theme needs the demo actually running, themed, with
        /// the popup open - which is what this is for.
        /// </summary>
        internal static void ApplyStartupTheme(string[] args, Window window)
        {
            if (args == null || args.Length < 2 || args[0] != "--theme") return;

            foreach (var theme in Themes)
            {
                if (!string.Equals(theme.Item1, args[1], StringComparison.OrdinalIgnoreCase)) continue;
                window.Background = Brush(theme.Item2);
                window.Foreground = Brush(theme.Item3);
                window.Loaded += (s, e) =>
                    ApplyToPane(window, Brush(theme.Item2), Brush(theme.Item3));
                return;
            }
        }

        /// <summary>
        /// Handles "--render &lt;dir&gt;". Returns false when the app was started
        /// normally, so the caller can carry on and show its window.
        /// </summary>
        internal static bool TryRender(string[] args, Func<Window> createWindow)
        {
            if (args == null || args.Length < 2 || args[0] != "--render") return false;

            var directory = args[1];
            Directory.CreateDirectory(directory);

            // Each theme gets its own window and closes it again. Under the
            // default OnLastWindowClose that first close ends the application,
            // and the run died after two of the four snapshots.
            if (Application.Current != null)
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            foreach (var theme in Themes)
            {
                // A fresh window per theme. The panes wire themselves up on
                // construction, and re-theming a live one would only prove that
                // re-theming works, not that a pane opened under this theme is
                // legible - which is the case that actually ships.
                var window = createWindow();
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -4000;
                window.Top = 0;
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Background = Brush(theme.Item2);
                window.Foreground = Brush(theme.Item3);

                window.Show();
                window.UpdateLayout();

                // The theme has to land on the pane's UserControl, not on the
                // Window. Every pane sets its own Background, so it covers the
                // window entirely and theming the window alone changed nothing -
                // all four "themes" rendered identically white. In production
                // this is exactly what the host does: OfficeThemeWatcher sets
                // Background and Foreground on the UserControl.
                ApplyToPane(window, Brush(theme.Item2), Brush(theme.Item3));
                window.UpdateLayout();
                Settle();

                Save(window, Path.Combine(directory, theme.Item1 + ".png"));
                window.Close();
            }

            Console.WriteLine("rendered " + Themes.Length + " snapshots to " + directory);
            return true;
        }

        /// <summary>Theme the first UserControl under the window, the way the host does.</summary>
        private static void ApplyToPane(DependencyObject root, Brush background, Brush foreground)
        {
            var pane = FindPane(root);
            if (pane == null) return;
            pane.Background = background;
            pane.Foreground = foreground;
        }

        private static System.Windows.Controls.UserControl FindPane(DependencyObject root)
        {
            if (root is System.Windows.Controls.UserControl found) return found;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var hit = FindPane(VisualTreeHelper.GetChild(root, i));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Let queued layout and render work finish before the bitmap is taken.</summary>
        private static void Settle()
        {
            for (var i = 0; i < 3; i++)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { }));
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
            }
        }

        private static void Save(Window window, string path)
        {
            var width = (int)Math.Max(window.ActualWidth, 1);
            var height = (int)Math.Max(window.ActualHeight, 1);

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var stream = File.Create(path))
                encoder.Save(stream);
        }

        private static SolidColorBrush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
