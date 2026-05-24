using System.Text;

namespace Nakdan.Core
{
    public static class HebrewTextExtensions
    {
        private const char NikkudFirst = '\u05B0';
        private const char NikkudLast = '\u05C7';
        private const char ShinDot = '\u05C1';
        private const char ShinDotSin = '\u05C2';

        public static bool IsNikkudChar(this char c)
        {
            return (c >= NikkudFirst && c <= NikkudLast)
                || c == ShinDot
                || c == ShinDotSin;
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
