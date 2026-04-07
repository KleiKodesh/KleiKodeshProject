---
inclusion: fileMatch
fileMatchPattern: '**/color-picker*|**/COLOR_PICKER*|**/*color*'
---

# RegexFind Color System

## Color Data Structure
```javascript
{
    hex: '#FF0000',           // Visual display
    decimal: 255,             // Word VSTO (BGR format)
    type: 'standard',         // 'theme', 'standard', 'custom', 'auto', 'none'
    name: 'Red',             // Human-readable name
    themeIndex: 4            // For theme colors only
}
```

## Word Color Compatibility

### RGB ↔ BGR Conversion
```javascript
const r = parseInt(hex.substring(0, 2), 16);
const g = parseInt(hex.substring(2, 4), 16);
const b = parseInt(hex.substring(4, 6), 16);
const bgrDecimal = (b << 16) | (g << 8) | r;
```

### Theme Color Encoding
```javascript
// Format: 0xDCTTSSFF (as signed 32-bit)
const themeDecimal = (0xDC << 24) | (index << 16) | (shade << 8) | tint;
```

## Color Types

### Theme Colors
- **Display**: Base colors with tint/shade variations
- **Data**: Negative decimal values (e.g., -603914241)
- **Format**: Special Word encoding for theme references

### Standard Colors
- **Display**: Fixed RGB hex values
- **Data**: Positive decimal values in BGR format

### Special Colors
- **Auto**: `#000000` display, `-16777216` decimal
- **No Color**: No display, `null` decimal

## Integration Pattern
```javascript
// Visual update
button.style.borderBottom = `3px solid ${hexColor}`;

// Data storage
button.setAttribute('data-color-data', JSON.stringify({
    hex: hexColor, decimal: wordDecimal, type: colorType
}));
```