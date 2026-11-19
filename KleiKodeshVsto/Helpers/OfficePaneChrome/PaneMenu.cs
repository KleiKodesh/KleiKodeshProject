using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A dropdown in the look of Word's own small-icon ribbon menus (measured from Word's Share and Paste menus):
    /// square window with a 1px border, an optional shaded header band with bold text, 24 dip rows with a 16 dip
    /// icon (image or Fluent glyph) and the text, a full-row hover fill, bare 1px separators. The current row's
    /// icon sits in a framed box, as Office marks a checked menu item. RTL draws the content mirrored; the
    /// window itself stays where it is put. Owned by the window that opened it; colours follow Office's light
    /// or dark menu palette, chosen from a title-bar background.
    /// </summary>
    public static class PaneMenu
    {
        public sealed class Row
        {
            public string Text; public Image Image; public char Glyph; public bool Current; public Action Click;
            /// <summary>Tooltip title (default: the text) and description, as the ribbon's screentip / supertip.</summary>
            public string Tip, SuperTip;
            public static Row Separator => new Row();
            internal bool IsSeparator => Text == null;
        }

        static readonly Font RowFont = new Font("Segoe UI", 9f);
        static readonly Font HeaderFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        /// <summary>A menu's colours. <see cref="Light"/> / <see cref="Dark"/>: Word's own menus in its Colorful / Black themes (measured).</summary>
        public sealed class MenuPalette
        {
            public Color Back, Text, Hover, Border, Separator, HeaderBack, HeaderText;
            static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            public MenuPalette(int back, int text, int hover, int border, int separator, int headerBack, int headerText)
            { Back = C(back); Text = C(text); Hover = C(hover); Border = C(border); Separator = C(separator); HeaderBack = C(headerBack); HeaderText = C(headerText); }
            public static readonly MenuPalette Light = new MenuPalette(0xFFFFFF, 0x262626, 0xD2D2D2, 0x949494, 0xE6E6E6, 0xEBEBEB, 0x6A6A6A);
            public static readonly MenuPalette Dark = new MenuPalette(0x363636, 0xEBEBEB, 0x505050, 0x808080, 0x666666, 0x4A4A4A, 0xEBEBEB);
        }

        /// <summary>The host's menu colours (e.g. by Office theme); null = <see cref="MenuPalette.Light"/> or <see cref="MenuPalette.Dark"/> by the title-bar background.</summary>
        public static Func<MenuPalette> Palette = () => null;

        // Office's menu geometry in dip: row height, icon size, icon inset from the row edge, text start, text end
        // padding, header band height, header text inset.
        const int RowDip = 24, IconDip = 16, IconInsetDip = 4, TextStartDip = 32, TextEndDip = 20, HeaderDip = 21, HeaderInsetDip = 9;

        /// <summary>Shows the menu under <paramref name="anchor"/> (screen rect), aligned to its reading start.</summary>
        public static void Show(string header, IList<Row> rows, Rectangle anchor, IntPtr owner, Color titleBack, bool rtl, float scale, Action closed = null)
        {
            MenuPalette p = null;
            try { p = Palette(); } catch { }
            if (p == null) p = Fluent.IsDark(titleBack) ? MenuPalette.Dark : MenuPalette.Light;
            var c = new Colors
            {
                Back = p.Back, Text = p.Text, Hover = p.Hover, Border = p.Border, Separator = p.Separator,
                HeaderBack = p.HeaderBack, HeaderText = p.HeaderText, Scale = scale, Rtl = rtl,
            };
            int Px(int v) => (int)Math.Round(v * scale);

            ChromeToolTip tip = null;
            var menu = new ContextMenuStrip
            {
                RightToLeft = RightToLeft.No,                  // the window stays put; content is drawn RTL by the renderer
                ShowImageMargin = false, ShowCheckMargin = false, Renderer = new Renderer(c), BackColor = c.Back,
                DropShadowEnabled = true, Padding = new Padding(1), Font = RowFont,
            };
            if (!string.IsNullOrEmpty(header))
            {
                if (!header.EndsWith(":")) header += ":";                    // as Office's menu headers
                menu.Items.Add(new ToolStripLabel(header) { AutoSize = false, Height = Px(HeaderDip), Font = HeaderFont, Margin = Padding.Empty });
            }
            foreach (var row in rows)
            {
                if (row.IsSeparator) { menu.Items.Add(new ToolStripSeparator { AutoSize = false, Height = 1, Margin = Padding.Empty }); continue; }   // one device pixel, as Office
                var r = row;
                var item = new ToolStripMenuItem(row.Text) { AutoSize = false, Height = Px(RowDip), Margin = Padding.Empty, Checked = row.Current, Tag = (object)row.Image ?? (row.Glyph != '\0' ? (object)row.Glyph : null) };
                item.Click += (s, e) => r.Click?.Invoke();
                if (!string.IsNullOrWhiteSpace(r.SuperTip) || !string.IsNullOrWhiteSpace(r.Tip))
                {
                    // Beside the menu, at the hovered row (Office shows a menu item's supertip there too).
                    item.MouseEnter += (s, e) =>
                    {
                        if (Native.FromTouchOrPen()) return;                   // touch: no hover tooltip
                        if (tip == null || tip.IsDisposed) tip = new ChromeToolTip();
                        var at = menu.RectangleToScreen(item.Bounds);
                        tip.Request(item, string.IsNullOrWhiteSpace(r.Tip) ? r.Text : r.Tip, at, at.Top, titleBack, c.Text, rtl, scale, r.SuperTip, menu.Bounds);
                    };
                    item.MouseLeave += (s, e) => tip?.Request(null, null, Rectangle.Empty, 0, titleBack, c.Text, rtl, scale);
                }
                menu.Items.Add(item);
            }

            menu.HandleCreated += (s, e) =>
            {
                Native.SetWindowLongPtr(menu.Handle, Native.GWLP_HWNDPARENT, owner);
                int square = 1;                                              // DWMWCP_DONOTROUND: square, like Office's menus
                Native.DwmSetWindowAttribute(menu.Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref square, 4);
            };

            // Size to content (no unused shortcut column). Hooks are attached first: sizing may create the handle.
            int content = 0;
            foreach (ToolStripItem i in menu.Items)
                content = Math.Max(content, i is ToolStripLabel
                    ? TextRenderer.MeasureText(i.Text ?? "", HeaderFont).Width + 2 * Px(HeaderInsetDip)
                    : TextRenderer.MeasureText(i.Text ?? "", RowFont).Width + Px(TextStartDip) + Px(TextEndDip));
            int width = Math.Max(content, anchor.Width - menu.Padding.Horizontal - 2);
            int height = menu.Padding.Vertical + 2;
            foreach (ToolStripItem i in menu.Items) { i.Width = width; height += i.Height; }
            menu.AutoSize = false;
            menu.Size = new Size(width + menu.Padding.Horizontal + 2, height);
            if (menu.IsHandleCreated) Native.SetWindowLongPtr(menu.Handle, Native.GWLP_HWNDPARENT, owner);

            var work = Screen.FromRectangle(anchor).WorkingArea;
            int x = rtl ? anchor.Right - menu.Width : anchor.Left;
            x = Math.Max(work.Left, Math.Min(x, work.Right - menu.Width));
            int y = anchor.Bottom + 1 + menu.Height > work.Bottom ? anchor.Top - menu.Height : anchor.Bottom + 1;
            menu.KeyDown += (s, e) => tip?.Reset();                         // keyboard moves the highlight: the mouse tip goes
            menu.Closed += (s, e) =>
            {
                tip?.Dispose();
                try { closed?.Invoke(); } catch { }
                menu.BeginInvoke((Action)menu.Dispose);
            };
            menu.Show(new Point(x, y), ToolStripDropDownDirection.BelowRight);
        }

        sealed class Colors { public Color Back, Text, HeaderBack, HeaderText, Hover, Separator, Border; public float Scale; public bool Rtl; }

        sealed class Renderer : ToolStripRenderer
        {
            readonly Colors c;
            public Renderer(Colors colors) { c = colors; }
            int Px(int v) => (int)Math.Round(v * c.Scale);

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { using (var b = new SolidBrush(c.Back)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { using (var p = new Pen(c.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
            protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
            { using (var b = new SolidBrush(c.HeaderBack)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height); }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            { using (var b = new SolidBrush(c.Separator)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height); }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            { using (var b = new SolidBrush(e.Item.Selected ? c.Hover : c.Back)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height); }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var g = e.Graphics; var item = e.Item;
                int w = item.Width, h = item.Height;
                var align = c.Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
                if (!(item is ToolStripMenuItem row))
                {
                    int inset = Px(HeaderInsetDip);
                    TextRenderer.DrawText(g, item.Text, HeaderFont, new Rectangle(inset, 0, w - 2 * inset, h), c.HeaderText,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | align);
                    return;
                }
                int icon = Px(IconDip), iconInset = Px(IconInsetDip);
                var iconRect = new Rectangle(c.Rtl ? w - iconInset - icon : iconInset, (h - icon) / 2, icon, icon);
                if (row.Checked)
                {
                    // Office marks a checked item by framing its icon.
                    var box = Rectangle.Inflate(iconRect, Px(2), Px(2));
                    using (var b = new SolidBrush(c.Hover)) g.FillRectangle(b, box);
                    using (var p = new Pen(c.Border)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
                }
                if (item.Tag is Image img) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, iconRect); }
                else if (item.Tag is char glyph) Fluent.Glyph(g, glyph, iconRect, c.Text, 12 * c.Scale);
                int start = Px(TextStartDip), end = Px(TextEndDip);
                var textRect = new Rectangle(c.Rtl ? end : start, 0, w - start - end, h);
                TextRenderer.DrawText(g, item.Text, RowFont, textRect, c.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | align);
            }
        }
    }
}
