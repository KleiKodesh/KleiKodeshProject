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

        // ── Conversion helpers ────────────────────────────────────────────────

        /// <summary>Convert Word BGR int to WPF Color.</summary>
        public static Color WordDecimalToColor(int wordDecimal)
        {
            if (wordDecimal < 0)
            {
                // Theme color — look up in our table
                foreach (var tc in ThemeColors)
                    if (tc.WordDecimal == wordDecimal)
                        return tc.WpfColor;
                return Colors.Black;
            }
            byte r = (byte)(wordDecimal & 0xFF);
            byte g = (byte)((wordDecimal >> 8) & 0xFF);
            byte b = (byte)((wordDecimal >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        /// <summary>Convert WPF Color to Word BGR int.</summary>
        public static int ColorToWordDecimal(Color c) =>
            c.R | (c.G << 8) | (c.B << 16);

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
        /// <summary>Convert an RGB int (0xRRGGBB) to Word BGR decimal.</summary>
        public static int BgrToWord(this int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return r | (g << 8) | (b << 16);
        }
    }
}
