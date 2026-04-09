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
        private struct NumLevelDef
        {
            public string Format, LvlText;
            public int    Start;
            public float  IndentLeft, IndentRight, Hanging;
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
                    var lvlText = lvl.LevelText?.Val?.Value ?? "%1.";

                    Indentation ind = null;
                    var pprEl = lvl.ChildElements.FirstOrDefault(e => e.LocalName == "pPr");
                    if (pprEl != null) ind = pprEl.Elements<Indentation>().FirstOrDefault();

                    float indL = 0, hanging = 0;
                    if (ind?.Left?.Value    != null && int.TryParse(ind.Left.Value,    out int il)) indL    = il / 20f;
                    if (ind?.Hanging?.Value != null && int.TryParse(ind.Hanging.Value, out int hg)) hanging = hg / 20f;

                    var def = docRtl
                        ? new NumLevelDef { Format = fmt, LvlText = lvlText, Start = start, IndentRight = indL, Hanging = hanging }
                        : new NumLevelDef { Format = fmt, LvlText = lvlText, Start = start, IndentLeft  = indL, Hanging = hanging };
                    map[$"{numId}:{ilvl}"] = def;
                    log?.WriteLine($"[Numbering] numId={numId} ilvl={ilvl} fmt={fmt} start={start} indL={def.IndentLeft} indR={def.IndentRight} hanging={def.Hanging}");
                }
            }
            return map;
        }

        private static string FormatListPrefix(string format, string lvlText, int counter, int level)
        {
            if (format.ToLower() == "bullet")
            {
                if (string.IsNullOrEmpty(lvlText)) return "•";
                if (lvlText.Length == 1)
                {
                    char c = lvlText[0];
                    if (c < 0x20 || (c >= 0xF000 && c <= 0xF0FF)) return "•";
                }
                return lvlText;
            }
            string numStr = GetFormattedNumber(format, counter);
            string result = lvlText ?? $"{counter}.";
            result = result.Replace($"%{level + 1}", numStr);
            for (int i = 1; i <= 9; i++) result = result.Replace($"%{i}", "");
            return result;
        }

        private static string GetFormattedNumber(string format, int counter)
        {
            switch (format.ToLower())
            {
                case "decimal":     return counter.ToString();
                case "lowerroman":  return ToRoman(counter).ToLower();
                case "upperroman":  return ToRoman(counter);
                case "lowerletter": return ((char)('a' + (counter - 1) % 26)).ToString();
                case "upperletter": return ((char)('A' + (counter - 1) % 26)).ToString();
                case "decimalzero": return counter.ToString("D2");
                default:            return counter.ToString();
            }
        }

        private static string ToRoman(int n)
        {
            if (n < 1) return "";
            var vals = new[] { 1000,900,500,400,100,90,50,40,10,9,5,4,1 };
            var syms = new[] { "M","CM","D","CD","C","XC","L","XL","X","IX","V","IV","I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < vals.Length; i++)
                while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }
    }
}
