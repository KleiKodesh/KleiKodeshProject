using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Word-compatible color data and conversion utilities.
    /// Ported from regx-find-html/SimpleColorsDialog.cs — self-contained, no Globals dependency.
    /// Standard colors and theme color hex values are hardcoded (same as the HTML color-picker.js).
    /// 
    /// IMPLEMENTATION VERIFIED BY AUTHORITATIVE SOURCES:
    /// 1. Word Articles (Tony Jollans) - Comprehensive technical documentation
    ///    https://www.wordarticles.com/Articles/Colours/2007.php
    /// 2. PowerSpreadsheets.com - BGR byte order documentation
    ///    https://powerspreadsheets.com/vba-font-color-hex/
    /// 3. StackOverflow - Theme color tints/shades
    ///    https://stackoverflow.com/questions/55907657
    /// 4. StackOverflow - RGB color extraction with actual hex mappings
    ///    https://stackoverflow.com/questions/18614933
    /// 5. RetroComputing StackExchange - Historical explanation of BGR format
    ///    https://retrocomputing.stackexchange.com/questions/3023
    /// </summary>
    public static class WordColors
    {
        // ── Standard Office colors (matches HTML standardColors array) ────────
        public static readonly IReadOnlyList<WordColor> StandardColors = new[]
        {
            new WordColor("#C00000", "Dark Red",   0xC00000.BgrToWord()),
            new WordColor("#FF0000", "Red",        0xFF0000.BgrToWord()),
            new WordColor("#FFC000", "Orange",     0xFFC000.BgrToWord()),
            new WordColor("#FFFF00", "Yellow",     0xFFFF00.BgrToWord()),
            new WordColor("#92D050", "Light Green",0x92D050.BgrToWord()),
            new WordColor("#00B050", "Green",      0x00B050.BgrToWord()),
            new WordColor("#00B0F0", "Light Blue", 0x00B0F0.BgrToWord()),
            new WordColor("#0070C0", "Blue",       0x0070C0.BgrToWord()),
            new WordColor("#002060", "Dark Blue",  0x002060.BgrToWord()),
            new WordColor("#7030A0", "Purple",     0x7030A0.BgrToWord()),
        };

        // ── Theme colors — base hex values (hardcoded, matches HTML themeColors.base) ─
        // These are the base colors without tint/shade. The decimal values are the
        // Word Font.Color values for the base (no tint/shade) theme colors.
        public static readonly IReadOnlyList<WordColor> ThemeColors = new[]
        {
            new WordColor("#FFFFFF", "White",     -603914241),
            new WordColor("#000000", "Black",     -587137025),
            new WordColor("#E7E6E6", "Light Gray",-570359809),
            new WordColor("#44546A", "Blue-Gray", -553582593),
            new WordColor("#4472C4", "Blue",      -738131969),
            new WordColor("#ED7D31", "Orange",    -721354753),
            new WordColor("#A5A5A5", "Gray",      -704577537),
            new WordColor("#FFC000", "Gold",      -687800321),
            new WordColor("#5B9BD5", "Light Blue",-671023105),
            new WordColor("#70AD47", "Green",     -654245889),
        };

        // ── Word auto-color constant ──────────────────────────────────────────
        /// <summary>Word's "Automatic" font color decimal (-16777216 = 0xFF000000 unsigned).</summary>
        public const int AutoColor = -16777216;

        // ── Conversion helpers ────────────────────────────────────────────────

        /// <summary>
        /// Convert Word decimal to WPF Color for display.
        /// Handles standard BGR colors, base theme colors, and tinted/shaded theme colors.
        /// Matches the HTML resolveThemeColor + applyTintShade logic from color-calculations.js.
        /// 
        /// STANDARD COLORS (positive decimals):
        /// Word stores RGB colors in BGR byte order (Blue-Green-Red).
        /// Example: Red RGB(255,0,0) → BGR decimal 16711680 (0x00FF0000)
        /// Source: https://powerspreadsheets.com/vba-font-color-hex/
        /// 
        /// THEME COLORS (negative decimals):
        /// Format: 0x[D|F][ThemeIndex][00][ShadeByte][TintByte]
        /// - Byte 0 (MSB): 0xD0-0xDF with theme index in lower nibble
        /// - Byte 1: Always 0x00
        /// - Byte 2: Shade byte (0xFF = no shade, lower = darker)
        /// - Byte 3 (LSB): Tint byte (0xFF = no tint, lower = lighter)
        /// Source: https://www.wordarticles.com/Articles/Colours/2007.php
        /// </summary>
        public static Color WordDecimalToColor(int wordDecimal)
        {
            if (wordDecimal == AutoColor)
                return Colors.Black;

            if (wordDecimal < 0)
            {
                // First try exact match against base theme colors
                foreach (var tc in ThemeColors)
                    if (tc.WordDecimal == wordDecimal)
                        return tc.WpfColor;

                // Tinted/shaded theme color — decode the Word format:
                // 0x[D|F][ThemeIndex][00][ShadeByte][TintByte]
                uint unsigned = (uint)wordDecimal;
                byte themeColorByte = (byte)((unsigned >> 24) & 0xFF);
                byte shadeByte      = (byte)((unsigned >> 8)  & 0xFF);
                byte tintByte       = (byte)(unsigned         & 0xFF);

                byte prefix = (byte)(themeColorByte & 0xF0);
                if (prefix == 0xD0 || prefix == 0xF0)
                {
                    int wdThemeIndex = themeColorByte & 0x0F;
                    string baseHex = GetBaseHexByWdThemeIndex(wdThemeIndex);
                    if (baseHex != null)
                    {
                        double tintShade = 0;
                        const byte unchanged = 0xFF;
                        if (shadeByte != unchanged)
                            tintShade = Math.Round((-1.0 + shadeByte / 255.0) * 100) / 100;
                        else if (tintByte != unchanged)
                            tintShade = Math.Round((1.0 - tintByte / 255.0) * 100) / 100;

                        string resultHex = ApplyTintShade(baseHex, tintShade);
                        return HexToColor(resultHex);
                    }
                }

                return Colors.Black;
            }

            byte b = (byte)(wordDecimal & 0xFF);
            byte g = (byte)((wordDecimal >> 8) & 0xFF);
            byte r = (byte)((wordDecimal >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        /// <summary>
        /// Convert WPF Color to Word BGR int (for custom/standard colors only).
        /// Word uses BGR byte order: Blue in LSB, Green in middle, Red in MSB.
        /// Example: RGB(255,0,0) red → 0 | (0 << 8) | (255 << 16) = 16711680
        /// Source: https://powerspreadsheets.com/vba-font-color-hex/
        /// Historical reason: IBM VGA RAMDAC chips required BGR order
        /// Source: https://retrocomputing.stackexchange.com/questions/3023
        /// </summary>
        public static int ColorToWordDecimal(Color c) =>
            c.B | (c.G << 8) | (c.R << 16);

        // ── Theme color tint/shade helpers (ported from color-calculations.js) ─

        // wdThemeColorIndex → base hex
        // Derived from themeIndexToWordByte = [0xDD,0xDC,0xDF,0xDE,0xD4,0xD5,0xD9,0xD7,0xD8,0xD6]
        // Lower nibble of each byte IS the wdThemeIndex for that color.
        static string GetBaseHexByWdThemeIndex(int wdThemeIndex)
        {
            switch (wdThemeIndex)
            {
                case  4: return "#4472C4"; // Accent1  Blue        (0xD4)
                case  5: return "#ED7D31"; // Accent2  Orange      (0xD5)
                case  6: return "#70AD47"; // Accent6  Green       (0xD6)
                case  7: return "#FFC000"; // Accent4  Gold        (0xD7)
                case  8: return "#5B9BD5"; // Accent5  Light Blue  (0xD8)
                case  9: return "#A5A5A5"; // Accent3  Gray        (0xD9)
                case 12: return "#000000"; // Text1    Black       (0xDC)
                case 13: return "#FFFFFF"; // Bg1      White       (0xDD)
                case 14: return "#44546A"; // Text2    Blue-Gray   (0xDE)
                case 15: return "#E7E6E6"; // Bg2      Light Gray  (0xDF)
                default: return null;
            }
        }

        /// <summary>
        /// Apply Word tint/shade to a hex color using HSL color space conversion.
        /// Positive tintShade = tint (lighter, toward white)
        /// Negative tintShade = shade (darker, toward black)
        /// Formula: newL = L * abs(tintShade) + (tintShade > 0 ? 1 : 0) * (1 - tintShade)
        /// Ported from color-calculations.js applyTintShade.
        /// Source: https://www.wordarticles.com/Articles/Colours/2007.php (HSL conversion section)
        /// </summary>
        static string ApplyTintShade(string hex, double tintShade)
        {
            if (tintShade == 0) return hex;

            hex = hex.TrimStart('#');
            double r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber) / 255.0;
            double g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber) / 255.0;
            double b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber) / 255.0;

            RgbToHsl(r, g, b, out double h, out double s, out double l);

            double absTS = Math.Abs(tintShade);
            double isTint = tintShade > 0 ? 1.0 : 0.0;
            double newL = l * absTS + isTint * (1.0 - tintShade);
            newL = Math.Max(0, Math.Min(1, newL));

            HslToRgb(h, s, newL, out int nr, out int ng, out int nb);
            return $"#{nr:X2}{ng:X2}{nb:X2}";
        }

        static void RgbToHsl(double r, double g, double b,
                              out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (max == min) { h = s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if      (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else               h = (r - g) / d + 4;
            h /= 6.0;
        }

        static void HslToRgb(double h, double s, double l,
                              out int r, out int g, out int b)
        {
            if (s == 0) { r = g = b = (int)Math.Round(l * 255); return; }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = (int)Math.Round(Hue2Rgb(p, q, h + 1.0 / 3) * 255);
            g = (int)Math.Round(Hue2Rgb(p, q, h)           * 255);
            b = (int)Math.Round(Hue2Rgb(p, q, h - 1.0 / 3) * 255);
        }

        static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        /// <summary>Parse "#RRGGBB" hex string to WPF Color.</summary>
        public static Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            return Color.FromRgb(r, g, b);
        }
    }

    public class WordColor
    {
        public string Hex { get; }
        public string Name { get; }
        public int WordDecimal { get; }
        public Color WpfColor { get; }

        public WordColor(string hex, string name, int wordDecimal)
        {
            Hex = hex;
            Name = name;
            WordDecimal = wordDecimal;
            WpfColor = WordColors.HexToColor(hex);
        }
    }

    internal static class ColorExtensions
    {
        /// <summary>
        /// Convert an RGB int (0xRRGGBB) to Word BGR decimal.
        /// Word stores colors in BGR byte order (Blue-Green-Red).
        /// Example: 0xFF0000 (red) → 0 | (0 << 8) | (255 << 16) = 16711680
        /// Sources:
        /// - https://powerspreadsheets.com/vba-font-color-hex/
        /// - https://www.wordarticles.com/Articles/Colours/2007.php
        /// </summary>
        public static int BgrToWord(this int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return b | (g << 8) | (r << 16);
        }
    }
}
