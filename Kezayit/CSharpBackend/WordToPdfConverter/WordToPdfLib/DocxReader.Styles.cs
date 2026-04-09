using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO;

namespace WordToPdfLib
{
    internal static partial class DocxReader
    {
        internal struct ThemeFontMap
        {
            public string MajorBidi, MinorBidi, MajorHAnsi, MinorHAnsi;

            public string Resolve(string theme)
            {
                if (theme == null) return null;
                switch (theme)
                {
                    case "MajorBidi":   case "majorBidi":   return MajorBidi;
                    case "MinorBidi":   case "minorBidi":   return MinorBidi;
                    case "MajorHighAnsi": case "majorHAnsi": return MajorHAnsi;
                    case "MinorHighAnsi": case "minorHAnsi": return MinorHAnsi;
                    case "MajorAscii":  case "majorAscii":  return MajorHAnsi;
                    case "MinorAscii":  case "minorAscii":  return MinorHAnsi;
                    default:
                        if (theme.StartsWith("minor")) return MinorHAnsi;
                        if (theme.StartsWith("Major")) return MajorHAnsi;
                        return null;
                }
            }
        }

        internal static ThemeFontMap BuildThemeFonts(ThemePart part)
        {
            var map = new ThemeFontMap { MajorBidi = "Times New Roman", MinorBidi = "Arial", MajorHAnsi = "Calibri Light", MinorHAnsi = "Calibri" };
            if (part == null) return map;
            try
            {
                using (var stream = part.GetStream())
                {
                    var xml = new System.Xml.XmlDocument();
                    xml.Load(stream);
                    var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
                    ns.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
                    var majorHebr  = xml.SelectSingleNode("//a:majorFont/a:font[@script='Hebr']/@typeface", ns)?.Value;
                    var minorHebr  = xml.SelectSingleNode("//a:minorFont/a:font[@script='Hebr']/@typeface", ns)?.Value;
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

        private struct DocDefaultsInfo
        {
            public string FontName;
            public float  FontSize, SpaceAfter, LineSpacing;
        }

        private static DocDefaultsInfo BuildDocDefaults(StyleDefinitionsPart part, ThemeFontMap theme)
        {
            var d = new DocDefaultsInfo { FontName = theme.MinorBidi, FontSize = 12f, SpaceAfter = 8f, LineSpacing = 1.15f };
            if (part?.Styles == null) return d;

            var rPrDef = part.Styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
            if (rPrDef?.RunFonts != null)
            {
                string fontName = null;
                var csTheme = rPrDef.RunFonts.ComplexScriptTheme?.Value;
                if (csTheme != null) fontName = theme.Resolve(csTheme.ToString());
                if (fontName == null)
                {
                    try
                    {
                        var attr = rPrDef.RunFonts.GetAttribute("cstheme", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                        if (!string.IsNullOrEmpty(attr.Value)) fontName = theme.Resolve(attr.Value);
                    }
                    catch { }
                }
                if (fontName == null) fontName = rPrDef.RunFonts.ComplexScript?.Value;
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
                if (spc?.After?.Value != null && int.TryParse(spc.After.Value, out int sa)) d.SpaceAfter  = sa / 20f;
                if (spc?.Line?.Value  != null && int.TryParse(spc.Line.Value,  out int ls) && ls > 0) d.LineSpacing = ls / 240f;
            }
            return d;
        }

        internal struct StyleInfo
        {
            public float?  FontSize;
            public string  FontName, Color, BasedOn;
            public bool    Bold, Italic, ContextualSpacing;
            public float   SpaceBefore, SpaceAfter, IndentLeft;
        }

        private static string ResolveRunFont(RunFonts fonts, ThemeFontMap theme)
        {
            if (fonts == null) return null;
            var csTheme    = fonts.ComplexScriptTheme?.Value;
            var hAnsiTheme = fonts.HighAnsiTheme?.Value;
            string name = null;
            if (csTheme    != null) name = theme.Resolve(csTheme.ToString());
            if (name == null && hAnsiTheme != null) name = theme.Resolve(hAnsiTheme.ToString());
            if (name == null)
            {
                // GetAttribute throws KeyNotFoundException if attribute absent — use HasAttributes check
                try
                {
                    var attr = fonts.GetAttribute("cstheme", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                    if (!string.IsNullOrEmpty(attr.Value)) name = theme.Resolve(attr.Value);
                }
                catch { }
            }
            return name ?? fonts.ComplexScript?.Value ?? fonts.Ascii?.Value;
        }

        internal static Dictionary<string, StyleInfo> BuildStyleMap(StyleDefinitionsPart part, ThemeFontMap theme, TextWriter log = null)
        {
            var map = new Dictionary<string, StyleInfo>(StringComparer.OrdinalIgnoreCase);
            if (part?.Styles == null) return map;

            foreach (var style in part.Styles.Elements<Style>())
            {
                var id = style.StyleId?.Value;
                if (id == null) continue;
                var si = new StyleInfo { BasedOn = style.BasedOn?.Val?.Value };

                var rpr = style.StyleRunProperties;
                si.FontName = ResolveRunFont(rpr?.RunFonts, theme);

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

            // Resolve basedOn inheritance
            foreach (var id in map.Keys.ToList())
            {
                var si = map[id];
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
                var parent = si.BasedOn;
                while (parent != null && map.TryGetValue(parent, out var p) && !visited.Contains(parent))
                {
                    visited.Add(parent);
                    if (si.FontName  == null && p.FontName  != null) si.FontName  = p.FontName;
                    if (si.FontSize  == null && p.FontSize  != null) si.FontSize  = p.FontSize;
                    if (si.Color     == null && p.Color     != null) si.Color     = p.Color;
                    if (!si.Bold     && p.Bold)                      si.Bold      = true;
                    if (!si.Italic   && p.Italic)                    si.Italic    = true;
                    if (!si.ContextualSpacing && p.ContextualSpacing) si.ContextualSpacing = true;
                    if (si.SpaceBefore == 0 && p.SpaceBefore > 0)   si.SpaceBefore = p.SpaceBefore;
                    if (si.SpaceAfter  == 0 && p.SpaceAfter  > 0)   si.SpaceAfter  = p.SpaceAfter;
                    parent = p.BasedOn;
                }
                map[id] = si;
            }
            return map;
        }
    }
}
