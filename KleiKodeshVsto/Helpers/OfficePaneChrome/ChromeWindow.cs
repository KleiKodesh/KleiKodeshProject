using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A window with its own title bar, ported from Kitvei Hakodesh's FluentChromeTabsForm (NonClient,
    /// Metrics, caption-button painting): a real resizable frame whose caption is reclaimed as client space
    /// (WM_NCCALCSIZE + DwmExtendFrameIntoClientArea), so Windows keeps resizing, shadow, snap and the
    /// Windows 11 snap-layouts flyout on maximise, while the title bar is drawn here.
    ///
    ///   - per-monitor DPI read from the window itself (Control.DeviceDpi can stay 96 in .NET Framework);
    ///   - caption buttons drawn as Fluent vector glyphs; close turns red on hover;
    ///   - extra title-bar buttons (<see cref="TitleButtons"/>) next to the caption buttons, as Fluent icon glyphs;
    ///   - a title content area (override <see cref="TitleContentWidth"/> / <see cref="PaintTitleContent"/> /
    ///     <see cref="OnTitleContentClick"/>), by default the window text;
    ///   - right-to-left: buttons at the left, content at the right;
    ///   - dark frame when <see cref="TitleBack"/> is dark; system menu on right-click; restore-from-minimise fix.
    /// Put your content in <see cref="Content"/>.
    /// </summary>
    public class ChromeWindow : Form
    {
        struct MINMAXINFO { public Native.POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

        public sealed class TitleButton { public char Glyph; public Action Click; public string Tip; }

        /// <summary>Tooltips of the caption buttons, by reading direction (RTL = Hebrew by default).</summary>
        public static Func<bool, string> MinimizeTip = rtl => rtl ? "\u05DE\u05D6\u05E2\u05E8" : "Minimize";
        public static Func<bool, string> MaximizeTip = rtl => rtl ? "\u05D4\u05D2\u05D3\u05DC" : "Maximize";
        public static Func<bool, string> RestoreTip = rtl => rtl ? "\u05E9\u05D7\u05D6\u05E8 \u05DC\u05DE\u05D8\u05D4" : "Restore Down";
        public static Func<bool, string> CloseTip = rtl => rtl ? "\u05E1\u05D2\u05D5\u05E8" : "Close";

        public readonly Panel Content = new Panel { Dock = DockStyle.Fill };
        public readonly List<TitleButton> TitleButtons = new List<TitleButton>();
        public Color TitleBack = Color.FromArgb(243, 243, 243);
        public Color TitleInk = Color.FromArgb(32, 32, 32);
        public bool TitleRightToLeft;

        /// <summary>
        /// Makes this window owned by <paramref name="owner"/>'s top-level window (stays above it, minimises with it),
        /// like the add-in's old pop-out: creates the handle if needed and sets the owner at once.
        /// Call it right before Show().
        /// </summary>
        public void SetOwner(IntPtr owner) =>
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, Native.GetAncestor(owner, Native.GA_ROOT));

        /// <summary>An owned window gets no taskbar button by default; WS_EX_APPWINDOW gives it one when ShowInTaskbar is on.</summary>
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; if (ShowInTaskbar) cp.ExStyle |= 0x00040000 /*WS_EX_APPWINDOW*/; return cp; }
        }

        const int HTCLIENT = Native.HTCLIENT, HTCAPTION = Native.HTCAPTION, HTMINBUTTON = Native.HTMINBUTTON,
            HTMAXBUTTON = Native.HTMAXBUTTON, HTTOP = Native.HTTOP, HTCLOSE = Native.HTCLOSE;
        const int ContentArea = 99, ExtraBase = 100;

        int _hot, _pressed;
        bool _windowActive;
        ChromeToolTip _tip;
        Rectangle? _boundsBeforeMinimize;
        bool _restoreFixPending;

        public ChromeWindow()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            DoubleBuffered = true;
            Controls.Add(Content);
        }

        // ---------------- metrics ----------------

        int _effectiveDpi;
        /// <summary>The window's real DPI (FluentChromeTabsForm.EffectiveDpi): DeviceDpi can stay 96 in .NET Framework.</summary>
        protected int EffectiveDpi
        {
            get
            {
                if (_effectiveDpi > 0) return _effectiveDpi;
                if (!IsHandleCreated) return DeviceDpi;
                try { _effectiveDpi = (int)Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { _effectiveDpi = DeviceDpi; }
                if (_effectiveDpi <= 0) _effectiveDpi = DeviceDpi;
                return _effectiveDpi;
            }
        }
        protected float DpiScale => EffectiveDpi / 96f;
        protected int Dpi(int logical) => (int)Math.Round(logical * EffectiveDpi / 96.0);
        /// <summary>No title bar in full screen (FormBorderStyle.None), like FluentChromeTabs hides its strip.</summary>
        protected int TitleHeightPx => FormBorderStyle == FormBorderStyle.None ? 0 : CaptionButtonHeightPx;   // exactly the buttons: no empty band under them
        int CaptionButtonWidthPx => Dpi(46);
        int CaptionButtonHeightPx => Dpi(32);
        /// <summary>The top resize border at this window's DPI (the plain metrics are the primary monitor's).</summary>
        int ResizeBorderHeight()
        {
            if (IsHandleCreated)
                try { return Native.GetSystemMetricsForDpi(33 /*SM_CYSIZEFRAME*/, (uint)EffectiveDpi) + Native.GetSystemMetricsForDpi(92 /*SM_CXPADDEDBORDER*/, (uint)EffectiveDpi); }
                catch (EntryPointNotFoundException) { /* before Windows 10 1607 */ }
            return Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92);
        }
        protected Color TitleHover => Fluent.Hover(TitleBack, TitleInk);
        /// <summary>Title ink, dimmed while the window is inactive (FluentChromeTabsForm.StripTextColor).</summary>
        protected Color TitleText => _windowActive ? TitleInk : Fluent.Blend(TitleInk, TitleBack, 0.4);

        /// <summary>Slot 1 = close (outermost), 2 = max, 3 = min, 4.. = extra buttons; at the reading end.</summary>
        Rectangle Slot(int slot)
        {
            int x = TitleRightToLeft ? (slot - 1) * CaptionButtonWidthPx : ClientSize.Width - slot * CaptionButtonWidthPx;
            return new Rectangle(x, 0, CaptionButtonWidthPx, CaptionButtonHeightPx);
        }
        Rectangle ButtonRect(int code) => Slot(code == HTCLOSE ? 1 : code == HTMAXBUTTON ? 2 : code == HTMINBUTTON ? 3 : 4 + (code - ExtraBase));
        int ClusterWidth => (3 + TitleButtons.Count) * CaptionButtonWidthPx;
        /// <summary>Free title space always left for dragging the window (empty title = HTCAPTION).</summary>
        int DragReservePx => Dpi(48);

        /// <summary>The title content area: hugs <see cref="TitleContentWidth"/>, at the reading start.</summary>
        protected Rectangle TitleContentRect()
        {
            // Same height and top as the caption buttons, so it reads as one of them.
            int pad = Dpi(8);
            int w = Math.Max(0, Math.Min(TitleContentWidth(), ClientSize.Width - ClusterWidth - DragReservePx - 2 * pad));
            return TitleRightToLeft ? new Rectangle(ClientSize.Width - pad - w, 0, w, CaptionButtonHeightPx)
                                    : new Rectangle(pad, 0, w, CaptionButtonHeightPx);
        }

        int HitArea(Point p)
        {
            if (p.Y >= TitleHeightPx) return 0;
            foreach (int code in new[] { HTCLOSE, HTMAXBUTTON, HTMINBUTTON }) if (ButtonRect(code).Contains(p)) return code;
            for (int i = 0; i < TitleButtons.Count; i++) if (ButtonRect(ExtraBase + i).Contains(p)) return ExtraBase + i;
            if (IsInteractiveContent && TitleContentRect().Contains(p)) return ContentArea;
            return 0;
        }

        protected void InvalidateTitle() { if (IsHandleCreated) Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleHeightPx)); }
        void UpdateMetrics() => Padding = new Padding(0, TitleHeightPx, 0, 0);

        // ---------------- title content (override) ----------------

        /// <summary>True if clicking the content area does something (hover + click handled); false = it drags the window.</summary>
        protected virtual bool IsInteractiveContent => false;
        protected virtual int TitleContentWidth()
        {
            using (var f = new Font("Segoe UI", 9f * DpiScale * 96f / 72f, GraphicsUnit.Pixel)) return TextRenderer.MeasureText(Text, f).Width + Dpi(16);
        }
        protected virtual void PaintTitleContent(Graphics g, Rectangle r)
        {
            using (var f = new Font("Segoe UI", 9f * DpiScale * 96f / 72f, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, Text, f, r, TitleText, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
        protected virtual void OnTitleContentClick(Rectangle screenRect) { }

        bool _contentOpen;
        int _contentClosedAt = Environment.TickCount - 100000;
        /// <summary>The content shows its open (pressed) look while its menu is open; a press then closes the menu, as a dropdown.</summary>
        protected void SetTitleContentOpen(bool open)
        {
            _contentOpen = open;
            if (!open) _contentClosedAt = Environment.TickCount;
            InvalidateTitle();
        }
        /// <summary>Tooltip of the interactive title content, or null.</summary>
        protected virtual string TitleContentTip => null;

        // ---------------- lifecycle ----------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _effectiveDpi = 0;
            ApplyDwmDarkMode();
            var margins = new Native.MARGINS { Top = 1 };          // keeps DWM's subtle frame line above the title
            Native.DwmExtendFrameIntoClientArea(Handle, ref margins);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020 /*NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED*/);
            UpdateMetrics();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); _effectiveDpi = 0; UpdateMetrics(); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateMetrics(); InvalidateTitle(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); _windowActive = true; InvalidateTitle(); }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            _windowActive = false; _tip?.Reset();
            if (_pressed >= ExtraBase) _pressed = 0;                 // a press lost to another window (Alt+Tab) is cancelled
            InvalidateTitle();
        }
        protected override void Dispose(bool disposing) { if (disposing) _tip?.Dispose(); base.Dispose(disposing); }

        void ApplyDwmDarkMode()
        {
            int dark = Fluent.IsDark(TitleBack) ? 1 : 0;
            if (Native.DwmSetWindowAttribute(Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref dark, 4) != 0)
                Native.DwmSetWindowAttribute(Handle, 19 /*pre-20H1 Windows 10*/, ref dark, 4);
        }

        // ---------------- painting ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var b = new SolidBrush(TitleBack)) g.FillRectangle(b, 0, 0, ClientSize.Width, TitleHeightPx);
            var content = TitleContentRect();
            if (IsInteractiveContent && (_contentOpen || _hot == ContentArea))
                using (var b = new SolidBrush(_contentOpen ? Fluent.Blend(TitleBack, TitleInk, 0.18) : TitleHover)) g.FillRectangle(b, content);
            PaintTitleContent(g, content);
            for (int i = 0; i < TitleButtons.Count; i++) DrawButton(g, ExtraBase + i);
            DrawButton(g, HTMINBUTTON);
            DrawButton(g, HTMAXBUTTON);
            DrawButton(g, HTCLOSE);
        }

        Pen GlyphPen(Color color) => new Pen(color, Math.Max(1f, 1.1f * DpiScale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        /// <summary>FluentChromeTabsForm.DrawCaptionButton; extra buttons use their Fluent icon glyph.</summary>
        void DrawButton(Graphics g, int code)
        {
            var rect = ButtonRect(code);
            // Pressed only while the mouse is still on it (dragging off shows it released, as a native button).
            bool hot = _hot == code, pressed = _pressed == code && _hot == code, isClose = code == HTCLOSE;
            if (hot || pressed)
            {
                var closeHover = Color.FromArgb(196, 43, 28);
                var back = isClose ? (pressed ? Fluent.Blend(closeHover, TitleBack, 0.25) : closeHover) : (pressed ? Fluent.Blend(TitleBack, TitleInk, 0.18) : TitleHover);
                using (var b = new SolidBrush(back)) g.FillRectangle(b, rect);
            }
            var color = isClose && (hot || pressed) ? Color.White : TitleText;
            if (code >= ExtraBase) { Fluent.Glyph(g, TitleButtons[code - ExtraBase].Glyph, rect, color, 14f * DpiScale); return; }

            float s = DpiScale, cx = rect.Left + rect.Width / 2f, cy = rect.Top + rect.Height / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = GlyphPen(color))
                switch (code)
                {
                    case HTMINBUTTON:
                        g.DrawLine(pen, cx - 5f * s, cy, cx + 5f * s, cy);
                        break;
                    case HTMAXBUTTON:
                        if (Native.IsZoomed(Handle))
                        {
                            float gx = cx - 5f * s, gy = cy - 5f * s, front = 8f * s, corner = 1.6f * s;
                            using (var back = new GraphicsPath())
                            {
                                back.AddLine(gx + 3.5f * s, gy, gx + 10f * s - corner, gy);
                                back.AddArc(gx + 10f * s - 2f * corner, gy, 2f * corner, 2f * corner, 270, 90);
                                back.AddLine(gx + 10f * s, gy + corner, gx + 10f * s, gy + 6.5f * s);
                                g.DrawPath(pen, back);
                            }
                            using (var path = RoundedRect(gx, gy + 2f * s, front, front, corner)) g.DrawPath(pen, path);
                        }
                        else using (var box = RoundedRect(cx - 4.5f * s, cy - 4.5f * s, 9f * s, 9f * s, 1.8f * s)) g.DrawPath(pen, box);
                        break;
                    case HTCLOSE:
                        g.DrawLine(pen, cx - 4.5f * s, cy - 4.5f * s, cx + 4.5f * s, cy + 4.5f * s);
                        g.DrawLine(pen, cx - 4.5f * s, cy + 4.5f * s, cx + 4.5f * s, cy - 4.5f * s);
                        break;
                }
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------------- our client areas: content + extra buttons ----------------

        void SetHot(int code)
        {
            if (_hot == code) return;
            _hot = code;
            InvalidateTitle();
            string text = code == HTMINBUTTON ? MinimizeTip(TitleRightToLeft)
                : code == HTMAXBUTTON ? (Native.IsZoomed(Handle) ? RestoreTip : MaximizeTip)(TitleRightToLeft)
                : code == HTCLOSE ? CloseTip(TitleRightToLeft)
                : code >= ExtraBase ? TitleButtons[code - ExtraBase].Tip
                : code == ContentArea ? TitleContentTip : null;
            if (text == null) { _tip?.Request(null, null, Rectangle.Empty, 0, TitleBack, TitleInk, TitleRightToLeft, DpiScale); return; }
            if (_tip == null || _tip.IsDisposed) _tip = new ChromeToolTip();
            var item = RectangleToScreen(code == ContentArea ? TitleContentRect() : ButtonRect(code));
            int below = RectangleToScreen(new Rectangle(0, 0, 1, TitleHeightPx)).Bottom + Dpi(4);
            _tip.Request(code, text, item, below, TitleBack, TitleInk, TitleRightToLeft, DpiScale);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitArea(e.Location);
            // Touch has no hover: no hover look or tooltip, except tracking a held button (pressed look on / off it).
            if (Native.FromTouchOrPen() && !(_pressed >= ExtraBase && h == _pressed)) h = 0;
            SetHot(h >= ContentArea ? h : 0);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHot(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitArea(e.Location);
            _tip?.Reset();
            if (h == ContentArea)
            {
                SetHot(0);                                       // the menu takes the mouse: no MouseLeave will come
                // Pressed while its menu is open, or in the very click that just closed it: closes, doesn't reopen.
                if (!_contentOpen && unchecked(Environment.TickCount - _contentClosedAt) > 300)
                    OnTitleContentClick(RectangleToScreen(TitleContentRect()));
                return;
            }
            if (h >= ExtraBase) { _pressed = h; InvalidateTitle(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int was = _pressed;
            if (was < ExtraBase) return;
            _pressed = 0; InvalidateTitle();
            if (HitArea(e.Location) == was) TitleButtons[was - ExtraBase].Click?.Invoke();
        }

        // ---------------- non-client (FluentChromeTabsForm.NonClient) ----------------

        protected override void WndProc(ref Message m)
        {
            // WinForms grows the window by the reclaimed caption on every restore-from-minimise: snapshot and reassert.
            if (m.Msg == Native.WM_SIZE)
            {
                int type = m.WParam.ToInt32();
                if (type == 1 /*SIZE_MINIMIZED*/) _boundsBeforeMinimize = RestoreBounds;
                else if (type == 2 /*SIZE_MAXIMIZED*/) _boundsBeforeMinimize = null;
                else if (type == 0 /*SIZE_RESTORED*/ && _boundsBeforeMinimize.HasValue && !_restoreFixPending)
                {
                    _restoreFixPending = true;
                    var expected = _boundsBeforeMinimize.Value;
                    _boundsBeforeMinimize = null;
                    BeginInvoke((Action)(() => { _restoreFixPending = false; if (WindowState == FormWindowState.Normal && Bounds != expected) Bounds = expected; }));
                }
            }

            if (m.Msg == 0x0100 /*WM_KEYDOWN*/ || m.Msg == 0x0104 /*WM_SYSKEYDOWN*/ || m.Msg == 0x020A /*WM_MOUSEWHEEL*/ || m.Msg == 0x0204 /*WM_RBUTTONDOWN*/)
                _tip?.Reset();                                       // as a native tooltip

            switch ((uint)m.Msg)
            {
                case 0x0024: // WM_GETMINMAXINFO: never narrower than the buttons + the title content
                    base.WndProc(ref m);
                    if (IsHandleCreated && ClientSize.Width > 0)
                    {
                        var info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                        int frame = Math.Max(0, Width - ClientSize.Width);
                        int minimum = ClusterWidth + DragReservePx + Math.Min(TitleContentWidth(), Dpi(120)) + Dpi(16) + frame;   // the selector truncates below its full width
                        if (info.ptMinTrackSize.X < minimum) { info.ptMinTrackSize.X = minimum; Marshal.StructureToPtr(info, m.LParam, false); }
                    }
                    return;

                case Native.WM_DPICHANGED:
                {
                    // WinForms on .NET Framework raises OnDpiChanged only in its PerMonitorV2 app.config mode; Word's
                    // add-in thread still gets this message when the window moves to a monitor with another DPI.
                    base.WndProc(ref m);
                    var suggested = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                    var target = Rectangle.FromLTRB(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom);
                    if (Bounds != target && WindowState == FormWindowState.Normal)
                        Native.SetWindowPos(Handle, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, 0x0004 | 0x0010 /*NOZORDER|NOACTIVATE*/);
                    _effectiveDpi = 0;
                    UpdateMetrics();
                    Invalidate();
                    return;
                }

                case Native.WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        var prm = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                        int originalTop = prm.rgrc0.Top;
                        base.WndProc(ref m);
                        bool maximizedWithFrame = Native.IsZoomed(Handle) && FormBorderStyle != FormBorderStyle.None;
                        prm = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                        prm.rgrc0.Top = originalTop + (maximizedWithFrame ? ResizeBorderHeight() : 0);
                        Marshal.StructureToPtr(prm, m.LParam, false);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case Native.WM_NCHITTEST:
                {
                    base.WndProc(ref m);
                    if ((int)m.Result == HTCLIENT)
                    {
                        var p = PointToClient(Native.PointFromLParam(m.LParam));
                        if (!Native.IsZoomed(Handle) && p.Y < ResizeBorderHeight() && HitArea(p) < ContentArea) m.Result = (IntPtr)HTTOP;
                        else if (p.Y < TitleHeightPx)
                        {
                            int area = HitArea(p);
                            if (area == HTCLOSE || area == HTMAXBUTTON || area == HTMINBUTTON) m.Result = (IntPtr)area;   // HTMAXBUTTON = snap layouts
                            else if (area == 0) m.Result = (IntPtr)HTCAPTION;                                           // drag, double-click maximise
                        }
                    }
                    return;
                }

                case Native.WM_NCMOUSEMOVE:
                {
                    int hit = (int)m.WParam;
                    bool overButton = (hit == HTMINBUTTON || hit == HTMAXBUTTON || hit == HTCLOSE) && !Native.FromTouchOrPen();
                    SetHot(overButton ? hit : 0);
                    if (overButton)
                    {
                        var tme = new Native.TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT)), dwFlags = 0x02 | 0x10 /*LEAVE|NONCLIENT*/, hwndTrack = Handle };
                        Native.TrackMouseEvent(ref tme);
                    }
                    break;
                }

                case Native.WM_NCMOUSELEAVE:
                    SetHot(0);
                    if (_pressed != 0 && _pressed < ContentArea) { _pressed = 0; InvalidateTitle(); }
                    break;

                case Native.WM_NCLBUTTONDOWN:
                case Native.WM_NCLBUTTONDBLCLK:      // a fast second click on a caption button is a press too
                {
                    int hit = (int)m.WParam;
                    if (hit == HTMINBUTTON || hit == HTMAXBUTTON || hit == HTCLOSE)
                    {   // handled here so DefWindowProc doesn't paint classic buttons over the title
                        _tip?.Reset();
                        _pressed = hit; InvalidateTitle(); m.Result = IntPtr.Zero; return;
                    }
                    break;
                }

                case Native.WM_NCLBUTTONUP:
                {
                    if (_pressed == 0 || _pressed >= ContentArea) break;
                    int hit = (int)m.WParam, was = _pressed;
                    _pressed = 0; InvalidateTitle();
                    if (hit == was)
                        switch (was)
                        {
                            case HTCLOSE: Close(); break;
                            case HTMINBUTTON: WindowState = FormWindowState.Minimized; break;
                            case HTMAXBUTTON: WindowState = Native.IsZoomed(Handle) ? FormWindowState.Normal : FormWindowState.Maximized; break;
                        }
                    m.Result = IntPtr.Zero;
                    return;
                }

                case Native.WM_NCRBUTTONUP:
                    if ((int)m.WParam == HTCAPTION)
                    {
                        var pt = Native.PointFromLParam(m.LParam);
                        int cmd = Native.TrackPopupMenuEx(Native.GetSystemMenu(Handle, false), 0x0100 /*TPM_RETURNCMD*/, pt.X, pt.Y, Handle, IntPtr.Zero);
                        if (cmd != 0) Native.SendMessage(Handle, Native.WM_SYSCOMMAND, (IntPtr)cmd, IntPtr.Zero);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
            }
            base.WndProc(ref m);
        }
    }
}
