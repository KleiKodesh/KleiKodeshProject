using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfLib.Gallery
{
    /// <summary>
    /// Renders every section in every theme straight to PNG, without ever
    /// putting a window on screen.
    ///
    /// Why this exists: capturing the palette by photographing the screen is
    /// unreliable on a machine somebody is using. Any window that takes focus
    /// mid-run gets photographed instead, which happened repeatedly, and a
    /// run has to steal the desktop for a minute to have a chance at all.
    /// FlaUI's Capture.Element does not avoid this - it grabs the screen inside
    /// the element's rectangle, so it inherits the same problem.
    ///
    /// RenderTargetBitmap draws the visual tree directly. It does not care what
    /// is in front, whether the window is visible, or where it sits. The output
    /// is deterministic, which is the whole prerequisite for pixel comparison.
    ///
    /// Driven by: WpfLib.Gallery.exe --render &lt;outputDirectory&gt;
    /// </summary>
    internal static class SnapshotRenderer
    {
        private const int Width = 900;
        private const int Height = 620;
        private const double Dpi = 96;

        internal static int RenderAll(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            // The window has to be shown for RenderTargetBitmap to have a
            // realised visual tree; measuring and arranging an unshown Window
            // yields a blank bitmap. Showing it off-screen and unactivated
            // costs nothing here, because this is not a screen grab: the
            // bitmap comes from the visual tree, so being off-screen, behind
            // other windows, or unfocused makes no difference to the output.
            var window = new MainWindow
            {
                Width = Width,
                Height = Height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = 0,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            window.Show();
            window.UpdateLayout();

            var count = 0;
            foreach (var theme in MainWindow.ThemeNames)
            {
                window.ApplyTheme(theme);
                foreach (var section in MainWindow.SectionNames)
                {
                    window.ApplySection(section);
                    window.UpdateLayout();
                    Flush();

                    var file = Path.Combine(outputDirectory, $"{Slug(section)}.{Slug(theme)}.png");
                    Save(window, file);
                    count++;
                }
            }

            window.Close();
            return count;
        }

        /// <summary>
        /// Let the dispatcher finish the layout and render passes that were
        /// queued by the property changes above. Without this the bitmap can
        /// catch the tree mid-update.
        /// </summary>
        private static void Flush()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { }));
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(() => { }));
        }

        private static void Save(FrameworkElement element, string path)
        {
            var bitmap = new RenderTargetBitmap(Width, Height, Dpi, Dpi, PixelFormats.Pbgra32);
            bitmap.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static string Slug(string s) =>
            s.Replace(" & ", "-").Replace(' ', '-').ToLowerInvariant();
    }
}
