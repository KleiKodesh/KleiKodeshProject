using System;
using System.Collections.Generic;
using System.Linq;

namespace RegexInWord.SimpleColorsDialog
{
    public static class Hex
    {
        public static int? HexToInt(this string hexColor)
        {
            if (string.IsNullOrEmpty(hexColor)) return null;

            string cleanedHex = hexColor.TrimStart('#');
            string reordered = ReverseHex(cleanedHex);
            return Convert.ToInt32(reordered, 16);
        }

        public static string IntToHex(this int color)
        {
            string toHex = color.ToString("X6");
            return ReverseHex(toHex);
        }

        public static string ReverseHex(this string input)
        {
            List<string> chunks = new List<string>();
            for (int i = 0; i < input.Length; i += 2)
            {
                int length = Math.Min(2, input.Length - i);
                chunks.Add(input.Substring(i, length));
            }

            chunks.Reverse();
            return string.Join("", chunks);
        }

        //public static string ThemeColorHex(string wdHex)
        //{
        //    string hexId = wdHex.Substring(1, 1);
        //    int colorThemeIndex = (int)Hex.HexToInt(hexId);
        //    var colorScheme = GetThemeColorSchemeIndex(colorThemeIndex);
        //    var colorFormat = Globals.ThisAddIn.Application.ActiveDocument.DocumentTheme.ThemeColorScheme.Colors(colorScheme);
        //    return "#" + Hex.IntToHex(colorFormat.RGB);
        //}

        //public static MsoThemeColorSchemeIndex GetThemeColorSchemeIndex(int index)
        //{
        //    switch (index)
        //    {
        //        case 12: return (MsoThemeColorSchemeIndex)2;
        //        case 13: return (MsoThemeColorSchemeIndex)1;
        //        case 14: return (MsoThemeColorSchemeIndex)4;
        //        case 15: return (MsoThemeColorSchemeIndex)3;
        //        default: return (MsoThemeColorSchemeIndex)index + 1;
        //    }
        //}

        public static bool IsValidHexColor(this string val) =>
            !string.IsNullOrEmpty(val) && val.Length == 6 && val.All(IsHexDigit);

        public static bool IsHexDigit(this char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        //public static string AdjustHexColorBrightness(string hex, double percent)
        //{
        //    // Remove '#' if present
        //    if (hex.StartsWith("#"))
        //        hex = hex.Substring(1);

        //    // Parse hex to RGB
        //    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        //    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        //    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

        //    // Convert RGB to HSL
        //    Hsl.RgbToHsl(r, g, b, out double h, out double s, out double l);

        //    // Adjust brightness (lightness)
        //    l = Clamp(percent >= 0 ? l + (1 - l) * percent : l * (1 + percent), 0.0, 1.0);  // use percent = +0.2 to lighten, -0.2 to darken

        //    // Convert back to RGB
        //    Hsl.HslToRgb(h, s, l, out r, out g, out b);

        //    // Return new hex color
        //    return $"#{r:X2}{g:X2}{b:X2}";
        //}

        //public static double Clamp(double value, double min, double max)
        //{
        //    return (value < min) ? min : (value > max) ? max : value;
        //}

    }
}
