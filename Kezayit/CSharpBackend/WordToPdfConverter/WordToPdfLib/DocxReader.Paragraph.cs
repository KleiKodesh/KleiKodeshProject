using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    var lineRule = spc.LineRule?.Value.ToString() ?? "auto";
                    dp.LineSpacing = (lineRule == "exact" || lineRule == "atLeast")
                        ? (ls / 20f) / 12f
                        : ls / 240f;
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

                var tr = new TextRun
                {
                    Bold           = rp?.Bold != null || rp?.BoldComplexScript != null,
                    Italic         = rp?.Italic != null || rp?.ItalicComplexScript != null,
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
                    if (styleMap.TryGetValue(styleId, out var runSi))
                    {
                        if (string.IsNullOrEmpty(tr.Color)    && !string.IsNullOrEmpty(runSi.Color))    tr.Color    = runSi.Color;
                        if (string.IsNullOrEmpty(tr.FontName) && !string.IsNullOrEmpty(runSi.FontName)) tr.FontName = runSi.FontName;
                        if (tr.FontSize == null && runSi.FontSize.HasValue)                              tr.FontSize = runSi.FontSize;
                        if (dp.Type != ParagraphType.Normal && runSi.Bold && !tr.Bold)                  tr.Bold     = true;
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
    }
}
