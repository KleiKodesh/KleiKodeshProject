using System.Collections.Generic;
using System.Linq;
using PdfSharp.Drawing;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private struct Segment { public string Word; public int RunIdx; }

        private void RenderParagraph(DocParagraph para, bool isFootnote, bool suppressSpaceAfter = false)
        {
            bool rtl = para.IsRtl || _opts.DefaultRtl;
            if (para.PageBreakBefore) NewPage();
            _cursorY += para.SpaceBefore;

            if (para.Runs.Count == 0)
            {
                var ef = GetFont(para, null, isFootnote);
                double emptyLineH = para.LineSpacingExact && para.LineSpacing.HasValue
                    ? para.LineSpacing.Value
                    : ef.GetHeight() * (para.LineSpacing ?? _opts.LineSpacing) * 0.5;
                _cursorY += emptyLineH;
                _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
                return;
            }

            var fullText = string.Concat(para.Runs.Select(r => r.Text));
            if (string.IsNullOrWhiteSpace(fullText))
            {
                _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
                return;
            }

            bool hasPrefix = !string.IsNullOrEmpty(para.ListPrefix);
            XFont prefixFont = hasPrefix ? GetFont(para, para.Runs.Count > 0 ? para.Runs[0] : null, isFootnote) : null;
            double prefixW = 0;
            if (hasPrefix)
                prefixW = para.HangingIndent > 0 ? para.HangingIndent : _gfx.MeasureString(para.ListPrefix + " ", prefixFont).Width;

            double textUsableW = rtl && hasPrefix
                ? _contentWidth - para.IndentLeft - para.IndentRight - prefixW
                : _contentWidth - para.IndentLeft - para.IndentRight;

            var segments    = BuildSegments(para);
            var wrappedLines = WrapSegments(segments, para, isFootnote, textUsableW);

            _log?.WriteLine($"[Render Para] col={_currentColumn}/{_columnCount} colLeft={_columnLeft:F1} textUsableW={textUsableW:F1} rtl={rtl} lines={wrappedLines.Count} prefix='{para.ListPrefix}'");

            bool firstLine = true;
            foreach (var lineSegs in wrappedLines)
            {
                if (lineSegs.Count == 0) { firstLine = false; continue; }

                var lineFonts = lineSegs.Select(s => GetFont(para, para.Runs[s.RunIdx], isFootnote)).ToList();
                double maxFontH = lineFonts.Max(f => f.GetHeight());
                // LineSpacing: if exact, use the stored point value directly; otherwise multiply by font height
                double lineH = para.LineSpacingExact && para.LineSpacing.HasValue
                    ? para.LineSpacing.Value
                    : maxFontH * (para.LineSpacing ?? _opts.LineSpacing);

                if (_cursorY + lineH > BottomLimit) OverflowToNext();
                double baseline = _cursorY + maxFontH;

                if (firstLine && hasPrefix)
                {
                    double prefixTop    = baseline - prefixFont.GetHeight();
                    double actualPrefixW = _gfx.MeasureString(para.ListPrefix, prefixFont).Width;
                    if (rtl)
                    {
                        double colLeft = _columnLeft + _contentWidth - para.IndentRight - prefixW;
                        double prefixX = colLeft + (prefixW - actualPrefixW) / 2;
                        _gfx.DrawString(para.ListPrefix, prefixFont, GetBrush(para.Runs.Count > 0 ? para.Runs[0] : null),
                            new XRect(prefixX, prefixTop, actualPrefixW + 2, prefixFont.GetHeight() * 2),
                            new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });
                        _log?.WriteLine($"  [Prefix RTL] x={prefixX:F1} actualW={actualPrefixW:F1}");
                    }
                    else
                    {
                        double prefixX = _columnLeft + para.IndentLeft - prefixW + (prefixW - actualPrefixW) / 2;
                        _gfx.DrawString(para.ListPrefix, prefixFont, GetBrush(para.Runs.Count > 0 ? para.Runs[0] : null),
                            new XRect(prefixX, prefixTop, actualPrefixW + 2, prefixFont.GetHeight() * 2),
                            new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });
                        _log?.WriteLine($"  [Prefix LTR] x={prefixX:F1} actualW={actualPrefixW:F1}");
                    }
                }

                double lineIndentLeft  = para.IndentLeft;
                double lineIndentRight = para.IndentRight + (rtl ? prefixW : 0);
                double lineUsableW     = _contentWidth - lineIndentLeft - lineIndentRight - (!rtl ? prefixW : 0);

                _log?.WriteLine($"  [Line {(firstLine ? "1" : "n")}] baseline={baseline:F1} lineIndL={lineIndentLeft:F1} lineIndR={lineIndentRight:F1} lineW={lineUsableW:F1} segs={lineSegs.Count}");
                DrawSegmentLine(lineSegs, lineFonts, para, rtl, baseline, lineUsableW, lineIndentLeft);
                _cursorY += lineH;
                firstLine = false;
            }

            _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
        }

        private static List<Segment> BuildSegments(DocParagraph para)
        {
            var result = new List<Segment>();
            for (int ri = 0; ri < para.Runs.Count; ri++)
                foreach (var w in (para.Runs[ri].Text ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                    result.Add(new Segment { Word = w, RunIdx = ri });
            return result;
        }

        private List<List<Segment>> WrapSegments(List<Segment> segments, DocParagraph para, bool isFootnote, double maxW)
        {
            var lines   = new List<List<Segment>>();
            var current = new List<Segment>();
            double currentW = 0;

            foreach (var seg in segments)
            {
                var font   = GetFont(para, para.Runs[seg.RunIdx], isFootnote);
                double sw  = _gfx.MeasureString(seg.Word, font).Width;
                double spW = _gfx.MeasureString(" ", font).Width;
                double needed = current.Count == 0 ? sw : spW + sw;

                if (current.Count > 0 && currentW + needed > maxW)
                {
                    lines.Add(current);
                    current  = new List<Segment>();
                    currentW = 0;
                    needed   = sw;
                }
                current.Add(seg);
                currentW += needed;
            }
            if (current.Count > 0) lines.Add(current);
            return lines;
        }
    }
}
