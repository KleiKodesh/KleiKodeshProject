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
                        DockToScreen(w, Screen.FromHandle(w.Handle), (SnapCell)Enum.Parse(typeof(SnapCell), p[2]));
                        break;
                    case "beside":
                        DockBeside(w, SnapDebugWindow(p[2]), (DropSide)Enum.Parse(typeof(DropSide), p[3]));
                        break;
                    case "size":
                    {
                        int edge = int.Parse(p[2]), dx = int.Parse(p[3]), dy = int.Parse(p[4]);
                        Rectangle b = w.Bounds;
                        var r = new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
                        bool l = edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT;
                        bool rr = edge == WMSZ_RIGHT || edge == WMSZ_TOPRIGHT || edge == WMSZ_BOTTOMRIGHT;
                        bool t = edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT;
                        bool bb = edge == WMSZ_BOTTOM || edge == WMSZ_BOTTOMLEFT || edge == WMSZ_BOTTOMRIGHT;
                        if (l) r.Left += dx; if (rr) r.Right += dx; if (t) r.Top += dy; if (bb) r.Bottom += dy;
                        OnWindowSizing(w, edge, ref r);
                        w.Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        break;
                    }
                    case "move":
                    {
                        int dx = int.Parse(p[2]), dy = int.Parse(p[3]);
                        Rectangle b = w.Bounds;
                        var r = new RECT { Left = b.Left + dx, Top = b.Top + dy, Right = b.Right + dx, Bottom = b.Bottom + dy };
                        OnWindowMovingRect(w, ref r);
                        w.Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        OnWindowMoving(w);
                        break;
                    }
                    case "close": w.Close(); break;
                    case "max": w.WindowState = FormWindowState.Maximized; break;
                    case "min": w.WindowState = FormWindowState.Minimized; break;
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
            foreach (var kv in SnapDebugNames) if (kv.Value == w) return kv.Key.ToString();
            return "key" + ((PageWindowForm)w).WindowKey;
        }

        private static string SnapDebugDump()
        {
            var sb = new StringBuilder();
            foreach (FluentChromeTabsForm w in AllAppWindows())
            {
                if (w.IsDisposed) continue;
                SnapGroup g = GroupOf(w, out int slot, out int part);
                string name = SnapDebugName(w);
                sb.AppendLine("  " + name + " visible=" + w.Visible + " state=" + w.WindowState + " seen=" + VisibleBounds(w)
                    + (g == null ? " free" : " docked " + CellAt(g.Rows, slot, part) + " split=" + g.Split.ToString("0.00")
                        + " slotSplit=" + g.Slots[slot].Split.ToString("0.00") + " cell=" + RectOf(g, slot, part < 0 ? 0 : part)));
            }
            sb.AppendLine("  groups=" + SnapGroups.Count);
            return sb.ToString();
        }
    }
}
#endif
