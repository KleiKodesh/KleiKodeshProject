using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordToPdfLib
{
    internal static partial class DocxReader
    {
        public static DocContent Read(string docxPath, TextWriter log = null)
        {
            var content = new DocContent();

            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                var sectPr = doc.MainDocumentPart?.Document?.Body
                    ?.Elements<SectionProperties>().LastOrDefault();
                var pgMar = sectPr?.Elements<PageMargin>().FirstOrDefault();
                if (pgMar != null)
                {
                    content.PageLayout.MarginTop    = (pgMar.Top?.Value    ?? 1440) / 20f;
                    content.PageLayout.MarginBottom = (pgMar.Bottom?.Value ?? 1440) / 20f;
                    content.PageLayout.MarginLeft   = (pgMar.Left?.Value   ?? 1800) / 20f;
                    content.PageLayout.MarginRight  = (pgMar.Right?.Value  ?? 1800) / 20f;
                }
                var pgSz = sectPr?.Elements<PageSize>().FirstOrDefault();
                if (pgSz != null)
                {
                    if (pgSz.Width?.Value  != null) content.PageLayout.PageWidth  = pgSz.Width.Value  / 20f;
                    if (pgSz.Height?.Value != null) content.PageLayout.PageHeight = pgSz.Height.Value / 20f;
                }
                var cols = sectPr?.Elements<Columns>().FirstOrDefault();
                if (cols != null)
                {
                    // This is the FINAL sectPr — store as FinalColumnCount
                    content.FinalColumnCount = (int)(cols.ColumnCount?.Value ?? 1);
                    if (cols.Space?.Value != null && int.TryParse(cols.Space.Value, out int colSpace))
                        content.FinalColumnGap = colSpace / 20f;
                }
                log?.WriteLine($"[Layout] margins L={content.PageLayout.MarginLeft} R={content.PageLayout.MarginRight} T={content.PageLayout.MarginTop} B={content.PageLayout.MarginBottom}");

                bool docRtl = sectPr?.Elements<BiDi>().Any() == true;
                content.PageLayout.IsRtl = docRtl;
                log?.WriteLine($"[Layout] docRtl={docRtl}");

                var themeFonts = BuildThemeFonts(doc.MainDocumentPart?.ThemePart);
                log?.WriteLine($"[Theme] majorBidi={themeFonts.MajorBidi} minorBidi={themeFonts.MinorBidi} majorHAnsi={themeFonts.MajorHAnsi} minorHAnsi={themeFonts.MinorHAnsi}");

                var docDefaults = BuildDocDefaults(doc.MainDocumentPart?.StyleDefinitionsPart, themeFonts);
                content.PageLayout.DefaultFontName    = docDefaults.FontName;
                content.PageLayout.DefaultFontSize    = docDefaults.FontSize;
                content.PageLayout.DefaultSpaceAfter  = docDefaults.SpaceAfter;
                content.PageLayout.DefaultLineSpacing = docDefaults.LineSpacing;
                log?.WriteLine($"[DocDefaults] font={docDefaults.FontName} sz={docDefaults.FontSize} spAfter={docDefaults.SpaceAfter} ls={docDefaults.LineSpacing}");

                var styleMap     = BuildStyleMap(doc.MainDocumentPart?.StyleDefinitionsPart, themeFonts, log);
                var numberingMap = BuildNumberingMap(doc.MainDocumentPart?.NumberingDefinitionsPart, log, docRtl);

                var footnotePart = doc.MainDocumentPart?.FootnotesPart;
                if (footnotePart != null)
                {
                    foreach (var fn in footnotePart.Footnotes.Elements<Footnote>())
                    {
                        var idAttr = fn.Id?.Value.ToString();
                        if (idAttr == null || idAttr == "0" || idAttr == "-1") continue;
                        var footnote = new DocFootnote { Id = idAttr };
                        foreach (var para in fn.Elements<Paragraph>())
                            footnote.Paragraphs.Add(ReadParagraph(para, null, numberingMap, styleMap, null, log));
                        content.Footnotes[idAttr] = footnote;
                    }
                }

                var listCounters = new Dictionary<string, int>();
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return content;

                // Initialize column count from the FIRST inline sectPr (defines section 1 = title area)
                var firstInlineSectPr = body.Elements<Paragraph>()
                    .Select(p => p.ParagraphProperties?.Elements<SectionProperties>().FirstOrDefault())
                    .FirstOrDefault(s => s != null);
                if (firstInlineSectPr != null)
                {
                    var firstCols = firstInlineSectPr.Elements<Columns>().FirstOrDefault();
                    content.PageLayout.ColumnCount = (int)(firstCols?.ColumnCount?.Value ?? 1);
                    if (firstCols?.Space?.Value != null && int.TryParse(firstCols.Space.Value, out int fcs))
                        content.PageLayout.ColumnGap = fcs / 20f;
                }
                log?.WriteLine($"[Layout] initial cols={content.PageLayout.ColumnCount} final cols={content.FinalColumnCount}");

                int paraIdx = 0;
                foreach (var para in body.Elements<Paragraph>())
                {
                    string footnoteId = null;
                    var fnRef = para.Descendants<FootnoteReference>().FirstOrDefault();
                    if (fnRef != null) footnoteId = fnRef.Id?.Value.ToString();
                    content.Paragraphs.Add(ReadParagraph(para, footnoteId, numberingMap, styleMap, listCounters, log, paraIdx++, docRtl));
                }
            }

            return content;
        }
    }
}
