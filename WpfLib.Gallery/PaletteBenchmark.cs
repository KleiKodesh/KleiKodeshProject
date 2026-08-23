using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfLib.Gallery
{
    /// <summary>
    /// Measures what the palette costs, so performance work is aimed rather
    /// than guessed at.
    ///
    /// Two costs, and keeping them apart matters more than any single number:
    ///
    ///   LOAD    parsing a dictionary and everything it merges. Paid once, but
    ///           a task pane opening is exactly that once.
    ///   BUILD   what every element pays afterwards - style lookup, template
    ///           expansion, measure and arrange.
    ///
    /// An earlier version of this built the dictionary inside the timed block,
    /// and so charged load to build. That made the merged tree look like it
    /// cost five times bare per element, when it costs almost nothing, and sent
    /// the investigation after the wrong thing entirely.
    ///
    /// It also created a Window per iteration. Creating a window is an OS call
    /// whose cost swings by a factor of four for reasons that have nothing to
    /// do with a resource dictionary, and that variance was large enough to
    /// hide the effect being measured. The host windows are made once, up
    /// front, and each iteration only swaps their content.
    ///
    /// Run: WpfLib.Gallery.exe --benchmark [controlCount]
    /// </summary>
    internal static class PaletteBenchmark
    {
        private const string PaletteUri = "pack://application:,,,/WpfLib;component/themes/officepalette.xaml";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static Window _bare;
        private static Window _themed;

        internal static void Run(int count)
        {
            AttachConsole(-1);   // a WinExe has no console of its own; borrow the caller's
            Console.WriteLine("WpfLib palette benchmark - " + count + " of each control");

            _bare = MakeHost(null);
            _themed = MakeHost(new ResourceDictionary { Source = new Uri(PaletteUri) });

            // The first WPF content in a process pays for a great deal that has
            // nothing to do with this palette. Counting it would drown the rest.
            Fill(_themed, () => Six(8));
            Fill(_bare, () => Six(8));

            Rule();
            FreezeCheck();
            Rule();
            LoadCost();
            Rule();
            SharingCost();
            Rule();
            PerControl(count);
            Rule();
            VisualCost();
            Rule();
            FlattenCost(count);
            Rule();

            var themed = Median(() => Fill(_themed, () => Six(count)));
            var bare = Median(() => Fill(_bare, () => Six(count)));
            Console.WriteLine("build + layout, palette   : {0,8:F1} ms", themed);
            Console.WriteLine("build + layout, bare      : {0,8:F1} ms", bare);
            Console.WriteLine("palette overhead          : {0,8:F1} ms  ({1:F2}x bare)",
                themed - bare, bare > 0 ? themed / bare : 0);
            Console.WriteLine("per control               : {0,8:F3} ms", (themed - bare) / (count * 6.0));

            _bare.Close();
            _themed.Close();
        }

        /// <summary>
        /// Whether the brush tokens are frozen.
        ///
        /// Marking them po:Freeze is the standard advice, and here it made no
        /// measurable difference to any timing in this benchmark. The obvious
        /// explanation - that BAML had frozen them already - was wrong: with
        /// the attribute removed this reported 0 of 19 frozen. So the attribute
        /// does change something, just not speed, and it is kept in Brushes.xaml
        /// for immutability across panes rather than for performance.
        ///
        /// Left here so that result stays visible and nobody has to rediscover
        /// it by trying the same thing again.
        /// </summary>
        private static void FreezeCheck()
        {
            var brushes = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WpfLib;component/themes/brushes.xaml"),
            };
            int frozen = 0, total = 0;
            foreach (var key in brushes.Keys)
            {
                var freezable = brushes[key] as System.Windows.Freezable;
                if (freezable == null) continue;
                total++;
                if (freezable.IsFrozen) frozen++;
            }
            Console.WriteLine("brush tokens frozen: {0} of {1}", frozen, total);
        }

        /// <summary>
        /// What each dictionary costs to parse. This is the number a pane pays
        /// when it opens, and the reason the palette is split into pieces: a
        /// pane that needs buttons should not pay for tree views.
        /// </summary>
        private static void LoadCost()
        {
            Console.WriteLine("{0,-24} {1,9} {2,8}", "dictionary", "load", "keys");
            foreach (var name in new[] { "typography", "brushes", "tokens", "buttonstyles",
                                         "popupstyles", "scrollbarstyles", "defaults", "officepalette" })
            {
                var uri = new Uri("pack://application:,,,/WpfLib;component/themes/" + name + ".xaml");
                var keys = 0;
                // A fresh instance every pass: a dictionary that has already
                // been parsed would report the cost of nothing at all.
                var ms = Median(() =>
                {
                    var flat = new ResourceDictionary();
                    Copy(new ResourceDictionary { Source = uri }, flat);
                    keys = flat.Count;
                });
                Console.WriteLine("{0,-24} {1,7:F1}ms {2,8}", name, ms, keys);
            }
        }

        /// <summary>
        /// What a SECOND pane pays.
        ///
        /// Every pane in the suite merges the palette into its own Resources,
        /// and each one writes "new ResourceDictionary { Source = ... }" to do
        /// it. If WPF caches by URI then only the first pane pays and there is
        /// nothing to do here. If it does not, every pane pays the full parse
        /// again, and the fix is to hand them all one shared instance.
        /// </summary>
        private static void SharingCost()
        {
            var uri = new Uri(PaletteUri);
            const int panes = 5;

            var fresh = Median(() =>
            {
                for (var i = 0; i < panes; i++)
                {
                    var d = new ResourceDictionary { Source = uri };
                    Copy(d, new ResourceDictionary());   // realise it, as a pane would by using it
                }
            });

            var shared = new ResourceDictionary { Source = uri };
            Copy(shared, new ResourceDictionary());
            var reused = Median(() =>
            {
                for (var i = 0; i < panes; i++)
                {
                    var host = new ResourceDictionary();
                    host.MergedDictionaries.Add(shared);
                    Copy(host, new ResourceDictionary());
                }
            });

            Console.WriteLine("{0} panes, own instance each : {1,7:F1}ms", panes, fresh);
            Console.WriteLine("{0} panes, one shared instance: {1,7:F1}ms", panes, reused);
            Console.WriteLine("{0,-30} {1,7:F1}ms", "saved by sharing", fresh - reused);

            // Sharing by hand is not the shipping path; panes write XAML. This
            // loads a dictionary that merges the palette through
            // SharedResourceDictionary, the way a pane would, and checks that
            // the second load is the cheap one.
            WpfLib.Themes.SharedResourceDictionary.ClearCache();
            var probe = new Uri("pack://application:,,,/WpfLib.Gallery;component/sharedpaletteprobe.xaml");

            var firstPane = Time(() => Copy(new ResourceDictionary { Source = probe }, new ResourceDictionary()));
            var cachedAfterFirst = WpfLib.Themes.SharedResourceDictionary.CachedCount;
            var nextPane = Time(() => Copy(new ResourceDictionary { Source = probe }, new ResourceDictionary()));

            Console.WriteLine("{0,-30} {1,7:F1}ms", "via XAML, first pane", firstPane);
            Console.WriteLine("{0,-30} {1,7:F1}ms", "via XAML, next pane", nextPane);
            Console.WriteLine("{0,-30} {1,7} {2}", "dictionaries cached", cachedAfterFirst,
                cachedAfterFirst == 1 ? "" : "<- SHARING DID NOT TAKE EFFECT");
        }

        /// <summary>One pass, for things that only happen once and cannot be repeated.</summary>
        private static double Time(Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Which control is expensive. An aggregate number says the palette
        /// costs something; it does not say where to look.
        /// </summary>
        private static void PerControl(int count)
        {
            Console.WriteLine("{0,-14} {1,10} {2,10} {3,8}", "control", "palette", "bare", "ratio");
            foreach (var kind in new[] { "Button", "CheckBox", "RadioButton", "TextBox", "ComboBox", "TextBlock" })
            {
                var k = kind;
                var themed = Median(() => Fill(_themed, () => Many(k, count)));
                var bare = Median(() => Fill(_bare, () => Many(k, count)));
                Console.WriteLine("{0,-14} {1,8:F1}ms {2,8:F1}ms {3,7:F2}x",
                    kind, themed, bare, bare > 0 ? themed / bare : 0);
            }
        }

        /// <summary>
        /// How many visual elements each control template produces, and how
        /// deep they nest.
        ///
        /// This is the number that governs painting. Timings say whether
        /// something is slow today on this machine; element count says how much
        /// work was asked for, does not move when the machine is busy, and is
        /// the figure every write-up on WPF performance points at first. A
        /// replacement template that quietly doubles the element count of a
        /// control used in a list is the classic way a theme library gets a
        /// reputation for being heavy.
        /// </summary>
        private static void VisualCost()
        {
            // Identical element counts on both hosts would be a fine result and
            // also exactly what a palette that never applied would produce, so
            // say which it is rather than leaving it to be assumed.
            var probe = new Button();
            _themed.Content = probe;
            _themed.UpdateLayout();
            var styled = probe.Style != null;
            _themed.Content = null;
            Console.WriteLine("palette applied to a Button: {0}{1}", styled,
                styled ? "" : "   <- NOTHING BELOW MEANS ANYTHING");

            Console.WriteLine("{0,-14} {1,18} {2,16}", "control", "elements p/b", "depth p/b");
            foreach (var kind in new[] { "Button", "CheckBox", "RadioButton", "TextBox", "ComboBox", "TextBlock" })
            {
                int themedCount, themedDepth, bareCount, bareDepth;
                Inspect(_themed, kind, out themedCount, out themedDepth);
                Inspect(_bare, kind, out bareCount, out bareDepth);

                var flag = themedCount > bareCount * 1.5 ? "  <- heavier" : "";
                Console.WriteLine("{0,-14} {1,8} {2,8} {3,8} {4,7}{5}",
                    kind, themedCount, bareCount, themedDepth, bareDepth, flag);
            }
        }

        private static void Inspect(Window host, string kind, out int elements, out int depth)
        {
            var panel = Many(kind, 1);
            host.Content = panel;
            host.UpdateLayout();

            var child = VisualTreeHelper.GetChildrenCount(panel) > 0
                ? VisualTreeHelper.GetChild(panel, 0)
                : null;
            elements = child == null ? 0 : CountVisuals(child);
            depth = child == null ? 0 : Depth(child);

            host.Content = null;
        }

        private static int CountVisuals(DependencyObject node)
        {
            var total = 1;
            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++)
                total += CountVisuals(VisualTreeHelper.GetChild(node, i));
            return total;
        }

        private static int Depth(DependencyObject node)
        {
            var deepest = 0;
            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++)
                deepest = Math.Max(deepest, Depth(VisualTreeHelper.GetChild(node, i)));
            return deepest + 1;
        }

        /// <summary>
        /// The palette is a tree of merged dictionaries, so price it flattened:
        /// every key copied into one dictionary with no merges at all. Styles
        /// resolve their BasedOn and StaticResource references when they are
        /// parsed, so the copies are already-built objects and keep working.
        /// </summary>
        private static void FlattenCost(int count)
        {
            var flat = new ResourceDictionary();
            Copy(new ResourceDictionary { Source = new Uri(PaletteUri) }, flat);
            var flatHost = MakeHost(flat);
            Fill(flatHost, () => Six(8));

            Console.WriteLine("flattened palette: {0} keys, 0 merges", flat.Count);

            // A then B then A again. Timings drift upwards over a long run as
            // the heap grows, and a plain A-then-B reads that drift as though
            // it were the difference between A and B. If the two A passes agree,
            // the gap to B is real; if they do not, nothing here is.
            var nestedFirst = Median(() => Fill(_themed, () => Six(count)));
            var flattened = Median(() => Fill(flatHost, () => Six(count)));
            var nestedAgain = Median(() => Fill(_themed, () => Six(count)));

            Console.WriteLine("{0,-24} {1,8:F1}ms", "build, nested (first)", nestedFirst);
            Console.WriteLine("{0,-24} {1,8:F1}ms", "build, flattened", flattened);
            Console.WriteLine("{0,-24} {1,8:F1}ms", "build, nested (again)", nestedAgain);

            var nested = Math.Min(nestedFirst, nestedAgain);
            var drift = Math.Abs(nestedFirst - nestedAgain) / Math.Max(nestedFirst, nestedAgain);
            Console.WriteLine("{0,-24} {1,7:F0}%", "drift between the two", drift * 100);
            Console.WriteLine("{0,-24} {1,7:F2}x {2}", "flattened vs nested",
                nested > 0 ? flattened / nested : 0,
                drift > 0.10 ? "<- drift too large to trust" : "");

            flatHost.Close();
        }

        private static ResourceDictionary Copy(ResourceDictionary from, ResourceDictionary into)
        {
            // Depth first, so a key defined later wins the way merging does.
            // Reading a key is also what realises it, which is the point here:
            // an unrealised dictionary has not yet done its parsing work.
            foreach (var merged in from.MergedDictionaries) Copy(merged, into);
            foreach (var key in from.Keys) into[key] = from[key];
            return into;
        }

        private static Window MakeHost(ResourceDictionary dictionary)
        {
            var window = new Window
            {
                Width = 1200, Height = 900, Left = -6000, Top = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false, ShowInTaskbar = false,
                Background = Brushes.White,
            };
            if (dictionary != null) window.Resources.MergedDictionaries.Add(dictionary);
            window.Show();
            return window;
        }

        private static void Fill(Window host, Func<Panel> build)
        {
            host.Content = new ScrollViewer { Content = build() };
            host.UpdateLayout();
            host.Content = null;
        }

        private static Panel Six(int count)
        {
            var panel = new WrapPanel();
            for (var i = 0; i < count; i++)
            {
                panel.Children.Add(new Button { Content = "Button " + i });
                panel.Children.Add(new CheckBox { Content = "Check " + i });
                panel.Children.Add(new RadioButton { Content = "Radio " + i });
                panel.Children.Add(new TextBox { Text = "Text " + i, Width = 90 });
                panel.Children.Add(new ComboBox { Width = 90 });
                panel.Children.Add(new TextBlock { Text = "Label " + i });
            }
            return panel;
        }

        private static Panel Many(string kind, int count)
        {
            var panel = new WrapPanel();
            for (var i = 0; i < count; i++)
            {
                switch (kind)
                {
                    case "Button":      panel.Children.Add(new Button { Content = "B" + i }); break;
                    case "CheckBox":    panel.Children.Add(new CheckBox { Content = "C" + i }); break;
                    case "RadioButton": panel.Children.Add(new RadioButton { Content = "R" + i }); break;
                    case "TextBox":     panel.Children.Add(new TextBox { Text = "T" + i, Width = 90 }); break;
                    case "ComboBox":    panel.Children.Add(new ComboBox { Width = 90 }); break;
                    default:            panel.Children.Add(new TextBlock { Text = "L" + i }); break;
                }
            }
            return panel;
        }

        /// <summary>
        /// Median of nine, after two discarded warm-up passes. Best-of-three
        /// was not enough: this machine is shared, and a single unlucky pass
        /// was moving reported figures by a factor of four.
        /// </summary>
        private static double Median(Action action)
        {
            action(); action();

            var runs = new List<double>();
            for (var i = 0; i < 9; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                runs.Add(sw.Elapsed.TotalMilliseconds);
            }
            runs.Sort();
            return runs[runs.Count / 2];
        }

        private static void Rule()
        {
            Console.WriteLine(new string('-', 56));
        }
    }
}
