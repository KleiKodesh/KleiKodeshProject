using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Represents a single font in the font picker.
    /// IsHebrew is true when the font's glyph map contains 'א' — same detection
    /// used by KitveiHakodeshLib.Helpers.FontsProvider.
    /// </summary>
    public class FontItem
    {
        public string Name     { get; }
        public bool   IsHebrew { get; }

        /// <summary>Preview text shown in the dropdown — Hebrew sample for Hebrew fonts,
        /// Latin sample for all others.</summary>
        public string Preview  => IsHebrew ? "אבגד הוז" : "ABC abc";

        public FontItem(string name, bool isHebrew)
        {
            Name     = name;
            IsHebrew = isHebrew;
        }

        /// <summary>
        /// Returns true when the font family contains a glyph for 'א'.
        /// </summary>
        public static bool DetectHebrew(string fontName)
        {
            try
            {
                var family = new FontFamily(fontName);
                foreach (var typeface in family.GetTypefaces())
                {
                    GlyphTypeface glyph;
                    if (typeface.TryGetGlyphTypeface(out glyph) &&
                        glyph.CharacterToGlyphMap.ContainsKey('א'))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // So the editable ComboBox can display/match by name
        public override string ToString() => Name;
    }
}
