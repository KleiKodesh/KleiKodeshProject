using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A compact Outlook-style dropdown: header, rows with icon (image or Fluent glyph) and text, accent bar
    /// on the current row, square hover, separators. RTL draws the content mirrored; the window itself stays
    /// where it is put. Owned by the window that opened it; colours follow a title-bar background.
    /// </summary>
    public static class PaneMenu
    {
        public sealed class Row
        {
            public string Text; public Image Image; public char Glyph; public bool Current; public Action Click;
            public static Row Separator => new Row();
            internal bool IsSeparator => Text == null;
        }

        static readonly Font RowFont = new Font("Segoe UI", 9f);
        static readonly Font RowFontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font HeaderFont = new Font("Segoe UI Semibold", 8f);

        /// <summary>Shows the menu under <paramref name="anchor"/> (screen rect), aligned to its reading start.</summary>
        public static void Show(string header, IList<Row> rows, Rectangle anchor, IntPtr owner, Color titleBack, bool rtl, float scale)
        {
            bool dark = Fluent.IsDark(titleBack);
            var c = new Colors
            {
                Back = dark ? Color.FromArgb(41, 41, 41) : Color.White,
                Text = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(36, 36, 36),
                Header = dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(97, 97, 97),
                Hover = dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(240, 240, 240),
                Accent = dark ? Color.FromArgb(71, 158, 245) : Color.FromArgb(15, 108, 189),
                Border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(209, 209, 209),
                Scale = scale, Rtl = rtl,
            };
            int Px(int v) => (int)(v * scale);

            var menu = new ContextMenuStrip
            {
                RightToLeft = RightToLeft.No,                  // the window stays put; content is drawn RTL by the renderer
                ShowImageMargin = false, ShowCheckMargin = false, Renderer = new Renderer(c), BackColor = c.Back,
                DropShadowEnabled = true, Padding = new Padding(0, Px(2), 0, Px(2)), Font = RowFont,
            };
            if (header != null) menu.Items.Add(new ToolStripLabel(header) { AutoSize = false, Height = Px(22), Font = HeaderFont });
            foreach (var row in rows)
            {
                if (row.IsSeparator) { menu.Items.Add(new ToolStripSeparator { AutoSize = false, Height = Px(9) }); continue; }
                var r = row;
                var item = new ToolStripMenuItem(row.Text) { AutoSize = false, Height = Px(26), Checked = row.Current, Tag = (object)row.Image ?? (row.Glyph != '\0' ? (object)row.Glyph : null) };
                item.Click += (s, e) => r.Click?.Invoke();
                menu.Items.Add(item);
            }

            menu.HandleCreated += (s, e) =>
            {
                Native.SetWindowLongPtr(menu.Handle, Native.GWLP_HWNDPARENT, owner);
                int square = 1;                                              // DWMWCP_DONOTROUND: square, like Office's strip
                Native.DwmSetWindowAttribute(menu.Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref square, 4);
            };

            // Size to content (no unused shortcut column). Hooks are attached first: sizing may create the handle.
            int textWidth = 0;
            foreach (ToolStripItem i in menu.Items) textWidth = Math.Max(textWidth, TextRenderer.MeasureText(i.Text ?? "", RowFontBold).Width);
            int width = Math.Max(Px(10) * 2 + Px(16) + Px(8) + textWidth + Px(6), anchor.Width);
            int height = menu.Padding.Vertical + 2;
            foreach (ToolStripItem i in menu.Items) { i.Width = width; height += i.Height + i.Margin.Vertical; }
            menu.AutoSize = false;
            menu.Size = new Size(width + 2, height);
            if (menu.IsHandleCreated) Native.SetWindowLongPtr(menu.Handle, Native.GWLP_HWNDPARENT, owner);

            var work = Screen.FromRectangle(anchor).WorkingArea;
            int x = rtl ? anchor.Right - menu.Width : anchor.Left;
            x = Math.Max(work.Left, Math.Min(x, work.Right - menu.Width));
            int y = anchor.Bottom + 1 + menu.Height > work.Bottom ? anchor.Top - menu.Height : anchor.Bottom + 1;
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            menu.Show(new Point(x, y), ToolStripDropDownDirection.BelowRight);
        }

        sealed class Colors { public Color Back, Text, Header, Hover, Accent, Border; public float Scale; public bool Rtl; }

        sealed class Renderer : ToolStripRenderer
        {
            readonly Colors c;
            public Renderer(Colors colors) { c = colors; }
            int Px(int v) => (int)(v * c.Scale);

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { using (var p = new Pen(c.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
            protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
            { using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height); }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height);
                using (var p = new Pen(c.Border)) e.Graphics.DrawLine(p, Px(10), e.Item.Height / 2, e.Item.Width - Px(10), e.Item.Height / 2);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height);
                if (e.Item.Selected) using (var b = new SolidBrush(c.Hover)) e.Graphics.FillRectangle(b, Px(2), 1, e.Item.Width - Px(4), e.Item.Height - 2);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var g = e.Graphics; var item = e.Item;
                int w = item.Width, h = item.Height, pad = Px(10), icon = Px(16), gap = Px(8);
                var align = c.Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
                if (!(item is ToolStripMenuItem row))
                {
                    TextRenderer.DrawText(g, item.Text, item.Font, new Rectangle(pad, 0, w - 2 * pad, h), c.Header, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | align);
                    return;
                }
                if (row.Checked)
                    using (var b = new SolidBrush(c.Accent)) g.FillRectangle(b, c.Rtl ? w - Px(3) - Math.Max(2, Px(3)) : Px(3), (h - Px(14)) / 2, Math.Max(2, Px(3)), Px(14));
                var iconRect = new Rectangle(c.Rtl ? w - pad - icon : pad, (h - icon) / 2, icon, icon);
                if (item.Tag is Image img) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, iconRect); }
                else if (item.Tag is char glyph) Fluent.Glyph(g, glyph, iconRect, c.Text, 12 * c.Scale);
                var textRect = new Rectangle(c.Rtl ? pad : pad + icon + gap, 0, w - 2 * pad - icon - gap, h);
                TextRenderer.DrawText(g, item.Text, row.Checked ? RowFontBold : RowFont, textRect, c.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | align);
            }
        }
    }
}
