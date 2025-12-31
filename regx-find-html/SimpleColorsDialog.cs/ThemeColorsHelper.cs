using Microsoft.Office.Core;
using System.Globalization;

namespace RegexInWord.SimpleColorsDialog
{
    public static class ThemeColorsHelper
    {
        public static string WdToWpfHex(string wdHex)
        {
            string hexId = wdHex.Substring(1, 1);
            int colorThemeIndex = (int)Hex.HexToInt(hexId);
            var colorScheme = GetThemeColorSchemeIndex(colorThemeIndex);
            var colorFormat = Globals.ThisAddIn.Application.ActiveDocument.DocumentTheme.ThemeColorScheme.Colors(colorScheme);

            // Base WPF color from theme
            string wpfHex = "#" + Hex.IntToHex(colorFormat.RGB);

            // Parse tint and shade values
            if (wdHex.Length >= 6)
            {
                byte shade = byte.Parse(wdHex.Substring(wdHex.Length - 4, 2), NumberStyles.HexNumber);
                byte tint = byte.Parse(wdHex.Substring(wdHex.Length - 2), NumberStyles.HexNumber);

                double brightnessFactor = 0;

                // Tint makes it lighter (255 means white), shade makes it darker (255 means no darkening)
                // Normalize both around 0, range [-1.0, +1.0]
                if (shade < 255)
                    brightnessFactor -= (1 - shade / 255.0); // darken
                if (tint < 255)
                    brightnessFactor += (1 - tint / 255.0);  // lighten

                wpfHex = AdjustHexColorBrightness(wpfHex, brightnessFactor);
            }

            return wpfHex;
        }

        static string AdjustHexColorBrightness(string hex, double percent)
        {
            // Remove '#' if present
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            // Parse hex to RGB
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

            // Convert RGB to HSL
            Hsl.RgbToHsl(r, g, b, out double h, out double s, out double l);

            // Adjust brightness (lightness)
            l = Clamp(percent >= 0 ? l + (1 - l) * percent : l * (1 + percent), 0.0, 1.0);  // use percent = +0.2 to lighten, -0.2 to darken

            // Convert back to RGB
            Hsl.HslToRgb(h, s, l, out r, out g, out b);

            // Return new hex color
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        static double Clamp(double value, double min, double max) =>
            (value < min) ? min : (value > max) ? max : value;


        static MsoThemeColorSchemeIndex GetThemeColorSchemeIndex(int index)
        {
            switch (index)
            {
                case 12: return (MsoThemeColorSchemeIndex)2;
                case 13: return (MsoThemeColorSchemeIndex)1;
                case 14: return (MsoThemeColorSchemeIndex)4;
                case 15: return (MsoThemeColorSchemeIndex)3;
                default: return (MsoThemeColorSchemeIndex)index + 1;
            }
        }
    }
}
