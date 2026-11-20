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
    /// draws a pop-out button one step past Office's arrow, a hide button one step further, and a selector (icon + title + chevron) at the far
    /// side, hugging its content. Office is clipped away from both (ValidateRect) so they never flicker.
    ///
    /// It also tells whether a hide happened while Office handled a click that went down on its X.
    /// Office destroys this window on hide and builds a new one on show: call <see cref="Attach"/> after every show.
    /// Anything unexpected (window chain, not exactly Office's arrow + X, any exception) unhooks it: the pane is
    /// stock until its next show (<see cref="Reattach"/>), which tries again.
    /// </summary>
    public sealed class OfficeTitleStrip : IDisposable
    {
        public event Action SelectorClicked, PopOutClicked, HideClicked;
        /// <summary>What the selector shows.</summary>
        public Func<(string Title, Image Icon)> Current = () => (null, null);
        /// <summary>Tooltip of a part: 1 pop-out, 2 selector, 3 hide; null = none.</summary>
        public Func<int, string> TipFor = _ => null;
        ChromeToolTip _tip;

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
        Rectangle _popOut = Rectangle.Empty, _hide = Rectangle.Empty, _selector = Rectangle.Empty, _closeBox = Rectangle.Empty;
        bool _failed, _disposed, _pressedOnClose, _armed, _menuOpen;
        int _hover, _pressed, _officeClickDepth;               // parts: 0 none, 1 pop-out, 2 selector, 3 hide
        int _menuClosedAt = Environment.TickCount - 100000;
        string _lastLayout;
        static readonly int[] SampleRows = { 35, 45, 50, 55, 65 };   // % of strip height through Office's glyphs

        public OfficeTitleStrip(Control paneContent) { _content = paneContent; _proc = WndProc; _frameProc = FrameProc; Attach(); }

        // ---- Word's frame: Office repaints the strip while handling the frame's messages ----
        // Measured: when Word is activated / deactivated, Office paints the pane titles' active / inactive look while
        // its main window (OpusApp) handles WM_NCACTIVATE / WM_ACTIVATE / WM_ACTIVATEAPP - the strip window itself
        // gets no message at all. A click anywhere in Word reaches the frame as WM_PARENTNOTIFY, and the focus change
        // it starts repaints the titles the same way. So the frame is subclassed too, and our parts are put back
        // right after Office handled those messages.
        readonly Native.SubclassProc _frameProc;
        readonly List<IntPtr> _frames = new List<IntPtr>();
        static int _nextFrameId = 100;
        readonly UIntPtr _frameId = (UIntPtr)System.Threading.Interlocked.Increment(ref _nextFrameId);   // one per strip on a shared frame

        /// <summary>
        /// The strip's top-level window, and Word's main window when that is another one: a floating (undocked) pane is
        /// its own top-level window, owned by Word's, and Office repaints its title on Word's activation too.
        /// </summary>
        void HookFrame()
        {
            var root = Native.GetAncestor(_bar, Native.GA_ROOT);
            var wanted = new List<IntPtr>();
            if (root != IntPtr.Zero) wanted.Add(root);
            var owner = root == IntPtr.Zero ? IntPtr.Zero : Native.GetWindow(root, 4 /*GW_OWNER*/);
            if (owner != IntPtr.Zero && !wanted.Contains(owner)) wanted.Add(owner);
            foreach (var f in _frames.Where(f => !wanted.Contains(f)).ToList()) { Native.RemoveWindowSubclass(f, _frameProc, _frameId); _frames.Remove(f); }
            foreach (var f in wanted)
                if (!_frames.Contains(f) && Native.SetWindowSubclass(f, _frameProc, _frameId, IntPtr.Zero)) _frames.Add(f);
        }

        void UnhookFrame()
        {
            foreach (var f in _frames) Native.RemoveWindowSubclass(f, _frameProc, _frameId);
            _frames.Clear();
        }

        IntPtr FrameProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
        {
            if (msg == Native.WM_NCDESTROY) { Native.RemoveWindowSubclass(hWnd, _frameProc, id); _frames.Remove(hWnd); return Native.DefSubclassProc(hWnd, msg, wParam, lParam); }
            var result = Native.DefSubclassProc(hWnd, msg, wParam, lParam);
            if (_failed || _disposed || _bar == IntPtr.Zero) return result;
            try { AfterFrameMessage(msg, wParam); } catch (Exception ex) { Fail(ex.Message); }   // never into Word's own window proc
            return result;
        }

        void AfterFrameMessage(uint msg, IntPtr wParam)
        {
            if (msg == 0x0086 /*WM_NCACTIVATE*/ || msg == 0x0006 /*WM_ACTIVATE*/ || msg == 0x001C /*WM_ACTIVATEAPP*/)
            {
                _tip?.Reset();
                DrawSafe(relayout: true);                               // Office just painted the new look: put ours back
            }
            else if (msg == 0x0210 /*WM_PARENTNOTIFY*/)
            {
                int what = (int)(wParam.ToInt64() & 0xFFFF);
                if ((what == 0x0201 || what == 0x0204 || what == 0x0207) && _content.IsHandleCreated && !_content.IsDisposed)
                    // The click's focus change repaints the titles after this returns: draw once the click is handled.
                    _content.BeginInvoke((Action)(() => { if (!_failed && !_disposed && _bar != IntPtr.Zero) DrawSafe(relayout: true); }));
            }
        }

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
                HookFrame();
                return true;
            }
            catch (Exception ex) { Fail(ex.Message); return true; }
        }

        /// <summary>
        /// Runs a click's action after this message returns: it may hide the pane, and Office destroys this very window
        /// on hide - doing that inside its own click handling leaves the mouse stuck.
        /// </summary>
        void Run(Action action)
        {
            _content.BeginInvoke((Action)(() =>
            {
                if (_disposed || _bar == IntPtr.Zero) return;     // the frame went away meanwhile
                try { action?.Invoke(); } catch (Exception ex) { SidePanes.Log("strip click: " + ex.Message); }
            }));
        }

        /// <summary>The selector shows its open (pressed) look while its menu is open; a press then closes the menu.</summary>
        public void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            if (!open) _menuClosedAt = Environment.TickCount;
            _hover = 0;
            DrawSafe(relayout: false);
        }

        void TrackLeave()
        {
            var t = new Native.TRACKMOUSEEVENT { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT)), dwFlags = 0x02 /*TME_LEAVE*/, hwndTrack = _bar };
            Native.TrackMouseEvent(ref t);
        }

        int HitTest(Point p) => _popOut.Contains(p) ? 1 : _selector.Contains(p) ? 2 : _hide.Contains(p) ? 3 : 0;

        IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
        {
            if (_failed) return Native.DefSubclassProc(hWnd, msg, wParam, lParam);
            try
            {
                switch (msg)
                {
                    case Native.WM_NCDESTROY:
                        Native.RemoveWindowSubclass(hWnd, _proc, id);
                        _tip?.Reset();
                        _bar = _workPane = IntPtr.Zero; _popOut = _hide = _selector = _closeBox = Rectangle.Empty; _hover = _pressed = 0; _armed = false; _lastLayout = null; _layoutKey = null;
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
                    case Native.WM_MOUSEMOVE:
                    {
                        int over = HitTest(Native.PointFromLParam(lParam));
                        if (Native.FromTouchOrPen()) { if (_pressed == 0) SetHover(0); else { bool on = over == _pressed; if (on != _armed) { _armed = on; DrawSafe(relayout: false); } } break; }
                        if (_pressed != 0) { bool armed = over == _pressed; if (armed != _armed) { _armed = armed; DrawSafe(relayout: false); } break; }
                        if (over != 0 && _hover == 0) TrackLeave();       // our own leave notice: Office may not ask for one
                        SetHover(over);
                        break;
                    }
                    case Native.WM_MOUSELEAVE:
                        if (_pressed == 0) SetHover(0);
                        break;
                    case Native.WM_CAPTURECHANGED:                        // a press lost to another window: cancel it
                        if (_pressed != 0) { _pressed = 0; _armed = false; _hover = 0; DrawSafe(relayout: false); }
                        break;
                    case 0x0008 /*WM_KILLFOCUS*/:
                    case 0x001C /*WM_ACTIVATEAPP*/:
                        _tip?.Reset();
                        if (_pressed == 0) SetHover(0);
                        break;
                    case 0x0100 /*WM_KEYDOWN*/: case 0x0104 /*WM_SYSKEYDOWN*/: case 0x020A /*WM_MOUSEWHEEL*/: case 0x0204 /*WM_RBUTTONDOWN*/:
                        _tip?.Reset();                                    // as a native tooltip: any key, wheel or other button hides it
                        break;
                    case Native.WM_LBUTTONDOWN:
                    case Native.WM_LBUTTONDBLCLK:                         // a fast second click is a click too
                    {
                        int hit = HitTest(Native.PointFromLParam(lParam));
                        _tip?.Reset();
                        if (hit == 0) break;
                        if (hit == 2)
                        {
                            // A dropdown opens on press (as Office's). Pressed while its menu is open, or in the same
                            // click that just closed it: that click closes it, it doesn't reopen it.
                            if (!_menuOpen && unchecked(Environment.TickCount - _menuClosedAt) > 300) Run(SelectorClicked);
                            return IntPtr.Zero;
                        }
                        // Buttons act on release inside them, with a pressed look while held; dragging off cancels.
                        _pressed = hit; _armed = true;
                        Native.SetCapture(hWnd);
                        DrawSafe(relayout: false);
                        return IntPtr.Zero;                               // Office never sees it
                    }
                    case Native.WM_LBUTTONUP:
                        if (_pressed != 0)
                        {
                            int was = _pressed; bool fire = HitTest(Native.PointFromLParam(lParam)) == was;
                            _pressed = 0; _armed = false;
                            Native.ReleaseCapture();
                            SetHover(0);                                  // the action changes the pane; a move re-hovers
                            DrawSafe(relayout: false);
                            if (fire) Run(was == 1 ? PopOutClicked : HideClicked);
                            return IntPtr.Zero;
                        }
                        break;
                    case Native.WM_PAINT:
                        // The strip changed size: our areas are stale. Let Office paint all of it, then lay out afresh.
                        if (StripRect().Size != _laidOutFor && !(_popOut.IsEmpty && _selector.IsEmpty))
                        {
                            _popOut = _hide = _selector = Rectangle.Empty; _layoutKey = null;
                            Native.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }
                        // Clip Office away from our areas (it would paint over them), then draw them.
                        foreach (var r in new[] { _popOut, _hide, _selector })
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
        // Where Office's X and arrow were last seen, for this strip size and DPI. Office paints its inactive look
        // (pane not active: another window clicked) with fainter or no X and arrow, so a scan then finds nothing:
        // the last seen anchors stay valid until the strip itself changes size or DPI.
        int _anchorClose, _anchorArrow; string _anchorFor;

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
                    if ((x >= _popOut.Left && x < _popOut.Right) || (x >= _hide.Left && x < _hide.Right) || (x >= _selector.Left && x < _selector.Right)) continue;   // ours
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

                Rectangle popOut = Rectangle.Empty, hide = Rectangle.Empty, selector = Rectangle.Empty, closeBox = Rectangle.Empty;
                // Size, DPI and the strip's direction (mirrored DC = RTL): anchors from another direction would mirror our parts.
                string anchorFor = s.Width + "x" + s.Height + "@" + Native.GetDpiForWindow(_bar) + (Native.GetLayout(screenDc) != 0 ? "rtl" : "ltr");
                int ccx = 0, acx = 0; bool found = false;
                int box = (int)Math.Round(32 * Scale);
                if (runs.Count == 2)
                {
                    bool closeAtLeft = runs[0][0] < s.Width - runs[1][1];      // X sits at the strip's edge
                    var close = closeAtLeft ? runs[0] : runs[1];
                    var arrow = closeAtLeft ? runs[1] : runs[0];
                    ccx = (close[0] + close[1]) / 2; acx = (arrow[0] + arrow[1]) / 2;
                    int d = Math.Abs(acx - ccx);
                    found = d >= box * 0.8 && d <= box * 3;
                    if (found) { _anchorClose = ccx; _anchorArrow = acx; _anchorFor = anchorFor; }
                }
                if (!found && _anchorFor == anchorFor) { ccx = _anchorClose; acx = _anchorArrow; found = true; }   // inactive look
                if (!found) _layoutKey = null;                                 // nothing known: look again on the next paint
                {
                    int step = acx - ccx;
                    if (found)
                    {
                        closeBox = new Rectangle(ccx - box / 2, s.Height - box, box, box);
                        popOut = new Rectangle(2 * acx - ccx - box / 2, s.Height - box, box, box);
                        hide = new Rectangle(3 * acx - 2 * ccx - box / 2, s.Height - box, box, box);   // one more step: Office's X closes, this only hides
                        RightToLeft = step > 0;                              // X at the left: RTL UI, title reads from the right
                        int margin = (int)Math.Round(6 * Scale);
                        var cur = Current();
                        int want = Fluent.SelectorWidth(cur.Title, cur.Icon, Scale);
                        selector = step > 0
                            ? Rectangle.FromLTRB(Math.Max(hide.Right + margin / 2, s.Width - margin - want), s.Height - box, s.Width - margin, s.Height)
                            : Rectangle.FromLTRB(margin, s.Height - box, Math.Min(hide.Left - margin / 2, margin + want), s.Height);
                        if (selector.Width < box) selector = Rectangle.Empty;
                    }
                }
                _popOut = popOut; _hide = selector.IsEmpty ? Rectangle.Empty : hide; _selector = selector; _closeBox = closeBox;
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
                _popOut = _hide = _selector = _closeBox = Rectangle.Empty; _layoutKey = null;
                return;
            }
            var screenDc = Native.GetDC(_bar);
            try
            {
                if (relayout || _selector.IsEmpty) Layout(screenDc, s);
                // Parts of the previous selector the new one doesn't cover go back to the strip's background.
                if (!clear.IsEmpty && !_selector.IsEmpty)
                {
                    if (clear.Left < _selector.Left) Compose(screenDc, Rectangle.FromLTRB(clear.Left, clear.Top, Math.Min(clear.Right, _selector.Left), clear.Bottom), 0, g => { });
                    if (clear.Right > _selector.Right) Compose(screenDc, Rectangle.FromLTRB(Math.Max(clear.Left, _selector.Right), clear.Top, clear.Right, clear.Bottom), 0, g => { });
                }
                else if (!clear.IsEmpty) Compose(screenDc, clear, 0, g => { });
                int glyphLine = (int)Math.Round(s.Height / 2f + 3);              // Office's glyph centre line
                if (!_popOut.IsEmpty)
                    Compose(screenDc, _popOut, StateOf(1), g =>
                        Fluent.Glyph(g, Fluent.PopOut, new Rectangle(0, glyphLine - _popOut.Top - _popOut.Width / 2, _popOut.Width, _popOut.Width), Ink, (float)Math.Round(13 * Scale)));
                if (!_hide.IsEmpty)
                    Compose(screenDc, _hide, StateOf(3), g =>
                        Fluent.Glyph(g, Fluent.HidePane, new Rectangle(0, glyphLine - _hide.Top - _hide.Width / 2, _hide.Width, _hide.Width), Ink, (float)Math.Round(13 * Scale)));
                if (!_selector.IsEmpty)
                {
                    var cur = Current();
                    Compose(screenDc, _selector, StateOf(2), g =>
                        Fluent.PaintSelector(g, new Rectangle(0, 0, _selector.Width, _selector.Height), cur.Title, cur.Icon, Ink, RightToLeft, Scale, glyphLine - _selector.Top));
                }
            }
            finally { Native.ReleaseDC(_bar, screenDc); }
        }

        /// <summary>Off-screen, one blit: no flicker.</summary>
        /// <summary>0 normal, 1 hover, 2 pressed (held down on it, or the selector while its menu is open).</summary>
        int StateOf(int part) =>
            (part == 2 && _menuOpen) || (_pressed == part && _armed) ? 2 : _pressed == 0 && _hover == part ? 1 : 0;

        void Compose(IntPtr screenDc, Rectangle area, int state, Action<Graphics> paint)
        {
            var dc = Native.CreateCompatibleDC(screenDc);
            var bmp = Native.CreateCompatibleBitmap(screenDc, area.Width, area.Height);
            var old = Native.SelectObject(dc, bmp);
            try
            {
                using (var g = Graphics.FromHdc(dc))
                {
                    using (var back = new SolidBrush(state == 2 ? Fluent.Blend(Background, Ink, 0.18) : state == 1 ? Fluent.Hover(Background, Ink) : Background)) g.FillRectangle(back, 0, 0, area.Width, area.Height);
                    paint(g);
                }
                Native.BitBlt(screenDc, area.Left, area.Top, area.Width, area.Height, dc, 0, 0, Native.SRCCOPY);
            }
            finally { Native.SelectObject(dc, old); Native.DeleteObject(bmp); Native.DeleteDC(dc); }
        }

        void SetHover(int which)
        {
            if (which == _hover) return;
            _hover = which;
            Draw(relayout: false);
            ShowTip(which);
        }

        void ShowTip(int which)
        {
            string text = which == 0 || _bar == IntPtr.Zero || (which == 2 && _menuOpen) ? null : TipFor(which);
            if (text == null) { _tip?.Request(null, null, Rectangle.Empty, 0, Background, Ink, RightToLeft, Scale); return; }
            if (_tip == null || _tip.IsDisposed) _tip = new ChromeToolTip();
            var r = which == 1 ? _popOut : which == 3 ? _hide : _selector;
            var a = new Native.POINT { X = r.Left, Y = r.Top }; var b = new Native.POINT { X = r.Right, Y = r.Bottom };
            Native.ClientToScreen(_bar, ref a); Native.ClientToScreen(_bar, ref b);
            var screen = Rectangle.FromLTRB(Math.Min(a.X, b.X), a.Y, Math.Max(a.X, b.X), b.Y);
            _tip.Request(which, text, screen, screen.Bottom + (int)Math.Round(4 * Scale), Background, Ink, RightToLeft, Scale);
        }

        void Fail(string why)
        {
            if (_failed) return;
            _failed = true;
            SidePanes.Log("title strip hook disabled, stock pane: " + why);
            Unhook();
        }

        void Unhook()
        {
            _tip?.Reset();
            _retry?.Dispose(); _retry = null;
            UnhookFrame();
            if (_bar == IntPtr.Zero) return;
            Native.RemoveWindowSubclass(_bar, _proc, (UIntPtr)1);
            var bar = _bar;
            _bar = _workPane = IntPtr.Zero; _popOut = _hide = _selector = _closeBox = Rectangle.Empty;
            Native.InvalidateRect(bar, IntPtr.Zero, true);
        }

        public void Dispose() { if (!_disposed) { _disposed = true; Unhook(); _tip?.Dispose(); } }

        Point ScreenToStrip(IntPtr l) { var p0 = Native.PointFromLParam(l); var p = new Native.POINT { X = p0.X, Y = p0.Y }; Native.ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
        Point CursorInStrip() { Native.GetCursorPos(out var p); Native.ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
    }
}
