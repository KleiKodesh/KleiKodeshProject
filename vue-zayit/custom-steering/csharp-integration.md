# C# Integration Guide

## Overview

The Zayit C# project (`Zayit-cs/`) hosts the zayit-vue frontend via WebView2 using a modern Services architecture with JSON message-based communication.

## ✅ VERIFIED COMMUNICATION PIPELINE

### Architecture Overview

**Vue Frontend** ↔ **WebView Bridge** ↔ **C# Services** ↔ **Database/File System**

All communication pipelines have been verified and are fully functional:

1. **Category Tree Loading** - Database queries with client-side tree building
2. **TOC Loading** - Hierarchical table of contents with alt-TOC support
3. **Book Lines Loading** - Virtualized batch loading with smart prioritization
4. **Commentary Loading** - Dynamic commentary based on selected lines
5. **PDF Files** - SetVirtualHostNameToFolderMapping with session persistence
6. **Hebrew Books** - Download capture, cache management, and virtual URLs

## C# Services Architecture

### ServiceProvider.cs - Central Hub

All Vue operations route through the ServiceProvider which delegates to specialized services:

```csharp
public class ServiceProvider
{
    private readonly DbService _db;
    private readonly HebrewBooksService _hebrewBooks;
    private readonly PdfService _pdf;

    // Database Operations - Core Vue Communication Pipeline
    public object GetConnectionTypes(string q) => _db.GetConnectionTypes(q);
    public object GetTree(string cq, string bq) => _db.GetTree(cq, bq);
    public object GetToc(int bookId, string q) => _db.GetToc(bookId, q);
    public object GetLineRange(int bookId, int s, int e, string q) => _db.GetLineRange(bookId, s, e, q);

    // PDF Operations - SetVirtualHostNameToFolderMapping
    public object OpenPdfFilePicker() => _pdf.OpenPdfFilePicker();
    public string RecreateVirtualUrlFromPath(string path) => _pdf.RecreateVirtualUrlFromPath(path);

    // Hebrew Books Operations - Download Capture & Cache Management
    public object PrepareHebrewBookDownload(string id, string title, string action) =>
        _hebrewBooks.PrepareDownload(id, title, action).GetAwaiter().GetResult();
}
```

### WebViewBridgeService.cs - Message Handler

Handles JSON message parsing and method invocation:

```csharp
public async void HandleMessage(string json)
{
    var msg = JsonSerializer.Deserialize<Message>(json);
    var result = await Execute(msg.Method, msg.Params);
    await SendResponse(msg.Id, result, null);
}
```

### Specialized Services

#### **DbService.cs** - Database Operations

- Executes SQL queries via DbQueries
- Handles all database communication
- Returns properly formatted JSON data

#### **PdfService.cs** - PDF File Operations

- Uses `SetVirtualHostNameToFolderMapping` for secure file access
- Provides file picker dialog with proper filtering
- Handles session persistence via originalPath storage
- Creates virtual HTTPS URLs for PDF.js integration

#### **HebrewBooksService.cs** - Hebrew Books Operations

- Captures downloads via `webView.CoreWebView2.DownloadStarting`
- Manages cache with `HebrewBooksCacheManager` (LRU eviction)
- Uses static global `SetVirtualHostNameToFolderMapping`
- Automatically closes download dialog with `CloseDefaultDownloadDialog()`
- Provides tab closure cleanup notifications

## Communication Protocol

### Vue → C# (JSON Messages)

```typescript
// Vue sends JSON messages via webviewBridge
await webviewBridge.call(
  "GetTree",
  SqlQueries.getAllCategories,
  SqlQueries.getAllBooks,
);
await webviewBridge.call("OpenPdfFilePicker");
await webviewBridge.call("PrepareHebrewBookDownload", bookId, title, "view");
```

### C# → Vue (JSON Responses)

```csharp
// C# responds with JSON via postMessage
var response = new { id = messageId, result = data, error = null };
var responseJson = JsonSerializer.Serialize(response);
await webView.ExecuteScriptAsync($"window.chrome.webview.postMessage({responseJson});");
```

### WebView Bridge (Vue Side)

```typescript
class WebViewBridge {
  async call<T = any>(method: string, ...params: any[]): Promise<T> {
    const message = { id: `req_${++this.requestCounter}`, method, params };
    return new Promise<T>((resolve, reject) => {
      this.pendingRequests.set(message.id, { resolve, reject });
      window.chrome.webview.postMessage(JSON.stringify(message));
    });
  }
}
```

## File Operations with SetVirtualHostNameToFolderMapping

### PDF Files

```csharp
// C# creates secure virtual URLs for PDF access
webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "zayitHost",
    htmlPath,
    CoreWebView2HostResourceAccessKind.Allow
);

// Returns: https://zayitHost/temp/uniqueId_filename.pdf
```

### Hebrew Books

```csharp
// Static global mapping for all Hebrew books
webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "zayitHost",
    Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache"),
    CoreWebView2HostResourceAccessKind.Allow
);

// Returns: https://zayitHost/pdfjs/web/hebrewbookscache/hebrewbook_123.pdf
```

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
    dialog.Filter = "PDF files (*.pdf)|*.pdf";
    if (dialog.ShowDialog() == DialogResult.OK)
    {
        // Process file
    }
}

// ✅ Good - Explicit type check and cast
if (sender is CoreWebView2DownloadStartingEventArgs e)
{
    // Handle download
}

// ✅ Good - Traditional object creation
var dialog = new SaveFileDialog
{
    Filter = "PDF Files (*.pdf)|*.pdf",
    Title = "Select PDF File"
};
```

## Database Layer

### DbQueries.cs

Simplified to execute SQL queries from TypeScript:

```csharp
public object ExecuteQuery(string sql, object[] parameters = null)
{
    if (parameters == null || parameters.Length == 0)
    {
        return _connection.Query(sql).ToArray();
    }
    else
    {
        return _connection.Query(sql, parameters).ToArray();
    }
}
```

### SQL Query Source of Truth

All SQL queries are defined in `zayit-vue/src/services/sqlQueries.ts` and passed to C# via the bridge.

## Session Persistence

### PDF Files

- **Storage**: `originalPath` stored in tab state
- **Restoration**: `RecreateVirtualUrlFromPath` recreates virtual URL on app restart
- **Security**: Files accessed via SetVirtualHostNameToFolderMapping

### Hebrew Books

- **Cache**: Persistent cache with LRU eviction (max 10 files)
- **Global Mapping**: Static virtual host mapping shared by all Hebrew books
- **Cleanup**: Tab closure notifications for cache management

## Error Handling & Fallbacks

### WebView Unavailable

```typescript
// Vue gracefully falls back to development mode
if (!webviewBridge.isAvailable()) {
  // Use dev server or browser-based alternatives
  return await devQuery(sql);
}
```

### File Access Issues

```csharp
// C# provides clear error messages
try {
    var result = pdf.OpenPdfFilePicker();
    return result;
} catch (Exception ex) {
    return new { fileName = null, dataUrl = null, error = ex.Message };
}
```

## Performance Optimizations

### Database Operations

- **Batch Loading**: 200-line batches with smart prioritization
- **Caching**: Category tree and TOC cached in Vue stores
- **Streaming**: Progressive line loading with priority queue

### File Operations

- **Virtual URLs**: Instant access via SetVirtualHostNameToFolderMapping
- **Cache Management**: LRU eviction for Hebrew books
- **Session Restore**: Efficient virtual URL recreation

## File Locations

```
Zayit-cs/ZayitLib/
├── Services/
│   ├── ServiceProvider.cs          # Central hub for all operations
│   ├── WebViewBridgeService.cs     # JSON message handler
│   ├── DbService.cs                # Database operations
│   ├── DbQueries.cs                # SQL execution
│   ├── PdfService.cs               # PDF file operations
│   ├── HebrewBooksService.cs       # Hebrew books operations
│   └── HebrewBooksCacheManager.cs  # Cache management
├── Viewer/
│   ├── ZayitViewer.cs              # WebView2 host with services
│   └── ZayitViewerHost.cs          # UserControl wrapper
└── Html/                           # Vue build output (deployed here)
    └── index.html
```

## Adding New Operations

1. **Add method** to appropriate C# service (DbService, PdfService, etc.)
2. **Expose method** in ServiceProvider.cs
3. **Add Vue service call** using webviewBridge.call()
4. **Test both directions** of communication
5. **Add error handling** for WebView unavailable scenarios

## Debugging

### C# Side

```csharp
Console.WriteLine($"[ServiceProvider] Executing method: {method}");
Debug.WriteLine($"WebView message: {json}");
```

### Vue Side

```typescript
console.log("[WebViewBridge] Calling method:", method, "with params:", params);
console.log("[WebViewBridge] Received response:", response);
```

Check Visual Studio Output window and browser DevTools Console for debugging information.
