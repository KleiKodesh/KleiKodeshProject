using System;
using System.Windows;

namespace WpfLib.Gallery
{
    public partial class App : Application
    {
        /// <summary>
        /// "--render &lt;dir&gt;" renders every section in every theme to PNG and
        /// exits without showing a window. That is how the visual tests get
        /// their images: drawing the visual tree directly is deterministic,
        /// where photographing the screen is at the mercy of whatever window
        /// has focus.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length >= 1 && e.Args[0] == "--benchmark")
            {
                var count = e.Args.Length >= 2 && int.TryParse(e.Args[1], out var n) ? n : 100;
                PaletteBenchmark.Run(count);
                Shutdown(0);
                return;
            }

            if (e.Args.Length >= 2 && e.Args[0] == "--render")
            {
                var written = SnapshotRenderer.RenderAll(e.Args[1]);
                Console.WriteLine($"rendered {written} snapshots to {e.Args[1]}");
                Shutdown(0);
                return;
            }

            base.OnStartup(e);
            new MainWindow().Show();
        }
    }
}
