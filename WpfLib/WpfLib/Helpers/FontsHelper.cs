using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace WpfLib.Helpers
{
    public static class FontsHelper
    {
        public static List<FontFamily> FontsCollection { get; private set; } = new List<FontFamily>
                    (Fonts.SystemFontFamilies.OrderBy(font => font.HasHebCharacters() ? 0 : 1).ThenBy(font => font.Source));
       
        public static bool HasHebCharacters(this FontFamily family)
        {
            return family
                .GetTypefaces()
                .Any(typeface => typeface.TryGetGlyphTypeface(out var glyph) && glyph.CharacterToGlyphMap.ContainsKey('א'));
        }
    }
}
