using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace WordToPdfLib
{
    internal class PdfWriter
    {
        private readonly ConversionOptions _opts;
        private PdfDocument _pdf;
        private PdfPage _page;
        private XGraphics _gfx;

        private double _contentWidth;
        private double _cursorY;

        private readonly List<DocFootnote> _pageFootnotes = new List<DocFootnote>();
        private double _footnoteAreaHeight;

        private readonly Dictionary<string, XFont> _fontCache = new Dictionary<string, XFont>(StringComparer.OrdinalIgnoreCase);
        private TextWriter _log;

        public PdfWriter(ConversionOptions opts) { _opts = opts; }

        public void Write(DocContent content, string outputPath, TextWriter log = null)
        {
            _log = log;
            _pdf = new PdfDocument();
            NewPage();

            var paras = content.Paragraphs;
            for (int i = 0; i < paras.Count; i++)
            {
                var para = paras[i];
                if (para.FootnoteId != null && content.Footnotes.TryGetValue(para.FootnoteId, out var fn))
                {
                    _pageFootnotes.Add(fn);
                    _footnoteAreaHeight += MeasureFootnote(fn) + 4;
                }

                // contextualSpacing: suppress spaceAfter if next para also has contextualSpacing
                bool suppressSpaceAfter = para.ContextualSpacing
                    && i + 1 < paras.Count
                    && paras[i + 1].ContextualSpacing;

                RenderParagraph(para, isFootnote: false, suppressSpaceAfter: suppressSpaceAfter);
            }

            FlushFootnotes();
            _pdf.Save(outputPath);
        }

        // ── Page ──────────────────────────────────────────────────────────────

        private void NewPage()
        {
            FlushFootnotes();
            _page = _pdf.AddPage();
            _page.Size    = PdfSharp.PageSize.A4;
            _gfx          = XGraphics.FromPdfPage(_page);
            _contentWidth = _page.Width.Point - _opts.MarginLeft - _opts.MarginRight;
            _cursorY      = _opts.MarginTop;
            _pageFootnotes.Clear();
            _footnoteAreaHeight = 0;
        }

        private double BottomLimit =>
            _page.Height.Point - _opts.MarginBottom - _footnoteAreaHeight - (_pageFootnotes.Count > 0 ? 12 : 0);

        // ── Paragraph ─────────────────────────────────────────────────────────

        // A segment is a word (or punctuation token) with its associated run index.
        private struct Segment
        {
            public string Word;
            public int RunIdx;
        }

        private void RenderParagraph(DocParagraph para, bool isFootnote, bool suppressSpaceAfter = false)
        {
            bool rtl = para.IsRtl || _opts.DefaultRtl;

            if (para.PageBreakBefore) NewPage();
            _cursorY += para.SpaceBefore;

            if (para.Runs.Count == 0)
            {
                var ef = GetFont(para, null, isFootnote);
                _cursorY += ef.GetHeight() * (para.LineSpacing ?? _opts.LineSpacing) * 0.5;
                _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
                return;
            }

            var fullText = string.Concat(para.Runs.Select(r => r.Text));
            if (string.IsNullOrWhiteSpace(fullText))
            {
                _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
                return;
            }

            // List prefix: use HangingIndent from numbering definition as the column width.
            // IndentLeft already includes the full indent; text starts at IndentLeft,
            // prefix hangs in [IndentLeft - HangingIndent .. IndentLeft].
            bool hasPrefix = !string.IsNullOrEmpty(para.ListPrefix);
            XFont prefixFont = hasPrefix ? GetFont(para, para.Runs.Count > 0 ? para.Runs[0] : null, isFootnote) : null;
            // prefixW = hanging indent from numbering (e.g. 18pt), or measure if not set
            double prefixW = 0;
            if (hasPrefix)
            {
                prefixW = para.HangingIndent > 0
                    ? para.HangingIndent
                    : _gfx.MeasureString(para.ListPrefix + " ", prefixFont).Width;
            }

            // Text area: IndentLeft already accounts for the full indent including hanging.
            // For RTL: text is right-aligned within [marginLeft, marginLeft + contentWidth - IndentRight - prefixW]
            //          prefix hangs in [contentWidth - IndentRight - prefixW .. contentWidth - IndentRight]
            // For LTR: text starts at [marginLeft + IndentLeft]
            //          prefix hangs in [marginLeft + IndentLeft - prefixW .. marginLeft + IndentLeft]
            double textIndentLeft  = para.IndentLeft  + (!rtl ? 0 : 0); // IndentLeft is already the text start
            double textIndentRight = para.IndentRight + ( rtl ? prefixW : 0);
            double textUsableW     = _contentWidth - textIndentLeft - textIndentRight - (!rtl ? prefixW : 0);
            // For LTR with prefix: text area is [IndentLeft .. contentWidth - IndentRight], prefix is left of IndentLeft
            // Actually for LTR: textUsableW = contentWidth - IndentLeft - IndentRight (prefix is outside, to the left)
            // For RTL: textUsableW = contentWidth - IndentLeft - IndentRight - prefixW (prefix column on right)
            if (!rtl && hasPrefix)
                textUsableW = _contentWidth - para.IndentLeft - para.IndentRight;
            else if (rtl && hasPrefix)
                textUsableW = _contentWidth - para.IndentLeft - para.IndentRight - prefixW;
            else
                textUsableW = _contentWidth - para.IndentLeft - para.IndentRight;

            double wrapW = textUsableW;

            var segments = BuildSegments(para);
            var wrappedLines = WrapSegmentsWithFirstWidth(segments, para, isFootnote, wrapW, wrapW);

            _log?.WriteLine($"[Render Para] prefix='{para.ListPrefix}' prefixW={prefixW:F1} indL={para.IndentLeft:F1} indR={para.IndentRight:F1} hang={para.HangingIndent:F1} textUsableW={textUsableW:F1} rtl={rtl} lines={wrappedLines.Count}");

            bool firstLine = true;
            foreach (var lineSegs in wrappedLines)
            {
                if (lineSegs.Count == 0) { firstLine = false; continue; }

                var lineFonts = lineSegs.Select(s => GetFont(para, para.Runs[s.RunIdx], isFootnote)).ToList();
                double maxFontH = lineFonts.Max(f => f.GetHeight());
                double lineH    = maxFontH * (para.LineSpacing ?? _opts.LineSpacing);

                if (_cursorY + lineH > BottomLimit) NewPage();

                double baseline = _cursorY + maxFontH;

                // Draw the list prefix on the first line in its reserved column.
                // Always use Near alignment with explicit X positioning to prevent
                // PdfSharp from reordering LTR characters (digits/dots) in RTL context.
                if (firstLine && hasPrefix)
                {
                    double prefixTop = baseline - prefixFont.GetHeight();
                    double actualPrefixW = _gfx.MeasureString(para.ListPrefix, prefixFont).Width;
                    if (rtl)
                    {
                        // Center the prefix in its column between text area right edge and right indent
                        double colLeft  = _opts.MarginLeft + _contentWidth - para.IndentRight - prefixW;
                        double prefixX  = colLeft + (prefixW - actualPrefixW) / 2;
                        _gfx.DrawString(para.ListPrefix, prefixFont,
                            GetBrush(para.Runs.Count > 0 ? para.Runs[0] : null),
                            new XRect(prefixX, prefixTop, actualPrefixW + 2, prefixFont.GetHeight() * 2),
                            new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });
                        _log?.WriteLine($"  [Prefix RTL] x={prefixX:F1} actualW={actualPrefixW:F1}");
                    }
                    else
                    {
                        double colRight = _opts.MarginLeft + para.IndentLeft;
                        double prefixX  = colRight - prefixW + (prefixW - actualPrefixW) / 2;
                        _gfx.DrawString(para.ListPrefix, prefixFont,
                            GetBrush(para.Runs.Count > 0 ? para.Runs[0] : null),
                            new XRect(prefixX, prefixTop, actualPrefixW + 2, prefixFont.GetHeight() * 2),
                            new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });
                        _log?.WriteLine($"  [Prefix LTR] x={prefixX:F1} actualW={actualPrefixW:F1}");
                    }
                }

                // Text area — same usable width for all lines
                double lineIndentLeft  = para.IndentLeft;
                double lineIndentRight = para.IndentRight + (rtl ? prefixW : 0);
                double lineUsableW     = _contentWidth - lineIndentLeft - lineIndentRight - (!rtl ? prefixW : 0);

                _log?.WriteLine($"  [Line {(firstLine?"1":"n")}] baseline={baseline:F1} lineIndL={lineIndentLeft:F1} lineIndR={lineIndentRight:F1} lineW={lineUsableW:F1} segs={lineSegs.Count}");
                DrawSegmentLine(lineSegs, lineFonts, para, rtl, baseline, lineUsableW, lineIndentLeft);
                _cursorY += lineH;
                firstLine = false;
            }

            _cursorY += suppressSpaceAfter ? 0 : (para.SpaceAfter > 0 ? para.SpaceAfter : _opts.ParagraphSpacing);
        }

        // Split all runs into word-level segments preserving run index.
        // Empty segments (from split boundaries) are dropped here.
        private static List<Segment> BuildSegments(DocParagraph para)
        {
            var result = new List<Segment>();
            for (int ri = 0; ri < para.Runs.Count; ri++)
            {
                var text = para.Runs[ri].Text ?? "";
                var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var w in words)
                    result.Add(new Segment { Word = w, RunIdx = ri });
            }
            return result;
        }

        // Wrap segments into lines. First line may have a narrower width (for list prefix).
        private List<List<Segment>> WrapSegmentsWithFirstWidth(
            List<Segment> segments, DocParagraph para, bool isFootnote,
            double firstLineMaxW, double restMaxW)
        {
            var lines   = new List<List<Segment>>();
            var current = new List<Segment>();
            double currentW = 0;
            double maxW     = firstLineMaxW;

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
                    maxW     = restMaxW;
                }

                current.Add(seg);
                currentW += needed;
            }

            if (current.Count > 0) lines.Add(current);
            return lines;
        }

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

            double lineX = _opts.MarginLeft + indentLeft;

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
                // Superscript: raise by ~40% of normal line height; Subscript: lower by ~20%
                double top = baseline - font.GetHeight();
                if (run?.Superscript == true) top -= font.GetHeight() * 0.4;
                if (run?.Subscript   == true) top += font.GetHeight() * 0.2;

                _gfx.DrawString(word, font, GetBrush(run),
                    new XRect(cx, top, ww + 2, font.GetHeight() * 2),
                    new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near });

                if (run?.Underline == true)
                    _gfx.DrawLine(new XPen(XColors.Black, 0.5),
                        cx, top + font.GetHeight() - 1, cx + ww, top + font.GetHeight() - 1);

                if (run?.Strikethrough == true)
                    _gfx.DrawLine(new XPen(XColors.Black, 0.5),
                        cx, top + font.GetHeight() * 0.55, cx + ww, top + font.GetHeight() * 0.55);

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

        // ── Footnotes ─────────────────────────────────────────────────────────

        private double MeasureFootnote(DocFootnote fn)
        {
            double h = 0;
            var font = GetFallbackFont(_opts.DefaultFontSize * 0.85f, false, false);
            foreach (var p in fn.Paragraphs)
            {
                var text  = string.Concat(p.Runs.Select(r => r.Text));
                var lines = WrapTextSimple(text, font, _contentWidth);
                h += lines.Count * font.GetHeight() * _opts.LineSpacing + _opts.ParagraphSpacing;
            }
            return h + 10;
        }

        private void FlushFootnotes()
        {
            if (_pageFootnotes.Count == 0 || _gfx == null) return;

            bool rtl    = _opts.DefaultRtl;
            double sepY = _page.Height.Point - _opts.MarginBottom - _footnoteAreaHeight - 8;
            double lx1  = rtl ? _opts.MarginLeft + _contentWidth - 60 : _opts.MarginLeft;
            double lx2  = rtl ? _opts.MarginLeft + _contentWidth      : _opts.MarginLeft + 60;
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

                    var lines = WrapTextSimple(text, fnFont, _contentWidth - 20);
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
                            new XRect(_opts.MarginLeft, fnY, _contentWidth, fnFont.GetHeight() * 2), fmt);
                        fnY  += fnFont.GetHeight() * _opts.LineSpacing;
                        first = false;
                    }
                    fnY += _opts.ParagraphSpacing;
                }
                counter++;
            }
        }

        // ── Fonts ─────────────────────────────────────────────────────────────

        private XFont GetFont(DocParagraph para, TextRun run, bool isFootnote)
        {
            float size = run?.FontSize ?? _opts.DefaultFontSize;
            if (isFootnote) size = run?.FontSize ?? _opts.DefaultFontSize * 0.85f;

            // Fallback heading sizes only if run has no explicit size from style
            if (run?.FontSize == null)
            {
                switch (para?.Type)
                {
                    case ParagraphType.Heading1: size = 20f; break;
                    case ParagraphType.Heading2: size = 16f; break;
                    case ParagraphType.Heading3: size = 13f; break;
                }
            }

            // Superscript/subscript: reduce to 65% of resolved size
            if (run?.Superscript == true || run?.Subscript == true)
                size *= 0.65f;

            // Bold: from run explicitly, or from heading type (headings are bold by convention)
            bool bold   = (run?.Bold ?? false) || (para?.Type != ParagraphType.Normal);
            bool italic = run?.Italic ?? false;

            return GetFallbackFont(size, bold, italic, run?.FontName);
        }

        private XFont GetFallbackFont(float ptSize, bool bold, bool italic, string familyName = null)
        {
            var style = bold && italic ? XFontStyleEx.BoldItalic
                      : bold           ? XFontStyleEx.Bold
                      : italic         ? XFontStyleEx.Italic
                                       : XFontStyleEx.Regular;

            var key = $"{familyName ?? ""}|{bold}|{italic}|{ptSize:F1}";
            if (_fontCache.TryGetValue(key, out var cached)) return cached;

            var enc = new XPdfFontOptions(PdfFontEncoding.Unicode);

            if (!string.IsNullOrEmpty(familyName))
            {
                try { var f = new XFont(familyName, ptSize, style, enc); _fontCache[key] = f; return f; }
                catch { }
            }

            foreach (var name in new[] { _opts.DefaultFontName, "Arial", "Tahoma", "Microsoft Sans Serif" })
            {
                try { var f = new XFont(name, ptSize, style, enc); _fontCache[key] = f; return f; }
                catch { }
            }

            var fallback = new XFont("Arial", ptSize, XFontStyleEx.Regular, enc);
            _fontCache[key] = fallback;
            return fallback;
        }

        // ── Text helpers ──────────────────────────────────────────────────────

        // Simple single-font word-wrap (used for footnotes)
        private List<string> WrapTextSimple(string text, XFont font, double maxWidth)
        {
            var lines   = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var words   = text.Split(' ');
            var current = string.Empty;

            foreach (var word in words)
            {
                var test = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (_gfx.MeasureString(test, font).Width > maxWidth && !string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                    current = word;
                }
                else current = test;
            }

            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            return lines;
        }

        private static string ToVisualRtl(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (string.IsNullOrEmpty(w)) continue;
                if (w.Any(IsRtlChar))
                {
                    words[i] = new string(w.ToCharArray().Reverse().ToArray());
                    words[i] = MirrorPunctuation(words[i]);
                }
            }
            Array.Reverse(words);
            return string.Join(" ", words);
        }

        private static string MirrorPunctuation(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                switch (chars[i])
                {
                    case '(': chars[i] = ')'; break;
                    case ')': chars[i] = '('; break;
                    case '[': chars[i] = ']'; break;
                    case ']': chars[i] = '['; break;
                    case '{': chars[i] = '}'; break;
                    case '}': chars[i] = '{'; break;
                    case '<': chars[i] = '>'; break;
                    case '>': chars[i] = '<'; break;
                    case '\u201C': chars[i] = '\u201D'; break;
                    case '\u201D': chars[i] = '\u201C'; break;
                    case '\u2018': chars[i] = '\u2019'; break;
                    case '\u2019': chars[i] = '\u2018'; break;
                }
            }
            return new string(chars);
        }

        private static bool IsRtlChar(char c) =>
            (c >= '\u0590' && c <= '\u05FF') ||
            (c >= '\u0600' && c <= '\u06FF');
    }
}
