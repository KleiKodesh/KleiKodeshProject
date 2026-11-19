using System;
using System.Drawing;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A themed tooltip, ported from FluentChromeTabs' ThemedToolTip: a borderless, non-activating top-level
    /// window at explicit screen coordinates - never steals focus, never in Alt+Tab, never under the mouse
    /// (clicks pass through), placed correctly in RTL.
    ///
    ///   - Show delay and warm switching as Windows tooltips: wait before the first tip, then switch at once.
    ///   - Leaving an item hides after a short grace, so moving to the next item doesn't blink it off and on.
    ///   - It closes itself once the mouse is no longer over its item: hosts don't always report a mouse leave
    ///     (Office's own strip, switching apps), so it never relies on that alone.
    ///   - Under its item, or beside a menu (<c>beside</c>): on the reading-start side, flipped when that side
    ///     has no room - never on top of what it describes.
    /// </summary>
    internal sealed class ChromeToolTip : Form
    {
        const int InitialDelayMs = 400, WarmMs = 400, HideGraceMs = 120, WatchMs = 150;
        Color _back, _border, _text;
        bool _rtl;
        string _tip = "", _body;
        Font _bold;
        float _scale = 1f;
        object _key;
        Rectangle _item;                                          // screen rect of the item the tip belongs to
        readonly Timer _delay = new Timer { Interval = InitialDelayMs };
        readonly Timer _grace = new Timer { Interval = HideGraceMs };
        readonly Timer _watch = new Timer { Interval = WatchMs };
        Action _pending;
        int _hiddenAt = Environment.TickCount - 100000;

        public ChromeToolTip()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Enabled = false;                                      // purely visual - never takes input
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _delay.Tick += (s, e) => { _delay.Stop(); _pending?.Invoke(); };
            _grace.Tick += (s, e) => { _grace.Stop(); HideNow(); };
            _watch.Tick += (s, e) => { if (!_item.Contains(Cursor.Position) || !OurAppInFront()) Reset(); };
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>A tooltip belongs to the app in front: Alt+Tab away hides it even with the mouse still.</summary>
        static bool OurAppInFront()
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
            return pid == OurPid;
        }
        static readonly uint OurPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>The tip's font at its scale (in pixels: the tip may sit on a monitor with another DPI than the process).</summary>
        Font _font;
        float _fontScale;
        Font TipFont
        {
            get
            {
                if (_font == null || _fontScale != _scale)
                {
                    _font?.Dispose(); _bold?.Dispose(); _bold = null;
                    _font = new Font("Segoe UI", 12f * _scale, GraphicsUnit.Pixel); _fontScale = _scale;
                }
                return _font;
            }
        }
        Font BoldFont => _bold ?? (_bold = new Font(TipFont, FontStyle.Bold));

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/ | 0x00000008 /*TOPMOST*/ | 0x00000020 /*TRANSPARENT*/;
                cp.ClassStyle |= 0x00020000;                     // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084 /*WM_NCHITTEST*/) { m.Result = (IntPtr)(-1) /*HTTRANSPARENT*/; return; }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 3;                                        // DWMWCP_ROUNDSMALL
            Native.DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref pref, 4);
        }

        int S(int logical) => (int)Math.Round(logical * _scale);

        /// <summary>
        /// Shows <paramref name="text"/> for <paramref name="key"/> over the item at <paramref name="itemScreen"/>: under it
        /// at <paramref name="topScreen"/>, or beside <paramref name="beside"/> (e.g. the menu) level with the item.
        /// Same key = no change; null key or text = hide (after a short grace).
        /// </summary>
        /// <param name="body">Optional description under <paramref name="text"/>, which is then drawn bold (a ribbon supertip).</param>
        public void Request(object key, string text, Rectangle itemScreen, int topScreen, Color back, Color ink, bool rtl, float scale,
            string body = null, Rectangle? beside = null)
        {
            if (Equals(key, _key)) return;
            _key = key;
            _delay.Stop();
            if (key == null || string.IsNullOrEmpty(text))
            {
                if (Visible) _grace.Start();                     // the next item may follow at once: no blink
                return;
            }
            _grace.Stop();
            _item = itemScreen;
            bool dark = Fluent.IsDark(back);
            _back = dark ? Color.FromArgb(44, 44, 44) : Color.White;
            _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(209, 209, 209);
            _text = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(36, 36, 36);
            _pending = () => Place(text, body, itemScreen, topScreen, beside, rtl, scale);
            bool warm = Visible || unchecked(Environment.TickCount - _hiddenAt) < WarmMs;
            if (warm) _pending();
            else { HideNow(); _delay.Start(); _watch.Start(); }   // watching already: the mouse may leave before it shows
        }

        const TextFormatFlags BodyFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        void Place(string text, string body, Rectangle item, int top, Rectangle? beside, bool rtl, float scale)
        {
            if (!item.Contains(Cursor.Position)) { Reset(); return; }   // left during the delay
            _tip = text; _body = string.IsNullOrWhiteSpace(body) ? null : body; _rtl = rtl; _scale = scale;
            int w, h;
            if (_body == null)
            {
                var size = TextRenderer.MeasureText(_tip, TipFont, new Size(S(300), 0), BodyFlags);   // wraps, as native tips
                w = size.Width + S(16); h = size.Height + S(8);
            }
            else
            {
                var title = TextRenderer.MeasureText(_tip, BoldFont, new Size(S(300), 0), BodyFlags);
                var desc = TextRenderer.MeasureText(_body, TipFont, new Size(S(300), 0), BodyFlags);
                w = Math.Max(title.Width, desc.Width) + S(20); h = title.Height + S(6) + desc.Height + S(16);
            }

            var work = Screen.FromRectangle(item).WorkingArea;
            int x, y;
            if (beside.HasValue)
            {
                // Beside the menu, level with the item; the reading-start side first, the other if that has no room.
                var b = beside.Value; int gap = S(4);
                int before = b.Left - gap - w, after = b.Right + gap;
                bool roomBefore = before >= work.Left, roomAfter = after + w <= work.Right;
                x = rtl ? (roomBefore || !roomAfter ? before : after) : (roomAfter || !roomBefore ? after : before);
                y = item.Top;
            }
            else
            {
                // Under the item, aligned to its reading-start edge; above it when there is no room below.
                x = rtl ? item.Right - w : item.Left;
                y = top + h <= work.Bottom ? top : item.Top - S(4) - h;
            }
            x = Math.Max(work.Left, Math.Min(x, work.Right - w));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - h));

            Bounds = new Rectangle(x, y, w, h);                     // one move + size: no jump while switching
            Invalidate();
            if (!Visible) Show();
            _watch.Start();
        }

        void HideNow()
        {
            _delay.Stop(); _grace.Stop();
            if (Visible) { _hiddenAt = Environment.TickCount; Hide(); }
            _watch.Stop();
        }

        public void HideTip() => HideNow();
        /// <summary>Hides at once and forgets the item (a click, the host going away).</summary>
        public void Reset() { _key = null; HideNow(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(_back)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(_border)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            var side = _rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            if (_body == null)
            {
                TextRenderer.DrawText(g, _tip, TipFont, new Rectangle(S(8), S(4), ClientSize.Width - S(16), ClientSize.Height - S(8)), _text, BodyFlags | side);
                return;
            }
            int titleH = TextRenderer.MeasureText(_tip, BoldFont, new Size(S(300), 0), BodyFlags).Height;
            TextRenderer.DrawText(g, _tip, BoldFont, new Rectangle(S(10), S(8), ClientSize.Width - S(20), titleH), _text, BodyFlags | side);
            TextRenderer.DrawText(g, _body, TipFont, new Rectangle(S(10), S(8) + titleH + S(6), ClientSize.Width - S(20), ClientSize.Height - titleH - S(20)), _text, BodyFlags | side);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _delay.Dispose(); _grace.Dispose(); _watch.Dispose(); _bold?.Dispose(); _font?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
