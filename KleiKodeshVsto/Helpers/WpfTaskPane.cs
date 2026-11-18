using System;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    /// <summary>The WinForms wrapper a WPF view lives in inside a side pane.</summary>
    public class WpfHostControl : WinForms.UserControl { }

    /// <summary>Shows WPF views as side-pane views (see <see cref="TaskPaneManager"/>).</summary>
    public static class WpfTaskPane
    {
        /// <summary>
        /// Shows WPF view <paramref name="id"/> in the current side pane, building it with
        /// <paramref name="factory"/> only when the side pane doesn't have it yet - views cost
        /// 250-560 ms to build and some hook Word events, so a reveal must not build a throwaway one.
        /// </summary>
        public static T Show<T>(string id, Func<T> factory, string title) where T : UserControl
        {
            var host = TaskPaneManager.Show(id, () => Wrap(factory()), title);
            return (host?.Controls.Count > 0 ? (host.Controls[0] as ElementHost)?.Child : null) as T;
        }

        /// <summary>Wraps a WPF view for WinForms, following the host's (Office theme) colours.</summary>
        static WpfHostControl Wrap(UserControl view)
        {
            var hostControl = new WpfHostControl();
            hostControl.Controls.Add(new ElementHost { Dock = WinForms.DockStyle.Fill, Child = view });

            // Unchanged from the previous WpfTaskPane: the theme colours arrive with red and blue swapped
            // relative to WPF, so they are swapped back here.
            void SetColors()
            {
                var fore = hostControl.ForeColor;
                view.Foreground = new SolidColorBrush(Color.FromArgb(fore.A, fore.B, fore.G, fore.R));
                var back = hostControl.BackColor;
                view.Background = new SolidColorBrush(Color.FromArgb(back.A, back.B, back.G, back.R));
            }
            SetColors();
            hostControl.ForeColorChanged += (_, __) => SetColors();
            return hostControl;
        }
    }
}
