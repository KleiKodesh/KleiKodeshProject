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
                bool suppressSpaceAfter = para.ContextualSpacing
                    && i + 1 < paras.Count
                    && paras[i + 1].ContextualSpacing;
                RenderParagraph(para, isFootnote: false, suppressSpaceAfter: suppressSpaceAfter);
            }

            FlushFootnotes();
            _pdf.Save(outputPath);
        }

        private void NewPage()
        {
            FlushFootnotes();
            _page         = _pdf.AddPage();
            _page.Size    = PdfSharp.PageSize.A4;
            _gfx          = XGraphics.FromPdfPage(_page);
            _contentWidth = _page.Width.Point - _opts.MarginLeft - _opts.MarginRight;
            _cursorY      = _opts.MarginTop;
            _pageFootnotes.Clear();
            _footnoteAreaHeight = 0;
        }

        private double BottomLimit =>
            _page.Height.Point - _opts.MarginBottom - _footnoteAreaHeight - (_pageFootnotes.Count > 0 ? 12 : 0);
    }
}
