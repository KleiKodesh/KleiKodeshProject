using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace WpfLib.Helpers
{
    /// <summary>
    /// The WPF font list for pickers — Hebrew-capable families first, alphabetical within each
    /// group. A thin projection of <see cref="FontsProvider"/>, which is the single font source
    /// shared across the solution; see that class for why DirectWrite replaced WPF's
    /// Fonts.SystemFontFamilies (a process-lifetime snapshot that never sees fonts installed
    /// while the app runs).
    /// </summary>
    public static class FontsHelper
    {
        /// <summary>Every system font family as a WPF FontFamily, Hebrew ones first. Enumerates
        /// fresh on every call so fonts installed mid-session show up — expect roughly a second,
        /// so call it off the UI thread and show a loading row.</summary>
        public static List<FontFamily> GetFontsCollection()
        {
            return FontsProvider.GetFontFamilies()
                                .Select(font => new FontFamily(font.Name))
                                .ToList();
        }

        /// <summary>True when the family has a glyph for א.</summary>
        public static bool HasHebCharacters(this FontFamily family)
        {
            return FontsProvider.HasHebrew(family.Source);
        }
    }
}
