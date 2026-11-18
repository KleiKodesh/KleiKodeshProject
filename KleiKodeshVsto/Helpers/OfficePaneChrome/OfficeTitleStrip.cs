using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// Office's title strip of one custom task pane (the MsoCommandBar it draws the title, options arrow and X
    /// into), subclassed from inside the add-in. The pane's Office title must be blank (" "): in its place this
    /// draws a pop-out button one step past Office's arrow and a selector (icon + title + chevron) at the far
    /// side, hugging its content. Office is clipped away from both (ValidateRect) so they never flicker.
    ///
    /// It also tells whether a hide happened while Office handled a click that went down on its X.
    /// Office destroys this window on hide and builds a new one on show: call <see cref="Attach"/> after every show.
    /// Anything unexpected (window chain, not exactly Office's arrow + X, any exception) unhooks it: the pane is
    /// stock until its next show (<see cref="Reattach"/>), which tries again.
    /// </summary>
    public sealed class OfficeTitleStrip : IDisposable
    {
        public event Action SelectorClicked, PopOutClicked;
        /// <summary>What the selector shows.</summary>
        public Func<(string Title, Image Icon)> Current = () => (null, null);

        public bool InOfficeClose => _officeClickDepth > 0 && _pressedOnClose;
        public bool RightToLeft { get; private set; }
        public Color Background { get; private set; } = Color.FromArgb(230, 230, 230);
        public Color Ink { get; private set; } = Color.FromArgb(38, 38, 38);
        public float Scale { get; private set; } = 1f;
        public IntPtr OwnerWindow => _bar == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(_bar, Native.GA_ROOT);

        readonly Control _content;
        readonly Native.SubclassProc _proc;
        IntPtr _bar, _workPane;
        Timer _retry;
        Rectangle _popOut = Rectangle.Empty, _selector = Rectangle.Empty, _closeBox = Rectangle.Empty;
        bool _failed, _disposed, _ourMouseDown, _pressedOnClose;
        int _hover, _officeClickDepth;                         // hover: 0 none, 1 pop-out, 2 selector
        string _lastLayout;
        static readonly int[] SampleRows = { 35, 45, 50, 55, 65 };   // % of strip height through Office's glyphs

        public OfficeTitleStrip(Control paneContent) { _content = paneContent; _proc = WndProc; Attach(); }

        /// <summary>Redraw after the selector's content changed (it may shrink: Office repaints what it no longer covers).</summary>
        public void Repaint()
        {
            if (_bar == IntPtr.Zero) return;
            // Draw at once: waiting for Office's next WM_PAINT shows a stale or empty title while a heavy view
            // keeps the UI thread busy. What the old selector covered beyond the new one is cleared here too.
            var old = _selector;
            try { Draw(relayout: true, clear: old); } catch (Exception ex) { Fail(ex.Message); }
        }

        /// <summary>A draw after Office already handled the message: a failure unhooks, it never reaches Office.</summary>
        void DrawSafe(bool relayout) { try { Draw(relayout); } catch (Exception ex) { Fail(ex.Message); } }

        public Rectangle SelectorScreenRect()
        {
            var a = new Native.POINT { X = _selector.Left, Y = _selector.Top };
            var b = new Native.POINT { X = _selector.Right, Y = _selector.Bottom };
            Native.ClientToScreen(_bar, ref a); Native.ClientToScreen(_bar, ref b);
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), a.Y, Math.Max(a.X, b.X), b.Y);
        }

        /// <summary>Hooks the pane's current frame; Office builds it after Visible = true returns, so retry briefly.</summary>
        public void Attach()
        {
            if (_failed || _disposed || _bar != IntPtr.Zero) return;
            _retry?.Dispose(); _retry = null;
            if (TryHook()) return;
            int tries = 0;
            _retry = new Timer { Interval = 50 };
            _retry.Tick += (s, e) => { if (TryHook() || ++tries > 60) { _retry?.Dispose(); _retry = null; } };
            _retry.Start();
        }

        /// <summary>After the pane is shown again: a strip that failed before gets one new try.</summary>
        public void Reattach() { _failed = false; Attach(); }

        bool TryHook()
        {
            try
            {
                if (_content.IsDisposed || !_content.IsHandleCreated) return false;
                IntPtr bar = IntPtr.Zero, work = IntPtr.Zero;
                for (var h = Native.GetParent(_content.Handle); h != IntPtr.Zero; h = Native.GetParent(h))
                {
                    var cls = Native.ClassName(h);
                    if (cls == "MsoWorkPane" && work == IntPtr.Zero) work = h;
                    if (cls == "MsoCommandBar") { bar = h; break; }
                    if (cls == "OpusApp") return false;                 // frame not built yet
                }
                if (bar == IntPtr.Zero || work == IntPtr.Zero) return false;
                _bar = bar; _workPane = work;
                var s = StripRect();
                Scale = Native.GetDpiForWindow(_bar) / 96f;
                // Not laid out yet (or a very narrow pane): not a failure, look again shortly.
                if (s.Height < 16 * Scale || s.Width < 120 * Scale) { _bar = _workPane = IntPtr.Zero; return false; }
                if (s.Height > 90 * Scale) { _bar = _workPane = IntPtr.Zero; Fail("unexpected strip " + s); return true; }
                if (!Native.SetWindowSubclass(_bar, _proc, (UIntPtr)1, IntPtr.Zero)) { _bar = _workPane = IntPtr.Zero; Fail("subclass refused"); return true; }
                Native.InvalidateRect(_bar, IntPtr.Zero, false);
                return true;
            }
            catch (Exception ex) { Fail(ex.Message); return true; }
        }

        int HitTest(Point p) => _popOut.Contains(p) ? 1 : _selector.Contains(p) ? 2 : 0;

        IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
        {
            if (_failed) return Native.DefSubclassProc(hWnd, msg, wParam, lParam);
            try
            {
                switch (msg)
                {
                    case Native.WM_NCDESTROY:
                        Native.RemoveWindowSubclass(hWnd, _proc, id);
                        _bar = _workPane = IntPtr.Zero; _popOut = _selector = _closeBox = Rectangle.Empty; _hover = 0; _lastLayout = null; _layoutKey = null;
                        // Office rebuilds this window on hide/show and also on its own (e.g. when the pane's width
                        // changes): hook the new one once it exists. A hidden pane just retries briefly and stops.
                        if (!_disposed && !_content.IsDisposed && _content.IsHandleCreated)
                            _content.BeginInvoke((Action)Attach);
                        return Native.DefSubclassProc(hWnd, msg, wParam, lParam);
                    case Native.WM_NCHITTEST:
                        if (HitTest(ScreenToStrip(lParam)) != 0) return (IntPtr)Native.HTCLIENT;
                        break;
                    case Native.WM_SETCURSOR:
                        if (HitTest(CursorInStrip()) != 0) { Native.SetCursor(Native.LoadCursor(IntPtr.Zero, (IntPtr)32512)); return (IntPtr)1; }
                        break;
                    case Native.WM_MOUSEMOVE: SetHover(HitTest(Native.PointFromLParam(lParam))); break;
                    case Native.WM_MOUSELEAVE: _ourMouseDown = false; SetHover(0); break;
                    case Native.WM_CAPTURECHANGED: _ourMouseDown = false; break;
                    case Native.WM_LBUTTONDBLCLK:
                        if (HitTest(Native.PointFromLParam(lParam)) != 0) { _ourMouseDown = true; return IntPtr.Zero; }
                        break;
                    case Native.WM_LBUTTONDOWN:
                        int hit = HitTest(Native.PointFromLParam(lParam));
                        _ourMouseDown = hit != 0;                     // never left over from a click whose release went elsewhere
                        if (hit != 0)
                        {
                            // The menu / pop-out takes the mouse, so no WM_MOUSELEAVE comes: drop the hover now.
                            _hover = 0;
                            Draw(relayout: false);
                            // Run after this message returns: the action may hide the pane, and Office destroys this
                            // very window on hide - doing that inside its own click handling leaves the mouse stuck.
                            var action = hit == 1 ? PopOutClicked : SelectorClicked;
                            _content.BeginInvoke((Action)(() =>
                            {
                                if (_disposed || _bar == IntPtr.Zero) return;     // the frame went away meanwhile
                                try { action?.Invoke(); } catch (Exception ex) { SidePanes.Log("strip click: " + ex.Message); }
                            }));
                            return IntPtr.Zero;                       // Office never sees it
                        }
                        break;
                    case Native.WM_LBUTTONUP:
                        if (_ourMouseDown) { _ourMouseDown = false; return IntPtr.Zero; }
                        break;
                    case Native.WM_PAINT:
                        // The strip changed size: our areas are stale. Let Office paint all of it, then lay out afresh.
                        if (StripRect().Size != _laidOutFor && !(_popOut.IsEmpty && _selector.IsEmpty))
                        {
                            _popOut = _selector = Rectangle.Empty; _layoutKey = null;
                            Native.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }
                        // Clip Office away from our areas (it would paint over them), then draw them.
                        foreach (var r in new[] { _popOut, _selector })
                            if (!r.IsEmpty) { var rc = new Native.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom }; Native.ValidateRect(hWnd, ref rc); }
                        var painted = Native.DefSubclassProc(hWnd, msg, wParam, lParam);
                        DrawSafe(relayout: true);
                        return painted;
                }

                bool officeClick = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP || msg == Native.WM_NCLBUTTONDOWN || msg == Native.WM_NCLBUTTONUP;
                // Only Office's X counts - not a title drag / undock or its options menu.
                if (msg == Native.WM_LBUTTONDOWN) _pressedOnClose = _closeBox.Contains(Native.PointFromLParam(lParam));
                if (msg == Native.WM_NCLBUTTONDOWN) _pressedOnClose = _closeBox.Contains(ScreenToStrip(lParam));
                if (officeClick) _officeClickDepth++;
                try { return Native.DefSubclassProc(hWnd, msg, wParam, lParam); }
                finally
                {
                    if (officeClick) _officeClickDepth--;
                    if ((msg == Native.WM_LBUTTONUP || msg == Native.WM_NCLBUTTONUP) && _officeClickDepth == 0) _pressedOnClose = false;
                    // Office repaints the strip outside WM_PAINT too - hover states, and its active/inactive look when focus
                    // moves into or out of the pane (e.g. a page finishing loading). Put our parts back (cheap: no rescan).
                    if (msg == Native.WM_MOUSEMOVE || msg == Native.WM_MOUSELEAVE || msg == Native.WM_TIMER || msg == Native.WM_LBUTTONUP ||
                        msg == 0x0086 /*WM_NCACTIVATE*/ || msg == 0x0006 /*WM_ACTIVATE*/ || msg == 0x0007 /*WM_SETFOCUS*/ || msg == 0x0008 /*WM_KILLFOCUS*/ ||
                        msg == 0x0047 /*WM_WINDOWPOSCHANGED*/ || msg == 0x0005 /*WM_SIZE*/ || msg == 0x000B /*WM_SETREDRAW*/)
                        DrawSafe(relayout: false);
                }
            }
            catch (Exception ex) { Fail(ex.Message); return Native.DefSubclassProc(hWnd, msg, wParam, lParam); }
        }

        Rectangle StripRect()
        {
            Native.GetClientRect(_bar, out var client);
            Native.GetWindowRect(_workPane, out var work);
            var top = new Native.POINT { X = work.Left, Y = work.Top };
            Native.ScreenToClient(_bar, ref top);
            return new Rectangle(0, 0, client.Right, Math.Max(0, top.Y));
        }

        /// <summary>
        /// Reads what Office painted (one memory copy): with the title blank there are exactly two ink runs, its X
        /// and its options arrow. Pop-out: Office's button size, one step past the arrow. Selector: hugging its
        /// content at the far side. Anything else = nothing of ours.
        /// </summary>
        string _layoutKey;
        Size _laidOutFor;

        void Layout(IntPtr screenDc, Rectangle s)
        {
            _laidOutFor = s.Size;
            var shown = Current();
            string key = s.Width + "x" + s.Height + "@" + Native.GetDpiForWindow(_bar) + "|" + shown.Title + "|" + (shown.Icon != null);
            if (key == _layoutKey && !_closeBox.IsEmpty &&
                ColorTranslator.FromWin32((int)Native.GetPixel(screenDc, 2, s.Height / 2)).ToArgb() == Background.ToArgb())
                return;                                                    // same strip, same theme: layout still valid
            _layoutKey = key;
            // One copy of the strip, read as a pixel array: GetPixel per pixel cost 24-76 ms per repaint.
            int[] pixels; int stride;
            using (var copy = new Bitmap(s.Width, s.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(copy))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        Native.SetLayout(hdc, Native.GetLayout(screenDc));     // read in the strip's own (logical) coordinates
                        Native.BitBlt(hdc, 0, 0, s.Width, s.Height, screenDc, 0, 0, Native.SRCCOPY);
                    }
                    finally { g.ReleaseHdc(hdc); }
                }
                var data = copy.LockBits(new Rectangle(0, 0, s.Width, s.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    stride = data.Stride / 4;
                    pixels = new int[stride * s.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally { copy.UnlockBits(data); }
            }
            Color PixelAt(int x, int y) { int v = pixels[y * stride + x]; return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF); }

            {
                Scale = Native.GetDpiForWindow(_bar) / 96f;
                Background = PixelAt(2, s.Height / 2);
                var inked = new bool[s.Width];
                int strongest = 0;
                for (int x = 0; x < s.Width; x++)
                {
                    if ((x >= _popOut.Left && x < _popOut.Right) || (x >= _selector.Left && x < _selector.Right)) continue;   // ours
                    foreach (int pct in SampleRows)
                    {
                        var px = PixelAt(x, s.Height * pct / 100);
                        int d = Math.Max(Math.Abs(px.R - Background.R), Math.Max(Math.Abs(px.G - Background.G), Math.Abs(px.B - Background.B)));
                        if (d > 60) { inked[x] = true; if (d > strongest) { strongest = d; Ink = px; } }
                    }
                }
                var runs = new List<int[]>();
                int merge = (int)Math.Round(9 * Scale);
                for (int x = 0; x < s.Width; x++)
                    if (inked[x])
                    {
                        if (runs.Count > 0 && x - runs[runs.Count - 1][1] <= merge) runs[runs.Count - 1][1] = x;
                        else runs.Add(new[] { x, x });
                    }

                Rectangle popOut = Rectangle.Empty, selector = Rectangle.Empty, closeBox = Rectangle.Empty;
                if (runs.Count == 2)
                {
                    bool closeAtLeft = runs[0][0] < s.Width - runs[1][1];      // X sits at the strip's edge
                    var close = closeAtLeft ? runs[0] : runs[1];
                    var arrow = closeAtLeft ? runs[1] : runs[0];
                    int ccx = (close[0] + close[1]) / 2, acx = (arrow[0] + arrow[1]) / 2;
                    int box = (int)Math.Round(32 * Scale), step = acx - ccx;
                    if (Math.Abs(step) >= box * 0.8 && Math.Abs(step) <= box * 3)
                    {
                        closeBox = new Rectangle(ccx - box / 2, s.Height - box, box, box);
                        popOut = new Rectangle(2 * acx - ccx - box / 2, s.Height - box, box, box);
                        RightToLeft = step > 0;                              // X at the left: RTL UI, title reads from the right
                        int margin = (int)Math.Round(6 * Scale);
                        var cur = Current();
                        int want = Fluent.SelectorWidth(cur.Title, cur.Icon, Scale);
                        selector = step > 0
                            ? Rectangle.FromLTRB(Math.Max(popOut.Right + margin / 2, s.Width - margin - want), s.Height - box, s.Width - margin, s.Height)
                            : Rectangle.FromLTRB(margin, s.Height - box, Math.Min(popOut.Left - margin / 2, margin + want), s.Height);
                        if (selector.Width < box) selector = Rectangle.Empty;
                    }
                }
                _popOut = popOut; _selector = selector; _closeBox = closeBox;
                var result = "strip layout runs=" + string.Join(" ", runs.Select(r => r[0] + "-" + r[1])) + " popout=" + _popOut + " selector=" + _selector;
                if (result != _lastLayout) { _lastLayout = result; SidePanes.Log(result); }
            }
        }

        void Draw(bool relayout, Rectangle clear = default(Rectangle))
        {
            if (_bar == IntPtr.Zero) return;
            var s = StripRect();
            if (s.Height < 12 || s.Width < 40)                    // nothing to draw into (and nothing to hit)
            {
                _popOut = _selector = _closeBox = Rectangle.Empty; _layoutKey = null;
                return;
            }
            var screenDc = Native.GetDC(_bar);
            try
            {
                if (relayout || _selector.IsEmpty) Layout(screenDc, s);
                // Parts of the previous selector the new one doesn't cover go back to the strip's background.
                if (!clear.IsEmpty && !_selector.IsEmpty)
                {
                    if (clear.Left < _selector.Left) Compose(screenDc, Rectangle.FromLTRB(clear.Left, clear.Top, Math.Min(clear.Right, _selector.Left), clear.Bottom), false, g => { });
                    if (clear.Right > _selector.Right) Compose(screenDc, Rectangle.FromLTRB(Math.Max(clear.Left, _selector.Right), clear.Top, clear.Right, clear.Bottom), false, g => { });
                }
                else if (!clear.IsEmpty) Compose(screenDc, clear, false, g => { });
                int glyphLine = (int)Math.Round(s.Height / 2f + 3);              // Office's glyph centre line
                if (!_popOut.IsEmpty)
                    Compose(screenDc, _popOut, _hover == 1, g =>
                        Fluent.Glyph(g, Fluent.PopOut, new Rectangle(0, glyphLine - _popOut.Top - _popOut.Width / 2, _popOut.Width, _popOut.Width), Ink, (float)Math.Round(13 * Scale)));
                if (!_selector.IsEmpty)
                {
                    var cur = Current();
                    Compose(screenDc, _selector, _hover == 2, g =>
                        Fluent.PaintSelector(g, new Rectangle(0, 0, _selector.Width, _selector.Height), cur.Title, cur.Icon, Ink, RightToLeft, Scale, glyphLine - _selector.Top));
                }
            }
            finally { Native.ReleaseDC(_bar, screenDc); }
        }

        /// <summary>Off-screen, one blit: no flicker.</summary>
        void Compose(IntPtr screenDc, Rectangle area, bool hover, Action<Graphics> paint)
        {
            var dc = Native.CreateCompatibleDC(screenDc);
            var bmp = Native.CreateCompatibleBitmap(screenDc, area.Width, area.Height);
            var old = Native.SelectObject(dc, bmp);
            try
            {
                using (var g = Graphics.FromHdc(dc))
                {
                    using (var back = new SolidBrush(hover ? Fluent.Hover(Background, Ink) : Background)) g.FillRectangle(back, 0, 0, area.Width, area.Height);
                    paint(g);
                }
                Native.BitBlt(screenDc, area.Left, area.Top, area.Width, area.Height, dc, 0, 0, Native.SRCCOPY);
            }
            finally { Native.SelectObject(dc, old); Native.DeleteObject(bmp); Native.DeleteDC(dc); }
        }

        void SetHover(int which) { if (which != _hover) { _hover = which; Draw(relayout: false); } }

        void Fail(string why)
        {
            if (_failed) return;
            _failed = true;
            SidePanes.Log("title strip hook disabled, stock pane: " + why);
            Unhook();
        }

        void Unhook()
        {
            _retry?.Dispose(); _retry = null;
            if (_bar == IntPtr.Zero) return;
            Native.RemoveWindowSubclass(_bar, _proc, (UIntPtr)1);
            var bar = _bar;
            _bar = _workPane = IntPtr.Zero; _popOut = _selector = _closeBox = Rectangle.Empty;
            Native.InvalidateRect(bar, IntPtr.Zero, true);
        }

        public void Dispose() { if (!_disposed) { _disposed = true; Unhook(); } }

        Point ScreenToStrip(IntPtr l) { var p0 = Native.PointFromLParam(l); var p = new Native.POINT { X = p0.X, Y = p0.Y }; Native.ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
        Point CursorInStrip() { Native.GetCursorPos(out var p); Native.ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
    }
}
