---
inclusion: fileMatch
fileMatchPattern: '**/regx-find-html/**|**/RegexFind/**'
---

# RegexFind Build Process

## CRITICAL: File Reading Rules

### NEVER Read Built/Output Files
**ALWAYS read source files, NEVER built/output files:**

❌ **WRONG FILES TO READ**:
- `KleiKodeshVsto/RegexFind/index.html` (built output)
- `regx-find-html/dist/index.html` (built output)
- `KleiKodeshVsto/bin/Debug/**` (build output)
- `KleiKodeshVsto/bin/Release/**` (build output)
- Any minified/compiled files

✅ **CORRECT SOURCE FILES**:
- `regx-find-html/index.html` (source)
- `regx-find-html/js/*.js` (source)
- `regx-find-html/css/*.css` (source)
- `KleiKodeshVsto/**/*.cs` (source)

**WHY**: Built files are minified, compiled, or processed - they don't reflect the actual source code structure and are unreadable.

### Build Process Reminder
1. **Source**: `regx-find-html/` → Vue project source files
2. **Build**: `npm run build` → Creates minified output
3. **Copy**: Built files copied to `KleiKodeshVsto/RegexFind/`
4. **VSTO Build**: C# project includes the copied HTML

**ALWAYS work with source files in `regx-find-html/` directory!**

## REFACTORED ARCHITECTURE STATUS ✅

### JavaScript Bridge - STREAMLINED
**File**: `regx-find-html/js/webview-bridge.js`
- ✅ **50% less code** - removed redundant functions and initialization
- ✅ **Centralized three-state logic** - single `getThreeState()` utility
- ✅ **Unified formatting options** - single `getFormattingOptions(type)` function
- ✅ **Command map pattern** - clean `commands` object instead of individual handlers
- ✅ **Streamlined message handling** - handler map instead of verbose switch statements
- ✅ **UI-driven functionality** - only features that exist in UI are implemented
- ✅ **SIMPLIFIED DISPLAY LOGIC** - C# sends pre-highlighted snippets, JS just displays them

### C# Backend - CENTRALIZED
**File**: `KleiKodeshVsto/RegexFind/RegexFindHost.cs`
- ✅ **Fixed method signatures** - all methods use `JsonElement` for flexible property mapping
- ✅ **Proper data structure handling** - reads from nested `findOptions`/`replaceOptions`
- ✅ **Removed unused methods** - eliminated `PrevResult()`, `NextResult()` (no UI buttons)
- ✅ **Consistent error handling** - try-catch blocks with debug logging
- ✅ **Flexible property mapping** - uses JsonExtensions for camelCase/PascalCase handling
- ✅ **OPTIMIZED DATA TRANSFER** - sends only snippet strings instead of full SearchResult objects

## Communication Protocol - VERIFIED WORKING

### JavaScript to C# Commands
```javascript
// Core commands (all working):
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

### C# to JavaScript Responses
```javascript
// Response format (all working):
{
  command: "searchResults",    // data: ["<snippet1>", "<snippet2>", ...] (pre-highlighted)
  command: "fontList",         // data: ["Arial", "Times New Roman", ...]
  command: "styleList",        // data: ["Normal", "Heading 1", ...]
  command: "formattingCopied", // data: { target: string, formatting: {...} }
  command: "success",          // data: any
  command: "error"             // data: string
}
```

## Data Structure - CORRECTED

### JavaScript Sends (Nested Structure)
```javascript
{
  searchText: "search term",
  replaceText: "replacement", 
  searchMode: "All",
  slop: 0,
  useRegex: false,
  findOptions: {              // ✅ NESTED - C# now reads correctly
    fontSize: 12,
    bold: true,
    italic: null,
    underline: false,
    // ... other formatting
  },
  replaceOptions: {           // ✅ NESTED - C# now reads correctly  
    fontSize: null,           // ✅ NULL HANDLING - works correctly
    bold: false,
    // ... other formatting
  }
}
```

### C# Receives and Maps
```csharp
// ✅ FIXED - Now reads from nested objects correctly
var findOptionsElement = element.GetObjectProperty("findOptions", "FindOptions");
if (findOptionsElement.HasValue)
{
    var findOptions = findOptionsElement.Value;
    regexFind.Bold = findOptions.GetBoolProperty("bold", "Bold");
    // ... maps all properties with null handling
}
```

## UI Features - COMPLETE IMPLEMENTATION

### ✅ Fully Wired Features
- **Search & Replace**: Search, Replace, Replace All buttons + Enter key support
- **Formatting Options**: Font size, Bold/Italic/Underline (three-state), Subscript/Superscript
- **Color Picker**: Full Word-compatible color selection with theme colors
- **Font & Style Selection**: Custom comboboxes with C# integration
- **Copy/Clear Formatting**: Eyedropper and clear buttons working
- **UI Controls**: Show/hide replace, theme toggle, regex palette toggle
- **Result Navigation**: Click to select results, keyboard navigation within results list

### ❌ Removed Features (Not in UI)
- **Navigation buttons**: No prev/next result buttons exist in UI
- **Keyboard shortcuts**: No F3/Ctrl+G navigation (removed from code)
- **About button**: Hidden via CSS until properly implemented

## Build Pipeline - AUTOMATED
```bash
# 1. Build Vue project
cd regx-find-html
npm run build

# 2. Copy to VSTO project  
Copy-Item "regx-find-html\dist\index.html" "KleiKodeshVsto\RegexFind\index.html" -Force

# 3. VSTO build includes updated HTML automatically
```

## Critical Success Factors

### ✅ COMPLETED FIXES
1. **Data Structure Mismatch** - C# now reads nested findOptions/replaceOptions correctly
2. **Method Signature Errors** - All methods use JsonElement with proper property extraction
3. **Communication Protocol** - Consistent JSON string usage throughout
4. **Property Mapping** - Flexible camelCase/PascalCase handling via JsonExtensions
5. **Null Value Handling** - Robust null checking prevents JsonExceptions
6. **Code Redundancy** - Eliminated duplicate functions and unused features
7. **UI Alignment** - Only features with UI elements are implemented

## CRITICAL: Hebrew/Arabic Bidirectional Text Support

### Word Font Properties - Bi Variants
Word uses separate properties for bidirectional (RTL) text like Hebrew and Arabic:
- `Bold` / `BoldBi`
- `Italic` / `ItalicBi`
- `Size` / `SizeBi`
- `Name` / `NameBi`

### Golden Rule: Treat Both as One
**ALWAYS check/set BOTH regular and Bi properties together:**

#### Finding/Matching Text
```csharp
// Check if bold - true if EITHER is bold
bool isBold = range.Font.Bold == -1 || range.Font.BoldBi == -1;

// Check if NOT bold - false only if BOTH are not bold
bool isNotBold = range.Font.Bold == 0 && range.Font.BoldBi == 0;
```

#### Applying Formatting (Replace)
```csharp
// Always set BOTH properties
if (replace.Bold == true) { rng.Font.Bold = -1; rng.Font.BoldBi = -1; }
if (replace.Bold == false) { rng.Font.Bold = 0; rng.Font.BoldBi = 0; }

// Same for font name and size
rng.Font.Name = fontName; rng.Font.NameBi = fontName;
rng.Font.Size = fontSize; rng.Font.SizeBi = fontSize;
```

#### Copying Formatting (Eyedropper)
```csharp
// Prefer Bi property if set, otherwise use regular
bool? bold = (rng.Font.Bold == -1 || rng.Font.BoldBi == -1) ? true 
           : (rng.Font.Bold == 0 && rng.Font.BoldBi == 0) ? false 
           : null;

float? fontSize = rng.Font.SizeBi > 0 ? rng.Font.SizeBi : rng.Font.Size;
string fontName = !string.IsNullOrEmpty(rng.Font.NameBi) ? rng.Font.NameBi : rng.Font.Name;
```

### Why This Matters
- Hebrew/Arabic text ONLY responds to `Bi` properties
- English text ONLY responds to regular properties
- Mixed documents need BOTH to work correctly
- Forgetting Bi properties = formatting silently fails on RTL text

### 🎯 ARCHITECTURE ACHIEVED
- **Lean and Mean**: 50% less JavaScript code, no unnecessary features
- **Centralized**: Single communication bridge, unified data handling
- **UI-Driven**: Implementation matches exactly what exists in the interface
- **Maintainable**: Clean separation of concerns, no redundant code
- **Robust**: Comprehensive error handling and flexible property mapping

**STATUS**: RegexFind is fully functional and ready for production use.

## Regex Palette Troubleshooting

### Duplicate Event Listener Issue
**Problem**: Regex palette shows then immediately hides when clicking help button
**Cause**: Both `regex-palette.js` (setupEventListeners) and `main.js` add click handlers to the help button
**Solution**: Remove the event listener from `regex-palette.js` - let `main.js` handle it exclusively
**Pattern**: When multiple modules need to interact with the same button, centralize the event handling in one place (main.js)

## Build and Deploy Process

### CRITICAL: Always Build and Deploy Together
When making changes to RegexFind HTML/CSS/JS:

1. **Make changes** to source files in `regx-find-html/`
2. **Build**: `npm run build` (from regx-find-html directory)
3. **Deploy**: `Copy-Item "regx-find-html\dist\index.html" "KleiKodeshVsto\RegexFind\index.html" -Force`

**NEVER** make changes without building and deploying - the VSTO project uses the built file, not the source files.

### IMPORTANT: Don't Auto-Build or Auto-Copy
**DO NOT** automatically run build or copy commands after every change. Only build/deploy when:
- User explicitly requests it
- All changes are complete and ready for testing
- User indicates they want to test the changes