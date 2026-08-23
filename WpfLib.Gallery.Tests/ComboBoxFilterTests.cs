using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WpfLib.AttachedProperties;
using Xunit;

namespace WpfLib.Gallery.Tests
{
    /// <summary>
    /// The type-to-filter behaviour, exercised directly rather than through the
    /// UI.
    ///
    /// Driving this through UIAutomation was misleading: setting the text
    /// through the Value pattern reported the same item count either way, so
    /// the test passed while the filter did nothing. Constructing the ComboBox
    /// here and reading ItemCollection back is unambiguous.
    ///
    /// WPF needs an STA thread and the xunit runsettings cannot supply one on
    /// .NET 10, so each test brings its own.
    /// </summary>
    public class ComboBoxFilterTests
    {
        [Fact]
        public void Typing_narrows_the_list_and_clearing_restores_it()
        {
            Sta(() =>
            {
                var combo = BuildCombo("Aleph", "Bet", "Gimel", "Dalet", "He");

                Assert.Equal(5, VisibleCount(combo));

                SetText(combo, "be");
                Assert.Equal(1, VisibleCount(combo));           // Bet

                SetText(combo, "e");
                // Aleph, Bet, Gimel, Dalet, He - every one of them has an 'e'
                Assert.Equal(5, VisibleCount(combo));

                SetText(combo, "");
                Assert.Equal(5, VisibleCount(combo));
            });
        }

        [Fact]
        public void Filtering_is_case_insensitive_and_matches_anywhere()
        {
            Sta(() =>
            {
                var combo = BuildCombo("Aleph", "Bet", "Gimel");

                SetText(combo, "LEP");                          // middle of "Aleph", wrong case
                Assert.Equal(1, VisibleCount(combo));
            });
        }

        [Fact]
        public void Closing_the_drop_down_restores_the_whole_list()
        {
            Sta(() =>
            {
                var combo = BuildCombo("Aleph", "Bet", "Gimel");

                SetText(combo, "be");
                Assert.Equal(1, VisibleCount(combo));

                combo.IsDropDownOpen = true;
                Pump();
                combo.IsDropDownOpen = false;                   // raises DropDownClosed
                Pump();
                Assert.Equal(3, VisibleCount(combo));
            });
        }

        [Fact]
        public void Turning_the_behaviour_off_removes_the_filter()
        {
            Sta(() =>
            {
                var combo = BuildCombo("Aleph", "Bet", "Gimel");
                SetText(combo, "be");
                Assert.Equal(1, VisibleCount(combo));

                ComboBoxFilter.SetIsEnabled(combo, false);
                Assert.Equal(3, VisibleCount(combo));
            });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static ComboBox BuildCombo(params string[] items)
        {
            var combo = new ComboBox { IsEditable = true, Width = 200 };
            foreach (var i in items) combo.Items.Add(new ComboBoxItem { Content = i });

            ComboBoxFilter.SetIsEnabled(combo, true);

            // Realise the template so PART_EditableTextBox exists to listen on.
            var host = new Window
            {
                Width = 300, Height = 120, Left = -4000,
                ShowActivated = false, ShowInTaskbar = false,
                Content = combo,
            };
            host.Show();
            combo.ApplyTemplate();
            combo.UpdateLayout();
            return combo;
        }

        /// <summary>Set the text the way a person typing would: on the edit box.</summary>
        private static void SetText(ComboBox combo, string text)
        {
            var box = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
            box.Text = text;
            combo.UpdateLayout();
        }

        private static int VisibleCount(ComboBox combo)
        {
            var n = 0;
            foreach (var _ in combo.Items) n++;      // ItemCollection honours Filter
            return n;
        }

        /// <summary>Let queued dispatcher work run, so events actually fire.</summary>
        private static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(30));

            if (failure != null) throw new Exception("STA test failed: " + failure.Message, failure);
        }
    }
}
