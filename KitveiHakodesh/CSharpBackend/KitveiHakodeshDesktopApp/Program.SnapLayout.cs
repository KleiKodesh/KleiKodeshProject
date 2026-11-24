using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FluentChromeTabs;

namespace KitveiHakodeshDesktopApp
{
    // Windows docked on a screen - Windows Snap's layouts, reached with our drop areas.
    //
    // A screen holds at most one group, and a group always fills the screen's working area.
    // It is two slots along a main line: left and right (columns), or top and bottom (rows).
    // A slot holds one window (a half) or two (quarters), split by a line of its own:
    //
    //   columns: | L | R |      rows:  |  T  |      quarters: | TL | TR |
    //                                  |  B  |                | BL | BR |
    //
    // Dragging a line moves every window on it. A window that closes or is dragged out by its
    // title bar leaves its place to its neighbour (a quarter's partner becomes the half again),
    // and a window dragged out gets back the size it had before it docked. Maximize is the
    // window's own; restoring it puts it back in its place. Docked windows have square corners,
    // like Snap's. UI thread only.
    internal static partial class Program
    {
        internal enum SnapCell { Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        // A docked window is never made narrower or lower than this (device pixels).
        private const int MinDockedWidth = 320;
        private const int MinDockedHeight = 200;

        private sealed class SnapSlot
        {
            // A: the first along the slot's own line (top in columns, left in rows); B: the other,
            // or null when A fills the slot.
            public FluentChromeTabsForm A, B;
            public double Split = 0.5;
            public bool IsEmpty => A == null && B == null;
        }

        private sealed class SnapGroup
        {
            public string ScreenName;
            public bool Rows;
            public double Split = 0.5;
            public readonly SnapSlot[] Slots = { new SnapSlot(), new SnapSlot() };
            public Rectangle Area => ScreenByName(ScreenName).WorkingArea;
        }

        private static readonly List<SnapGroup> SnapGroups = new List<SnapGroup>();

        // Each docked window's size and place before it docked: it gets them back when dragged out.
        private static readonly Dictionary<FluentChromeTabsForm, Rectangle> PreDockBounds = new Dictionary<FluentChromeTabsForm, Rectangle>();

        private static Screen ScreenByName(string name)
        {
            foreach (Screen screen in Screen.AllScreens) if (screen.DeviceName == name) return screen;
            return Screen.PrimaryScreen;
        }

        // ── Cells ───────────────────────────────────────────────────────────────

        private static bool IsHalf(SnapCell cell) => cell <= SnapCell.Bottom;

        private static bool IsRowsHalf(SnapCell cell) => cell == SnapCell.Top || cell == SnapCell.Bottom;

        // Where a cell is in a group: its slot, and its part (-1: the whole slot, 0: A, 1: B).
        private static void Locate(SnapCell cell, bool rows, out int slot, out int part)
        {
            bool left = cell == SnapCell.Left || cell == SnapCell.TopLeft || cell == SnapCell.BottomLeft;
            bool top = cell == SnapCell.Top || cell == SnapCell.TopLeft || cell == SnapCell.TopRight;
            if (IsHalf(cell)) { slot = rows ? (top ? 0 : 1) : (left ? 0 : 1); part = -1; return; }
            slot = rows ? (top ? 0 : 1) : (left ? 0 : 1);
            part = rows ? (left ? 0 : 1) : (top ? 0 : 1);
        }

        private static SnapCell CellAt(bool rows, int slot, int part)
        {
            if (part < 0) return rows ? (slot == 0 ? SnapCell.Top : SnapCell.Bottom) : (slot == 0 ? SnapCell.Left : SnapCell.Right);
            bool left = rows ? part == 0 : slot == 0, top = rows ? slot == 0 : part == 0;
            return top ? (left ? SnapCell.TopLeft : SnapCell.TopRight) : (left ? SnapCell.BottomLeft : SnapCell.BottomRight);
        }

        // The rectangle of a slot (part -1) or of one of its parts, with the given lines.
        private static Rectangle CellRect(Rectangle area, bool rows, double split, double slotSplit, int slot, int part)
        {
            Rectangle s;
            if (!rows)
            {
                int x = area.Left + (int)Math.Round(area.Width * split);
                s = slot == 0 ? Rectangle.FromLTRB(area.Left, area.Top, x, area.Bottom) : Rectangle.FromLTRB(x, area.Top, area.Right, area.Bottom);
                if (part < 0) return s;
                int y = s.Top + (int)Math.Round(s.Height * slotSplit);
                return part == 0 ? Rectangle.FromLTRB(s.Left, s.Top, s.Right, y) : Rectangle.FromLTRB(s.Left, y, s.Right, s.Bottom);
            }
            int my = area.Top + (int)Math.Round(area.Height * split);
            s = slot == 0 ? Rectangle.FromLTRB(area.Left, area.Top, area.Right, my) : Rectangle.FromLTRB(area.Left, my, area.Right, area.Bottom);
            if (part < 0) return s;
            int mx = s.Left + (int)Math.Round(s.Width * slotSplit);
            return part == 0 ? Rectangle.FromLTRB(s.Left, s.Top, mx, s.Bottom) : Rectangle.FromLTRB(mx, s.Top, s.Right, s.Bottom);
        }

        private static Rectangle RectOf(SnapGroup group, int slot, int part)
        {
            SnapSlot s = group.Slots[slot];
            return CellRect(group.Area, group.Rows, group.Split, s.Split, slot, s.B == null ? -1 : part);
        }

        // A screen's cell as it would be now: with that screen's group's lines when its group
        // has the same orientation, else the plain halves and quarters.
        private static Rectangle ScreenCellRect(Screen screen, SnapCell cell)
        {
            SnapGroup group = SnapGroups.Find(g => g.ScreenName == screen.DeviceName);
            bool rows = IsHalf(cell) ? IsRowsHalf(cell) : group != null && group.Rows;
            Locate(cell, rows, out int slot, out int part);
            bool same = group != null && group.Rows == rows;
            return CellRect(screen.WorkingArea, rows, same ? group.Split : 0.5, same ? group.Slots[slot].Split : 0.5, slot, part);
        }

        private static SnapGroup GroupOf(FluentChromeTabsForm window, out int slot, out int part)
        {
            foreach (SnapGroup group in SnapGroups)
                for (int i = 0; i < 2; i++)
                {
                    SnapSlot s = group.Slots[i];
                    if (s.A == window) { slot = i; part = s.B == null ? -1 : 0; return group; }
                    if (s.B == window) { slot = i; part = 1; return group; }
                }
            slot = -1; part = -1;
            return null;
        }

        private static bool IsDocked(FluentChromeTabsForm window) => GroupOf(window, out _, out _) != null;

        // ── Docking ─────────────────────────────────────────────────────────────

        /// <summary>Docks a window into a cell of a screen; whatever held that cell is let go where it is.</summary>
        private static void DockToScreen(FluentChromeTabsForm window, Screen screen, SnapCell cell)
        {
            if (window.IsDisposed) return;
            // The size to go back to: from before it first docked, kept through a re-dock.
            Rectangle before;
            if (!PreDockBounds.TryGetValue(window, out before))
                before = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
            Undock(window, restoreSize: false);
            PreDockBounds[window] = before;
            if (window.WindowState != FormWindowState.Normal) window.WindowState = FormWindowState.Normal;

            SnapGroup group = SnapGroups.Find(g => g.ScreenName == screen.DeviceName);
            // A half of the other orientation starts the screen over: the old group is let go.
            if (group != null && IsHalf(cell) && group.Rows != IsRowsHalf(cell))
            {
                ReleaseGroup(group);
                group = null;
            }
            if (group == null)
            {
                group = new SnapGroup { ScreenName = screen.DeviceName, Rows = IsHalf(cell) && IsRowsHalf(cell) };
                SnapGroups.Add(group);
            }

            Locate(cell, group.Rows, out int slotIndex, out int part);
            SnapSlot slot = group.Slots[slotIndex];
            if (part < 0)
            {
                LetGo(slot.A);
                LetGo(slot.B);
                slot.A = window;
                slot.B = null;
            }
            else if (slot.A != null && slot.B == null)
            {
                // The half there becomes the other quarter.
                FluentChromeTabsForm half = slot.A;
                if (part == 0) { slot.A = window; slot.B = half; }
                else slot.B = window;
                slot.Split = 0.5;
            }
            else if (slot.IsEmpty)
            {
                // Alone in its slot, a quarter fills it: the slot is never half empty.
                slot.A = window;
            }
            else
            {
                if (part == 0) { LetGo(slot.A); slot.A = window; }
                else { LetGo(slot.B); slot.B = window; }
            }
            LayoutGroup(group);
            window.Activate();
        }

        /// <summary>
        /// Docks a window at a side of another window: a docked half splits into quarters along
        /// its slot, an empty slot beside it is taken; a window not docked makes the screen two
        /// halves with it. False when that place cannot be had (a quarter is not split again).
        /// </summary>
        private static bool CanDockBeside(FluentChromeTabsForm target, DropSide side, out Screen screen, out SnapCell cell, out SnapCell targetCell)
        {
            screen = Screen.FromHandle(target.Handle);
            SnapGroup group = GroupOf(target, out int slot, out int part);
            targetCell = cell = SnapCell.Left;
            if (group == null)
            {
                bool rows = side == DropSide.Top || side == DropSide.Bottom;
                cell = rows ? (side == DropSide.Top ? SnapCell.Top : SnapCell.Bottom) : (side == DropSide.Left ? SnapCell.Left : SnapCell.Right);
                targetCell = rows ? (side == DropSide.Top ? SnapCell.Bottom : SnapCell.Top) : (side == DropSide.Left ? SnapCell.Right : SnapCell.Left);
                return HalfFits(screen.WorkingArea, rows, target);
            }
            if (part >= 0) return false;
            bool along = group.Rows ? side == DropSide.Top || side == DropSide.Bottom : side == DropSide.Left || side == DropSide.Right;
            if (along)
            {
                int other = slot == 0 ? 1 : 0;
                bool towardOther = group.Rows ? (side == DropSide.Bottom) == (other == 1) : (side == DropSide.Right) == (other == 1);
                if (!towardOther || !group.Slots[other].IsEmpty) return false;
                cell = CellAt(group.Rows, other, -1);
                targetCell = CellAt(group.Rows, slot, -1);
                return true;
            }
            int newPart = group.Rows ? (side == DropSide.Left ? 0 : 1) : (side == DropSide.Top ? 0 : 1);
            cell = CellAt(group.Rows, slot, newPart);
            targetCell = CellAt(group.Rows, slot, newPart == 0 ? 1 : 0);
            Rectangle s = RectOf(group, slot, -1);
            return group.Rows ? s.Width / 2 >= MinDockedWidth : s.Height / 2 >= MinDockedHeight;
        }

        private static bool HalfFits(Rectangle area, bool rows, FluentChromeTabsForm window)
            => rows ? area.Height / 2 >= Math.Max(MinDockedHeight, SystemInformation.MinimumWindowSize.Height)
                    : area.Width / 2 >= Math.Max(MinDockedWidth, window.MinimumWindowWidthPx);

        private static void DockBeside(FluentChromeTabsForm window, FluentChromeTabsForm target, DropSide side)
        {
            if (window.IsDisposed || target.IsDisposed) return;
            if (!CanDockBeside(target, side, out Screen screen, out SnapCell cell, out SnapCell targetCell)) return;
            if (!IsDocked(target)) DockToScreen(target, screen, targetCell);
            DockToScreen(window, screen, cell);
        }

        /// <summary>Where a window dropped at that side of the target would land (the drop preview).</summary>
        private static Rectangle? DockBesidePreview(FluentChromeTabsForm target, DropSide side)
        {
            if (!CanDockBeside(target, side, out Screen screen, out SnapCell cell, out _)) return null;
            SnapGroup group = GroupOf(target, out int slot, out _);
            if (group == null) return ScreenCellRect(screen, cell);
            Locate(cell, group.Rows, out int s, out int p);
            return CellRect(group.Area, group.Rows, group.Split, s == slot && p >= 0 ? 0.5 : group.Slots[s].Split, s, p);
        }

        // A window let go of its place by another taking it: it stays where it is, undocked.
        private static void LetGo(FluentChromeTabsForm window)
        {
            if (window == null) return;
            foreach (SnapGroup group in SnapGroups)
                foreach (SnapSlot slot in group.Slots)
                {
                    if (slot.A == window) { slot.A = slot.B; slot.B = null; }
                    else if (slot.B == window) slot.B = null;
                }
            PreDockBounds.Remove(window);
            SetSquareCorners(window, false);
        }

        private static void ReleaseGroup(SnapGroup group)
        {
            foreach (SnapSlot slot in group.Slots)
            {
                FluentChromeTabsForm a = slot.A, b = slot.B;
                LetGo(a);
                LetGo(b);
            }
            SnapGroups.Remove(group);
        }

        /// <summary>
        /// Takes a window out of its group (closed, or dragged out): its neighbour takes its
        /// place. True when it was docked.
        /// </summary>
        private static bool Undock(FluentChromeTabsForm window, bool restoreSize)
        {
            SnapGroup group = GroupOf(window, out int slotIndex, out _);
            if (group == null) return false;
            SnapSlot slot = group.Slots[slotIndex];
            if (slot.A == window) { slot.A = slot.B; slot.B = null; }
            else slot.B = null;
            slot.Split = 0.5;
            SetSquareCorners(window, false);
            if (!restoreSize) PreDockBounds.Remove(window);
            if (group.Slots[0].IsEmpty && group.Slots[1].IsEmpty) { SnapGroups.Remove(group); return true; }
            FillEmptiedHalf(group);
            LayoutGroup(group);
            return true;
        }

        // A half left empty while the other half holds two quarters: those two become the two
        // halves of the screen (the group turns from columns to rows or back), so nothing stays empty.
        private static void FillEmptiedHalf(SnapGroup group)
        {
            int empty = group.Slots[0].IsEmpty ? 0 : group.Slots[1].IsEmpty ? 1 : -1;
            if (empty < 0) return;
            SnapSlot full = group.Slots[empty == 0 ? 1 : 0];
            if (full.B == null) return; // a lone half stays a half, as Snap leaves it
            FluentChromeTabsForm a = full.A, b = full.B;
            group.Rows = !group.Rows;
            group.Split = full.Split;
            group.Slots[0].A = a; group.Slots[0].B = null; group.Slots[0].Split = 0.5;
            group.Slots[1].A = b; group.Slots[1].B = null; group.Slots[1].Split = 0.5;
        }

        private static void LayoutGroup(SnapGroup group)
        {
            for (int i = 0; i < 2; i++)
            {
                SnapSlot slot = group.Slots[i];
                Place(slot.A, RectOf(group, i, 0));
                Place(slot.B, RectOf(group, i, 1));
            }
            KeepGroupShadowsHidden(group);
        }

        private static void Place(FluentChromeTabsForm window, Rectangle cell)
        {
            if (window == null || window.IsDisposed) return;
            SetSquareCorners(window, true);
            if (window.WindowState == FormWindowState.Minimized) return;
            if (window.WindowState == FormWindowState.Maximized) return; // its own; restore puts it back
            SetVisibleBounds(window, cell);
        }

        private static void SetSquareCorners(FluentChromeTabsForm window, bool square)
        {
            if (window == null || window.IsDisposed || !window.IsHandleCreated) return;
            const int DONOTROUND = 1, DEFAULT = 0;
            int value = square ? DONOTROUND : DEFAULT;
            DwmSetWindowAttribute(window.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }

        // ── The reader moves and resizes docked windows ──────────────────────────

        /// <summary>
        /// A docked window's edge is dragged (WM_SIZING): an inside edge moves the line it is on,
        /// and every window on that line follows; an outside edge stays on the screen's edge.
        /// </summary>
        private static bool OnDockedWindowSizing(FluentChromeTabsForm window, int edge, ref RECT proposed)
        {
            SnapGroup group = GroupOf(window, out int slot, out int part);
            if (group == null) return false;
            Rectangle area = group.Area;
            Rectangle outer = window.Bounds, seen = VisibleBounds(window);
            // What the eye would see with the proposed rectangle.
            int left = proposed.Left + (seen.Left - outer.Left), right = proposed.Right - (outer.Right - seen.Right);
            int top = proposed.Top + (seen.Top - outer.Top), bottom = proposed.Bottom - (outer.Bottom - seen.Bottom);
            bool l = edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT;
            bool r = edge == WMSZ_RIGHT || edge == WMSZ_TOPRIGHT || edge == WMSZ_BOTTOMRIGHT;
            bool t = edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT;
            bool b = edge == WMSZ_BOTTOM || edge == WMSZ_BOTTOMLEFT || edge == WMSZ_BOTTOMRIGHT;
            SnapSlot s = group.Slots[slot];

            if (!group.Rows)
            {
                // The main line is vertical: slot 0's right edge, slot 1's left edge.
                if (slot == 0 && r) group.Split = Clamp((right - area.Left) / (double)area.Width, area.Width, MinDockedWidth);
                if (slot == 1 && l) group.Split = Clamp((left - area.Left) / (double)area.Width, area.Width, MinDockedWidth);
                // The slot's line is horizontal: part A's bottom, part B's top.
                if (part == 0 && b) s.Split = Clamp((bottom - area.Top) / (double)area.Height, area.Height, MinDockedHeight);
                if (part == 1 && t) s.Split = Clamp((top - area.Top) / (double)area.Height, area.Height, MinDockedHeight);
            }
            else
            {
                if (slot == 0 && b) group.Split = Clamp((bottom - area.Top) / (double)area.Height, area.Height, MinDockedHeight);
                if (slot == 1 && t) group.Split = Clamp((top - area.Top) / (double)area.Height, area.Height, MinDockedHeight);
                if (part == 0 && r) s.Split = Clamp((right - area.Left) / (double)area.Width, area.Width, MinDockedWidth);
                if (part == 1 && l) s.Split = Clamp((left - area.Left) / (double)area.Width, area.Width, MinDockedWidth);
            }

            // Everyone takes their place on the moved lines; this window's comes back as the proposal.
            Rectangle mine = RectOf(group, slot, part < 0 ? 0 : part);
            for (int i = 0; i < 2; i++)
            {
                SnapSlot o = group.Slots[i];
                if (o.A != null && o.A != window) Place(o.A, RectOf(group, i, 0));
                if (o.B != null && o.B != window) Place(o.B, RectOf(group, i, 1));
            }
            proposed.Left = mine.Left - (seen.Left - outer.Left);
            proposed.Top = mine.Top - (seen.Top - outer.Top);
            proposed.Right = mine.Right + (outer.Right - seen.Right);
            proposed.Bottom = mine.Bottom + (outer.Bottom - seen.Bottom);
            return true;
        }

        // A line's place as a share of the area, kept so both sides stay at least `min` long.
        private static double Clamp(double share, int length, int min)
        {
            double m = Math.Min(0.5, min / (double)Math.Max(1, length));
            return Math.Max(m, Math.Min(1 - m, share));
        }

        /// <summary>
        /// A docked window starts moving by its title bar (WM_MOVING): it leaves its group and
        /// gets back the size it had before it docked, under the cursor at the same share of its width.
        /// </summary>
        internal static void OnWindowMovingRect(FluentChromeTabsForm window, ref RECT proposed)
        {
            if (!IsDocked(window)) return;
            Rectangle before;
            bool known = PreDockBounds.TryGetValue(window, out before);
            Undock(window, restoreSize: true);
            PreDockBounds.Remove(window);
            if (!known) return;
            Point cursor = Cursor.Position;
            int width = proposed.Right - proposed.Left;
            double share = width > 0 ? (cursor.X - proposed.Left) / (double)width : 0.5;
            int newLeft = cursor.X - (int)Math.Round(before.Width * share);
            proposed.Left = newLeft;
            proposed.Right = newLeft + before.Width;
            proposed.Bottom = proposed.Top + before.Height;
        }

        /// <summary>A docked window was restored from maximized: back in its place.</summary>
        internal static void OnDockedWindowResized(FluentChromeTabsForm window)
        {
            if (window.WindowState != FormWindowState.Normal) return;
            SnapGroup group = GroupOf(window, out int slot, out int part);
            if (group == null) return;
            Rectangle cell = RectOf(group, slot, part < 0 ? 0 : part);
            if (VisibleBounds(window) != cell) window.BeginInvoke(new Action(() => { if (!window.IsDisposed) LayoutGroup(group); }));
        }

        // ── Shadows ─────────────────────────────────────────────────────────────

        // The window directly below this one in its group (its shadow would fall on it), or null.
        private static FluentChromeTabsForm WindowBelow(FluentChromeTabsForm window)
        {
            SnapGroup group = GroupOf(window, out int slot, out int part);
            if (group == null) return null;
            if (!group.Rows) return part == 0 ? group.Slots[slot].B : null;
            return slot == 0 ? group.Slots[1].A : null;
        }

        private static void KeepGroupShadowsHidden(SnapGroup group)
        {
            foreach (SnapSlot slot in group.Slots)
            {
                KeepUpperBelowLower(slot.A);
                KeepUpperBelowLower(slot.B);
            }
        }

        // Windows draws a window's shadow under any window above it. A docked window casts its
        // strongest shadow downward, onto the window below it; kept directly beneath that window,
        // the shadow falls under it and is not seen. It stays the active window - only its place
        // in the window order changes.
        private static void KeepUpperBelowLower(FluentChromeTabsForm window)
        {
            FluentChromeTabsForm lower = window == null ? null : WindowBelow(window);
            if (lower == null || lower.IsDisposed || !lower.IsHandleCreated || !window.IsHandleCreated) return;
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
            SetWindowPos(window.Handle, lower.Handle, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }
}
