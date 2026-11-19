using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>Fluent look shared by the title strip, the menu and the pop-out window: glyphs, colours, the selector.</summary>
    public static class Fluent
    {
        // Segoe Fluent Icons (falls back to Segoe MDL2 Assets, same code points).
        public const char PopOut = '', PopIn = '', Chevron = '', NewPane = '';

        /// <summary>Hide the side pane (ChromeMinimize).</summary>
        public const char HidePane = '';

        public static readonly string IconFont = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
            .FirstOrDefault(n => { using (var f = new Font(n, 10)) return f.Name == n; }) ?? "Segoe UI Symbol";

        public static Color Blend(Color a, Color b, double t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        /// <summary>Office's own hover tint for its title-strip buttons (measured: E6 -> D2 on the light strip).</summary>
        public static Color Hover(Color back, Color ink) => Blend(back, ink, 0.105);

        public static bool IsDark(Color back) => back.GetBrightness() < 0.45f;

        /// <summary>Draws an icon-font glyph centred in <paramref name="r"/>.</summary>
        public static void Glyph(Graphics g, char glyph, Rectangle r, Color color, float px)
        {
            using (var f = new Font(IconFont, px, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, glyph.ToString(), f, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ---- the selector: icon + title + chevron, reading from the right in RTL ----

        static int Px(double logical, float scale) => (int)Math.Round(logical * scale);

        /// <summary>Width the selector needs, so it hugs its content.</summary>
        public static int SelectorWidth(string title, Image icon, float scale)
        {
            using (var f = new Font("Segoe UI", (float)Math.Round(14.5 * scale), GraphicsUnit.Pixel))
                return Px(8, scale) * 2 + (icon != null ? Px(18, scale) + Px(6, scale) : 0) + TextRenderer.MeasureText(title ?? "", f).Width + Px(6, scale) + Px(12, scale);
        }

        /// <summary>Paints the selector content into <paramref name="r"/>, centred vertically on <paramref name="line"/>.</summary>
        public static void PaintSelector(Graphics g, Rectangle r, string title, Image icon, Color ink, bool rtl, float scale, int line)
        {
            int pad = Px(8, scale), iconSize = Px(18, scale), gap = Px(6, scale), chev = Px(12, scale);
            int x = rtl ? r.Right - pad : r.Left + pad;
            int Next(int width) { int start = rtl ? x - width : x; x = rtl ? x - width - gap : x + width + gap; return start; }
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, new Rectangle(Next(iconSize), line - iconSize / 2, iconSize, iconSize));
            }
            using (var f = new Font("Segoe UI", (float)Math.Round(14.5 * scale), GraphicsUnit.Pixel))
            {
                int room = Math.Max(0, (rtl ? x - r.Left : r.Right - x) - pad - chev - gap);
                int tw = Math.Min(room, TextRenderer.MeasureText(title ?? "", f).Width);
                int th = Px(24, scale);
                TextRenderer.DrawText(g, title ?? "", f, new Rectangle(Next(tw), line - th / 2, tw, th), ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.RightToLeft : 0));
            }
            Glyph(g, Chevron, new Rectangle(Next(chev), line - chev / 2, chev, chev), ink, Px(10, scale));
        }
    }
}
