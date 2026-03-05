# Hebrew Fonts & Typography Guidelines

## Overview

This guide covers Hebrew font management, detection, and prioritization for the Zayit project. The system supports three main font sources with specific prioritization for Biblical Hebrew texts.

## Font Sources & Priorities

### 1. Culmus/Kulmus Project (TOP PRIORITY)

**Location**: `src/data/hebrewFonts.ts` (first in array)

#### Premium Fonts (Niqqud + Ta'amim Support)

- `Taamey Ashkenaz` - BEST for Biblical Hebrew with Ashkenazi cantillation
- `Taamey David CLM` - David font with cantillation marks
- `Taamey Frank CLM` - Frank Ruehl with cantillation marks
- `Keter YG` - Modernized Aleppo Codex style (4 weights)
- `Keter Aram Tsova` - Based on Aleppo Codex manuscript
- `Shofar` - Hebrew font with cantillation support

#### Excellent Niqqud Support

- `Frank Ruehl CLM` - Traditional serif Hebrew (4 weights)
- `David CLM` - Serif Hebrew based on Charter (3 weights)
- `Miriam CLM` - Hebrew font (2 weights)
- `Miriam Mono CLM` - Monospace Hebrew (4 weights)
- `Simple CLM` - Basic Hebrew font with romanization and niqqud
- `Nachlieli CLM` - Sans-serif Hebrew (4 weights)
- `Aharoni CLM` - Sans-serif Hebrew (4 weights)

### 2. System Fonts (Windows/MS Office)

**Detection**: Use PowerShell script `scripts/detect-hebrew-fonts-simple.ps1`

#### Premium System Fonts

- `David` - Traditional Hebrew serif with excellent niqqud
- `Miriam` - Classic Hebrew font with full vowel support
- `FrankRuehl` - Traditional Hebrew serif
- Guttman collection (25+ professional Hebrew fonts)

#### Windows Hebrew Standards

- `Gisha` - Modern Hebrew sans-serif with niqqud
- `Arial` - Full Hebrew support including vowels
- `Times New Roman` - Hebrew support with vowels
- `Calibri` - Modern sans-serif with Hebrew

### 3. Google Fonts Hebrew

**Web deployment ready fonts**

#### With Niqqud Support

- `Noto Sans Hebrew` - Google's comprehensive Hebrew sans
- `Noto Serif Hebrew` - Google's comprehensive Hebrew serif
- `Frank Ruhl Libre` - Open source Hebrew serif
- `IBM Plex Sans Hebrew` - IBM's Hebrew font

#### Modern Hebrew Fonts

- `Alef`, `Assistant`, `Heebo`, `Rubik` - Contemporary designs
- `Secular One`, `Suez One`, `Karantina` - Display fonts

## Font Detection Scripts

### PowerShell Detection Script

**File**: `scripts/detect-hebrew-fonts-simple.ps1`

```powershell
# Quick system font detection
Add-Type -AssemblyName System.Drawing
[System.Drawing.FontFamily]::Families | Where-Object {
    $_.Name -match 'hebrew|david|miriam|aharoni|gisha|frank|culmus|clm|guttman|keter|taamey|noto|arial|times|calibri|segoe|tahoma'
} | Select-Object Name | Sort-Object Name
```

### Node.js Detection Script

**File**: `scripts/detect-hebrew-fonts.js`

- More comprehensive browser-based testing
- Tests actual Hebrew character rendering
- Categorizes fonts by Hebrew capability

## Implementation Guidelines

### Font Array Structure

```typescript
export const hebrewFonts = [
  // === CULMUS/KULMUS PROJECT - PREMIUM (NIQQUD + TA'AMIM) ===
  "Taamey Ashkenaz", // Biblical Hebrew - TOP PRIORITY

  // === CULMUS/KULMUS PROJECT - EXCELLENT NIQQUD SUPPORT ===
  "Frank Ruehl CLM", // Traditional serif Hebrew

  // === YOUR SYSTEM - PREMIUM HEBREW FONTS ===
  "David", // Windows Hebrew classic

  // === GOOGLE FONTS - HEBREW WITH NIQQUD SUPPORT ===
  "Noto Sans Hebrew", // Web-ready Hebrew

  // ... continue with decreasing priority
];
```

### Priority Rules

1. **Culmus fonts with ta'amim support** - First (Biblical Hebrew)
2. **Culmus fonts with niqqud support** - Second (Academic Hebrew)
3. **System fonts with niqqud support** - Third (Local reliability)
4. **Google Fonts with niqqud support** - Fourth (Web deployment)
5. **Basic Hebrew support fonts** - Last (Fallback)

## Font Testing & Validation

### Hebrew Unicode Ranges

- **Basic Hebrew**: `0x05D0-0x05EA` (א-ת)
- **Hebrew Points**: `0x05B0-0x05BD` (Niqqud/vowels)
- **Cantillation**: `0x0591-0x05AF` (Ta'amim marks)
- **Final Forms**: `0x05DA, 0x05DD, 0x05DF, 0x05E3, 0x05E5` (ךםןףץ)

### Testing Method

```javascript
// Test Hebrew character rendering
const testChars = {
  basic: "\u05D0\u05D1\u05D2\u05D3\u05D4", // אבגדה
  niqqud: "\u05B0\u05B1\u05B2\u05B3\u05B4", // Vowel marks
  cantillation: "\u0591\u0592\u0593\u0594\u0595", // Ta'amim
};
```

## Maintenance

### Adding New Fonts

1. **Research Hebrew capability** - Check niqqud/ta'amim support
2. **Determine priority tier** - Based on Hebrew feature support
3. **Insert in correct position** - Maintain priority order
4. **Test rendering** - Verify actual Hebrew display
5. **Update documentation** - Add to appropriate category

### Font Detection Updates

- **Run detection script** when system fonts change
- **Compare with current list** - Identify new/removed fonts
- **Update hebrewFonts.ts** - Maintain priority structure
- **Test in application** - Verify font dropdown works

### Web Font Loading

- **Google Fonts**: Load via CSS imports or font API
- **Culmus Fonts**: May need local installation or web font conversion
- **System Fonts**: Available locally, fallback for web users

## Best Practices

1. **Always prioritize Culmus fonts** for Biblical Hebrew applications
2. **Test font rendering** with actual Hebrew text including niqqud
3. **Provide fallbacks** - System fonts for reliability
4. **Consider web deployment** - Google Fonts for broader compatibility
5. **Document font capabilities** - Clear comments about Hebrew support level
6. **Regular updates** - Keep font list current with new releases

## Resources

- **Culmus Project**: https://culmus.sourceforge.io/
- **Hebrew Unicode**: https://unicode.org/charts/PDF/U0590.pdf
- **Font Detection Method**: https://tchumim.com/topic/16808/
- **Google Fonts Hebrew**: https://fonts.google.com/?subset=hebrew

## Session Accomplishments

### ✅ Completed in This Session

#### Hebrew Font Collection & Prioritization

- **Comprehensive Font List**: Created prioritized list of 130+ Hebrew fonts
- **Culmus Priority**: Placed Culmus/Kulmus fonts with ta'amim support at top
- **System Integration**: Detected and integrated actual system fonts via PowerShell
- **Google Fonts**: Added complete collection of Hebrew-supporting Google Fonts
- **Smart Organization**: Organized by Hebrew capability (Premium → Excellent → Good → Basic)

#### Font Detection Scripts

- **PowerShell Script**: `scripts/detect-hebrew-fonts-simple.ps1` for system font detection
- **Node.js Script**: `scripts/detect-hebrew-fonts.js` for comprehensive browser-based testing
- **Unicode Testing**: Scripts test actual Hebrew character rendering capability

#### Implementation Status

- **File Updated**: `src/data/hebrewFonts.ts` with final prioritized font array
- **Detection Working**: PowerShell script successfully detected 70+ system Hebrew fonts
- **Priority Verified**: Culmus fonts with niqqud/ta'amim support listed first
- **Fallbacks Included**: System and Google fonts provide reliable fallbacks

#### Key Achievements

1. **Biblical Hebrew Focus**: Taamey Ashkenaz and cantillation fonts prioritized
2. **Academic Support**: Comprehensive niqqud-supporting fonts available
3. **Web Compatibility**: Google Fonts ensure cross-platform availability
4. **System Integration**: Actual installed fonts detected and utilized
5. **Maintenance Ready**: Scripts available for future font updates
