---
inclusion: fileMatch
fileMatchPattern: '**/regx-find-html/**|**/RegexFind/**'
---

# RegexFind Module Architecture

## Component Structure
```
regx-find-html/          # Vue source project
├── index.html           # Main UI
├── js/webview-bridge.js # Communication layer
└── css/styles.css       # Styling

KleiKodeshVsto/RegexFind/ # C# backend
├── RegexFindHost.cs     # WebView host
├── RegexSearch.cs       # Search logic
└── index.html           # Built output (copied from Vue)
```

## Communication Protocol

### JavaScript → C# Commands
```javascript
{
  Command: "Search",           // Args: [searchParameters]
  Command: "Replace",          // Args: [searchParameters] 
  Command: "ReplaceAll",       // Args: [searchParameters]
  Command: "SelectResult",     // Args: [{ Index: number }]
  Command: "GetFontList",      // Args: []
  Command: "GetStyleList",     // Args: []
  Command: "CopyFormatting",   // Args: [{ target: "find"|"replace" }]
}
```

### Data Structure (Nested)
```javascript
{
  searchText: "search term",
  replaceText: "replacement", 
  searchMode: "All",
  slop: 0,
  useRegex: false,
  findOptions: {              // NESTED - C# reads correctly
    fontSize: 12,
    bold: true,
    italic: null,             // NULL HANDLING works
    // ... other formatting
  },
  replaceOptions: {           // NESTED - C# reads correctly  
    fontSize: null,
    bold: false,
    // ... other formatting
  }
}
```

## Hebrew/Arabic Bidirectional Text Support

### Word Font Properties - Bi Variants
Word uses separate properties for RTL text:
- `Bold` / `BoldBi`
- `Italic` / `ItalicBi` 
- `Size` / `SizeBi`
- `Name` / `NameBi`

### Golden Rule: Treat Both as One
```csharp
// Finding - true if EITHER is bold
bool isBold = range.Font.Bold == -1 || range.Font.BoldBi == -1;

// Applying - set BOTH properties
if (replace.Bold == true) { 
    rng.Font.Bold = -1; 
    rng.Font.BoldBi = -1; 
}
```

## Build Process
1. **Source**: `regx-find-html/` → Vue project
2. **Build**: `npm run build` → Creates `dist/index.html`
3. **Copy**: Built file → `KleiKodeshVsto/RegexFind/index.html`
4. **VSTO Build**: Includes copied HTML automatically

**CRITICAL**: Always work with source files in `regx-find-html/`!