using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Office.Tools;
using Office = Microsoft.Office.Core;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// Extra chrome for VSTO custom task panes, drawn into Office's own title strip:
    ///   - a pane-switch button next to Office's options arrow, opening a compact menu of the
    ///     add-in's panes. The list comes from the ribbon itself: every ribbon &lt;button&gt; with
    ///     tag="pane", labelled, iconed and filtered by the ribbon's own getLabel / getImage /
    ///     getVisible, and opened by its own onAction. Switching only HIDES the current pane
    ///     (Office's stock behaviour) before the ribbon opens the other.
    ///   - Office's X closes for real: it calls the app's close action (remove + dispose).
    ///     Every other hide (code, pop-out, Word view changes, Office's own menu) stays stock.
    ///
    /// Usage:  TaskPaneChrome.UseRibbon(() => new MyRibbon());          // once, any time
    ///         TaskPaneChrome.Attach(pane, () => ClosePaneForReal(pane)); // after creating a pane
    ///
    /// Safety: purely optional. If <see cref="Enabled"/> is false, or the title strip isn't the
    /// window / size / layout we know (title + options arrow + X), or anything throws, the hook
    /// removes itself and the pane is a plain Office task pane. It never eats a message it
    /// doesn't own.
    /// </summary>
    public static class TaskPaneChrome
    {
        public static bool Enabled = true;
        public static Action<string> Log = s => Debug.WriteLine("[TaskPaneChrome] " + s);
        public static char SwitchGlyph = '';                       // Fluent "stacked windows"
        public static Func<bool, string> MenuTitle = rtl => rtl ? "חלוניות" : "Task panes";

        static Func<object> _createRibbon;

        /// <summary>Where pane entries come from: a ribbon object (IRibbonExtensibility + its callbacks). Created on first use.</summary>
        public static void UseRibbon(Func<object> createRibbon) => _createRibbon = createRibbon;

        /// <summary>Adds the switch button and real-close X to a pane the app just created.</summary>
        public static void Attach(CustomTaskPane pane, Action closePane)
        {
            if (!Enabled || pane == null) return;
            try
            {
                var content = pane.Control;
                var strip = new TitleStrip(content);
                strip.SwitchClicked += () => ShowSwitchMenu(pane, strip);

                EventHandler onVisibleChanged = null;
                onVisibleChanged = (s, e) =>
                {
                    try
                    {
                        if (pane.Visible) { content.BeginInvoke((Action)strip.Attach); return; }   // Office built a new frame
                        if (!strip.InOfficeClick) return;                                            // not the X: stock hide
                        Log("X: closing '" + pane.Title + "'");
                        content.BeginInvoke((Action)(() =>                                           // after Office's notification
                        {
                            pane.VisibleChanged -= onVisibleChanged;
                            strip.Dispose();
                            try { closePane(); } catch (Exception ex) { Log("close failed: " + ex.Message); }
                        }));
                    }
                    catch (Exception ex) { Log("VisibleChanged: " + ex.Message); }
                };
                pane.VisibleChanged += onVisibleChanged;
                content.Disposed += (s, e) => strip.Dispose();
            }
            catch (Exception ex) { Log("attach failed, stock pane: " + ex.Message); }
        }

        // ================= pane entries, read from the ribbon =================

        sealed class PaneEntry { public string Id, Title; public Image Icon; public Action Open; }

        static readonly Dictionary<string, Image> IconCache = new Dictionary<string, Image>();

        sealed class RibbonButton { public string Id, Label, GetLabel, GetImage, GetVisible, OnAction; public bool Hidden; }

        static object _ribbon;
        static List<RibbonButton> _ribbonButtons;

        /// <summary>The ribbon's buttons tagged "pane", parsed once (GetCustomUI builds the whole XML).</summary>
        static List<RibbonButton> RibbonButtons()
        {
            if (_ribbonButtons != null || _createRibbon == null) return _ribbonButtons ?? new List<RibbonButton>();
            _ribbon = _createRibbon();
            string xml = (_ribbon as Office.IRibbonExtensibility)?.GetCustomUI("");
            _ribbonButtons = string.IsNullOrEmpty(xml) ? new List<RibbonButton>() : XDocument.Parse(xml).Descendants()
                .Where(e => e.Name.LocalName == "button" && (string)e.Attribute("tag") == "pane" && e.Attribute("id") != null)
                .GroupBy(e => (string)e.Attribute("id")).Select(g => g.First())
                .Select(e => new RibbonButton
                {
                    Id = (string)e.Attribute("id"), Label = (string)e.Attribute("label"),
                    GetLabel = (string)e.Attribute("getLabel"), GetImage = (string)e.Attribute("getImage"),
                    GetVisible = (string)e.Attribute("getVisible"), OnAction = (string)e.Attribute("onAction"),
                    Hidden = (string)e.Attribute("visible") == "false",
                }).ToList();
            return _ribbonButtons;
        }

        /// <summary>Pane entries as the ribbon's callbacks describe them right now (labels / visibility can change).</summary>
        static List<PaneEntry> ReadRibbonPanes()
        {
            var list = new List<PaneEntry>();
            foreach (var b in RibbonButtons())
            {
                var control = new RibbonControl(b.Id, "pane");
                if (b.Hidden || Invoke<bool?>(_ribbon, b.GetVisible, control) == false) continue;
                if (!IconCache.TryGetValue(b.Id, out var icon))
                    IconCache[b.Id] = icon = Invoke<Image>(_ribbon, b.GetImage, control);
                list.Add(new PaneEntry
                {
                    Id = b.Id,
                    Title = Invoke<string>(_ribbon, b.GetLabel, control) ?? b.Label ?? b.Id,
                    Icon = icon,
                    Open = () => Invoke<object>(_ribbon, b.OnAction, control),
                });
            }
            return list;
        }

        /// <summary>Calls ribbon.callback(IRibbonControl) by name, as Office would; default(T) if missing or failing.</summary>
        static T Invoke<T>(object ribbon, string callback, Office.IRibbonControl control)
        {
            if (string.IsNullOrEmpty(callback)) return default(T);
            var method = ribbon.GetType().GetMethod(callback, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(Office.IRibbonControl) }, null);
            if (method == null) { Log("ribbon callback missing: " + callback); return default(T); }
            try { return method.Invoke(ribbon, new object[] { control }) is T t ? t : default(T); }
            catch (TargetInvocationException ex) { Log(callback + " threw: " + ex.InnerException?.Message); return default(T); }
        }

        sealed class RibbonControl : Office.IRibbonControl
        {
            public RibbonControl(string id, string tag) { Id = id; Tag = tag; }
            public string Id { get; }
            public string Tag { get; }
            public object Context => null;
        }

        // ================= the switch menu =================

        static void ShowSwitchMenu(CustomTaskPane pane, TitleStrip strip)
        {
            var panes = ReadRibbonPanes();
            if (panes.Count == 0) { Log("no ribbon buttons tagged \"pane\": nothing to switch to"); return; }
            float dpi = strip.Dpi;
            bool rtl = strip.RightToLeft;
            bool dark = strip.Background.GetBrightness() < 0.45f;
            var colors = new MenuColors
            {
                Back = dark ? Color.FromArgb(41, 41, 41) : Color.White,
                Text = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(36, 36, 36),
                Header = dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(97, 97, 97),
                Hover = dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(240, 240, 240),
                Accent = dark ? Color.FromArgb(71, 158, 245) : Color.FromArgb(15, 108, 189),
                Border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(209, 209, 209),
                Dpi = dpi, Rtl = rtl,
            };
            string current = pane.Title;

            var menu = new ContextMenuStrip
            {
                RightToLeft = RightToLeft.No,                 // window stays LTR so its position never moves; content drawn RTL
                ShowImageMargin = false, ShowCheckMargin = false,
                Renderer = new MenuRenderer(colors), BackColor = colors.Back, DropShadowEnabled = true,
                Padding = new Padding(0, (int)(2 * dpi), 0, (int)(2 * dpi)),
                Font = RowFont,
            };
            menu.Items.Add(new ToolStripLabel(MenuTitle(rtl)) { AutoSize = false, Height = (int)(22 * dpi), Font = HeaderFont });
            foreach (var entry in panes)
            {
                var item = new ToolStripMenuItem(entry.Title) { AutoSize = false, Height = (int)(26 * dpi), Checked = entry.Title == current, Tag = entry.Icon };
                item.Click += (s, e) =>
                {
                    if (entry.Title == current) return;
                    Log("switch: " + current + " -> " + entry.Id);
                    try { pane.Visible = false; } catch { }           // stock hide: stays alive
                    entry.Open();                                     // the ribbon's own onAction
                };
                menu.Items.Add(item);
            }

            IntPtr owner = strip.OwnerWindow;
            menu.HandleCreated += (s, e) =>
            {
                SetWindowLongPtr(menu.Handle, -8 /*GWLP_HWNDPARENT*/, owner);   // owned by the Word window
                int square = 1; DwmSetWindowAttribute(menu.Handle, 33 /*CORNER_PREFERENCE*/, ref square, 4);
            };

            // Size to content (no unused shortcut column). Hooks above are attached first: this may create the handle.
            int pad = (int)(10 * dpi), iconSize = (int)(16 * dpi), gap = (int)(8 * dpi);
            int textWidth = 0;
            foreach (ToolStripItem i in menu.Items) textWidth = Math.Max(textWidth, TextRenderer.MeasureText(i.Text, RowFontBold).Width);
            int width = pad * 2 + iconSize + gap + textWidth + (int)(6 * dpi);
            int height = menu.Padding.Vertical + 2;
            foreach (ToolStripItem i in menu.Items) { i.Width = width; height += i.Height + i.Margin.Vertical; }
            menu.AutoSize = false;
            menu.Size = new Size(width + 2, height);
            if (menu.IsHandleCreated) SetWindowLongPtr(menu.Handle, -8, owner);

            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            menu.Show(strip.MenuAnchor(), ToolStripDropDownDirection.BelowRight);
        }

        static readonly Font RowFont = new Font("Segoe UI", 9f);
        static readonly Font RowFontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font HeaderFont = new Font("Segoe UI Semibold", 8f);

        sealed class MenuColors { public Color Back, Text, Header, Hover, Accent, Border; public float Dpi; public bool Rtl; }

        /// <summary>Compact Outlook-style rows: square hover, accent bar on the current pane, icon + text, RTL-aware.</summary>
        sealed class MenuRenderer : ToolStripRenderer
        {
            readonly MenuColors c;
            public MenuRenderer(MenuColors colors) { c = colors; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, e.AffectedBounds); }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { using (var p = new Pen(c.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }

            protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
            { using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height); }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                int inset = (int)(2 * c.Dpi);
                using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height);
                if (e.Item.Selected)
                    using (var b = new SolidBrush(c.Hover)) e.Graphics.FillRectangle(b, inset, 1, e.Item.Width - 2 * inset, e.Item.Height - 2);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var g = e.Graphics;
                var item = e.Item;
                int w = item.Width, h = item.Height;
                int pad = (int)(10 * c.Dpi), iconSize = (int)(16 * c.Dpi), gap = (int)(8 * c.Dpi);
                var align = c.Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;

                if (!(item is ToolStripMenuItem row))   // header
                {
                    TextRenderer.DrawText(g, item.Text, item.Font, new Rectangle(pad, 0, w - 2 * pad, h), c.Header,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | align);
                    return;
                }
                if (row.Checked)                        // accent bar on the current pane
                {
                    int bw = Math.Max(2, (int)(3 * c.Dpi)), bh = (int)(14 * c.Dpi), bx = (int)(3 * c.Dpi);
                    using (var b = new SolidBrush(c.Accent)) g.FillRectangle(b, c.Rtl ? w - bx - bw : bx, (h - bh) / 2, bw, bh);
                }
                var iconRect = new Rectangle(c.Rtl ? w - pad - iconSize : pad, (h - iconSize) / 2, iconSize, iconSize);
                if (item.Tag is Image icon)
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, iconRect);
                }
                var textRect = new Rectangle(c.Rtl ? pad : pad + iconSize + gap, 0, w - 2 * pad - iconSize - gap, h);
                TextRenderer.DrawText(g, item.Text, row.Checked ? RowFontBold : RowFont, textRect, c.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | align);
            }
        }

        // ================= the title strip (Office's MsoCommandBar) =================

        /// <summary>
        /// Subclasses the MsoCommandBar that Office draws a pane's title, options arrow and X into.
        /// Office destroys it on hide and builds a new one on show, so <see cref="Attach"/> runs after every show.
        /// </summary>
        sealed class TitleStrip : IDisposable
        {
            public event Action SwitchClicked;
            public bool InOfficeClick => _officeClickDepth > 0 && _pressedOnClose;   // Office is handling a click that went down on its X
            public bool RightToLeft { get; private set; }
            public Color Background { get; private set; } = Color.White;
            public float Dpi => _bar == IntPtr.Zero ? 1f : GetDpiForWindow(_bar) / 96f;
            public IntPtr OwnerWindow => _bar == IntPtr.Zero ? IntPtr.Zero : GetAncestor(_bar, 2 /*GA_ROOT*/);

            readonly Control _content;                            // its Handle is read fresh: WinForms may recreate it
            readonly SubclassProc _proc;                          // kept alive for the native side
            IntPtr _bar, _workPane;
            Timer _retry;
            Rectangle _button = Rectangle.Empty, _closeBox = Rectangle.Empty;
            Color _ink = Color.Black;
            bool _pressedOnClose;
            bool _failed, _disposed, _hover, _ourMouseDown;
            int _officeClickDepth;

            public TitleStrip(Control paneContent) { _content = paneContent; _proc = WndProc; Attach(); }

            /// <summary>Hooks the pane's current frame; Office builds it after Visible = true returns, so retry briefly.</summary>
            public void Attach()
            {
                if (_failed || _disposed || _bar != IntPtr.Zero || TryHook()) return;
                _retry?.Dispose();
                int tries = 0;
                _retry = new Timer { Interval = 50 };
                _retry.Tick += (s, e) => { if (TryHook() || ++tries > 60) { _retry?.Dispose(); _retry = null; } };
                _retry.Start();
            }

            bool TryHook()
            {
                try
                {
                    IntPtr bar = IntPtr.Zero, work = IntPtr.Zero;
                    if (_content.IsDisposed || !_content.IsHandleCreated) return false;
                    for (var h = GetParent(_content.Handle); h != IntPtr.Zero; h = GetParent(h))
                    {
                        var cls = ClassName(h);
                        if (cls == "MsoWorkPane" && work == IntPtr.Zero) work = h;
                        if (cls == "MsoCommandBar") { bar = h; break; }
                        if (cls == "OpusApp") return false;           // frame not built yet
                    }
                    if (bar == IntPtr.Zero || work == IntPtr.Zero) return false;
                    _bar = bar; _workPane = work;
                    var s = StripRect();
                    if (s.Height < 16 * Dpi || s.Height > 90 * Dpi || s.Width < 120 * Dpi) { Fail("unexpected strip " + s); return true; }
                    if (!SetWindowSubclass(_bar, _proc, (UIntPtr)1, IntPtr.Zero)) { Fail("subclass refused"); return true; }
                    Log("hooked strip " + s);
                    InvalidateRect(_bar, IntPtr.Zero, false);
                    return true;
                }
                catch (Exception ex) { Fail(ex.Message); return true; }
            }

            IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
            {
                if (_failed) return DefSubclassProc(hWnd, msg, wParam, lParam);
                try
                {
                    switch (msg)
                    {
                        case WM_NCDESTROY:
                            RemoveWindowSubclass(hWnd, _proc, id);
                            _bar = _workPane = IntPtr.Zero; _button = _closeBox = Rectangle.Empty; _hover = false;
                            return DefSubclassProc(hWnd, msg, wParam, lParam);
                        case WM_NCHITTEST:
                            if (_button.Contains(ScreenToStrip(lParam))) return (IntPtr)1 /*HTCLIENT*/;
                            break;
                        case WM_SETCURSOR:
                            if (!_button.IsEmpty && _button.Contains(CursorInStrip())) { SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)32512)); return (IntPtr)1; }
                            break;
                        case WM_MOUSEMOVE: SetHover(_button.Contains(ClientPoint(lParam))); break;
                        case WM_MOUSELEAVE: SetHover(false); break;
                        case WM_LBUTTONDBLCLK:
                            if (_button.Contains(ClientPoint(lParam))) { _ourMouseDown = true; return IntPtr.Zero; }
                            break;
                        case WM_LBUTTONDOWN:
                            if (_button.Contains(ClientPoint(lParam)))
                            {
                                _ourMouseDown = true;
                                try { SwitchClicked?.Invoke(); } catch (Exception ex) { Log("switch menu: " + ex.Message); }
                                return IntPtr.Zero;                   // Office never sees it
                            }
                            break;
                        case WM_LBUTTONUP:
                            if (_ourMouseDown) { _ourMouseDown = false; return IntPtr.Zero; }
                            break;
                        case WM_PAINT:
                            // Clip Office away from our button (it would paint over it), then draw it ourselves.
                            if (!_button.IsEmpty) { var r = ToRECT(_button); ValidateRect(hWnd, ref r); }
                            var painted = DefSubclassProc(hWnd, msg, wParam, lParam);
                            Draw(relayout: true);
                            return painted;
                    }

                    bool officeClick = msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_NCLBUTTONDOWN || msg == WM_NCLBUTTONUP;
                    // Only Office's X counts - not a title drag / undock or the options menu, which Office
                    // also runs inside the strip's button-down.
                    if (msg == WM_LBUTTONDOWN) _pressedOnClose = _closeBox.Contains(ClientPoint(lParam));
                    if (msg == WM_NCLBUTTONDOWN) _pressedOnClose = _closeBox.Contains(ScreenToStrip(lParam));
                    if (officeClick) _officeClickDepth++;
                    try { return DefSubclassProc(hWnd, msg, wParam, lParam); }
                    finally
                    {
                        if (officeClick) _officeClickDepth--;
                        if ((msg == WM_LBUTTONUP || msg == WM_NCLBUTTONUP) && _officeClickDepth == 0) _pressedOnClose = false;
                        if (msg == WM_MOUSEMOVE || msg == WM_MOUSELEAVE || msg == WM_TIMER || msg == WM_LBUTTONUP)
                            Draw(relayout: false);                   // Office repaints hover states outside WM_PAINT
                    }
                }
                catch (Exception ex) { Fail(ex.Message); return DefSubclassProc(hWnd, msg, wParam, lParam); }
            }

            /// <summary>The strip: the MsoCommandBar client area above the MsoWorkPane.</summary>
            Rectangle StripRect()
            {
                GetClientRect(_bar, out RECT client);
                GetWindowRect(_workPane, out RECT work);
                var top = new POINT { X = work.Left, Y = work.Top };
                ScreenToClient(_bar, ref top);
                return new Rectangle(0, 0, client.Right, Math.Max(0, top.Y));
            }

            /// <summary>
            /// Reads what Office painted (one memory copy) into ink runs: title, options arrow, X.
            /// Our button: Office's button size (32 dip, flush with the strip bottom), one step past the
            /// arrow - the step Office uses from X to arrow. Anything else = no button.
            /// </summary>
            void Layout(IntPtr screenDc, Rectangle s)
            {
                var dc = CreateCompatibleDC(screenDc);
                var bmp = CreateCompatibleBitmap(screenDc, s.Width, s.Height);
                var old = SelectObject(dc, bmp);
                try
                {
                    SetLayout(dc, GetLayout(screenDc));
                    BitBlt(dc, 0, 0, s.Width, s.Height, screenDc, 0, 0, SRCCOPY);
                    float dpi = Dpi;
                    Background = ColorTranslator.FromWin32((int)GetPixel(dc, 2, s.Height / 2));
                    var inked = new bool[s.Width];
                    int strongest = 0;
                    for (int x = 0; x < s.Width; x++)
                    {
                        if (x >= _button.Left && x < _button.Right) continue;          // our own pixels
                        foreach (int pct in SampleRows)
                        {
                            var px = ColorTranslator.FromWin32((int)GetPixel(dc, x, s.Height * pct / 100));
                            int d = Math.Max(Math.Abs(px.R - Background.R), Math.Max(Math.Abs(px.G - Background.G), Math.Abs(px.B - Background.B)));
                            if (d > 60) { inked[x] = true; if (d > strongest) { strongest = d; _ink = px; } }
                        }
                    }
                    var runs = new List<int[]>();
                    int merge = (int)Math.Round(9 * dpi);
                    for (int x = 0; x < s.Width; x++)
                        if (inked[x])
                        {
                            if (runs.Count > 0 && x - runs[runs.Count - 1][1] <= merge) runs[runs.Count - 1][1] = x;
                            else runs.Add(new[] { x, x });
                        }

                    _button = Rectangle.Empty;
                    try { DecideButton(runs, s, dpi); }
                    finally
                    {
                        var result = "layout runs=" + string.Join(" ", runs.Select(r => r[0] + "-" + r[1])) + " button=" + _button;
                        if (result != _lastLayout) { _lastLayout = result; Log(result); }
                    }
                }
                finally { SelectObject(dc, old); DeleteObject(bmp); DeleteDC(dc); }
            }

            string _lastLayout;
            static readonly int[] SampleRows = { 35, 45, 50, 55, 65 };   // % of strip height through Office's glyphs

            void DecideButton(List<int[]> runs, Rectangle s, float dpi)
            {
                    if (runs.Count < 3) return;                                       // not title + arrow + X
                    var title = runs.OrderByDescending(r => r[1] - r[0]).First();
                    var near = runs.Where(r => r != title).OrderBy(r => Math.Min(Math.Abs(r[0] - title[1]), Math.Abs(r[1] - title[0]))).ToList();
                    int arrow = (near[0][0] + near[0][1]) / 2, close = (near[1][0] + near[1][1]) / 2;
                    RightToLeft = title[0] > arrow;
                    int box = (int)Math.Round(32 * dpi), step = arrow - close;
                    _closeBox = new Rectangle(close - box / 2, s.Height - box, box, box);       // Office's X button area
                    if (Math.Abs(step) < box * 0.8 || Math.Abs(step) > box * 3) return;
                    var b = new Rectangle(2 * arrow - close - box / 2, s.Height - box, box, box);
                    if (b.Left >= 0 && b.Right <= s.Width && b.Top >= 0 && !runs.Any(r => r[1] >= b.Left && r[0] <= b.Right))
                        _button = b;
            }

            /// <summary>Our button: Office's hover tint when hovered, the switch glyph in Office's ink. Composed off-screen, one blit.</summary>
            void Draw(bool relayout)
            {
                if (_bar == IntPtr.Zero) return;
                var s = StripRect();
                if (s.Height < 12) return;
                var screenDc = GetDC(_bar);
                try
                {
                    if (relayout || _button.IsEmpty) Layout(screenDc, s);
                    if (_button.IsEmpty) return;
                    int w = _button.Width, h = _button.Height;
                    var dc = CreateCompatibleDC(screenDc);
                    var bmp = CreateCompatibleBitmap(screenDc, w, h);
                    var old = SelectObject(dc, bmp);
                    try
                    {
                        using (var g = Graphics.FromHdc(dc))
                        {
                            using (var back = new SolidBrush(_hover ? Blend(Background, _ink, 0.105) : Background)) g.FillRectangle(back, 0, 0, w, h);
                            int glyphY = (int)Math.Round(s.Height / 2f + 3) - _button.Top;     // Office's glyph centre line
                            using (var f = new Font(IconFont, (int)Math.Round(13 * Dpi), GraphicsUnit.Pixel))
                                TextRenderer.DrawText(g, SwitchGlyph.ToString(), f, new Rectangle(0, glyphY - w / 2, w, w), _ink,
                                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        BitBlt(screenDc, _button.Left, _button.Top, w, h, dc, 0, 0, SRCCOPY);
                    }
                    finally { SelectObject(dc, old); DeleteObject(bmp); DeleteDC(dc); }
                }
                finally { ReleaseDC(_bar, screenDc); }
            }

            void SetHover(bool on) { if (on != _hover && !_button.IsEmpty) { _hover = on; Draw(relayout: false); } }

            /// <summary>Screen point under our button's left edge (the menu opens down and right from it).</summary>
            public Point MenuAnchor()
            {
                var p = new POINT { X = _button.Left, Y = _button.Bottom };
                ClientToScreen(_bar, ref p);
                return new Point(p.X, p.Y);
            }

            void Fail(string why)
            {
                if (_failed) return;
                _failed = true;
                Log("disabled, stock pane: " + why);
                Unhook();
            }

            void Unhook()
            {
                _retry?.Dispose(); _retry = null;
                if (_bar == IntPtr.Zero) return;
                RemoveWindowSubclass(_bar, _proc, (UIntPtr)1);
                var bar = _bar;
                _bar = _workPane = IntPtr.Zero; _button = Rectangle.Empty;
                InvalidateRect(bar, IntPtr.Zero, true);              // Office repaints without us
            }

            public void Dispose() { if (!_disposed) { _disposed = true; Unhook(); } }

            Point ScreenToStrip(IntPtr l) { var p = new POINT { X = (short)(l.ToInt64() & 0xFFFF), Y = (short)((l.ToInt64() >> 16) & 0xFFFF) }; ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
            Point CursorInStrip() { GetCursorPos(out POINT p); ScreenToClient(_bar, ref p); return new Point(p.X, p.Y); }
        }

        // ================= helpers / interop =================

        static readonly string IconFont = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
            .FirstOrDefault(n => { using (var f = new Font(n, 10)) return f.Name == n; }) ?? "Segoe UI Symbol";

        static Color Blend(Color a, Color b, double t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        static Point ClientPoint(IntPtr l) => new Point((short)(l.ToInt64() & 0xFFFF), (short)((l.ToInt64() >> 16) & 0xFFFF));
        static RECT ToRECT(Rectangle r) => new RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        static string ClassName(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

        const uint WM_PAINT = 0xF, WM_SETCURSOR = 0x20, WM_NCDESTROY = 0x82, WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1,
            WM_NCLBUTTONUP = 0xA2, WM_TIMER = 0x113, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202,
            WM_LBUTTONDBLCLK = 0x203, WM_MOUSELEAVE = 0x2A3, SRCCOPY = 0x00CC0020;

        struct RECT { public int Left, Top, Right, Bottom; }
        struct POINT { public int X, Y; }
        delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);

        [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id, IntPtr data);
        [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id);
        [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
        [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h, ref RECT r);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr c);
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint GetLayout(IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint SetLayout(IntPtr dc, uint l);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
    }
}
