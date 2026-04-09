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
