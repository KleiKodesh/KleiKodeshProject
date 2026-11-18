using System;
using System.Drawing;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A themed tooltip, ported from FluentChromeTabs' ThemedToolTip: a borderless, non-activating top-level
    /// window at explicit screen coordinates - never steals focus, never in Alt+Tab, placed correctly in RTL.
    /// Show delay and warm switching as Windows tooltips (and ChromeTabs): wait before the first tip, then switch at once.
    /// </summary>
    internal sealed class ChromeToolTip : Form
    {
        const int InitialDelayMs = 400, WarmMs = 400;
        Color _back, _border, _text;
        bool _rtl;
        string _tip = "";
        float _scale = 1f;
        object _key;
        readonly Timer _delay = new Timer { Interval = InitialDelayMs };
        Action _pending;
        int _hiddenAt = Environment.TickCount - 100000;

        public ChromeToolTip()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Enabled = false;                                      // purely visual - never takes input
            Font = new Font("Segoe UI", 9f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _delay.Tick += (s, e) => { _delay.Stop(); _pending?.Invoke(); };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/ | 0x00000008 /*TOPMOST*/;
                cp.ClassStyle |= 0x00020000;                     // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 3;                                        // DWMWCP_ROUNDSMALL
            Native.DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref pref, 4);
        }

        int S(int logical) => (int)Math.Round(logical * _scale);

        /// <summary>Shows <paramref name="text"/> for <paramref name="key"/> under <paramref name="itemScreen"/>; same key = no change, null = hide.</summary>
        public void Request(object key, string text, Rectangle itemScreen, int topScreen, Color back, Color ink, bool rtl, float scale)
        {
            if (Equals(key, _key)) return;
            _key = key;
            _delay.Stop();
            if (key == null || string.IsNullOrEmpty(text)) { if (Visible) _hiddenAt = Environment.TickCount; HideTip(); return; }
            bool dark = Fluent.IsDark(back);
            _back = dark ? Color.FromArgb(44, 44, 44) : Color.White;
            _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(209, 209, 209);
            _text = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(36, 36, 36);
            _pending = () => Place(text, rtl ? itemScreen.Right : itemScreen.Left, topScreen, rtl, scale);
            bool warm = Visible || unchecked(Environment.TickCount - _hiddenAt) < WarmMs;
            if (warm) _pending(); else { HideTip(); _delay.Start(); }
        }

        void Place(string text, int startX, int top, bool rtl, float scale)
        {
            _tip = text; _rtl = rtl; _scale = scale;
            var size = TextRenderer.MeasureText(_tip, Font);
            int w = size.Width + S(16), h = size.Height + S(8);
            ClientSize = new Size(w, h);
            int left = rtl ? startX - w : startX;                 // the reading start edge is the anchor
            var work = Screen.FromPoint(new Point(startX, top)).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(left, work.Right - w)), Math.Min(top, work.Bottom - h));
            if (!Visible) Show(); else Invalidate();
        }

        public void HideTip() { _delay.Stop(); if (Visible) Hide(); }
        public void Reset() { _key = null; HideTip(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(_back)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(_border)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            TextRenderer.DrawText(g, _tip, Font, new Rectangle(S(8), 0, ClientSize.Width - S(16), ClientSize.Height), _text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | (_rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
        }

        protected override void Dispose(bool disposing) { if (disposing) _delay.Dispose(); base.Dispose(disposing); }
    }
}
