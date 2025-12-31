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

## Communication Protocol

### Property Naming Issue
**JavaScript (Vue) sends camelCase** → **C# expects specific property names**

**ALWAYS REMEMBER**: The built HTML sends different property names than C# RegexFind class expects!

JavaScript sends:
- `searchText` → C# needs `Text`
- `searchMode` → C# needs `Mode` 
- `useRegex` → C# needs `UseWildcards`

**Solution**: Use `object` parameter in C# methods and map properties manually.

## Build Pipeline
1. **HTML Project Build**: `npm run build` in `regx-find-html/` creates single-file output
2. **File Copy**: Built HTML copied to `KleiKodeshVsto/RegexFind/regexfind-index.html`
3. **VSTO Build**: dotnet build compiles VSTO with embedded HTML
4. **Output**: Complete VSTO add-in with integrated HTML interface

## Communication Protocol

### HTML to C# Commands (PascalCase Properties)
- `Search` - Search with RegexFindOptionsDto structure
- `Replace` - Replace current match
- `ReplaceAll` - Replace all matches
- `SelectResult` - Select specific result with { Index: number }
- `GetFontList` - Request available fonts
- `GetStyleList` - Request document styles
- `ThemeToggle` - Toggle theme with { Theme: string }

### C# to HTML Responses
- `searchResults` - Search results with match data
- `replaceComplete` - Replace operation completion
- `fontList` - Available fonts array
- `styleList` - Document styles array
- `success` - Operation success confirmation
- `error` - Error message

## Data Structure (JavaScript to C#)
```javascript
// What JavaScript ACTUALLY sends:
{
  searchText: "search term",      // NOT "Text"!
  replaceText: "replacement",     // NOT "Replace.Text"!
  searchMode: "All",              // NOT "Mode"!
  slop: 0,                        // OK - same name
  useRegex: false,                // NOT "UseWildcards"!
  findOptions: {
    fontSize: 12,                 // NOT "FontSize"!
    bold: false,                  // NOT "Bold"!
    color: { Hex: "#FF0000", Decimal: 255, Type: "standard" },
    style: "", font: ""
  },
  replaceOptions: { 
    // CRITICAL: Can contain null values!
    fontSize: null,               // This caused the JsonException
    /* same structure as findOptions */ 
  }
}
```

## Required C# Method Pattern
```csharp
// WRONG - will fail due to property name mismatch
public void Search(RegexFind options)

// CORRECT - handles property mapping AND null values
public void Search(JsonElement options)
{
    try
    {
        var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
        System.Diagnostics.Debug.WriteLine($"Received: {json}");
        
        CopyRegexFindProperties(options, _regexFind);
        _regexFind.Search();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
        SendErrorToHtml($"Search failed: {ex.Message}");
    }
}
```