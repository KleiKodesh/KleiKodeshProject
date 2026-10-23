using System.Text;

namespace Nakdan.Core
{
    public static class HebrewTextExtensions
    {
        // Points only. U+05B0..U+05C7 also holds punctuation that must survive
        // as text: maqaf U+05BE, paseq U+05C0, sof pasuq U+05C3, nun hafukha U+05C6.
        // Stripping the maqaf joined hyphenated words before they reached Dicta.
        // Same set as IsNikkudChar, as a Word wildcard / regex character-class body.
        public const string NikkudCharClass =
            "\u05B0-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7";

        public static bool IsNikkudChar(this char c)
        {
            return (c >= '\u05B0' && c <= '\u05BD')   // vowels, dagesh, meteg
                || c == '\u05BF'                        // rafe
                || c == '\u05C1' || c == '\u05C2'       // shin/sin dots
                || c == '\u05C4' || c == '\u05C5'       // upper/lower dot
                || c == '\u05C7';                       // qamats qatan
        }

        public static string StripNikkud(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new StringBuilder(s.Length);

            foreach (char c in s)
            {
                if (!c.IsNikkudChar())
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private static bool ContainsHebrew(this string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= '\u05D0' && c <= '\u05EA') return true;
            return false;
        }
    }
}
