using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordToPdfLib
{
    internal static partial class DocxReader
    {
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

            var styleId = pp?.ParagraphStyleId?.Val?.Value ?? "";
            if      (styleId == "1" || styleId.ToLower().Contains("heading1")) dp.Type = ParagraphType.Heading1;
            else if (styleId == "2" || styleId.ToLower().Contains("heading2")) dp.Type = ParagraphType.Heading2;
            else if (styleId == "3" || styleId.ToLower().Contains("heading3")) dp.Type = ParagraphType.Heading3;

            if (styleMap.TryGetValue(styleId, out var si))
            {
                if (si.SpaceBefore > 0)   dp.SpaceBefore      = si.SpaceBefore;
                if (si.SpaceAfter  > 0)   dp.SpaceAfter       = si.SpaceAfter;
                if (si.IndentLeft  > 0)   dp.IndentLeft       = si.IndentLeft;
                if (si.ContextualSpacing) dp.ContextualSpacing = true;
            }

            var bidi = pp?.BiDi;
            dp.IsRtl = bidi != null && (bidi.Val == null || bidi.Val.Value != false);
            dp.PageBreakBefore = pp?.PageBreakBefore != null;
            if (pp?.ContextualSpacing != null) dp.ContextualSpacing = true;

            // Inline sectPr = section break — the inline sectPr defines THIS section ending here.
            // The NEXT section's columns come from the document's final sectPr (passed via docRtl context).
            // We store the inline sectPr's cols as the CURRENT section, and the caller will
            // switch to the final sectPr's cols after this paragraph.
            var inlineSectPr = pp?.Elements<SectionProperties>().FirstOrDefault();
            if (inlineSectPr != null)
            {
                dp.IsSectionBreak = true;
                // SectionColumns = columns for the NEXT section (stored on the break para for the renderer)
                // We don't know the next section here, so set to 0 = "use document default"
                dp.SectionColumns   = 0;
                dp.SectionColumnGap = 36f;
            }

            var jc = pp?.Justification?.Val;
            if (jc != null)
            {
                if      (jc == JustificationValues.Center)                                        dp.Alignment = ParagraphAlignment.Center;
                else if (jc == JustificationValues.Both || jc == JustificationValues.Distribute)  dp.Alignment = ParagraphAlignment.Justify;
                else if (jc == JustificationValues.Right || jc == JustificationValues.End)        dp.Alignment = docRtl ? ParagraphAlignment.Left  : ParagraphAlignment.Right;
                else if (jc == JustificationValues.Left  || jc == JustificationValues.Start)      dp.Alignment = docRtl ? ParagraphAlignment.Right : ParagraphAlignment.Left;
                else                                                                               dp.Alignment = ParagraphAlignment.Left;
            }

            var ind = pp?.Indentation;
            if (ind?.Left?.Value    != null && int.TryParse(ind.Left.Value,    out int il)) { if (docRtl) dp.IndentRight = il / 20f; else dp.IndentLeft  = il / 20f; }
            if (ind?.Right?.Value   != null && int.TryParse(ind.Right.Value,   out int ir)) { if (docRtl) dp.IndentLeft  = ir / 20f; else dp.IndentRight = ir / 20f; }
            if (ind?.Hanging?.Value != null && int.TryParse(ind.Hanging.Value, out int hg)) dp.HangingIndent = hg / 20f;

            var spc = pp?.SpacingBetweenLines;
            if (spc != null)
            {
                if (spc.Before?.Value != null && int.TryParse(spc.Before.Value, out int sb)) dp.SpaceBefore = sb / 20f;
                if (spc.After?.Value  != null && int.TryParse(spc.After.Value,  out int sa)) dp.SpaceAfter  = sa / 20f;
                if (spc.Line?.Value   != null && int.TryParse(spc.Line.Value,   out int ls) && ls > 0)
                {
                    // Read lineRule from raw XML attribute since SDK enum .ToString() may vary
                    var lineRuleRaw = spc.GetAttribute("lineRule", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                    var lineRuleStr = string.IsNullOrEmpty(lineRuleRaw.Value)
                        ? (spc.LineRule?.Value.ToString()?.ToLower() ?? "auto")
                        : lineRuleRaw.Value.ToLower();
                    if (lineRuleStr == "exact" || lineRuleStr == "atleast")
                    {
                        dp.LineSpacing      = ls / 20f;
                        dp.LineSpacingExact = true;
                    }
                    else
                        dp.LineSpacing = ls / 240f;
                }
            }

            var numPr = pp?.NumberingProperties;
            if (numPr != null && listCounters != null)
            {
                var numId = numPr.NumberingId?.Val?.Value.ToString();
                var ilvl  = (int)(numPr.NumberingLevelReference?.Val?.Value ?? 0);
                var key   = $"{numId}:{ilvl}";

                if (numId != null && numberingMap.TryGetValue(key, out var def))
                {
                    if (!listCounters.ContainsKey(key)) listCounters[key] = def.Start;
                    dp.ListPrefix    = FormatListPrefix(def.Format, def.LvlText, listCounters[key], ilvl);
                    dp.HangingIndent = def.Hanging;
                    dp.IndentLeft    = def.IndentLeft;
                    dp.IndentRight   = def.IndentRight;
                    listCounters[key]++;
                    for (int deeper = ilvl + 1; deeper <= 8; deeper++)
                    {
                        var dk = $"{numId}:{deeper}";
                        if (numberingMap.TryGetValue(dk, out var dd)) listCounters[dk] = dd.Start;
                    }
                }
                else if (numId != null) dp.ListPrefix = "•";
            }

            // Paragraph-level run properties (from pPr/rPr) — apply as defaults to all runs
            var paraRpr = pp?.ParagraphMarkRunProperties;
            string paraRStyle = paraRpr?.Elements<RunStyle>().FirstOrDefault()?.Val?.Value;

            foreach (var run in para.Elements<Run>())
            {
                var rp = run.RunProperties;
                var va = rp?.VerticalTextAlignment?.Val;

                float? fontSize = null;
                var szCs = rp?.FontSizeComplexScript;
                var szEl = rp?.FontSize;
                if (szCs?.Val != null && int.TryParse(szCs.Val.Value, out int fcs)) fontSize = fcs / 2f;
                else if (szEl?.Val != null && int.TryParse(szEl.Val.Value, out int fel)) fontSize = fel / 2f;

                bool isSup = va != null && va.Value == VerticalPositionValues.Superscript;
                bool isSub = va != null && va.Value == VerticalPositionValues.Subscript;

                // w:b/w:bCs: present with no val or val="1"/"true" = bold; val="0"/"false" = not bold
                bool runBold   = IsBoolPropTrue(rp?.Bold) || IsBoolPropTrue(rp?.BoldComplexScript);
                bool runItalic = IsBoolPropTrue(rp?.Italic) || IsBoolPropTrue(rp?.ItalicComplexScript);

                // Character style from rStyle — apply its bold/italic/color/font
                string runStyleId = rp?.RunStyle?.Val?.Value ?? paraRStyle;
                if (runStyleId != null && styleMap.TryGetValue(runStyleId, out var charSi))
                {
                    if (charSi.Bold   && !HasExplicitFalse(rp?.Bold)   && !HasExplicitFalse(rp?.BoldComplexScript))   runBold   = true;
                    if (charSi.Italic && !HasExplicitFalse(rp?.Italic) && !HasExplicitFalse(rp?.ItalicComplexScript)) runItalic = true;
                }

                var tr = new TextRun
                {
                    Bold           = runBold,
                    Italic         = runItalic,
                    Underline      = rp?.Underline != null,
                    Strikethrough  = rp?.Strike != null,
                    Superscript    = isSup,
                    Subscript      = isSub,
                    Vanish         = rp?.Vanish != null,
                    VerticalOffset = rp?.Position?.Val != null && int.TryParse(rp.Position.Val.Value, out int pos) ? pos / 2f : (float?)null,
                    Color          = rp?.Color?.Val?.Value,
                    Highlight      = rp?.Highlight?.Val?.ToString(),
                    FontSize       = fontSize,
                    FontName       = rp?.RunFonts?.ComplexScript?.Value ?? rp?.RunFonts?.Ascii?.Value ?? rp?.RunFonts?.HighAnsi?.Value,
                    Text           = string.Concat(run.Elements<Text>().Select(t => t.Text))
                };

                if (!string.IsNullOrEmpty(tr.Text) && !tr.Vanish)
                {
                    // Apply paragraph style properties
                    if (styleMap.TryGetValue(styleId, out var runSi))
                    {
                        if (string.IsNullOrEmpty(tr.Color)    && !string.IsNullOrEmpty(runSi.Color))    tr.Color    = runSi.Color;
                        if (string.IsNullOrEmpty(tr.FontName) && !string.IsNullOrEmpty(runSi.FontName)) tr.FontName = runSi.FontName;
                        if (tr.FontSize == null && runSi.FontSize.HasValue)                              tr.FontSize = runSi.FontSize;
                        if (dp.Type != ParagraphType.Normal && runSi.Bold && !HasExplicitFalse(rp?.Bold)) tr.Bold = true;
                    }
                    // Apply character style color/font if not overridden by run
                    if (runStyleId != null && styleMap.TryGetValue(runStyleId, out var csi))
                    {
                        if (string.IsNullOrEmpty(tr.Color)    && !string.IsNullOrEmpty(csi.Color))    tr.Color    = csi.Color;
                        if (string.IsNullOrEmpty(tr.FontName) && !string.IsNullOrEmpty(csi.FontName)) tr.FontName = csi.FontName;
                        if (tr.FontSize == null && csi.FontSize.HasValue)                              tr.FontSize = csi.FontSize;
                    }
                    dp.Runs.Add(tr);
                }
            }

            if (log != null && paraIdx >= 0)
            {
                log.WriteLine($"[Para {paraIdx}] type={dp.Type} rtl={dp.IsRtl} align={dp.Alignment} " +
                    $"indL={dp.IndentLeft:F1} indR={dp.IndentRight:F1} hang={dp.HangingIndent:F1} " +
                    $"spB={dp.SpaceBefore:F1} spA={dp.SpaceAfter:F1} ls={dp.LineSpacing}(exact={dp.LineSpacingExact}) ctxSpc={dp.ContextualSpacing} " +
                    $"sectBreak={dp.IsSectionBreak}(cols={dp.SectionColumns}) prefix='{dp.ListPrefix}'");
                foreach (var r in dp.Runs)
                    log.WriteLine($"  run: sz={r.FontSize?.ToString("F1") ?? "def"} bold={r.Bold} italic={r.Italic} " +
                        $"ul={r.Underline} sup={r.Superscript} color={r.Color} font={r.FontName} | '{r.Text}'");
            }

            return dp;
        }

        // Returns true if the toggle property element is present and not explicitly set to false
        private static bool IsBoolPropTrue(OpenXmlElement el)
        {
            if (el == null) return false;
            var val = el.GetAttributes().FirstOrDefault(a => a.LocalName == "val");
            if (val.Value == null) return true;  // present with no val = true
            var v = val.Value.ToLower();
            return v != "0" && v != "false" && v != "off";
        }

        // Returns true if the element is explicitly set to false (val="0"/"false")
        private static bool HasExplicitFalse(OpenXmlElement el)
        {
            if (el == null) return false;
            var val = el.GetAttributes().FirstOrDefault(a => a.LocalName == "val");
            if (val.Value == null) return false;
            var v = val.Value.ToLower();
            return v == "0" || v == "false" || v == "off";
        }
    }
}
