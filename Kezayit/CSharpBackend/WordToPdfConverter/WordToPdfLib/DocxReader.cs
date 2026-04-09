using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordToPdfLib
{
    internal static class DocxReader
    {
        public static DocContent Read(string docxPath, TextWriter log = null)
        {
            var content = new DocContent();

            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                // ── Page layout ───────────────────────────────────────────────
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

                // ── Document RTL flag (w:bidi on sectPr) ─────────────────────
                bool docRtl = sectPr?.Elements<BiDi>().Any() == true;
                content.PageLayout.IsRtl = docRtl;
                log?.WriteLine($"[Layout] docRtl={docRtl}");

                // ── Theme fonts ───────────────────────────────────────────────
                var themeFonts = BuildThemeFonts(doc.MainDocumentPart?.ThemePart);
                log?.WriteLine($"[Theme] majorBidi={themeFonts.MajorBidi} minorBidi={themeFonts.MinorBidi} majorHAnsi={themeFonts.MajorHAnsi} minorHAnsi={themeFonts.MinorHAnsi}");

                // ── docDefaults ───────────────────────────────────────────────
                var docDefaults = BuildDocDefaults(doc.MainDocumentPart?.StyleDefinitionsPart, themeFonts);
                content.PageLayout.DefaultFontName    = docDefaults.FontName;
                content.PageLayout.DefaultFontSize    = docDefaults.FontSize;
                content.PageLayout.DefaultSpaceAfter  = docDefaults.SpaceAfter;
                content.PageLayout.DefaultLineSpacing = docDefaults.LineSpacing;
                log?.WriteLine($"[DocDefaults] font={docDefaults.FontName} sz={docDefaults.FontSize} spAfter={docDefaults.SpaceAfter} ls={docDefaults.LineSpacing}");

                // ── Style map ─────────────────────────────────────────────────
                var styleMap = BuildStyleMap(doc.MainDocumentPart?.StyleDefinitionsPart, themeFonts, log);

                // ── Numbering map ─────────────────────────────────────────────
                var numberingMap = BuildNumberingMap(doc.MainDocumentPart?.NumberingDefinitionsPart, log, docRtl);

                // ── Footnotes ─────────────────────────────────────────────────
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

                // ── Body paragraphs ───────────────────────────────────────────
                var listCounters = new Dictionary<string, int>();
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return content;

                int paraIdx = 0;
                foreach (var para in body.Elements<Paragraph>())
                {
                    string footnoteId = null;
                    var fnRef = para.Descendants<FootnoteReference>().FirstOrDefault();
                    if (fnRef != null) footnoteId = fnRef.Id?.Value.ToString();

                    var dp = ReadParagraph(para, footnoteId, numberingMap, styleMap, listCounters, log, paraIdx, docRtl);
                    content.Paragraphs.Add(dp);
                    paraIdx++;
                }
            }

            return content;
        }

        // ── Theme fonts ───────────────────────────────────────────────────────

        private struct ThemeFontMap
        {
            public string MajorBidi;
            public string MinorBidi;
            public string MajorHAnsi;
            public string MinorHAnsi;

            public string Resolve(string theme)
            {
                if (theme == null) return null;
                // Handle both SDK enum names (e.g. "MajorBidi") and raw XML values (e.g. "majorBidi")
                switch (theme)
                {
                    case "MajorBidi":
                    case "majorBidi":     return MajorBidi;
                    case "MinorBidi":
                    case "minorBidi":     return MinorBidi;
                    case "MajorHighAnsi":
                    case "majorHAnsi":    return MajorHAnsi;
                    case "MinorHighAnsi":
                    case "minorHAnsi":    return MinorHAnsi;
                    case "MajorAscii":
                    case "majorAscii":    return MajorHAnsi;
                    case "MinorAscii":
                    case "minorAscii":    return MinorHAnsi;
                    default:
                        // Handle "minor*" / "major*" prefix generically
                        if (theme.StartsWith("minor")) return MinorHAnsi;
                        if (theme.StartsWith("Major")) return MajorHAnsi;
                        return null;
                }
            }
        }

        private static ThemeFontMap BuildThemeFonts(ThemePart part)
        {
            var map = new ThemeFontMap { MajorBidi = "Times New Roman", MinorBidi = "Arial", MajorHAnsi = "Calibri Light", MinorHAnsi = "Calibri" };
            if (part == null) return map;

            try
            {
                // Parse theme XML directly for font scheme
                using (var stream = part.GetStream())
                {
                    var xml = new System.Xml.XmlDocument();
                    xml.Load(stream);
                    var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
                    ns.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

                    var majorHebr = xml.SelectSingleNode("//a:majorFont/a:font[@script='Hebr']/@typeface", ns)?.Value;
                    var minorHebr = xml.SelectSingleNode("//a:minorFont/a:font[@script='Hebr']/@typeface", ns)?.Value;
                    var majorLatin = xml.SelectSingleNode("//a:majorFont/a:latin/@typeface", ns)?.Value;
                    var minorLatin = xml.SelectSingleNode("//a:minorFont/a:latin/@typeface", ns)?.Value;

                    if (majorHebr  != null) map.MajorBidi  = majorHebr;
                    if (minorHebr  != null) map.MinorBidi  = minorHebr;
                    if (majorLatin != null) map.MajorHAnsi = majorLatin;
                    if (minorLatin != null) map.MinorHAnsi = minorLatin;
                }
            }
            catch { }

            return map;
        }

        // ── docDefaults ───────────────────────────────────────────────────────

        private struct DocDefaultsInfo
        {
            public string FontName;
            public float  FontSize;
            public float  SpaceAfter;
            public float  LineSpacing;
        }

        private static DocDefaultsInfo BuildDocDefaults(StyleDefinitionsPart part, ThemeFontMap theme)
        {
            var d = new DocDefaultsInfo { FontName = theme.MinorBidi, FontSize = 12f, SpaceAfter = 8f, LineSpacing = 1.15f };
            if (part?.Styles == null) return d;

            var rPrDef = part.Styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
            if (rPrDef != null)
            {
                // Resolve font: prefer complex script theme (Hebrew)
                string fontName = null;
                if (rPrDef.RunFonts != null)
                {
                    var csTheme = rPrDef.RunFonts.ComplexScriptTheme?.Value;
                    if (csTheme != null) fontName = theme.Resolve(csTheme.ToString());
                    if (fontName == null)
                    {
                        var rawAttr = rPrDef.RunFonts.GetAttribute("cstheme",
                            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                        var raw = rawAttr.Value;
                        if (!string.IsNullOrEmpty(raw))
                            fontName = theme.Resolve(raw);
                    }
                    if (fontName == null) fontName = rPrDef.RunFonts.ComplexScript?.Value;
                }
                if (fontName != null) d.FontName = fontName;

                var szCs = rPrDef.FontSizeComplexScript;
                var sz   = rPrDef.FontSize;
                if (szCs?.Val != null && int.TryParse(szCs.Val.Value, out int fcs)) d.FontSize = fcs / 2f;
                else if (sz?.Val != null && int.TryParse(sz.Val.Value, out int fs)) d.FontSize = fs / 2f;
            }

            var pPrDef = part.Styles.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
            if (pPrDef != null)
            {
                var spc = pPrDef.SpacingBetweenLines;
                if (spc?.After?.Value != null && int.TryParse(spc.After.Value, out int sa)) d.SpaceAfter = sa / 20f;
                if (spc?.Line?.Value  != null && int.TryParse(spc.Line.Value,  out int ls) && ls > 0)
                    d.LineSpacing = ls / 240f;
            }

            return d;
        }

        // ── Style map ─────────────────────────────────────────────────────────

        private struct StyleInfo
        {
            public float?  FontSize;
            public string  FontName;
            public string  Color;
            public bool    Bold;
            public bool    Italic;
            public float   SpaceBefore;
            public float   SpaceAfter;
            public float   IndentLeft;
            public bool    ContextualSpacing;
            public string  BasedOn;
        }

        private static Dictionary<string, StyleInfo> BuildStyleMap(StyleDefinitionsPart part, ThemeFontMap theme, TextWriter log = null)
        {
            var map = new Dictionary<string, StyleInfo>(StringComparer.OrdinalIgnoreCase);
            if (part?.Styles == null) return map;

            // First pass: read each style's own properties
            foreach (var style in part.Styles.Elements<Style>())
            {
                var id = style.StyleId?.Value;
                if (id == null) continue;

                var si = new StyleInfo();
                si.BasedOn = style.BasedOn?.Val?.Value;

                var rpr = style.StyleRunProperties;

                // Resolve font name: prefer complex script theme (Hebrew/Arabic)
                // StyleRunProperties.RunFonts maps w:rFonts; read w:cstheme attribute directly
                string fontName = null;
                if (rpr?.RunFonts != null)
                {
                    // Try typed property first
                    var csTheme    = rpr.RunFonts.ComplexScriptTheme?.Value;
                    var hAnsiTheme = rpr.RunFonts.HighAnsiTheme?.Value;
                    if (csTheme    != null) fontName = theme.Resolve(csTheme.ToString());
                    if (fontName   == null && hAnsiTheme != null) fontName = theme.Resolve(hAnsiTheme.ToString());
                    // Fallback: read raw XML attribute w:cstheme
                    if (fontName == null)
                    {
                        var rawAttr = rpr.RunFonts.GetAttribute("cstheme",
                            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                        var raw = rawAttr.Value;
                        if (!string.IsNullOrEmpty(raw))
                            fontName = theme.Resolve(raw); // raw is already e.g. "majorBidi"
                    }
                    if (fontName == null) fontName = rpr.RunFonts.ComplexScript?.Value ?? rpr.RunFonts.Ascii?.Value;
                }
                si.FontName = fontName;

                var szCs = rpr?.FontSizeComplexScript;
                var szEl = rpr?.FontSize;
                if (szCs?.Val != null && int.TryParse(szCs.Val.Value, out int fcs)) si.FontSize = fcs / 2f;
                else if (szEl?.Val != null && int.TryParse(szEl.Val.Value, out int fel)) si.FontSize = fel / 2f;

                si.Color  = rpr?.Color?.Val?.Value;
                si.Bold   = rpr?.Bold != null || rpr?.BoldComplexScript != null;
                si.Italic = rpr?.Italic != null || rpr?.ItalicComplexScript != null;

                var ppr = style.StyleParagraphProperties;
                var spc = ppr?.SpacingBetweenLines;
                if (spc?.Before?.Value != null && int.TryParse(spc.Before.Value, out int sb)) si.SpaceBefore = sb / 20f;
                if (spc?.After?.Value  != null && int.TryParse(spc.After.Value,  out int sa)) si.SpaceAfter  = sa / 20f;
                var ind = ppr?.Indentation;
                if (ind?.Left?.Value != null && int.TryParse(ind.Left.Value, out int il)) si.IndentLeft = il / 20f;
                si.ContextualSpacing = ppr?.ContextualSpacing != null
                    || ppr?.ChildElements.Any(e => e.LocalName == "contextualSpacing") == true;

                map[id] = si;
            }

            // Second pass: resolve basedOn inheritance
            foreach (var id in map.Keys.ToList())
            {
                var si = map[id];
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
                var parent = si.BasedOn;
                while (parent != null && map.TryGetValue(parent, out var parentSi) && !visited.Contains(parent))
                {
                    visited.Add(parent);
                    if (si.FontName   == null && parentSi.FontName   != null) si.FontName   = parentSi.FontName;
                    if (si.FontSize   == null && parentSi.FontSize   != null) si.FontSize   = parentSi.FontSize;
                    if (si.Color      == null && parentSi.Color      != null) si.Color      = parentSi.Color;
                    if (!si.Bold      && parentSi.Bold)                       si.Bold       = true;
                    if (!si.Italic    && parentSi.Italic)                     si.Italic     = true;
                    if (!si.ContextualSpacing && parentSi.ContextualSpacing) si.ContextualSpacing = true;
                    if (si.SpaceBefore == 0   && parentSi.SpaceBefore > 0)   si.SpaceBefore = parentSi.SpaceBefore;
                    if (si.SpaceAfter  == 0   && parentSi.SpaceAfter  > 0)   si.SpaceAfter  = parentSi.SpaceAfter;
                    parent = parentSi.BasedOn;
                }
                map[id] = si;
            }

            return map;
        }

        // ── Numbering map ─────────────────────────────────────────────────────

        private struct NumLevelDef
        {
            public string Format;
            public int    Start;
            public float  IndentLeft;
            public float  IndentRight;
            public float  Hanging;
        }

        private static Dictionary<string, NumLevelDef> BuildNumberingMap(
            NumberingDefinitionsPart part, TextWriter log, bool docRtl = false)
        {
            var map = new Dictionary<string, NumLevelDef>();
            if (part?.Numbering == null) return map;

            var abstractNums = part.Numbering.Elements<AbstractNum>()
                .ToDictionary(a => a.AbstractNumberId.Value.ToString());

            foreach (var num in part.Numbering.Elements<NumberingInstance>())
            {
                var numId      = num.NumberID?.Value.ToString();
                var abstractId = num.AbstractNumId?.Val?.Value.ToString();
                if (numId == null || abstractId == null) continue;
                if (!abstractNums.TryGetValue(abstractId, out var abstractNum)) continue;

                foreach (var lvl in abstractNum.Elements<Level>())
                {
                    var ilvl    = lvl.LevelIndex?.Value ?? 0;
                    var fmt     = lvl.NumberingFormat?.Val?.ToString() ?? "bullet";
                    var start   = (int)(lvl.StartNumberingValue?.Val?.Value ?? 1);
                    // Access <w:pPr> inside <w:lvl> via child elements
                    Indentation ind = null;
                    var pprEl = lvl.ChildElements.FirstOrDefault(e => e.LocalName == "pPr");
                    if (pprEl != null)
                        ind = pprEl.Elements<Indentation>().FirstOrDefault();
                    float indL = 0, hanging = 0;
                    if (ind?.Left?.Value    != null && int.TryParse(ind.Left.Value,    out int il)) indL    = il / 20f;
                    if (ind?.Hanging?.Value != null && int.TryParse(ind.Hanging.Value, out int hg)) hanging = hg / 20f;

                    // In RTL docs, w:left means indent from the right
                    var def = docRtl
                        ? new NumLevelDef { Format = fmt, Start = start, IndentRight = indL, Hanging = hanging }
                        : new NumLevelDef { Format = fmt, Start = start, IndentLeft  = indL, Hanging = hanging };
                    map[$"{numId}:{ilvl}"] = def;
                    log?.WriteLine($"[Numbering] numId={numId} ilvl={ilvl} fmt={fmt} start={start} indL={def.IndentLeft} indR={def.IndentRight} hanging={def.Hanging}");
                }
            }
            return map;
        }

        // ── Paragraph ─────────────────────────────────────────────────────────

        private static DocParagraph ReadParagraph(
            Paragraph para,
            string footnoteId,
            Dictionary<string, NumLevelDef> numberingMap,
            Dictionary<string, StyleInfo> styleMap,
            Dictionary<string, int> listCounters,
            TextWriter log,
            int paraIdx = -1,
            bool docRtl = false)
        {
            var dp = new DocParagraph { FootnoteId = footnoteId };
            var pp = para.ParagraphProperties;

            // Style
            var styleId = pp?.ParagraphStyleId?.Val?.Value ?? "";
            if (styleId == "1" || styleId.ToLower().Contains("heading1")) dp.Type = ParagraphType.Heading1;
            else if (styleId == "2" || styleId.ToLower().Contains("heading2")) dp.Type = ParagraphType.Heading2;
            else if (styleId == "3" || styleId.ToLower().Contains("heading3")) dp.Type = ParagraphType.Heading3;

            // Apply style-level spacing/indent as defaults
            if (styleMap.TryGetValue(styleId, out var si))
            {
                if (si.SpaceBefore > 0) dp.SpaceBefore = si.SpaceBefore;
                if (si.SpaceAfter  > 0) dp.SpaceAfter  = si.SpaceAfter;
                if (si.IndentLeft  > 0) dp.IndentLeft  = si.IndentLeft;
                if (si.ContextualSpacing) dp.ContextualSpacing = true;
            }

            // RTL: check paragraph bidi OR document bidi (handled by DefaultRtl in renderer)
            var bidi = pp?.BiDi;
            dp.IsRtl = bidi != null && (bidi.Val == null || bidi.Val.Value != false);

            dp.PageBreakBefore = pp?.PageBreakBefore != null;
            // ContextualSpacing: set from paragraph if explicit, otherwise keep style value
            if (pp?.ContextualSpacing != null)
                dp.ContextualSpacing = true;

            // Alignment — in RTL documents, left/right are visually flipped (per OpenXmlPowerTools CreateStyleFromJc)
            var jc = pp?.Justification?.Val;
            if (jc != null)
            {
                if (jc == JustificationValues.Center)
                    dp.Alignment = ParagraphAlignment.Center;
                else if (jc == JustificationValues.Both || jc == JustificationValues.Distribute)
                    dp.Alignment = ParagraphAlignment.Justify;
                else if (jc == JustificationValues.Right || jc == JustificationValues.End)
                    dp.Alignment = docRtl ? ParagraphAlignment.Left  : ParagraphAlignment.Right;
                else if (jc == JustificationValues.Left || jc == JustificationValues.Start)
                    dp.Alignment = docRtl ? ParagraphAlignment.Right : ParagraphAlignment.Left;
                else
                    dp.Alignment = ParagraphAlignment.Left;
            }

            // In RTL documents, w:ind w:left means indent from the right (mirrored).
            var ind = pp?.Indentation;
            if (ind?.Left?.Value    != null && int.TryParse(ind.Left.Value,    out int il))
            {
                if (docRtl) dp.IndentRight = il / 20f;
                else        dp.IndentLeft  = il / 20f;
            }
            if (ind?.Right?.Value   != null && int.TryParse(ind.Right.Value,   out int ir))
            {
                if (docRtl) dp.IndentLeft  = ir / 20f;
                else        dp.IndentRight = ir / 20f;
            }
            if (ind?.Hanging?.Value != null && int.TryParse(ind.Hanging.Value, out int hg)) dp.HangingIndent = hg / 20f;

            // Paragraph spacing (overrides style)
            var spc = pp?.SpacingBetweenLines;
            if (spc != null)
            {
                if (spc.Before?.Value != null && int.TryParse(spc.Before.Value, out int sb)) dp.SpaceBefore = sb / 20f;
                if (spc.After?.Value  != null && int.TryParse(spc.After.Value,  out int sa)) dp.SpaceAfter  = sa / 20f;
                if (spc.Line?.Value   != null && int.TryParse(spc.Line.Value,   out int ls) && ls > 0)
                    dp.LineSpacing = ls / 240f;
            }

            // List numbering — also sets IndentLeft and HangingIndent from numbering definition
            var numPr = pp?.NumberingProperties;
            if (numPr != null && listCounters != null)
            {
                var numId = numPr.NumberingId?.Val?.Value.ToString();
                var ilvl  = (int)(numPr.NumberingLevelReference?.Val?.Value ?? 0);
                var key   = $"{numId}:{ilvl}";

                if (numId != null && numberingMap.TryGetValue(key, out var def))
                {
                    if (!listCounters.ContainsKey(key)) listCounters[key] = def.Start;

                    dp.ListPrefix    = FormatListPrefix(def.Format, listCounters[key], ilvl);
                    dp.HangingIndent = def.Hanging;
                    dp.IndentLeft    = def.IndentLeft;
                    dp.IndentRight   = def.IndentRight;

                    listCounters[key]++;
                    for (int deeper = ilvl + 1; deeper <= 8; deeper++)
                    {
                        var deepKey = $"{numId}:{deeper}";
                        if (numberingMap.TryGetValue(deepKey, out var deepDef))
                            listCounters[deepKey] = deepDef.Start;
                    }
                }
                else if (numId != null)
                {
                    dp.ListPrefix = "•";
                }
            }

            // Runs
            foreach (var run in para.Elements<Run>())
            {
                var rp = run.RunProperties;
                var va = rp?.VerticalTextAlignment?.Val;

                float? fontSize = null;
                // Use szCs (complex script = Hebrew/Arabic) preferentially
                var szCs = rp?.FontSizeComplexScript;
                var szEl = rp?.FontSize;
                if (szCs?.Val != null && int.TryParse(szCs.Val.Value, out int fcs)) fontSize = fcs / 2f;
                else if (szEl?.Val != null && int.TryParse(szEl.Val.Value, out int fel)) fontSize = fel / 2f;

                // Superscript: reduce size
                bool isSup = va != null && va.Value == VerticalPositionValues.Superscript;
                bool isSub = va != null && va.Value == VerticalPositionValues.Subscript;
                // Don't reduce size here — GetFont will apply 65% reduction using the resolved size

                var tr = new TextRun
                {
                    Bold          = rp?.Bold != null || rp?.BoldComplexScript != null,
                    Italic        = rp?.Italic != null || rp?.ItalicComplexScript != null,
                    Underline     = rp?.Underline != null,
                    Strikethrough = rp?.Strike != null,
                    Superscript   = isSup,
                    Subscript     = isSub,
                    Color         = rp?.Color?.Val?.Value,
                    Highlight     = rp?.Highlight?.Val?.ToString(),
                    FontSize      = fontSize,
                    // For RTL/Hebrew runs: use cs font. For LTR: use ascii font.
                    // Per OpenXmlPowerTools DetermineFontTypeFromCharacter: Hebrew chars use Ascii slot,
                    // but runs with w:rtl element always use CS font.
                    FontName      = rp?.RunFonts?.ComplexScript?.Value
                                    ?? rp?.RunFonts?.Ascii?.Value
                                    ?? rp?.RunFonts?.HighAnsi?.Value,
                    Text          = string.Concat(run.Elements<Text>().Select(t => t.Text))
                };

                if (!string.IsNullOrEmpty(tr.Text))
                {
                    // Apply style-level properties to runs that have no explicit values
                    if (styleMap.TryGetValue(styleId, out var runSi))
                    {
                        if (string.IsNullOrEmpty(tr.Color)    && !string.IsNullOrEmpty(runSi.Color))    tr.Color    = runSi.Color;
                        if (string.IsNullOrEmpty(tr.FontName) && !string.IsNullOrEmpty(runSi.FontName)) tr.FontName = runSi.FontName;
                        if (tr.FontSize == null && runSi.FontSize.HasValue) tr.FontSize = runSi.FontSize;
                        if (dp.Type != ParagraphType.Normal && runSi.Bold && !tr.Bold) tr.Bold = true;
                    }
                    dp.Runs.Add(tr);
                }
            }

            if (log != null && paraIdx >= 0)
            {
                log.WriteLine($"[Para {paraIdx}] type={dp.Type} rtl={dp.IsRtl} align={dp.Alignment} " +
                    $"indL={dp.IndentLeft:F1} indR={dp.IndentRight:F1} hang={dp.HangingIndent:F1} " +
                    $"spB={dp.SpaceBefore:F1} spA={dp.SpaceAfter:F1} ls={dp.LineSpacing} ctxSpc={dp.ContextualSpacing} prefix='{dp.ListPrefix}'");
                foreach (var r in dp.Runs)
                    log.WriteLine($"  run: sz={r.FontSize?.ToString("F1") ?? "def"} bold={r.Bold} italic={r.Italic} " +
                        $"ul={r.Underline} sup={r.Superscript} color={r.Color} font={r.FontName} | '{r.Text}'");
            }

            return dp;
        }

        private static string FormatListPrefix(string format, int counter, int level)
        {
            switch (format.ToLower())
            {
                case "bullet":      return "•";
                case "decimal":     return $"{counter}.";
                case "lowerroman":  return $"{ToRoman(counter).ToLower()}.";
                case "upperroman":  return $"{ToRoman(counter)}.";
                case "lowerletter": return $"{(char)('a' + (counter - 1) % 26)}.";
                case "upperletter": return $"{(char)('A' + (counter - 1) % 26)}.";
                default:            return "•";
            }
        }

        private static string ToRoman(int n)
        {
            if (n < 1) return "";
            var vals = new[] { 1000,900,500,400,100,90,50,40,10,9,5,4,1 };
            var syms = new[] { "M","CM","D","CD","C","XC","L","XL","X","IX","V","IV","I" };
            var sb   = new System.Text.StringBuilder();
            for (int i = 0; i < vals.Length; i++)
                while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }
    }
}
