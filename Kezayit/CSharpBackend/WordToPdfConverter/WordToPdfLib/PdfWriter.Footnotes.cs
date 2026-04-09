using System.Linq;
using PdfSharp.Drawing;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private double MeasureFootnote(DocFootnote fn)
        {
            double h = 0;
            var font = GetFallbackFont(_opts.DefaultFontSize * 0.85f, false, false);
            foreach (var p in fn.Paragraphs)
            {
                var text  = string.Concat(p.Runs.Select(r => r.Text));
                var lines = WrapTextSimple(text, font, _totalContentWidth);
                h += lines.Count * font.GetHeight() * _opts.LineSpacing + _opts.ParagraphSpacing;
            }
            return h + 10;
        }

        private void FlushFootnotes()
        {
            if (_pageFootnotes.Count == 0 || _gfx == null) return;

            bool rtl    = _opts.DefaultRtl;
            double sepY = _page.Height.Point - _opts.MarginBottom - _footnoteAreaHeight - 8;
            double lx1  = rtl ? _opts.MarginLeft + _totalContentWidth - 60 : _opts.MarginLeft;
            double lx2  = rtl ? _opts.MarginLeft + _totalContentWidth      : _opts.MarginLeft + 60;
            _gfx.DrawLine(XPens.Black, lx1, sepY, lx2, sepY);

            double fnY  = sepY + 6;
            int counter = 1;
            var fnFont  = GetFallbackFont(_opts.DefaultFontSize * 0.85f, false, false);

            foreach (var fn in _pageFootnotes)
            {
                foreach (var para in fn.Paragraphs)
                {
                    var text = string.Concat(para.Runs.Select(r => r.Text));
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var lines = WrapTextSimple(text, fnFont, _totalContentWidth - 20);
                    bool first = true;
                    foreach (var line in lines)
                    {
                        string rendered = first ? $"{counter}. {line}" : $"   {line}";
                        if (rtl) rendered = ToVisualRtl(rendered);
                        var fmt = new XStringFormat
                        {
                            Alignment     = rtl ? XStringAlignment.Far : XStringAlignment.Near,
                            LineAlignment = XLineAlignment.Near
                        };
                        _gfx.DrawString(rendered, fnFont, XBrushes.Black,
                            new XRect(_opts.MarginLeft, fnY, _totalContentWidth, fnFont.GetHeight() * 2), fmt);
                        fnY  += fnFont.GetHeight() * _opts.LineSpacing;
                        first = false;
                    }
                    fnY += _opts.ParagraphSpacing;
                }
                counter++;
            }
        }
    }
}
