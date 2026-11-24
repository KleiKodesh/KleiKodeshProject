#if DEBUG
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FluentChromeTabs;

namespace KitveiHakodeshDesktopApp
{
    // Debug builds only: drives the Snap layout from forwarded launch arguments, so docking can be
    // tested without a real mouse. Every command appends the layout (or the error) to
    // snap-debug.txt beside the exe.
    //   --debug-snap=screen|<win>|<cell>             dock into a screen cell (Left, TopRight, ...)
    //   --debug-snap=beside|<win>|<target>|<side>    dock at a side of another window
    //   --debug-snap=size|<win>|<edge>|<dx>|<dy>     drag an edge (WMSZ number) by dx/dy
    //   --debug-snap=move|<win>|<dx>|<dy>            drag the window by its title bar
    //   --debug-snap=close|<win>    max|<win>    restore|<win>    dump
    // <win>: "m" the first main window, or "1", "2", ... the page windows in the order they opened.
    internal static partial class Program
    {
        private static string SnapDebugFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snap-debug.txt");

        internal static bool TryRunSnapDebug(string request)
        {
            const string prefix = "--debug-snap=";
            if (!request.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string[] p = request.Substring(prefix.Length).Split('|');
            var log = new StringBuilder("> " + string.Join(" ", p) + Environment.NewLine);
            try
            {
                FluentChromeTabsForm w = p.Length > 1 ? SnapDebugWindow(p[1]) : null;
                switch (p[0])
                {
                    case "screen":
                        ScreenDocking.Dock(w, Screen.FromHandle(w.Handle), (ScreenDocking.SnapCell)Enum.Parse(typeof(ScreenDocking.SnapCell), p[2]));
                        break;
                    case "beside":
                        ScreenDocking.DockNextTo(w, SnapDebugWindow(p[2]), (ScreenDocking.DropSide)Enum.Parse(typeof(ScreenDocking.DropSide), p[3]));
                        break;
                    case "size":
                    {
                        int edge = int.Parse(p[2]), dx = int.Parse(p[3]), dy = int.Parse(p[4]);
                        Rectangle b = w.Bounds;
                        int left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
                        bool l = edge == 1 || edge == 4 || edge == 7, rr = edge == 2 || edge == 5 || edge == 8;
                        bool t = edge == 3 || edge == 4 || edge == 5, bb = edge == 6 || edge == 7 || edge == 8;
                        if (l) left += dx; if (rr) right += dx; if (t) top += dy; if (bb) bottom += dy;
                        w.Bounds = ScreenDocking.ResizeEdge(w, edge, Rectangle.FromLTRB(left, top, right, bottom));
                        break;
                    }
                    case "move":
                    {
                        int dx = int.Parse(p[2]), dy = int.Parse(p[3]);
                        Rectangle b = w.Bounds;
                        w.Bounds = ScreenDocking.MoveBy(w, new Rectangle(b.Left + dx, b.Top + dy, b.Width, b.Height));
                        break;
                    }
                    case "close": w.Close(); break;
                    case "max": w.WindowState = FormWindowState.Maximized; break;
                    case "min": w.WindowState = FormWindowState.Minimized; break;
                    case "tabs": log.Append(SnapDebugTabs()); break;
                    case "front": w.Activate(); break;
                    case "restore": w.WindowState = FormWindowState.Normal; break;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR " + ex);
            }
            // Layout after the command has settled (closing and restore lay out on the next turn).
            var timer = new Timer { Interval = 400 };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                try { File.AppendAllText(SnapDebugFile, log + SnapDebugDump() + Environment.NewLine); }
                catch (Exception) { }
            };
            timer.Start();
            return true;
        }

        private static FluentChromeTabsForm SnapDebugWindow(string id)
        {
            if (id == "m") return OpenWindows.Count > 0 ? OpenWindows[0] : null;
            int n = int.Parse(id);
            if (!SnapDebugNames.ContainsKey(n))
            {
                // Numbered in the order they opened; a number stays with its window after others close.
                foreach (PageWindowForm open in PageWindows)
                    if (!SnapDebugNames.ContainsValue(open)) { SnapDebugNames[SnapDebugNames.Count + 1] = open; }
            }
            return SnapDebugNames.TryGetValue(n, out PageWindowForm w) && !w.IsDisposed ? w : throw new ArgumentException("no window " + id);
        }

        private static readonly System.Collections.Generic.Dictionary<int, PageWindowForm> SnapDebugNames = new System.Collections.Generic.Dictionary<int, PageWindowForm>();

        private static string SnapDebugName(FluentChromeTabsForm w)
        {
            if (w is MainForm) return "m";
            foreach (PageWindowForm open in PageWindows)
                if (!open.IsDisposed && !SnapDebugNames.ContainsValue(open)) SnapDebugNames[SnapDebugNames.Count + 1] = open;
            foreach (var kv in SnapDebugNames) if (kv.Value == w) return kv.Key.ToString();
            return "key" + ((PageWindowForm)w).WindowKey;
        }

        // Each window's tabs as screen points (their centres), and its strip's height: for driving
        // the real mouse. Read through the strip's private layout (debug only).
        private static string SnapDebugTabs()
        {
            var sb = new StringBuilder();
            var tabRect = typeof(FluentChromeTabsForm).GetMethod("TabRect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (FluentChromeTabsForm w in AllAppWindows())
            {
                if (w.IsDisposed || !w.Visible) continue;
                sb.Append("TABS " + SnapDebugName(w) + " h=" + w.Handle.ToInt64());
                for (int i = 0; i < w.Tabs.Count; i++)
                {
                    var r = (Rectangle)tabRect.Invoke(w, new object[] { i });
                    Point c = w.PointToScreen(new Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
                    sb.Append(" " + c.X + "," + c.Y);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string SnapDebugDump()
        {
            var sb = new StringBuilder();
            foreach (FluentChromeTabsForm w in AllAppWindows())
            {
                if (w.IsDisposed) continue;
                string name = SnapDebugName(w);
                sb.AppendLine("  " + name + " visible=" + w.Visible + " state=" + w.WindowState + " seen=" + ScreenDocking.VisibleBoundsOf(w)
                    + " " + ScreenDocking.Describe(w));
            }
            sb.AppendLine("  groups=" + ScreenDocking.GroupCount);
            return sb.ToString();
        }
    }
}
#endif
