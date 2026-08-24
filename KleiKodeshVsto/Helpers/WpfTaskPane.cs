using Microsoft.Office.Tools;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public class WpfHostControl : WinForms.UserControl { }
    public static class WpfTaskPane
    {
        /// <summary>
        /// Shows the pane for T, constructing the view only when no pane exists to
        /// reuse. The eager overload below builds the whole view - and the XAML
        /// builds its view model, and view models may hook Word events - just to
        /// throw it away when the pane is already open. With views costing
        /// 250-560ms to build and one of them leaking a SelectionChange handler
        /// per construction, every ribbon click on an open pane was paid for twice.
        /// </summary>
        public static CustomTaskPane Show<T>(Func<T> factory, string title, int width = 600)
            where T : UserControl
        {
            try
            {
                var panes = Globals.ThisAddIn.CustomTaskPanes;

                // Match on the hosted view type alone - see TaskPaneManager.Show for why
                // there is no window to compare against.
                var pane = FindReusable(panes, typeof(T));

                if (pane != null)
                {
                    TaskPaneManager.Reveal(pane);
                    return pane;
                }

                return CreateNew(factory(), title, width);
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        public static CustomTaskPane Show(UserControl userControl, string title, int width = 600)
        {
            try
            {
                var panes = Globals.ThisAddIn.CustomTaskPanes;
                var type = userControl.GetType();

                var pane = FindReusable(panes, type);

                if (pane != null)
                {
                    TaskPaneManager.Reveal(pane);
                    return pane;
                }

                return CreateNew(userControl, title, width);
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        /// <summary>
        /// The pane hosting <paramref name="viewType"/> that should be brought forward.
        /// Mirrors TaskPaneManager.FindReusable - duplication means a type match can find
        /// more than one pane, and the one the user is working in should win over one
        /// that merely came first.
        /// </summary>
        static CustomTaskPane FindReusable(CustomTaskPaneCollection panes, Type viewType)
        {
            var matches = panes.Cast<CustomTaskPane>()
                .Where(p => HostsView(p, viewType))
                .ToList();

            return matches.FirstOrDefault(TaskPaneManager.IsLastRevealed)
                ?? matches.FirstOrDefault(TaskPaneManager.IsUsable)
                ?? matches.FirstOrDefault();
        }

        /// <summary>
        /// True when <paramref name="pane"/> hosts a WPF view of <paramref name="viewType"/>.
        /// A pane whose document has closed throws from every member - the exception
        /// type varies with how far the teardown got - and a throw from inside a LINQ
        /// predicate would abort the whole lookup and reach the user as an error dialog.
        /// Any failure here means the same thing: not a match.
        /// </summary>
        static bool HostsView(CustomTaskPane pane, Type viewType)
        {
            try
            {
                return pane.Control is WinForms.UserControl c &&
                       c.Controls.OfType<ElementHost>().Any(h => h.Child?.GetType() == viewType);
            }
            catch { return false; }
        }

        public static CustomTaskPane DuplicateCurrent(WpfHostControl wpfHostControl, CustomTaskPane current)
        {
            var elementHost = wpfHostControl.Controls
                        .OfType<ElementHost>()
                        .FirstOrDefault();

            if (elementHost?.Child is UserControl wpfControl)
            {
                var wpfType = wpfControl.GetType();
                var newWpfControl = (UserControl)Activator.CreateInstance(wpfType);
                var newWpfPane = CreateNew(
                  newWpfControl,
                  "@" + current.Title,
                  current.Width
                );

                newWpfPane.Visible = true;
                return newWpfPane;
            }

            return null;
        }


        public static CustomTaskPane CreateNew(
           UserControl userControl,
           string title,
           int width = 600)
        {
            try
            {

                var hostControl = new WpfHostControl();
                var host = new ElementHost { Dock = WinForms.DockStyle.Fill, Child = userControl };
                hostControl.Controls.Add(host);

                void setColor()
                {
                    var foreColor = hostControl.ForeColor;
                    var adjustedForeColor = Color.FromArgb(foreColor.A, foreColor.B, foreColor.G, foreColor.R);
                    userControl.Foreground = new SolidColorBrush(Color.FromArgb(adjustedForeColor.A, adjustedForeColor.R, adjustedForeColor.G, adjustedForeColor.B));

                    var backColor = hostControl.BackColor;
                    var adjustedBackColor = Color.FromArgb(backColor.A, backColor.B, backColor.G, backColor.R);
                    userControl.Background = new SolidColorBrush(Color.FromArgb(adjustedBackColor.A, adjustedBackColor.R, adjustedBackColor.G, adjustedBackColor.B));
                }

                var pane = TaskPaneManager.CreateNew(hostControl, title, width);
                pane.Visible = true;

                // Forward the pop-out toggle to the WPF view if it wants one.
                // TaskPaneManager.CreateNew already built the handler for hostControl and
                // offered it SetPopOutToggleAction - which a WpfHostControl does not have,
                // so the action went nowhere and the real view never received it. Reuse
                // that handler rather than constructing a second one: two handlers on one
                // host both subscribe to the same events and both try to reparent the same
                // content, so whichever runs second finds the content already moved.
                var setPopOut = userControl.GetType().GetMethod("SetPopOutToggleAction");
                var popOut = TaskPanePopOut.For(pane);
                if (setPopOut != null && popOut != null)
                    setPopOut.Invoke(userControl, new object[] { new Action<bool>(popOut.Toggle) });
                else if (setPopOut != null)
                    // The view wants a pop-out button but no handler was registered, so
                    // clicking it would do nothing at all. Say so rather than shipping a
                    // dead button silently.
                    Console.WriteLine("[WpfTaskPane] No pop-out handler registered for " + title);

                setColor();
                hostControl.ForeColorChanged += (_, __) => setColor();

                return pane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }
    }
}
