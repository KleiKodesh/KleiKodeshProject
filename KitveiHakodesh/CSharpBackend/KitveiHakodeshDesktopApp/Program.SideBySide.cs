using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KitveiHakodeshDesktopApp
{
    // Two windows side by side - the app's "split view". A tab dragged out of the strip
    // becomes a window that Windows itself moves, so real Snap works at the screen edges and
    // on Windows 11's layout bar with no code here. This file adds the closer target: drop
    // the dragged window over the left or right part of another of our windows and the two
    // share that window's place, half each. Windows has no API to snap a window, so the two
    // are placed here, and their shared edge is kept shared here too (resizing one resizes
    // the other) until either is moved away.
    // UI thread only.
    internal static partial class Program
    {
        // How much of a window's width, on each side, is a drop target. The middle stays
        // neutral, so a window can still be dropped over another without splitting it.
        private const double DropZoneShare = 0.35;
        // Neither window of a pair is resized narrower than this (device pixels).
        private const int MinSharedWidth = 320;

        // The window being moved after a tab drag (a torn-off tab's new window, or a
        // window dragged by its only tab), the target it hovers and the side.
        private static MainForm _draggedWindow;
        private static MainForm _dropTarget;
        private static bool _dropOnLeft;
        private static DropZonePreview _dropPreview;

        // Windows placed side by side here: [left, right] and the gap between their window
        // rectangles when placed (negative: their invisible borders overlap).
        private sealed class SideBySidePair
        {
            public MainForm Left, Right;
            public int Gap;
        }
        private static readonly List<SideBySidePair> SideBySidePairs = new List<SideBySidePair>();

        /// <summary>The next native move of <paramref name="window"/> is a tab drag: it may drop onto a window's side.</summary>
        internal static void ExpectTabDragMove(MainForm window)
        {
            _draggedWindow = window;
        }

        /// <summary>The native move that <see cref="ExpectTabDragMove"/> announced has returned (or never began).</summary>
        internal static void TabDragMoveReturned(MainForm window)
        {
            if (_draggedWindow == window)
            {
                _draggedWindow = null;
                _dropTarget = null;
                HideDropPreview();
            }
        }

        // ── Called from MainForm.WndProc ──────────────────────────────────────────

        internal static void OnWindowMoving(MainForm window)
        {
            // Any move of a window that was placed side by side takes it out of its pair.
            SideBySidePairs.RemoveAll(p => p.Left == window || p.Right == window);
            if (window != _draggedWindow) return;

            Point cursor = Cursor.Position;
            MainForm target = WindowUnderCursorBelow(window, cursor);
            bool onLeft = false;
            if (target != null)
            {
                Rectangle visible = VisibleBounds(target);
                double share = (cursor.X - visible.Left) / (double)Math.Max(1, visible.Width);
                if (share <= DropZoneShare) onLeft = true;
                else if (share < 1 - DropZoneShare) target = null;
            }

            if (target == null)
            {
                HideDropPreview();
                _dropTarget = null;
                return;
            }

            _dropTarget = target;
            _dropOnLeft = onLeft;
            Rectangle area = SplitArea(target);
            Rectangle half = onLeft
                ? new Rectangle(area.Left, area.Top, area.Width / 2, area.Height)
                : new Rectangle(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height);
            ShowDropPreview(half);
        }

        internal static void OnWindowMoveEnded(MainForm window)
        {
            if (window != _draggedWindow) return;
            MainForm target = _dropTarget;
            bool onLeft = _dropOnLeft;
            _draggedWindow = null;
            _dropTarget = null;
            HideDropPreview();

            if (target == null || target.IsDisposed || window.IsDisposed) return;
            // Windows snapped it for real (screen edge, layout bar): that wins.
            if (IsArranged(window.Handle)) return;
            // After the move loop has fully unwound: resizing inside it fights the loop.
            window.BeginInvoke(new Action(() => PlaceSideBySide(window, target, onLeft)));
        }

        /// <summary>A window is being resized by an edge: its side-by-side partner follows the shared edge.</summary>
        internal static void OnWindowSizing(MainForm window, int edge, ref RECT proposed)
        {
            foreach (SideBySidePair pair in SideBySidePairs)
            {
                bool isLeft = pair.Left == window, isRight = pair.Right == window;
                if (!isLeft && !isRight) continue;
                MainForm other = isLeft ? pair.Right : pair.Left;
                if (other.IsDisposed || other.WindowState != FormWindowState.Normal) return;

                bool sharedEdgeMoves = isLeft
                    ? edge == WMSZ_RIGHT || edge == WMSZ_TOPRIGHT || edge == WMSZ_BOTTOMRIGHT
                    : edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT;
                if (!sharedEdgeMoves) return;

                Rectangle o = other.Bounds;
                if (isLeft)
                {
                    // The shared edge may not push the partner below its minimum width.
                    proposed.Right = Math.Min(proposed.Right, o.Right - MinSharedWidth - pair.Gap);
                    proposed.Right = Math.Max(proposed.Right, proposed.Left + MinSharedWidth);
                    int newLeft = proposed.Right + pair.Gap;
                    other.Bounds = Rectangle.FromLTRB(newLeft, o.Top, o.Right, o.Bottom);
                }
                else
                {
                    proposed.Left = Math.Max(proposed.Left, o.Left + MinSharedWidth - pair.Gap);
                    proposed.Left = Math.Min(proposed.Left, proposed.Right - MinSharedWidth);
                    int newRight = proposed.Left - pair.Gap;
                    other.Bounds = Rectangle.FromLTRB(o.Left, o.Top, newRight, o.Bottom);
                }
                return;
            }
        }

        internal static void ForgetSideBySide(MainForm window)
        {
            SideBySidePairs.RemoveAll(p => p.Left == window || p.Right == window);
            if (_draggedWindow == window) { _draggedWindow = null; _dropTarget = null; HideDropPreview(); }
            if (_dropTarget == window) { _dropTarget = null; HideDropPreview(); }
        }

        // ── Placing ───────────────────────────────────────────────────────────────

        // The area the two windows share: the target's place, or the whole working area of
        // its screen when it was maximized (the same two halves Snap would give).
        private static Rectangle SplitArea(MainForm target)
        {
            return target.WindowState == FormWindowState.Maximized
                ? Screen.FromHandle(target.Handle).WorkingArea
                : VisibleBounds(target);
        }

        private static void PlaceSideBySide(MainForm dropped, MainForm target, bool droppedOnLeft)
        {
            if (dropped.IsDisposed || target.IsDisposed) return;
            Rectangle area = SplitArea(target);
            if (target.WindowState != FormWindowState.Normal) target.WindowState = FormWindowState.Normal;
            if (dropped.WindowState != FormWindowState.Normal) dropped.WindowState = FormWindowState.Normal;

            var leftHalf = new Rectangle(area.Left, area.Top, area.Width / 2, area.Height);
            var rightHalf = new Rectangle(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height);
            MainForm left = droppedOnLeft ? dropped : target, right = droppedOnLeft ? target : dropped;

            SideBySidePairs.RemoveAll(p => p.Left == left || p.Right == left || p.Left == right || p.Right == right);
            SetVisibleBounds(left, leftHalf);
            SetVisibleBounds(right, rightHalf);
            SideBySidePairs.Add(new SideBySidePair { Left = left, Right = right, Gap = right.Bounds.Left - left.Bounds.Right });
            dropped.Activate();
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

        // The first of our windows under the cursor that lies below the dragged one.
        private static MainForm WindowUnderCursorBelow(MainForm dragged, Point cursor)
        {
            for (IntPtr h = GetWindow(dragged.Handle, GW_HWNDNEXT); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (!IsWindowVisible(h)) continue;
                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                if (cursor.X < r.Left || cursor.X >= r.Right || cursor.Y < r.Top || cursor.Y >= r.Bottom) continue;
                foreach (MainForm open in OpenWindows)
                    if (open != dragged && !open.IsDisposed && open.IsHandleCreated && open.Handle == h
                        && open.WindowState != FormWindowState.Minimized && !ClosingWindows.Contains(open))
                        return open;
                // Some other window covers this point: nothing of ours to drop on.
                return null;
            }
            return null;
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

        internal const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
        private const uint GW_HWNDNEXT = 2;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        // Windows 10 1903+: whether Windows holds the window snapped. Absent on older
        // systems, where a window is simply never reported as snapped.
        [DllImport("user32.dll", EntryPoint = "IsWindowArranged")]
        private static extern bool IsWindowArrangedNative(IntPtr hwnd);

        private static bool IsArranged(IntPtr hwnd)
        {
            try { return IsWindowArrangedNative(hwnd); }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
}
