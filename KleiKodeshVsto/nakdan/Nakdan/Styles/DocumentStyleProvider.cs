using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Nakdan.Core;
using Word = Microsoft.Office.Interop.Word;

namespace Nakdan.Styles
{
    /// <summary>
    /// Utility for extracting and providing style information from Word documents.
    /// Works directly with OOXML, no verification needed.
    /// </summary>
    public class DocumentStyleProvider
    {
        private readonly Word.Application _app;

        public DocumentStyleProvider(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>
        /// Get all styles used in the active document as DocumentStyle objects.
        /// Uses doc.Styles directly for the style list — no full document OOXML needed.
        /// Built-in styles are translated via NameLocal; custom styles use their name as-is.
        /// </summary>
        public List<DocumentStyle> GetUsedStyles()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] GetUsedStyles called");

                Word.Document doc = _app.ActiveDocument;
                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] No active document");
                    return new List<DocumentStyle>();
                }

                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] Active document found: {doc.Name}");

                // Use doc.Styles directly — already in memory, no OOXML fetch needed.
                // Get the style IDs from a small OOXML sample (first character only).
                string ooxml = doc.Range(0, 0).WordOpenXML;
                var xdoc = XDocument.Parse(ooxml);
                var allStyles = StyleExtractor.ExtractStylesFromOoxml(xdoc);

                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] Extracted {allStyles.Count} style definitions");

                var result = new List<DocumentStyle>();
                foreach (var kvp in allStyles)
                {
                    string styleId = kvp.Key;
                    string ooxmlName = kvp.Value;

                    string displayName = StyleNameResolver.Resolve(ooxmlName, doc);

                    result.Add(new DocumentStyle(styleId, displayName));
                    System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider]   - {styleId} = {displayName}");
                }

                result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] Returning {result.Count} styles");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DocumentStyleProvider] Stack trace: {ex.StackTrace}");
                return new List<DocumentStyle>();
            }
        }

        /// <summary>
        /// Get the style of the first character in the active document.
        /// Useful for determining the primary style of the document.
        /// </summary>
        public string GetFirstCharacterStyle()
        {
            try
            {
                Word.Document doc = _app.ActiveDocument;
                if (doc == null)
                    return string.Empty;

                string ooxml = doc.WordOpenXML;
                var xdoc = XDocument.Parse(ooxml);

                var allStyles = StyleExtractor.ExtractStylesFromOoxml(xdoc);
                return StyleExtractor.ExtractFirstCharacterStyle(xdoc, allStyles);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Get all available styles (both used and unused) from the active document.
        /// </summary>
        public Dictionary<string, string> GetAllStyles()
        {
            try
            {
                Word.Document doc = _app.ActiveDocument;
                if (doc == null)
                    return new Dictionary<string, string>();

                string ooxml = doc.WordOpenXML;
                var xdoc = XDocument.Parse(ooxml);

                return StyleExtractor.ExtractStylesFromOoxml(xdoc);
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
