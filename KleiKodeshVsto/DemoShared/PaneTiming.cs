using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace KleiKodesh.DemoShared
{
    /// <summary>
    /// Times how long a pane takes to build, so "the panes feel slow" can be
    /// answered with a number and a place to look.
    ///
    /// The split matters more than the total. Opening a pane in Word is a cold
    /// build the first time - resource dictionaries parsed, types JITted, the
    /// pane's own wiring run - and a warm one after that. Only the difference
    /// tells you which of those to go after, and a single figure covering
    /// process start, four builds and four PNG encodes tells you neither.
    ///
    /// Measured the same way as the WpfLib gallery benchmark, for the same
    /// reasons: median of several passes, warm-up discarded, and the hosting
    /// Window made once so the cost of asking the OS for a window does not
    /// land on the pane being measured.
    ///
    /// Usage from a demo's App, before base.OnStartup:
    ///     if (PaneTiming.TryTime(e.Args, () => new MainWindow())) { Shutdown(0); return; }
    /// </summary>
    internal static class PaneTiming
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        /// <summary>
        /// Handles "--timing [count]". Returns false when the app was started
        /// normally, so the caller can carry on and show its window.
        /// </summary>
        internal static bool TryTime(string[] args, Func<Window> createWindow)
        {
            if (args == null || args.Length < 1 || args[0] != "--timing") return false;

            AttachConsole(-1);   // a WinExe has no console of its own; borrow the caller's

            var passes = 7;
            if (args.Length >= 2)
            {
                int parsed;
                if (int.TryParse(args[1], out parsed) && parsed > 0) passes = parsed;
            }

            if (Application.Current != null)
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Make the host window first, and time that separately. The very
            // first Window in a process drags in most of WPF - the dispatcher,
            // the render thread, the composition target, the theme dictionaries
            // - and none of that belongs to the pane. Charging it to the pane
            // would make every pane in the suite look far worse than it is.
            var startup = Stopwatch.StartNew();
            Host();
            startup.Stop();
            var wpfStartup = startup.Elapsed.TotalMilliseconds;

            // Now the first real pane: its own dictionaries parsed, its types
            // JITted, its static constructors run. In Word this is what the user
            // waits for the first time they open the pane in a session.
            var cold = TimeOne(createWindow);

            var warm = new List<double>();
            var constructs = new List<double>();
            var layouts = new List<double>();
            for (var i = 0; i < passes; i++)
            {
                double construct, layout;
                warm.Add(TimeOne(createWindow, out construct, out layout));
                constructs.Add(construct);
                layouts.Add(layout);
            }
            warm.Sort(); constructs.Sort(); layouts.Sort();
            var median = warm[warm.Count / 2];

            Console.WriteLine("WPF startup, first window in process: {0,8:F1} ms", wpfStartup);
            Console.WriteLine("pane build, cold                    : {0,8:F1} ms", cold);
            Console.WriteLine("pane build, warm (median of {0,2})      : {1,8:F1} ms", passes, median);
            Console.WriteLine("  of which, once per process        : {0,8:F1} ms", cold - median);
            Console.WriteLine("  warm split: pane's own code       : {0,8:F1} ms", constructs[constructs.Count / 2]);
            Console.WriteLine("  warm split: WPF layout            : {0,8:F1} ms", layouts[layouts.Count / 2]);

            // Timings say how slow it is on this machine today; the element
            // count says how much work was asked for. The second is stable, and
            // it is the one that says what to do about it - a pane that builds
            // two thousand elements up front is a pane with content that could
            // have waited until someone looked at it.
            Report(createWindow);
            return true;
        }

        /// <summary>
        /// The size of the tree the pane builds, and the biggest single
        /// contributor to it.
        ///
        /// Naming the worst subtree matters as much as the total: "1800
        /// elements" is a fact, "1800 elements and 1200 of them are under the
        /// third tab nobody has clicked" is a thing to fix.
        /// </summary>
        private static void Report(Func<Window> createWindow)
        {
            var window = createWindow();
            var content = window.Content;
            window.Content = null;
            Host().Content = content;
            Host().UpdateLayout();

            var total = Count(Host());
            Console.WriteLine("visual elements in the pane         : {0,8}", total);

            string worstName = null;
            var worstCount = 0;
            Worst(Host(), ref worstName, ref worstCount);
            if (worstName != null)
                Console.WriteLine("largest named subtree               : {0,8}  {1}", worstCount, worstName);

            Host().Content = null;
            window.Close();
        }

        private static int Count(System.Windows.DependencyObject node)
        {
            var total = 1;
            var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++)
                total += Count(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            return total;
        }

        /// <summary>
        /// The heaviest element that has a Name, so the answer points at
        /// something findable in the markup rather than at an anonymous Grid.
        /// </summary>
        private static int Worst(System.Windows.DependencyObject node, ref string name, ref int best)
        {
            var total = 1;
            var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++)
                total += Worst(System.Windows.Media.VisualTreeHelper.GetChild(node, i), ref name, ref best);

            var element = node as FrameworkElement;
            if (element != null && !string.IsNullOrEmpty(element.Name) && total > best)
            {
                best = total;
                name = element.Name + " (" + node.GetType().Name + ")";
            }
            return total;
        }

        /// <summary>
        /// Construct the pane, lay it out, and throw it away. Layout is included
        /// deliberately: a pane that constructs quickly and then spends its time
        /// measuring and arranging is still a pane the user waits for.
        /// </summary>
        private static double TimeOne(Func<Window> createWindow)
        {
            double construct, layout;
            return TimeOne(createWindow, out construct, out layout);
        }

        /// <summary>
        /// Split into the two halves, because they are fixed in completely
        /// different places. CONSTRUCT is the pane's own code - its constructor,
        /// its InitializeComponent, whatever it reads or enumerates on the way
        /// up. LAYOUT is WPF measuring and arranging what that produced. A pane
        /// with sixty visual elements cannot be spending its time in layout, so
        /// knowing which half is which decides whether to look at the markup or
        /// at the code behind it.
        /// </summary>
        private static double TimeOne(Func<Window> createWindow, out double construct, out double layout)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var stopwatch = Stopwatch.StartNew();
            var window = createWindow();
            construct = stopwatch.Elapsed.TotalMilliseconds;

            // The pane's content goes into a host window that already exists,
            // instead of showing the demo's own window.
            //
            // Window.Show() is an OS call that costs more than everything else
            // here put together and swings by a factor of four between runs -
            // the empty-window baseline above is almost entirely Show(). Worse,
            // it is not a cost the real pane pays: in Word the pane is a
            // UserControl inside Word's task pane, and no window is created for
            // it at all. Timing Show() would be measuring the demo harness.
            var content = window.Content;
            window.Content = null;

            Host().Content = content;
            Host().UpdateLayout();

            stopwatch.Stop();
            layout = stopwatch.Elapsed.TotalMilliseconds - construct;

            Host().Content = null;
            window.Close();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static Window _host;

        /// <summary>The one window, made on first use and kept for the whole run.</summary>
        private static Window Host()
        {
            if (_host == null)
            {
                _host = new Window
                {
                    Width = 420, Height = 900,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000, Top = 0,
                    ShowActivated = false, ShowInTaskbar = false,
                };
                _host.Show();
            }
            return _host;
        }
    }
}
