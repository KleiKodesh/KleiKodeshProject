---
inclusion: manual
---

# Word Theme Colors - Future Implementation

## Overview
The current color picker implementation uses simplified tint/shade calculations, but Word uses a specific hex encoding system that needs to be implemented for exact color matching.

## Reference Materials

### Word Articles Reference
- **URL**: https://www.wordarticles.com/Articles/Colours/2007.php
- **Key Section**: "Setting Color Properties" and "VBA: Where The Fun Begins!"
- **Critical Info**: Word uses 32-bit values with specific byte encoding for theme colors

### Word Theme Color Hex Format
Based on Word Articles, theme colors use this format:
```
0x[D|F][ThemeIndex][00][Shade][Tint]
```
- **D/F**: Prefix (0xD for Font.Color, 0xF for Find.TextColor)
- **ThemeIndex**: Lower 4 bits of theme color index (0-15)
- **00**: Reserved byte (always 0x00)
- **Shade**: Darkness percentage (0xFF = unchanged, 0x00-0xFE = darker)
- **Tint**: Lightness percentage (0xFF = unchanged, 0x00-0xFE = lighter)

## Exact Column Patterns (User Provided)

### Column Layout (RTL Order)
Based on uploaded image, columns from right to left are:
1. **White (Background 1)**: 5,10,25,35,50% darker
2. **Black (Text 1)**: 50,35,25,15,5% lighter  
3. **Gray (Background 2)**: 10,25,50,75,90% darker
4. **Blue Gray (Text 2)**: 80,60,40% lighter then 25,50% darker
5. **Blue (הדגשה 1)**: 80,60,40% lighter then 25,50% darker
6. **Orange (הדגשה 2)**: 80,60,40% lighter then 25,50% darker
7. **Gray (הדגשה 3)**: 80,60,40% lighter then 25,50% darker
8. **Gold (הדגשה 4)**: 80,60,40% lighter then 25,50% darker
9. **Blue (הדגשה 5)**: 80,60,40% lighter then 25,50% darker
10. **Green (הדגשה 6)**: 80,60,40% lighter then 25,50% darker

## Implementation Requirements

### Step 1: Word Hex Generation
```javascript
function generateWordThemeHex(themeIndex, tintShade) {
    const prefix = 0xD0; // Font.Color prefix
    const themeIndexByte = themeIndex & 0x0F;
    const reserved = 0x00;
    
    let shadeByte = 0xFF; // Unchanged
    let tintByte = 0xFF;  // Unchanged
    
    if (tintShade < 0) {
        // Shade (darker) - encode as positive value
        shadeByte = Math.round(Math.abs(tintShade) * 255);
    } else if (tintShade > 0) {
        // Tint (lighter) - encode as (1 - tintShade) * 255
        tintByte = Math.round((1 - tintShade) * 255);
    }
    
    // Combine bytes: [Prefix+ThemeIndex][Reserved][Shade][Tint]
    return (prefix << 24) | (themeIndexByte << 24) | (reserved << 16) | (shadeByte << 8) | tintByte;
}
```

### Step 2: HSL Color Calculation
From Word Articles, the tint/shade formula is:
```
L = (L * Abs(TintAndShade)) + (Abs(TintAndShade > 0) * (1 - TintAndShade))
```

### Step 3: Theme Color Mapping
Map Word's wdThemeColorIndex to actual theme positions:
- Background1 (12) → Light1 (2)
- Text1 (13) → Dark1 (1)  
- Background2 (14) → Light2 (4)
- Text2 (15) → Dark2 (3)
- Accent1-6 (4-9) → Direct mapping

## Current Status
- **Basic tint/shade**: ✅ Implemented
- **Word hex encoding**: ❌ Not implemented
- **Exact column patterns**: ❌ Using consistent patterns instead
- **HSL calculations**: ❌ Using simple RGB calculations

## Next Steps
1. Implement proper Word hex encoding function
2. Add HSL color space conversion functions
3. Apply exact column-specific tint/shade patterns
4. Test against actual Word color picker output
5. Verify decimal values match Word's Font.Color property

## Notes
- Current implementation works for basic functionality
- Word compatibility requires the proper hex encoding
- Each column has unique patterns, not consistent formulas
- The visual appearance should match the uploaded reference image exactly