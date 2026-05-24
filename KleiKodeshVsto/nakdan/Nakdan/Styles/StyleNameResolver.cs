using System;
using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace Nakdan.Styles
{
    /// <summary>
    /// Resolves style names to localized display names using Word's built-in style enum.
    /// For built-in styles, uses Word's NameLocal (e.g. "Normal" → "רגיל").
    /// For custom styles, falls back to the OOXML name as-is.
    /// </summary>
    public static class StyleNameResolver
    {
        // Built once: normalized OOXML name (spaces stripped, lowercase) → WdBuiltinStyle
        // e.g. "heading1" → wdStyleHeading1
        private static readonly Dictionary<string, Word.WdBuiltinStyle> _enumMap = BuildEnumMap();

        private static Dictionary<string, Word.WdBuiltinStyle> BuildEnumMap()
        {
            var map = new Dictionary<string, Word.WdBuiltinStyle>(
                StringComparer.OrdinalIgnoreCase);

            foreach (Word.WdBuiltinStyle value in Enum.GetValues(typeof(Word.WdBuiltinStyle)))
            {
                // e.g. "wdStyleHeading1" → strip "wdStyle" → "Heading1" → lowercase → "heading1"
                string key = value.ToString()
                    .Replace("wdStyle", "")
                    .ToLowerInvariant();

                if (!map.ContainsKey(key))
                    map[key] = value;
            }

            return map;
        }

        /// <summary>
        /// Resolve an OOXML style name to a localized display name.
        /// Uses Word's NameLocal for built-in styles; returns ooxmlName for custom styles.
        /// </summary>
        public static string Resolve(string ooxmlName, Word.Document doc)
        {
            if (string.IsNullOrWhiteSpace(ooxmlName) || doc == null)
                return ooxmlName;

            // Normalize OOXML name: strip spaces, lowercase
            // e.g. "heading 1" → "heading1"
            string key = ooxmlName
                .Replace(" ", "")
                .ToLowerInvariant();

            if (_enumMap.TryGetValue(key, out Word.WdBuiltinStyle builtInStyle))
            {
                try
                {
                    return doc.Styles[builtInStyle].NameLocal;
                }
                catch { /* style not present in this document, fall through */ }
            }

            // Custom style — OOXML name is already the right display name
            return ooxmlName;
        }
    }
}
