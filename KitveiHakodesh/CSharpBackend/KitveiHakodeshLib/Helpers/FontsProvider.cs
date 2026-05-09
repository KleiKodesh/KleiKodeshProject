using System.Linq;
using System.Windows.Media;

namespace KitveiHakodeshLib.Helpers
{
    /// <summary>
    /// Pure data layer for system font enumeration.
    /// Returns Hebrew-capable font family names; has no knowledge of the web bridge.
    /// </summary>
    public static class FontsProvider
    {
        /// <summary>
        /// Returns the names of all system font families that contain Hebrew characters,
        /// sorted alphabetically.
        /// </summary>
        public static string[] GetHebrewFonts()
        {
            return System.Windows.Media.Fonts.SystemFontFamilies
                .Where(HasHebrewCharacters)
                .Select(f => f.Source)
                .OrderBy(n => n)
                .ToArray();
        }

        private static bool HasHebrewCharacters(FontFamily family)
        {
            return family.GetTypefaces().Any(typeface =>
            {
                GlyphTypeface glyph;
                return typeface.TryGetGlyphTypeface(out glyph)
                    && glyph.CharacterToGlyphMap.ContainsKey('א');
            });
        }
    }
}
