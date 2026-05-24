using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Nakdan.Core;

namespace Nakdan.WdStyles
{
    /// <summary>
    /// Extracts style information directly from OOXML document.
    /// This is a simpler approach than verifying against Word's style list.
    /// </summary>
    public class StyleExtractor
    {
        /// <summary>
        /// Extract all style names used in the document from the styles.xml part.
        /// Returns a dictionary mapping style IDs to style names.
        /// </summary>
        public static Dictionary<string, string> ExtractStylesFromOoxml(XDocument doc)
        {
            var styleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (doc == null)
                return styleMap;

            // Find the styles part
            XElement stylesPart = doc
                .Descendants(OoxmlHelper.W + "styles")
                .FirstOrDefault();

            if (stylesPart == null)
                return styleMap;

            // Extract all style definitions
            foreach (XElement style in stylesPart.Descendants(OoxmlHelper.W + "style"))
            {
                string styleId = style
                    .Attribute(OoxmlHelper.W + "styleId")
                    ?.Value;

                string styleName = style
                    .Element(OoxmlHelper.W + "name")
                    ?.Attribute(OoxmlHelper.W + "val")
                    ?.Value;

                if (string.IsNullOrWhiteSpace(styleId) || string.IsNullOrWhiteSpace(styleName))
                    continue;

                styleMap[styleId] = styleName;
            }

            return styleMap;
        }

        /// <summary>
        /// Extract all unique styles actually used in the document body.
        /// Returns a list of style IDs that are applied to paragraphs.
        /// </summary>
        public static List<string> ExtractUsedStylesFromBody(XDocument doc, Dictionary<string, string> styleMap)
        {
            var usedStyleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (doc == null)
                return new List<string>();

            // Iterate through all paragraphs and collect their style IDs
            foreach (XElement para in OoxmlHelper.GetParagraphs(doc))
            {
                string styleId = OoxmlHelper.GetParagraphStyleId(para);

                // If no explicit style, use the default style "Normal" or "a"
                if (string.IsNullOrWhiteSpace(styleId))
                    styleId = "a"; // "a" is the default Normal style ID in OOXML

                usedStyleIds.Add(styleId);
            }

            return usedStyleIds.OrderBy(s => s).ToList();
        }

        /// <summary>
        /// Extract the first character's style from a range.
        /// This is useful for getting the style of the first character to determine formatting.
        /// </summary>
        public static string ExtractFirstCharacterStyle(XDocument doc, Dictionary<string, string> styleMap)
        {
            if (doc == null)
                return string.Empty;

            // Get the first paragraph
            XElement firstPara = OoxmlHelper.GetParagraphs(doc).FirstOrDefault();
            if (firstPara == null)
                return string.Empty;

            // Get the style ID from the paragraph
            string styleId = OoxmlHelper.GetParagraphStyleId(firstPara);

            if (string.IsNullOrWhiteSpace(styleId))
                return string.Empty;

            // Resolve to style name
            if (styleMap.TryGetValue(styleId, out string styleName))
                return styleName;

            return styleId;
        }

        /// <summary>
        /// Build a complete style information object from OOXML.
        /// This combines the style map with used styles for easy access.
        /// </summary>
        public class StyleInfo
        {
            /// <summary>Maps style IDs to style names from styles.xml</summary>
            public Dictionary<string, string> AllStyles { get; set; }

            /// <summary>List of styles actually used in the document body</summary>
            public List<string> UsedStyles { get; set; }

            /// <summary>The style of the first character in the document</summary>
            public string FirstCharacterStyle { get; set; }
        }

        /// <summary>
        /// Extract complete style information from OOXML document.
        /// </summary>
        public static StyleInfo ExtractStyleInfo(XDocument doc)
        {
            var allStyles = ExtractStylesFromOoxml(doc);
            var usedStyles = ExtractUsedStylesFromBody(doc, allStyles);
            var firstCharStyle = ExtractFirstCharacterStyle(doc, allStyles);

            return new StyleInfo
            {
                AllStyles = allStyles,
                UsedStyles = usedStyles,
                FirstCharacterStyle = firstCharStyle
            };
        }
    }
}
