using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using FluentChromeTabs;

namespace KitveiHakodeshDesktopApp
{
    // Dragging a tab out of the strip - VS Code's way. What follows the cursor is the TAB (a
    // small floating copy with a preview of its page), never a window, so nothing is decided
    // until the button is released, and Windows' own Snap never fights the drag:
    //   over a tab strip          -> the tab goes into that strip at that spot
    //   over the zone at an edge
    //     of one of our windows   -> it opens as a window beside (or above/below) that one,
    //                                half each
    //   over the middle of
    //     another of our windows  -> it joins that window's tabs, as on its strip
    //   at a screen's edge        -> it opens as a window filling that half of the screen
    //   anywhere else             -> it opens as a window there
    //   Escape                    -> nothing happens
    // A window's only tab moves the window itself instead of opening a new one.
    // Windows placed side by side keep their shared edge shared (resizing one resizes the
    // other) until either is moved. Moving a whole window (by its title bar) over another
    // window's strip merges its tabs into that strip.
    // UI thread only.
    internal static partial class Program
    {
        // How much of a window's width (left/right) or height (top/bottom), from each edge,
        // places the tab beside it. The middle takes the tab in, like the strip.
        private const double DropZoneShare = 0.3;
        // The same for a screen, off the app: a smaller share, the screen being much larger.
        private const double ScreenZoneShare = 0.2;
        // Neither window of a pair is resized narrower than this (device pixels).
        private const int MinSharedWidth = 320;
        // Neither window of a stacked pair is resized lower than this (device pixels).
        private const int MinSharedHeight = 200;

        // IntoWindow: the middle of a window - the tab joins its tabs, at the end, as on its strip.
        private enum DropKind { None, Strip, IntoWindow, BesideWindow, ScreenHalf }

        // The tab drag in progress (one at a time: there is one mouse).
        private static TabDragGhost _ghost;
        private static DropKind _dropKind;
        private static FluentChromeTabsForm _dropWindow;       // Strip: that window's strip; BesideWindow: that window
        private static DropSide _dropSide;
        private static Rectangle _dropArea;        // ScreenHalf: the half
        private static DropZonePreview _dropPreview;
        // A window started (off screen) when the tab left the strip, so a drop that opens a
        // window shows one whose page is already up; closed unused when the drop goes elsewhere.

        // A window whose tab strip a window being MOVED (title bar) is over: on release the
        // moved window's tabs go in there.
        private static FluentChromeTabsForm _stripTarget;
        private static FluentChromeTabsForm _mergingWindow;

        // Windows placed side by side here: [left, right] and the gap between their window
        // rectangles when placed (negative: their invisible borders overlap).
        private enum DropSide { Left, Right, Top, Bottom }

        // Two windows placed side by side (or one above the other) here. First is the left
        // (or upper) one; Gap is between their window rectangles (negative: their invisible
        // borders overlap).
        private sealed class SideBySidePair
        {
            public FluentChromeTabsForm First, Second;
            public bool Stacked;
            public int Gap;
        }
        private static readonly List<SideBySidePair> SideBySidePairs = new List<SideBySidePair>();

        // Ends any pair the window is in.
        private static void UnpairWindow(FluentChromeTabsForm window)
        {
            SideBySidePairs.RemoveAll(p => p.First == window || p.Second == window);
        }

        // ── A tab dragged out of a strip (FluentChromeTabsForm.TabDragOutside) ──────────

        private static void OnTabDragOutside(FluentChromeTabsForm source, FluentTabDragOutsideEventArgs e)
        {
            switch (e.Phase)
            {
                case FluentTabDragPhase.Started:
                    EndTabDragVisuals();
                    // A window's only tab moves its own window: nothing to start.
                    _ghost = new TabDragGhost(e.Tab.Title, e.Tab.Icon, source.DeviceDpi);
                    _ghost.MoveTo(e.ScreenPoint);
                    _ghost.Show();
                    ShowPicture(_ghost, CapturePicture(source));
                    UpdateTabDropTarget(source, e.ScreenPoint);
                    break;
                case FluentTabDragPhase.Moved:
                    _ghost?.MoveTo(e.ScreenPoint);
                    UpdateTabDropTarget(source, e.ScreenPoint);
                    break;
                case FluentTabDragPhase.Returned:
                case FluentTabDragPhase.Cancelled:
                    EndTabDragVisuals();
                    break;
                case FluentTabDragPhase.Dropped:
                    DropKind kind = _dropKind;
                    FluentChromeTabsForm target = _dropWindow;
                    DropSide side = _dropSide;
                    Rectangle area = _dropArea;
                    EndTabDragVisuals();
                    if (e.Tab.Tag is string tabId) DropTab(source, tabId, e.Tab.Title, e.ScreenPoint, kind, target, side, area);
                    break;
            }
        }

        private static async void ShowPicture(TabDragGhost ghost, Task<Image> picture)
        {
            Image image = null;
            try { image = await picture; } catch (Exception) { }
            if (image != null && !ghost.IsDisposed) ghost.SetPicture(image);
        }

        private static void UpdateTabDropTarget(FluentChromeTabsForm source, Point cursor)
        {
            IntPtr ghost = _ghost != null && _ghost.IsHandleCreated ? _ghost.Handle : IntPtr.Zero;

            // Over a strip: the tab goes in there. Its own strip is where the drag came from.
            FluentChromeTabsForm strip = FluentChromeTabsForm.FormWithStripAt(cursor, ghost);
            if (strip != null && strip != source && IsDropTarget(strip))
            {
                SetDropTarget(DropKind.Strip, strip, DropSide.Left, Rectangle.Empty);
                return;
            }
            if (strip == source)
            {
                SetDropTarget(DropKind.None, null, DropSide.Left, Rectangle.Empty);
                return;
            }

            // Over one of our windows: the zone at its nearest edge places the tab beside it
            // (or above or below it); the middle of another window takes the tab in, like its strip.
            FluentChromeTabsForm under = OurWindowAt(cursor, ghost);
            if (under != null)
            {
                Rectangle visible = VisibleBounds(under);
                DropSide? side = EdgeZone(visible, cursor);
                // A window's only tab dropped on its own window has nothing to be beside.
                bool alone = under == source && TabCountOf(source) <= 1;
                // Only where two windows fit (SplitArea): otherwise the middle's rule applies.
                if (side.HasValue && !alone && SplitArea(under, side.Value).HasValue)
                    SetDropTarget(DropKind.BesideWindow, under, side.Value, Rectangle.Empty);
                else if (under != source && IsDropTarget(under))
                    SetDropTarget(DropKind.IntoWindow, under, DropSide.Left, Rectangle.Empty);
                else
                    SetDropTarget(DropKind.None, null, DropSide.Left, Rectangle.Empty);
                return;
            }

            // Off the app: the zone at the screen's nearest edge gives that half of the screen,
            // like a window's zones; the middle opens a window where the tab is let go.
            Rectangle work = Screen.FromPoint(cursor).WorkingArea;
            DropSide? screenSide = EdgeZone(work, cursor, ScreenZoneShare);
            if (screenSide.HasValue)
            {
                SetDropTarget(DropKind.ScreenHalf, null, screenSide.Value, HalfOf(work, screenSide.Value));
                return;
            }

            SetDropTarget(DropKind.None, null, DropSide.Left, Rectangle.Empty);
        }

        // The edge zone of a window the cursor is in (the nearest edge, when within its
        // share of the window), or null in the middle.
        private static DropSide? EdgeZone(Rectangle visible, Point cursor, double share = DropZoneShare)
        {
            double x = (cursor.X - visible.Left) / (double)Math.Max(1, visible.Width);
            double y = (cursor.Y - visible.Top) / (double)Math.Max(1, visible.Height);
            double toLeft = x, toRight = 1 - x, toTop = y, toBottom = 1 - y;
            double nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));
            if (nearest > share) return null;
            if (nearest == toLeft) return DropSide.Left;
            if (nearest == toRight) return DropSide.Right;
            return nearest == toTop ? DropSide.Top : DropSide.Bottom;
        }

        private static void SetDropTarget(DropKind kind, FluentChromeTabsForm window, DropSide side, Rectangle area)
        {
            _dropKind = kind;
            _dropWindow = window;
            _dropSide = side;
            _dropArea = area;

            // Strip and window drops mark the strip too: that is where the tab will appear.
            SetStripTarget(kind == DropKind.Strip || kind == DropKind.IntoWindow ? window : null);
            if (kind == DropKind.IntoWindow) ShowDropPreview(VisibleBounds(window));
            else if (kind == DropKind.BesideWindow) ShowDropPreview(HalfOf(SplitArea(window, side).Value, side));
            else if (kind == DropKind.ScreenHalf) ShowDropPreview(area);
            else HideDropPreview();
        }

        private static void EndTabDragVisuals()
        {
            if (_ghost != null && !_ghost.IsDisposed) _ghost.Dispose();
            _ghost = null;
            _dropKind = DropKind.None;
            _dropWindow = null;
            SetStripTarget(null);
            HideDropPreview();
        }

        private static void DropTab(FluentChromeTabsForm source, string tabId, string title, Point cursor,
            DropKind kind, FluentChromeTabsForm target, DropSide side, Rectangle area)
        {
            if (source.IsDisposed) return;
            bool onlyTab = TabCountOf(source) <= 1;

            switch (kind)
            {
                case DropKind.Strip:
                case DropKind.IntoWindow:
                    if (target == null || target.IsDisposed) return;
                    int position = kind == DropKind.Strip ? target.DropIndexAtScreenPoint(cursor) : -1;
                    MoveTab(source, tabId, target, position);
                    target.Activate();
                    return;

                case DropKind.BesideWindow:
                    if (target == null || target.IsDisposed || !SplitArea(target, side).HasValue) return;
                    if (onlyTab) PlaceSideBySide(source, target, side);
                    else OpenWindowForTab(source, tabId, window => PlaceSideBySide(window, target, side));
                    return;

                case DropKind.ScreenHalf:
                    Action<FluentChromeTabsForm> toHalf = window =>
                    {
                        if (window.WindowState != FormWindowState.Normal) window.WindowState = FormWindowState.Normal;
                        SetVisibleBounds(window, area);
                        window.Activate();
                    };
                    if (onlyTab) toHalf(source);
                    else OpenWindowForTab(source, tabId, toHalf);
                    return;

                default:
                    // A window where the tab was let go, the size of the one it came from; the
                    // cursor lands on its strip, near where tabs start (the right side: RTL).
                    Size size = source.WindowState == FormWindowState.Normal ? source.Size : source.RestoreBounds.Size;
                    int inset = (int)(140 * source.DeviceDpi / 96f);
                    var bounds = KeepOnScreen(
                        new Rectangle(cursor.X - size.Width + inset, cursor.Y - (int)(16 * source.DeviceDpi / 96f), size.Width, size.Height),
                        cursor);
                    Action<FluentChromeTabsForm> place = window =>
                    {
                        if (window.WindowState != FormWindowState.Normal) window.WindowState = FormWindowState.Normal;
                        window.Bounds = bounds;
                        window.Activate();
                    };
                    if (onlyTab) place(source);
                    else OpenWindowForTab(source, tabId, place);
                    return;
            }
        }

        // ── Our windows: the main ones and the windows their pages draw ─────────────
        //
        // A main window (FluentChromeTabsForm) has a page of its own; a page window (PageWindowForm) is
        // drawn by its owner's page (the VS Code model). A tab moving between windows of one
        // page moves live; between two pages it is packed and unpacked (Program.TabMoves).

        private static readonly List<PageWindowForm> PageWindows = new List<PageWindowForm>();

        // The window a page opens next for a dropped tab is placed by this, once shown.
        private static Action<FluentChromeTabsForm> _pendingPlacement;

        /// <summary>A page window was made for <paramref name="owner"/>'s page: it joins the drag and drop.</summary>
        private static void AddPageWindow(MainForm owner, PageWindowForm window)
        {
            PageWindows.Add(window);
            window.TabsMirror.EnableTabMovesBetweenWindows();
            window.TabDragOutside += (s, e) => OnTabDragOutside(window, e);
            window.Activated += (s, e) =>
            {
                lock (EarlyRequestsLock) _activeWindow = owner;
                OnWindowActivated(window);
            };
            window.FormClosed += (s, e) =>
            {
                PageWindows.Remove(window);
                ForgetSideBySide(window);
            };
            Action<FluentChromeTabsForm> place = _pendingPlacement;
            _pendingPlacement = null;
            if (place != null) window.Shown += (s, e) => place(window);
        }

        // The main window whose page shows this window's tabs.
        private static MainForm PageOf(FluentChromeTabsForm window)
            => window as MainForm ?? (window as PageWindowForm)?.PageOwner;

        // The page's key for the window: "" for a main window.
        private static string WindowKeyOf(FluentChromeTabsForm window)
            => (window as PageWindowForm)?.WindowKey ?? "";

        private static int TabCountOf(FluentChromeTabsForm window)
            => (window as MainForm)?.TabsMirror.PageTabCount ?? (window as PageWindowForm)?.TabsMirror.PageTabCount ?? 0;

        private static IEnumerable<FluentChromeTabsForm> AllAppWindows()
        {
            foreach (MainForm open in OpenWindows) yield return open;
            foreach (PageWindowForm open in PageWindows) yield return open;
        }

        // The drag picture of a window's page (a page window draws in its owner's renderer: none yet).
        private static Task<Image> CapturePicture(FluentChromeTabsForm window)
            => window is MainForm main ? main.Viewer.CapturePagePictureAsync() : Task.FromResult<Image>(null);

        // A tab into another window: live within one page, packed and unpacked between two.
        private static void MoveTab(FluentChromeTabsForm source, string tabId, FluentChromeTabsForm target, int position)
        {
            MainForm from = PageOf(source), to = PageOf(target);
            if (from == null || to == null || from.IsDisposed || to.IsDisposed) return;
            if (from == to) from.Viewer.MoveTabToAppWindow(tabId, WindowKeyOf(target), position);
            else _ = MoveTabToWindowAsync(from, tabId, to, position, null);
        }

        // A new window of the source's page for the tab, placed by `place` once it shows.
        private static void OpenWindowForTab(FluentChromeTabsForm source, string tabId, Action<FluentChromeTabsForm> place)
        {
            MainForm page = PageOf(source);
            if (page == null || page.IsDisposed) return;
            _pendingPlacement = place;
            page.Viewer.MoveTabToAppWindow(tabId, null, -1);
        }

        // A dropped window lands whole on the screen it was dropped on: no larger than its
        // working area, and moved in from any edge it would cross.
        private static Rectangle KeepOnScreen(Rectangle bounds, Point cursor)
        {
            Rectangle work = Screen.FromPoint(cursor).WorkingArea;
            int width = Math.Min(bounds.Width, work.Width), height = Math.Min(bounds.Height, work.Height);
            int left = Math.Max(work.Left, Math.Min(bounds.Left, work.Right - width));
            int top = Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - height));
            return new Rectangle(left, top, width, height);
        }

        private static bool IsDropTarget(FluentChromeTabsForm window)
        {
            if (window == null || window.IsDisposed) return false;
            if (window is PageWindowForm page) return PageWindows.Contains(page) && IsDropTarget(page.PageOwner);
            var main = window as MainForm;
            return main != null && main.Visible && OpenWindows.Contains(main) && !ClosingWindows.Contains(main);
        }

        private static Rectangle HalfOf(Rectangle area, DropSide side)
        {
            switch (side)
            {
                case DropSide.Left: return new Rectangle(area.Left, area.Top, area.Width / 2, area.Height);
                case DropSide.Right: return new Rectangle(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height);
                case DropSide.Top: return new Rectangle(area.Left, area.Top, area.Width, area.Height / 2);
                default: return new Rectangle(area.Left, area.Top + area.Height / 2, area.Width, area.Height - area.Height / 2);
            }
        }

        private static DropSide Opposite(DropSide side)
        {
            switch (side)
            {
                case DropSide.Left: return DropSide.Right;
                case DropSide.Right: return DropSide.Left;
                case DropSide.Top: return DropSide.Bottom;
                default: return DropSide.Top;
            }
        }

        // ── A whole window moved by its title bar (MainForm.WndProc) ───────────────────

        internal static void OnWindowMoving(FluentChromeTabsForm window)
        {
            // Any move of a window that was placed side by side takes it out of its pair.
            UnpairWindow(window);

            // Over another window's tab strip: this window's tabs go in there on release.
            FluentChromeTabsForm strip = FluentChromeTabsForm.FormWithStripAt(Cursor.Position, window.Handle);
            if (strip != null && strip != window && IsDropTarget(strip))
            {
                _mergingWindow = window;
                SetStripTarget(strip);
                return;
            }
            if (_mergingWindow == window)
            {
                _mergingWindow = null;
                SetStripTarget(null);
            }
        }

        internal static void OnWindowMoveEnded(FluentChromeTabsForm window)
        {
            if (_mergingWindow != window) return;
            FluentChromeTabsForm strip = _stripTarget;
            _mergingWindow = null;
            SetStripTarget(null);
            if (strip == null || strip.IsDisposed || window.IsDisposed) return;

            // Every tab of the moved window goes into the strip at the drop point, in order;
            // the window, left empty by the last one, closes (Program.TabMoves).
            var tabIds = new List<string>();
            foreach (FluentTab tab in window.Tabs)
                if (tab.Tag is string id) tabIds.Add(id);
            int position = strip.DropIndexAtScreenPoint(Cursor.Position);
            window.BeginInvoke(new Action(() => { _ = MergeWindowIntoAsync(window, tabIds, strip, position); }));
        }

        private static async Task MergeWindowIntoAsync(FluentChromeTabsForm source, List<string> tabIds, FluentChromeTabsForm target, int position)
        {
            MainForm from = PageOf(source), to = PageOf(target);
            for (int i = 0; i < tabIds.Count; i++)
            {
                if (source.IsDisposed || target.IsDisposed || from == null || to == null) return;
                if (from == to) { MoveTab(source, tabIds[i], target, position + i); continue; }
                // A failed move leaves that tab (and the rest) where they were.
                if (!await MoveTabToWindowAsync(from, tabIds[i], to, position + i, null)) return;
            }
        }

        private static void SetStripTarget(FluentChromeTabsForm strip)
        {
            if (_stripTarget == strip) return;
            if (_stripTarget != null && !_stripTarget.IsDisposed) _stripTarget.DropHintVisible = false;
            _stripTarget = strip;
            if (strip != null) strip.DropHintVisible = true;
        }

        /// <summary>A window is being resized by an edge: its partner follows the shared edge.</summary>
        internal static void OnWindowSizing(FluentChromeTabsForm window, int edge, ref RECT proposed)
        {
            foreach (SideBySidePair pair in SideBySidePairs)
            {
                bool isFirst = pair.First == window, isSecond = pair.Second == window;
                if (!isFirst && !isSecond) continue;
                FluentChromeTabsForm other = isFirst ? pair.Second : pair.First;
                if (other.IsDisposed || other.WindowState != FormWindowState.Normal) return;
                Rectangle o = other.Bounds;

                if (!pair.Stacked)
                {
                    bool sharedEdgeMoves = isFirst
                        ? edge == WMSZ_RIGHT || edge == WMSZ_TOPRIGHT || edge == WMSZ_BOTTOMRIGHT
                        : edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT;
                    if (!sharedEdgeMoves) return;
                    if (isFirst)
                    {
                        // The shared edge may not push the partner below its minimum width.
                        proposed.Right = Math.Min(proposed.Right, o.Right - MinSharedWidth - pair.Gap);
                        proposed.Right = Math.Max(proposed.Right, proposed.Left + MinSharedWidth);
                        other.Bounds = Rectangle.FromLTRB(proposed.Right + pair.Gap, o.Top, o.Right, o.Bottom);
                    }
                    else
                    {
                        proposed.Left = Math.Max(proposed.Left, o.Left + MinSharedWidth - pair.Gap);
                        proposed.Left = Math.Min(proposed.Left, proposed.Right - MinSharedWidth);
                        other.Bounds = Rectangle.FromLTRB(o.Left, o.Top, proposed.Left - pair.Gap, o.Bottom);
                    }
                }
                else
                {
                    bool sharedEdgeMoves = isFirst
                        ? edge == WMSZ_BOTTOM || edge == WMSZ_BOTTOMLEFT || edge == WMSZ_BOTTOMRIGHT
                        : edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT;
                    if (!sharedEdgeMoves) return;
                    if (isFirst)
                    {
                        proposed.Bottom = Math.Min(proposed.Bottom, o.Bottom - MinSharedHeight - pair.Gap);
                        proposed.Bottom = Math.Max(proposed.Bottom, proposed.Top + MinSharedHeight);
                        other.Bounds = Rectangle.FromLTRB(o.Left, proposed.Bottom + pair.Gap, o.Right, o.Bottom);
                    }
                    else
                    {
                        proposed.Top = Math.Max(proposed.Top, o.Top + MinSharedHeight - pair.Gap);
                        proposed.Top = Math.Min(proposed.Top, proposed.Bottom - MinSharedHeight);
                        other.Bounds = Rectangle.FromLTRB(o.Left, o.Top, o.Right, proposed.Top - pair.Gap);
                    }
                }
                return;
            }
        }

        /// <summary>A window was activated: the upper window of a stacked pair goes back beneath the lower one.</summary>
        internal static void OnWindowActivated(FluentChromeTabsForm window)
        {
            KeepUpperBelowLower(window);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        /// <summary>
        /// A window is about to change its place in the window order (WM_WINDOWPOSCHANGING) -
        /// a click inside it, activation, anything. The upper window of a stacked pair never goes
        /// above the lower one: it lands directly beneath it, and when it is coming to the front
        /// the lower window comes too (without the focus), so the pair stays in front together.
        /// </summary>
        internal static void OnWindowPosChanging(FluentChromeTabsForm window, ref WINDOWPOS pos)
        {
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
            if ((pos.flags & SWP_NOZORDER) != 0) return;
            SideBySidePair pair = SideBySidePairs.Find(p => p.Stacked && p.First == window);
            if (pair == null || pair.Second.IsDisposed || !pair.Second.IsHandleCreated) return;
            if (pair.Second.WindowState == FormWindowState.Minimized) return;
            IntPtr lower = pair.Second.Handle;
            if (pos.hwndInsertAfter == lower) return;

            // To the front (HWND_TOP / HWND_TOPMOST): the lower window first.
            if (pos.hwndInsertAfter == IntPtr.Zero || pos.hwndInsertAfter == new IntPtr(-1))
                SetWindowPos(lower, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            pos.hwndInsertAfter = lower;
        }

        // Windows draws a window's shadow under any window above it. The upper window of a
        // stacked pair casts its strongest shadow downward, onto the lower one; kept directly
        // beneath the lower window, that shadow falls under it and is not seen. It stays the
        // active window - only its place in the window order changes.
        private static void KeepUpperBelowLower(FluentChromeTabsForm window)
        {
            SideBySidePair pair = SideBySidePairs.Find(p => p.Stacked && p.First == window);
            if (pair == null || pair.Second.IsDisposed || !pair.Second.IsHandleCreated || !window.IsHandleCreated) return;
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
            SetWindowPos(window.Handle, pair.Second.Handle, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        internal static void ForgetSideBySide(FluentChromeTabsForm window)
        {
            UnpairWindow(window);
            if (_dropWindow == window) EndTabDragVisuals();
            if (_mergingWindow == window) { _mergingWindow = null; SetStripTarget(null); }
            if (_stripTarget == window) SetStripTarget(null);
        }

        // The topmost of our windows at the point, looking past `ignore` (the drag ghost);
        // null when another program's window covers the point.
        private static FluentChromeTabsForm OurWindowAt(Point cursor, IntPtr ignore)
        {
            IntPtr preview = _dropPreview != null && _dropPreview.IsHandleCreated ? _dropPreview.Handle : IntPtr.Zero;
            for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (h == ignore || h == preview || !IsWindowVisible(h)) continue;
                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                if (cursor.X < r.Left || cursor.X >= r.Right || cursor.Y < r.Top || cursor.Y >= r.Bottom) continue;
                foreach (FluentChromeTabsForm open in AllAppWindows())
                    if (!open.IsDisposed && open.IsHandleCreated && open.Handle == h
                        && open.WindowState != FormWindowState.Minimized && IsDropTarget(open))
                        return open;
                return null;
            }
            return null;
        }

        /// <summary>
        /// What follows the cursor while a tab is dragged: the tab as a rounded card, with a
        /// preview of its page under it - Windows 11's rounded corners and shadow, and the
        /// app's light or dark colors.
        /// </summary>
        private sealed class TabDragGhost : Form
        {
            private readonly string _title;
            private readonly Image _icon;
            private readonly float _scale;
            private readonly bool _dark;
            private readonly Color _accent;
            private Image _picture;

            public TabDragGhost(string title, Image icon, int dpi)
            {
                _title = title ?? "";
                _icon = icon;
                _scale = dpi / 96f;
                _dark = KitveiHakodesh.Core.Settings.AppSettingsRegistry.GetDarkMode();
                _accent = ParseColor(KitveiHakodesh.Core.Settings.AppSettingsRegistry.GetAccentColor(), Color.FromArgb(124, 92, 252));
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                RightToLeft = RightToLeft.Yes;
                BackColor = _dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
                Font = new Font("Segoe UI", 9f);
                Size = new Size(Px(260), Px(44));
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            private int Px(int logical) => (int)(logical * _scale);

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int CS_DROPSHADOW = 0x20000;
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    cp.ClassStyle |= CS_DROPSHADOW;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // Windows 11: rounded corners with the system's own shadow. Older Windows
                // ignores the attribute and keeps square corners with the class shadow.
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }

            /// <summary>The cursor holds the tab near its start (the right side: RTL).</summary>
            public void MoveTo(Point cursor)
            {
                Location = new Point(cursor.X - Width + Px(28), cursor.Y - Px(20));
            }

            /// <summary>The page's picture, shown small under the tab.</summary>
            public void SetPicture(Image picture)
            {
                _picture = picture;
                int width = Px(260);
                int previewWidth = width - Px(12);
                int previewHeight = Math.Min(Px(160), (int)((long)picture.Height * previewWidth / Math.Max(1, picture.Width)));
                Point anchor = new Point(Right - Px(28), Top + Px(20));
                Size = new Size(width, Px(44) + previewHeight + Px(6));
                MoveTo(anchor);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(BackColor);

                Color card = _dark ? Color.FromArgb(45, 45, 45) : Color.White;
                Color text = _dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(26, 26, 26);
                Color hairline = _dark ? Color.FromArgb(64, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);

                // The tab: a rounded card, an accent mark at its start, icon and title.
                var tab = new Rectangle(Px(6), Px(6), Width - Px(12), Px(32));
                using (GraphicsPath path = Rounded(tab, Px(6)))
                {
                    using (var brush = new SolidBrush(card)) g.FillPath(brush, path);
                    using (var pen = new Pen(hairline)) g.DrawPath(pen, path);
                }
                using (var mark = new SolidBrush(_accent))
                    g.FillRectangle(mark, tab.Right - Px(3), tab.Top + Px(9), Px(2), tab.Height - Px(18));

                int x = tab.Right - Px(12);
                if (_icon != null)
                {
                    int icon = Px(16);
                    x -= icon;
                    g.DrawImage(_icon, new Rectangle(x, tab.Top + (tab.Height - icon) / 2, icon, icon));
                    x -= Px(8);
                }
                var textRect = new Rectangle(tab.Left + Px(10), tab.Top, Math.Max(0, x - tab.Left - Px(10)), tab.Height);
                TextRenderer.DrawText(g, _title, Font, textRect, text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                // The page, small, in rounded corners under the tab.
                if (_picture != null)
                {
                    var preview = new Rectangle(Px(6), tab.Bottom + Px(4), Width - Px(12), Height - tab.Bottom - Px(10));
                    if (preview.Height > 0)
                    {
                        using (GraphicsPath clip = Rounded(preview, Px(6)))
                        {
                            GraphicsState state = g.Save();
                            g.SetClip(clip);
                            int sourceHeight = Math.Min(_picture.Height, (int)((long)_picture.Width * preview.Height / Math.Max(1, preview.Width)));
                            g.DrawImage(_picture, preview, 0, 0, _picture.Width, sourceHeight, GraphicsUnit.Pixel);
                            g.Restore(state);
                            using (var pen = new Pen(hairline)) g.DrawPath(pen, clip);
                        }
                    }
                }
            }

            private static GraphicsPath Rounded(Rectangle r, int radius)
            {
                float d = radius * 2f;
                var path = new GraphicsPath();
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            private static Color ParseColor(string hex, Color fallback)
            {
                try { return string.IsNullOrEmpty(hex) ? fallback : ColorTranslator.FromHtml(hex.StartsWith("#") ? hex : "#" + hex); }
                catch (Exception) { return fallback; }
            }
        }

        // ── Placing ───────────────────────────────────────────────────────────────

        // The area the two windows share: the target's place, or the whole working area of its
        // screen when it was maximized (the same two halves Snap would give) or when its place
        // is too small to halve. Null when not even the screen fits two of them that way.
        private static Rectangle? SplitArea(FluentChromeTabsForm target, DropSide side)
        {
            Rectangle screen = Screen.FromHandle(target.Handle).WorkingArea;
            Rectangle area = target.WindowState == FormWindowState.Maximized ? screen : VisibleBounds(target);
            if (!HalvesFit(target, area, side)) area = screen;
            return HalvesFit(target, area, side) ? area : (Rectangle?)null;
        }

        // Both halves at least as large as a window can be made (the strip sets the width).
        private static bool HalvesFit(FluentChromeTabsForm window, Rectangle area, DropSide side)
        {
            bool stacked = side == DropSide.Top || side == DropSide.Bottom;
            return stacked
                ? area.Height / 2 >= Math.Max(MinSharedHeight, SystemInformation.MinimumWindowSize.Height)
                : area.Width / 2 >= Math.Max(MinSharedWidth, window.MinimumWindowWidthPx);
        }

        // `dropped` takes the `side` half of the target's place; the target takes the other half.
        private static void PlaceSideBySide(FluentChromeTabsForm dropped, FluentChromeTabsForm target, DropSide side)
        {
            if (dropped.IsDisposed || target.IsDisposed) return;
            Rectangle? split = SplitArea(target, side);
            if (!split.HasValue) return;
            Rectangle area = split.Value;
            if (target.WindowState != FormWindowState.Normal) target.WindowState = FormWindowState.Normal;
            if (dropped.WindowState != FormWindowState.Normal) dropped.WindowState = FormWindowState.Normal;

            bool droppedFirst = side == DropSide.Left || side == DropSide.Top;
            bool stacked = side == DropSide.Top || side == DropSide.Bottom;
            FluentChromeTabsForm first = droppedFirst ? dropped : target, second = droppedFirst ? target : dropped;

            UnpairWindow(first);
            UnpairWindow(second);
            SetVisibleBounds(dropped, HalfOf(area, side));
            SetVisibleBounds(target, HalfOf(area, Opposite(side)));
            int gap = stacked ? second.Bounds.Top - first.Bounds.Bottom : second.Bounds.Left - first.Bounds.Right;
            SideBySidePairs.Add(new SideBySidePair { First = first, Second = second, Stacked = stacked, Gap = gap });
            dropped.Activate();
            if (stacked) KeepUpperBelowLower(first);
        }

        // A window's rectangle includes invisible resize borders; what the eye sees is the
        // DWM frame. Halves are measured and set by what is visible, so the two windows
        // meet edge to edge with no gap.
        private static Rectangle VisibleBounds(Form form)
        {
            RECT frame;
            if (DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf(typeof(RECT))) == 0
                && frame.Right > frame.Left)
                return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
            return form.Bounds;
        }

        private static void SetVisibleBounds(Form form, Rectangle visible)
        {
            Rectangle outer = form.Bounds, seen = VisibleBounds(form);
            form.Bounds = Rectangle.FromLTRB(
                visible.Left - (seen.Left - outer.Left),
                visible.Top - (seen.Top - outer.Top),
                visible.Right + (outer.Right - seen.Right),
                visible.Bottom + (outer.Bottom - seen.Bottom));
        }

        // ── The preview ───────────────────────────────────────────────────────────

        private static void ShowDropPreview(Rectangle bounds)
        {
            if (_dropPreview == null || _dropPreview.IsDisposed) _dropPreview = new DropZonePreview();
            if (_dropPreview.Bounds != bounds) _dropPreview.Bounds = bounds;
            if (!_dropPreview.Visible) _dropPreview.Show();
        }

        private static void HideDropPreview()
        {
            if (_dropPreview != null && !_dropPreview.IsDisposed && _dropPreview.Visible) _dropPreview.Hide();
        }

        /// <summary>The see-through sheet that marks where a dropped window will land. Never takes focus or clicks.</summary>
        private sealed class DropZonePreview : Form
        {
            public DropZonePreview()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.FromArgb(124, 92, 252); // the app's accent
                Opacity = 0.28;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    return cp;
                }
            }
        }

        // ── Win32 ─────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        internal const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
        private const uint GW_HWNDNEXT = 2;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    }
}
