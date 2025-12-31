# Working with Word Theme Colors in C# VSTO

A comprehensive guide to understanding and manipulating Microsoft Word's theme color system programmatically in C#.

## Introduction

Microsoft Word 2007+ introduced a sophisticated theme color system that allows colors to be specified either as absolute RGB values or as references to theme colors with optional tint/shade adjustments. This guide explains how to work with these colors in C# VSTO add-ins.

When you set a font color or highlight in Word, the color can be stored in two fundamentally different ways:
- **Absolute RGB colors**: Fixed colors stored as BGR values (positive integers)
- **Theme colors**: References to the document's color scheme with optional lightness adjustments (negative integers)

Understanding this distinction is critical for any VSTO application that needs to read, compare, or set colors programmatically.

## Color Storage Format

All colors in Word are stored as 32-bit signed integers (`int` in C#). The most significant byte determines the color type:

| First Byte | Color Type | Example |
|------------|------------|---------|
| `0x00` | Absolute RGB (BGR format) | `0x00FF0000` = Blue |
| `0x80` | System color | `0x80000005` = Window background |
| `0xD0-0xDF` | Theme color | `0xD400FFFF` = Accent 1 |
| `0xFF` | Automatic | `0xFF000000` = Auto color |

## Theme Color Encoding

Theme colors use a specific 4-byte format:

```
Byte 0 (MSB): 0xD[ThemeIndex]  - Theme indicator + wdThemeColorIndex
Byte 1:       0x00             - Reserved (always zero)
Byte 2:       Shade byte       - 0xFF = unchanged, lower = darker
Byte 3 (LSB): Tint byte        - 0xFF = unchanged, lower = lighter
```

### wdThemeColorIndex Values

The lower nibble of the first byte contains the `wdThemeColorIndex`:

```csharp
public enum WdThemeColorIndex
{
    MainDark1 = 0,        // Same as Text1
    MainLight1 = 1,       // Same as Background1
    MainDark2 = 2,        // Same as Text2
    MainLight2 = 3,       // Same as Background2
    Accent1 = 4,
    Accent2 = 5,
    Accent3 = 6,
    Accent4 = 7,
    Accent5 = 8,
    Accent6 = 9,
    Hyperlink = 10,
    FollowedHyperlink = 11,
    Background1 = 12,
    Text1 = 13,
    Background2 = 14,
    Text2 = 15
}
```

## C# Implementation

### Color Type Detection

```csharp
public enum WordColorType
{
    RGB,
    Automatic,
    System,
    Theme,
    Unknown
}

public static WordColorType GetColorType(int colorValue)
{
    // Convert to unsigned for bit manipulation
    uint unsigned = (uint)colorValue;
    byte firstByte = (byte)(unsigned >> 24);
    
    if (firstByte == 0x00)
        return WordColorType.RGB;
    
    if (firstByte == 0xFF)
        return WordColorType.Automatic;
    
    if (firstByte == 0x80)
        return WordColorType.System;
    
    if ((firstByte & 0xF0) == 0xD0)
        return WordColorType.Theme;
    
    return WordColorType.Unknown;
}
```

### Decoding Theme Colors

```csharp
public class ThemeColorInfo
{
    public int WdThemeColorIndex { get; set; }
    public double TintAndShade { get; set; }
    public byte ThemeByte { get; set; }
    public byte ShadeByte { get; set; }
    public byte TintByte { get; set; }
}

public static ThemeColorInfo DecodeThemeColor(int colorValue)
{
    if (GetColorType(colorValue) != WordColorType.Theme)
        return null;
    
    uint unsigned = (uint)colorValue;
    
    byte themeByte = (byte)(unsigned >> 24);
    byte shadeByte = (byte)(unsigned >> 8);
    byte tintByte = (byte)(unsigned & 0xFF);
    
    int wdIndex = themeByte & 0x0F;
    
    // Calculate tint/shade value
    double tintAndShade = 0;
    const byte Unchanged = 0xFF;
    
    if (shadeByte != Unchanged)
    {
        // Shade (darker): -1 + (shadeByte / 255)
        tintAndShade = Math.Round(-1.0 + (shadeByte / 255.0), 2);
    }
    
    if (tintByte != Unchanged)
    {
        // Tint (lighter): 1 - (tintByte / 255)
        tintAndShade = Math.Round(1.0 - (tintByte / 255.0), 2);
    }
    
    return new ThemeColorInfo
    {
        WdThemeColorIndex = wdIndex,
        TintAndShade = tintAndShade,
        ThemeByte = themeByte,
        ShadeByte = shadeByte,
        TintByte = tintByte
    };
}
```

### Encoding Theme Colors

```csharp
public static int EncodeThemeColor(int wdThemeColorIndex, double tintAndShade = 0)
{
    if (wdThemeColorIndex < 0 || wdThemeColorIndex > 15)
        throw new ArgumentOutOfRangeException(nameof(wdThemeColorIndex));
    
    byte themeByte = (byte)(0xD0 | (wdThemeColorIndex & 0x0F));
    byte shadeValue = 0xFF;  // Unchanged
    byte tintValue = 0xFF;   // Unchanged
    
    if (tintAndShade < 0)
    {
        // Shade (darker): shadeByte = (tintAndShade + 1) * 255
        shadeValue = (byte)Math.Round((tintAndShade + 1) * 255);
        shadeValue = Math.Max((byte)0, Math.Min((byte)255, shadeValue));
    }
    else if (tintAndShade > 0)
    {
        // Tint (lighter): tintByte = (1 - tintAndShade) * 255
        tintValue = (byte)Math.Round((1 - tintAndShade) * 255);
        tintValue = Math.Max((byte)0, Math.Min((byte)255, tintValue));
    }
    
    // Construct the 32-bit value
    uint result = ((uint)themeByte << 24) | (0x00u << 16) | ((uint)shadeValue << 8) | tintValue;
    
    // Convert to signed int
    return unchecked((int)result);
}
```

### RGB/BGR Conversion

Word stores RGB colors in BGR byte order:

```csharp
public static int RgbToWordDecimal(byte r, byte g, byte b)
{
    return (b << 16) | (g << 8) | r;
}

public static int HexToWordDecimal(string hexColor)
{
    string hex = hexColor.TrimStart('#');
    if (hex.Length != 6)
        throw new ArgumentException("Invalid hex color format");
    
    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
    
    return RgbToWordDecimal(r, g, b);
}

public static (byte R, byte G, byte B) WordDecimalToRgb(int wordDecimal)
{
    byte b = (byte)(wordDecimal >> 16);
    byte g = (byte)(wordDecimal >> 8);
    byte r = (byte)(wordDecimal & 0xFF);
    
    return (r, g, b);
}

public static string WordDecimalToHex(int wordDecimal)
{
    var (r, g, b) = WordDecimalToRgb(wordDecimal);
    return $"#{r:X2}{g:X2}{b:X2}";
}
```

## Resolving Theme Colors to RGB

To display a theme color, you need to resolve it to an actual RGB value by:
1. Getting the base color from the document's theme
2. Applying any tint/shade adjustment

### Getting Base Theme Colors

```csharp
public static int GetThemeBaseColor(Document document, int wdThemeColorIndex)
{
    // Map wdThemeColorIndex to msoThemeColorSchemeIndex
    var schemeIndex = MapToSchemeIndex(wdThemeColorIndex);
    
    // Get the RGB value from the document's theme
    var themeColor = document.DocumentTheme.ThemeColorScheme[schemeIndex];
    return themeColor.RGB;
}

private static MsoThemeColorSchemeIndex MapToSchemeIndex(int wdIndex)
{
    // The mapping between wdThemeColorIndex and msoThemeColorSchemeIndex
    return wdIndex switch
    {
        0 or 13 => MsoThemeColorSchemeIndex.msoThemeDark1,      // MainDark1/Text1
        1 or 12 => MsoThemeColorSchemeIndex.msoThemeLight1,     // MainLight1/Background1
        2 or 15 => MsoThemeColorSchemeIndex.msoThemeDark2,      // MainDark2/Text2
        3 or 14 => MsoThemeColorSchemeIndex.msoThemeLight2,     // MainLight2/Background2
        4 => MsoThemeColorSchemeIndex.msoThemeAccent1,
        5 => MsoThemeColorSchemeIndex.msoThemeAccent2,
        6 => MsoThemeColorSchemeIndex.msoThemeAccent3,
        7 => MsoThemeColorSchemeIndex.msoThemeAccent4,
        8 => MsoThemeColorSchemeIndex.msoThemeAccent5,
        9 => MsoThemeColorSchemeIndex.msoThemeAccent6,
        10 => MsoThemeColorSchemeIndex.msoThemeHyperlink,
        11 => MsoThemeColorSchemeIndex.msoThemeFollowedHyperlink,
        _ => throw new ArgumentOutOfRangeException(nameof(wdIndex))
    };
}
```

### Applying Tint/Shade

The tint/shade adjustment modifies the luminance in HSL color space:

```csharp
public static int ApplyTintShade(int rgbColor, double tintAndShade)
{
    if (tintAndShade == 0)
        return rgbColor;
    
    var (r, g, b) = WordDecimalToRgb(rgbColor);
    var (h, s, l) = RgbToHsl(r, g, b);
    
    // Apply the Word formula:
    // L = (L * |TintAndShade|) + (isTint * (1 - TintAndShade))
    double absTS = Math.Abs(tintAndShade);
    double isTint = tintAndShade > 0 ? 1 : 0;
    double newL = (l * absTS) + (isTint * (1 - tintAndShade));
    
    newL = Math.Max(0, Math.Min(1, newL));
    
    var (newR, newG, newB) = HslToRgb(h, s, newL);
    return RgbToWordDecimal(newR, newG, newB);
}
```

### HSL Conversion Functions

```csharp
public static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
{
    double rd = r / 255.0;
    double gd = g / 255.0;
    double bd = b / 255.0;
    
    double max = Math.Max(rd, Math.Max(gd, bd));
    double min = Math.Min(rd, Math.Min(gd, bd));
    double diff = max - min;
    
    double h = 0, s = 0, l = (max + min) / 2;
    
    if (diff != 0)
    {
        s = l > 0.5 ? diff / (2 - max - min) : diff / (max + min);
        
        if (max == rd)
            h = (gd - bd) / diff + (gd < bd ? 6 : 0);
        else if (max == gd)
            h = (bd - rd) / diff + 2;
        else
            h = (rd - gd) / diff + 4;
        
        h /= 6;
    }
    
    return (h, s, l);
}

public static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
{
    double r, g, b;
    
    if (s == 0)
    {
        r = g = b = l;
    }
    else
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        
        r = HueToRgb(p, q, h + 1.0 / 3);
        g = HueToRgb(p, q, h);
        b = HueToRgb(p, q, h - 1.0 / 3);
    }
    
    return (
        (byte)Math.Round(r * 255),
        (byte)Math.Round(g * 255),
        (byte)Math.Round(b * 255)
    );
}

private static double HueToRgb(double p, double q, double t)
{
    if (t < 0) t += 1;
    if (t > 1) t -= 1;
    
    if (t < 1.0 / 6) return p + (q - p) * 6 * t;
    if (t < 1.0 / 2) return q;
    if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
    
    return p;
}
```

## Complete Resolution Example

```csharp
public static string ResolveColorToHex(Document document, int colorValue)
{
    var colorType = GetColorType(colorValue);
    
    switch (colorType)
    {
        case WordColorType.RGB:
            return WordDecimalToHex(colorValue);
        
        case WordColorType.Automatic:
            return "#000000"; // Or determine from context
        
        case WordColorType.Theme:
            var themeInfo = DecodeThemeColor(colorValue);
            int baseRgb = GetThemeBaseColor(document, themeInfo.WdThemeColorIndex);
            int adjustedRgb = ApplyTintShade(baseRgb, themeInfo.TintAndShade);
            return WordDecimalToHex(adjustedRgb);
        
        default:
            return "#000000";
    }
}
```

## Office Default Theme Colors

For the default Office theme, these are the base colors:

| wdThemeColorIndex | Name | Hex | Word Decimal |
|-------------------|------|-----|--------------|
| 4 | Accent 1 (Blue) | #4472C4 | -738131969 |
| 5 | Accent 2 (Orange) | #ED7D31 | -721354753 |
| 6 | Accent 3 (Gray) | #A5A5A5 | -704577537 |
| 7 | Accent 4 (Gold) | #FFC000 | -687800321 |
| 8 | Accent 5 (Blue) | #5B9BD5 | -671023105 |
| 9 | Accent 6 (Green) | #70AD47 | -654245889 |
| 12 | Background 1 | #FFFFFF | -603914241 |
| 13 | Text 1 | #000000 | -587137025 |
| 14 | Background 2 | #E7E6E6 | -570359809 |
| 15 | Text 2 | #44546A | -553582593 |

## Practical Usage in VSTO

### Reading Font Color

```csharp
public void AnalyzeFontColor(Range range)
{
    int colorValue = (int)range.Font.Color;
    var colorType = GetColorType(colorValue);
    
    Console.WriteLine($"Color Type: {colorType}");
    Console.WriteLine($"Raw Value: {colorValue} (0x{(uint)colorValue:X8})");
    
    if (colorType == WordColorType.Theme)
    {
        var info = DecodeThemeColor(colorValue);
        Console.WriteLine($"Theme Index: {info.WdThemeColorIndex}");
        Console.WriteLine($"Tint/Shade: {info.TintAndShade}");
    }
    
    string hexColor = ResolveColorToHex(range.Document, colorValue);
    Console.WriteLine($"Resolved Hex: {hexColor}");
}
```

### Setting Theme Color

```csharp
public void SetThemeColor(Range range, int wdThemeColorIndex, double tintAndShade = 0)
{
    int colorValue = EncodeThemeColor(wdThemeColorIndex, tintAndShade);
    range.Font.Color = (WdColor)colorValue;
}

// Example: Set to Accent 1, 40% lighter
SetThemeColor(selection.Range, 4, 0.4);

// Example: Set to Accent 2, 25% darker
SetThemeColor(selection.Range, 5, -0.25);
```

## Special Values

```csharp
public static class WordColorConstants
{
    public const int Automatic = unchecked((int)0xFF000000);  // -16777216
    public const int NoColor = 0;  // For backgrounds/highlights
}
```

## References

- [Word Articles: Colours in 2007](https://www.wordarticles.com/Articles/Colours/2007.php) - The definitive reference for Word color encoding
- Microsoft Office Interop documentation

## Summary

Working with Word theme colors requires understanding:

1. **Color type detection** via the first byte (0x00=RGB, 0xD?=Theme, 0xFF=Auto)
2. **Theme color encoding**: `0xD[wdIndex][00][Shade][Tint]`
3. **Tint/shade formulas**:
   - Decode shade: `-1 + (shadeByte / 255)`
   - Decode tint: `1 - (tintByte / 255)`
   - Encode shade: `(tintAndShade + 1) * 255`
   - Encode tint: `(1 - tintAndShade) * 255`
4. **Luminance adjustment**: `L = (L * |tintAndShade|) + (isTint * (1 - tintAndShade))`
5. **BGR byte order** for RGB colors

This knowledge enables full programmatic control over Word's color system in VSTO add-ins.
