using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private XFont GetFont(DocParagraph para, TextRun run, bool isFootnote)
        {
            float size = run?.FontSize ?? _opts.DefaultFontSize;
            if (isFootnote) size = run?.FontSize ?? _opts.DefaultFontSize * 0.85f;

            if (run?.FontSize == null)
            {
                switch (para?.Type)
                {
                    case ParagraphType.Heading1: size = 20f; break;
                    case ParagraphType.Heading2: size = 16f; break;
                    case ParagraphType.Heading3: size = 13f; break;
                }
            }

            if (run?.Superscript == true || run?.Subscript == true)
                size *= 0.65f;

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
                if (string.IsNullOrEmpty(name)) continue;
                try { var f = new XFont(name, ptSize, style, enc); _fontCache[key] = f; return f; }
                catch { }
            }

            var fallback = new XFont("Arial", ptSize, XFontStyleEx.Regular, enc);
            _fontCache[key] = fallback;
            return fallback;
        }
    }
}
