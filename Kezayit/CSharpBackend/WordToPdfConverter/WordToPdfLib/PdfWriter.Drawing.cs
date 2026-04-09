using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Drawing;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private void DrawSegmentLine(
            List<Segment> segs, List<XFont> fonts, DocParagraph para, bool rtl,
            double baseline, double usableW, double indentLeft)
        {
            var words     = segs.Select(s => s.Word).ToList();
            var segsCopy  = segs.ToList();
            var fontsCopy = fonts.ToList();

            if (rtl)
            {
                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    if (w.Any(IsRtlChar))
                    {
                        w = new string(w.ToCharArray().Reverse().ToArray());
                        w = MirrorPunctuation(w);
                        words[i] = w;
                    }
                }
                words.Reverse();
                segsCopy.Reverse();
                fontsCopy.Reverse();
            }

            XStringAlignment xAlign;
            switch (para?.Alignment)
            {
                case ParagraphAlignment.Center: xAlign = XStringAlignment.Center; break;
                case ParagraphAlignment.Right:  xAlign = XStringAlignment.Far;    break;
                case ParagraphAlignment.Left:   xAlign = XStringAlignment.Near;   break;
                default: xAlign = rtl ? XStringAlignment.Far : XStringAlignment.Near; break;
            }

            double lineX  = _columnLeft + indentLeft;
            double totalW = 0;
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) totalW += _gfx.MeasureString(" ", fontsCopy[i]).Width;
                totalW += _gfx.MeasureString(words[i], fontsCopy[i]).Width;
            }

            double startX;
            switch (xAlign)
            {
                case XStringAlignment.Center: startX = lineX + (usableW - totalW) / 2; break;
                case XStringAlignment.Far:    startX = lineX + usableW - totalW;        break;
                default:                      startX = lineX;                            break;
            }

            double cx = startX;
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) cx += _gfx.MeasureString(" ", fontsCopy[i]).Width;

                var run  = para.Runs[segsCopy[i].RunIdx];
                var font = fontsCopy[i];
                var word = words[i];
                double ww  = _gfx.MeasureString(word, font).Width;

                double top = baseline - font.GetHeight();
                if      (run?.Superscript    == true)  top -= font.GetHeight() * 0.4;
                else if (run?.Subscript      == true)  top += font.GetHeight() * 0.2;
                else if (run?.VerticalOffset != null)  top -= run.VerticalOffset.Value;

                if (!string.IsNullOrEmpty(run?.Highlight))
                {
                    var hlBrush = GetHighlightBrush(run.Highlight);
                    if (hlBrush != null)
                        _gfx.DrawRectangle(hlBrush, new XRect(cx, top, ww, font.GetHeight()));
                }

                _gfx.DrawString(word, font, GetBrush(run),
                    new XRect(cx, top, ww + 2, font.GetHeight() * 2),
                    new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });

                if (run?.Underline == true)
                    _gfx.DrawLine(new XPen(XColors.Black, 0.5), cx, top + font.GetHeight() - 1, cx + ww, top + font.GetHeight() - 1);
                if (run?.Strikethrough == true)
                    _gfx.DrawLine(new XPen(XColors.Black, 0.5), cx, top + font.GetHeight() * 0.55, cx + ww, top + font.GetHeight() * 0.55);

                cx += ww;
            }
        }

        private static XBrush GetBrush(TextRun run)
        {
            if (!string.IsNullOrEmpty(run?.Color) && run.Color != "auto" && run.Color.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(run.Color.Substring(0, 2), 16);
                    int g = Convert.ToInt32(run.Color.Substring(2, 2), 16);
                    int b = Convert.ToInt32(run.Color.Substring(4, 2), 16);
                    return new XSolidBrush(XColor.FromArgb(r, g, b));
                }
                catch { }
            }
            return XBrushes.Black;
        }

        private static XBrush GetHighlightBrush(string name)
        {
            switch (name?.ToLower())
            {
                case "yellow":      return new XSolidBrush(XColor.FromArgb(255, 255,   0));
                case "green":       return new XSolidBrush(XColor.FromArgb(  0, 255,   0));
                case "cyan":        return new XSolidBrush(XColor.FromArgb(  0, 255, 255));
                case "magenta":     return new XSolidBrush(XColor.FromArgb(255,   0, 255));
                case "blue":        return new XSolidBrush(XColor.FromArgb(  0,   0, 255));
                case "red":         return new XSolidBrush(XColor.FromArgb(255,   0,   0));
                case "darkblue":    return new XSolidBrush(XColor.FromArgb(  0,   0, 139));
                case "darkcyan":    return new XSolidBrush(XColor.FromArgb(  0, 139, 139));
                case "darkgreen":   return new XSolidBrush(XColor.FromArgb(  0, 100,   0));
                case "darkmagenta": return new XSolidBrush(XColor.FromArgb(139,   0, 139));
                case "darkred":     return new XSolidBrush(XColor.FromArgb(139,   0,   0));
                case "darkyellow":  return new XSolidBrush(XColor.FromArgb(139, 139,   0));
                case "darkgray":    return new XSolidBrush(XColor.FromArgb(169, 169, 169));
                case "lightgray":   return new XSolidBrush(XColor.FromArgb(211, 211, 211));
                default:            return null;
            }
        }
    }
}
