using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private readonly ConversionOptions _opts;
        private PdfDocument _pdf;
        private PdfPage _page;
        private XGraphics _gfx;
        private double _contentWidth;   // width of one column
        private double _cursorY;
        private double _columnLeft;     // X start of current column
        private int    _currentColumn;  // 0-based
        private int    _columnCount;
        private double _columnGap;
        private double _totalContentWidth;
        private double _columnTop;      // Y where current section's columns start
        private readonly List<DocFootnote> _pageFootnotes = new List<DocFootnote>();
        private double _footnoteAreaHeight;
        private readonly Dictionary<string, XFont> _fontCache = new Dictionary<string, XFont>(StringComparer.OrdinalIgnoreCase);
        private TextWriter _log;

        public PdfWriter(ConversionOptions opts) { _opts = opts; }

        public void Write(DocContent content, string outputPath, TextWriter log = null)
        {
            _log = log;
            _pdf = new PdfDocument();
            _columnCount = (int)content.PageLayout.ColumnCount;
            _columnGap   = content.PageLayout.ColumnGap;
            NewPage(content.PageLayout);

            var paras = content.Paragraphs;
            for (int i = 0; i < paras.Count; i++)
            {
                var para = paras[i];
                if (para.FootnoteId != null && content.Footnotes.TryGetValue(para.FootnoteId, out var fn))
                {
                    _pageFootnotes.Add(fn);
                    _footnoteAreaHeight += MeasureFootnote(fn) + 4;
                }
                bool suppressSpaceAfter = para.ContextualSpacing
                    && i + 1 < paras.Count
                    && paras[i + 1].ContextualSpacing;
                RenderParagraph(para, isFootnote: false, suppressSpaceAfter: suppressSpaceAfter);

                // Handle section break — switch to next section's column layout
                if (para.IsSectionBreak)
                {
                    _columnCount   = content.FinalColumnCount;
                    _columnGap     = content.FinalColumnGap;
                    _currentColumn = 0;
                    _columnTop     = _cursorY;  // columns start here, not at page top
                    RecalcColumnWidth();
                }
            }

            FlushFootnotes();
            _pdf.Save(outputPath);
        }

        private void RecalcColumnWidth()
        {
            _totalContentWidth = _page.Width.Point - _opts.MarginLeft - _opts.MarginRight;
            _contentWidth = _columnCount > 1
                ? (_totalContentWidth - (_columnCount - 1) * _columnGap) / _columnCount
                : _totalContentWidth;
            // For RTL: column 0 is rightmost
            UpdateColumnLeft();
        }

        private void UpdateColumnLeft()
        {
            if (_opts.DefaultRtl)
                // RTL: column 0 = rightmost, column N-1 = leftmost
                _columnLeft = _opts.MarginLeft + (_columnCount - 1 - _currentColumn) * (_contentWidth + _columnGap);
            else
                _columnLeft = _opts.MarginLeft + _currentColumn * (_contentWidth + _columnGap);
        }

        private void NewPage(DocPageLayout layout = null)
        {
            FlushFootnotes();
            _page      = _pdf.AddPage();
            // Use actual page size from document if available
            if (layout != null && layout.PageWidth > 0)
            {
                _page.Width  = new PdfSharp.Drawing.XUnit(layout.PageWidth,  PdfSharp.Drawing.XGraphicsUnit.Point);
                _page.Height = new PdfSharp.Drawing.XUnit(layout.PageHeight, PdfSharp.Drawing.XGraphicsUnit.Point);
            }
            else
            {
                _page.Size = PdfSharp.PageSize.A4;
            }
            _gfx = XGraphics.FromPdfPage(_page);
            _currentColumn = 0;
            RecalcColumnWidth();
            _cursorY    = _opts.MarginTop;
            _columnTop  = _opts.MarginTop;
            _pageFootnotes.Clear();
            _footnoteAreaHeight = 0;
        }

        private void NextColumn()
        {
            _currentColumn++;
            if (_currentColumn >= _columnCount)
            {
                NewPage();
                return;
            }
            UpdateColumnLeft();
            _cursorY = _columnTop;  // use section's column top, not page top
        }

        private double BottomLimit =>
            _page.Height.Point - _opts.MarginBottom - _footnoteAreaHeight - (_pageFootnotes.Count > 0 ? 12 : 0);

        private void OverflowToNext()
        {
            if (_columnCount > 1 && _currentColumn < _columnCount - 1)
                NextColumn();
            else
                NewPage();
        }
    }
}
