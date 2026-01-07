# C# Integration Guide

## Overview

The Zayit C# project (`Zayit-cs/`) hosts the zayit-vue frontend via WebView2. SQL queries are defined in TypeScript and executed by C#.

## C# Version Requirements

**Target Framework**: C# 7.3 (.NET Framework 4.7.2)

### C# 7.3 Compatibility Guidelines
- **No `using` declarations**: Use traditional `using` statements with braces
- **No pattern matching enhancements**: Use explicit type checks with `is` and casting
- **No target-typed `new`**: Always specify type in object creation
- **No null-coalescing assignment**: Use traditional null checks

**Example - C# 7.3 Compatible Code**:
```csharp
// ✅ Good - Traditional using statement
using (var dialog = new SaveFileDialog())
{
    // Use dialog
}

// ❌ Bad - C# 8+ using declaration
using var dialog = new SaveFileDialog();

// ✅ Good - Explicit type check and cast
if (sender is CoreWebView2DownloadOperation download)
{
    // Use download
}

// ❌ Bad - C# 8+ pattern matching
if (sender is CoreWebView2DownloadOperation download && download.State == Completed)

// ✅ Good - Traditional object creation
var dialog = new SaveFileDialog
{
    Filter = "PDF Files (*.pdf)|*.pdf"
};

// ❌ Bad - C# 9+ target-typed new
SaveFileDialog dialog = new()
{
    Filter = "PDF Files (*.pdf)|*.pdf"
};
```

## Build Process

### Pre-Build Event
The C# project uses a smart pre-build script that only rebuilds Vue when needed:

```xml
<PreBuildEvent>call "$(ProjectDir)..\smart-prebuild.bat"</PreBuildEvent>
```

**Smart Build Logic**:
- Checks if Vue source files (*.vue, *.ts, *.js, *.css) are newer than deployed HTML
- Checks if config files (package.json, vite.config.ts, etc.) changed
- Only rebuilds Vue if changes detected
- Skips Vue build if only C# code changed

This ensures the latest Vue build is always embedded while avoiding unnecessary rebuilds.

### Manual Build
```bash
# Build Vue app and deploy to C#
cd zayit-vue
build-and-deploy.bat

# Build C# application
cd Zayit-cs
build.bat
```

## Architecture

### SQL Query Single Source of Truth
All SQL queries are defined in `zayit-vue/src/data/sqlQueries.ts`. C# copies these queries to ensure consistency.

**TypeScript** (source of truth):
```typescript
export const SqlQueries = {
  getAllCategories: `
    SELECT DISTINCT 
      Id,
      ParentId,
      Title,
      Level
    FROM category
    ORDER BY Level, Id
  `
}
```

**C#** (copies SQL from TypeScript):
```csharp
private async void GetTree()
{
    // SQL copied from sqlQueries.ts: SqlQueries.getAllCategories
    var categories = DbQueries.ExecuteQuery(@"
        SELECT DISTINCT 
          Id,
          ParentId,
          Title,
          Level
        FROM category
        ORDER BY Level, Id
    ");
    
    // Serialize and send to Vue
    string json = JsonSerializer.Serialize(categories, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    
    await ExecuteScriptAsync($"window.receiveTreeData({json});");
}
```

## Communication Protocol

### Vue → C# (WebView2 postMessage)
```typescript
window.chrome.webview.postMessage({
  command: 'GetTree',
  args: []
})
```

### C# → Vue (ExecuteScriptAsync)
```csharp
await ExecuteScriptAsync($"window.receiveTreeData({json});");
```

### Command Handlers

All commands are handled in `Zayit-cs/Zayit/Viewer/ZayitViewer.cs`:

| Command | Args | Response Callback |
|---------|------|-------------------|
| `GetTree` | `[]` | `window.receiveTreeData(data)` |
| `GetToc` | `[bookId]` | `window.receiveTocData(bookId, data)` |
| `GetLinks` | `[lineId, tabId, bookId]` | `window.receiveLinks(tabId, bookId, links)` |
| `GetTotalLines` | `[bookId]` | `window.receiveTotalLines(bookId, totalLines)` |
| `GetLineContent` | `[bookId, lineIndex]` | `window.receiveLineContent(bookId, lineIndex, content)` |
| `GetLineId` | `[bookId, lineIndex]` | `window.receiveLineId(bookId, lineIndex, lineId)` |
| `GetLineRange` | `[bookId, start, end]` | `window.receiveLineRange(bookId, start, end, lines)` |

## Database Layer

### DbQueries.cs
Simplified to a single method that executes SQL from TypeScript:

```csharp
public static object ExecuteQuery(string sql, object[] parameters = null)
{
    if (parameters == null || parameters.Length == 0)
    {
        return _db?.DapperConnection
            .Query(sql)
            .ToArray();
    }
    else
    {
        return _db?.DapperConnection
            .Query(sql, parameters)
            .ToArray();
    }
}
```

### Key Points
- No separate C# SQL query definitions
- SQL comes from TypeScript `sqlQueries.ts`
- Uses Dapper for query execution
- Returns dynamic objects serialized to JSON

## Adding New Commands

1. **Define SQL** in `zayit-vue/src/data/sqlQueries.ts`
2. **Add TypeScript handler** in `zayit-vue/src/data/sqliteDb.ts` (dev mode)
3. **Add bridge handler** in `zayit-vue/src/data/csharpBridge.ts`
4. **Add routing** in `zayit-vue/src/data/dbManager.ts`
5. **Add C# handler** in `Zayit-cs/Zayit/Viewer/ZayitViewer.cs`:
   - Copy SQL from `sqlQueries.ts`
   - Execute via `DbQueries.ExecuteQuery`
   - Serialize with `JsonNamingPolicy.CamelCase`
   - Call JavaScript callback via `ExecuteScriptAsync`

## JSON Serialization

Always use camelCase for JavaScript compatibility:

```csharp
var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
```

This ensures C# properties like `TotalLines` become `totalLines` in JavaScript.

## File Locations

```
Zayit-cs/
├── Zayit/
│   ├── Viewer/
│   │   ├── ZayitViewer.cs          # Command handlers
│   │   ├── ZayitViewerBase.cs      # WebView2 base class
│   │   └── ZayitViewerHost.cs      # UserControl host
│   ├── SeforimDb/
│   │   ├── DbManager.cs            # Database connection
│   │   └── DbQueries.cs            # Query execution
│   └── Html/
│       └── index.html              # Built Vue app (deployed here)
```

## Debugging

Enable debug output in C#:
```csharp
Debug.WriteLine($"Command received: {command}");
```

Check Visual Studio Output window for WebView2 messages.
