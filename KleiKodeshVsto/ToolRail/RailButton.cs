using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KleiKodesh.ToolRail
{
    /// <summary>
    /// One icon on the rail. Owner-drawn, and built for contrast: the tools' artwork is
    /// thin line drawing on 256-800 px canvases (18-53% of the pixels drawn, measured),
    /// made for a white background - shrunk to 20 px on the theme's background it all
    /// but vanished, and in Office's dark themes it did vanish. So every icon sits on
    /// its own light tile at 26 px, pre-scaled once in high quality, on a rail strip a
    /// step away from the pane's background. The open tool gets an accent border and an
    /// accent bar on the rail's inner edge; a tool in a floating window gets a dot.
    /// An item with no icon (an external web page, say) shows its first letter.
    /// </summary>
    internal sealed class RailButton : Control
    {
        const int TileLogical = 36, IconLogical = 26;

        Image _icon, _scaled;
        int _scaledFor;
        bool _active, _poppedOut, _hover;

        public RailButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public ToolEntry Entry { get; set; }

        public Image Icon
        {
            get => _icon;
            set { _icon = value; _scaled?.Dispose(); _scaled = null; Invalidate(); }
        }

        // Text drawn instead of an icon, without the tile (the "more" button's ⋯).
        public string Glyph { get; set; }

        public bool Active
        {
            get => _active;
            set { if (_active == value) return; _active = value; Invalidate(); }
        }

        public bool PoppedOut
        {
            get => _poppedOut;
            set { if (_poppedOut == value) return; _poppedOut = value; Invalidate(); }
        }

        /// <summary>The rail strip's colour: the pane background moved 7% toward the text colour.</summary>
        internal static Color StripColor(Color back, Color fore) => Blend(back, fore, 0.07);

        static bool IsDark(Color c) => c.R * 0.3 + c.G * 0.59 + c.B * 0.11 < 128;

        // Word's blue; a lighter one where the theme is dark.
        static Color Accent(Color back) => IsDark(back) ? Color.FromArgb(138, 180, 248) : Color.FromArgb(43, 87, 154);

        static Color Blend(Color a, Color b, double t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(StripColor(BackColor, ForeColor));

            var accent = Accent(BackColor);
            int tileSize = Math.Min(LogicalToDeviceUnits(TileLogical), Math.Min(Width, Height) - 2);
            var tile = new Rectangle((Width - tileSize) / 2, (Height - tileSize) / 2, tileSize, tileSize);

            if (Glyph != null)
            {
                if (_hover || _active)
                    using (var path = RoundRect(tile, LogicalToDeviceUnits(7)))
                    using (var fill = new SolidBrush(Color.FromArgb(_active ? 60 : 30, ForeColor)))
                        g.FillPath(fill, path);
                DrawText(g, Glyph, tile, ForeColor, 13f);
                return;
            }

            // The accent bar on the rail's inner edge (away from the pane's outer edge).
            if (_active)
            {
                int bar = LogicalToDeviceUnits(3);
                bool barOnRight = Parent == null || Parent.Dock != DockStyle.Right;
                var barRect = barOnRight
                    ? new Rectangle(Width - bar, tile.Top + 4, bar, tile.Height - 8)
                    : new Rectangle(0, tile.Top + 4, bar, tile.Height - 8);
                using (var b = new SolidBrush(accent))
                    g.FillRectangle(b, barRect);
            }

            using (var path = RoundRect(tile, LogicalToDeviceUnits(7)))
            {
                using (var fill = new SolidBrush(Color.FromArgb(252, 252, 252)))
                    g.FillPath(fill, path);

                Color border = _active ? accent
                    : _hover ? Color.FromArgb(160, ForeColor)
                    : Color.FromArgb(IsDark(BackColor) ? 0 : 60, ForeColor);
                float width = _active ? LogicalToDeviceUnits(2) : 1f;
                if (border.A > 0)
                    using (var pen = new Pen(border, width))
                        g.DrawPath(pen, path);
            }

            int iconSize = LogicalToDeviceUnits(IconLogical);
            var iconRect = new Rectangle(tile.Left + (tile.Width - iconSize) / 2, tile.Top + (tile.Height - iconSize) / 2, iconSize, iconSize);
            var scaled = Scaled(iconSize);
            if (scaled != null)
                g.DrawImageUnscaled(scaled, iconRect.Location);
            else
            {
                string text = Entry?.Label?.Invoke();
                string letter = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim().Substring(0, 1);
                DrawText(g, letter, tile, Color.FromArgb(40, 40, 40), 12f);
            }

            if (Focused && ShowFocusCues)
                using (var path = RoundRect(Rectangle.Inflate(tile, 2, 2), LogicalToDeviceUnits(8)))
                using (var pen = new Pen(accent) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);

            if (_poppedOut)
            {
                int dot = LogicalToDeviceUnits(8);
                var dotRect = new Rectangle(tile.Right - dot + 2, tile.Bottom - dot + 2, dot, dot);
                using (var b = new SolidBrush(accent))
                    g.FillEllipse(b, dotRect);
                using (var pen = new Pen(StripColor(BackColor, ForeColor), 1.5f))
                    g.DrawEllipse(pen, dotRect);
            }
        }

        // Scaled once per size, in high quality: drawing an 800 px image down to 26 px
        // on every paint is both slow and coarser than a single careful resample.
        Image Scaled(int size)
        {
            if (_icon == null) return null;
            if (_scaled != null && _scaledFor == size) return _scaled;
            _scaled?.Dispose();
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(_icon, new Rectangle(0, 0, size, size));
            }
            _scaled = bmp;
            _scaledFor = size;
            return _scaled;
        }

        void DrawText(Graphics g, string text, Rectangle r, Color color, float points)
        {
            using (var font = new Font("Segoe UI", points, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, r, fmt);
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icon?.Dispose();
                _scaled?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
