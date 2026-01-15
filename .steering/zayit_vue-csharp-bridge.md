---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**|**/Zayit*|**/csharpBridge*|**/dbManager*'
---

# Zayit Vue ↔ C# Communication Bridge

## CRITICAL: This Connection is Fragile - Do Not Break

The Vue frontend and C# backend communicate via WebView2 message passing. This is NOT a built-in framework - it's a custom bridge that can easily break. Every change must preserve the exact message format, method signatures, and response handlers.

---

## Communication Flow Overview

```
Vue Component
    ↓ calls
dbManager.ts (router)
    ↓ calls
csharpBridge.ts (singleton)
    ↓ sends
window.chrome.webview.postMessage({ command, args })
    ↓ WebView2 message passing
CoreWebView2.WebMessageReceived event (C#)
    ↓ parses JSON
KleiKodeshWebView.HandleWebMessage()
    ↓ reflection dispatch
ZayitViewerCommands.GetTree() (or other method)
    ↓ delegates
ZayitViewerDbCommands.GetTree()
    ↓ queries database
DbQueries.ExecuteQuery()
    ↓ serializes result
JsonSerializer.Serialize(data)
    ↓ sends response
_webView.ExecuteScriptAsync("window.receiveTreeData(...)")
    ↓ calls global handler
window.receiveTreeData(data) in csharpBridge.ts
    ↓ resolves promise
pendingRequests.get('GetTree').resolve(data)
    ↓ returns to
Vue Component receives data
```

---

## Vue Frontend Side

### File: `csharpBridge.ts`

**CRITICAL RULES:**
- Singleton pattern - only ONE instance manages all requests
- Message format MUST be: `{ command: string, args: any[] }`
- Response handlers MUST be global window functions
- Promise keys MUST match exactly between send and receive

**Message Sending:**
```typescript
// CORRECT - exact format required
window.chrome.webview.postMessage({
    command: 'GetTree',
    args: []
})

// WRONG - will not be received
window.chrome.webview.postMessage('GetTree')
window.chrome.webview.postMessage({ cmd: 'GetTree' })
```

**Response Handlers:**
```typescript
// MUST be on window object
window.receiveTreeData = (data: any) => {
    const request = this.pendingRequests.get('GetTree')
    if (request) {
        request.resolve(data)
        this.pendingRequests.delete('GetTree')
    }
}

// WRONG - will not be called by C#
this.receiveTreeData = (data) => { ... }
const receiveTreeData = (data) => { ... }
```

**Promise Management:**
```typescript
// Create promise BEFORE sending command
const promise = this.csharp.createRequest<T>('GetTree')
this.csharp.send('GetTree', [])
return await promise

// WRONG - promise created after send
this.csharp.send('GetTree', [])
const promise = this.csharp.createRequest<T>('GetTree')
```

### File: `dbManager.ts`

**CRITICAL RULES:**
- Routes to WebView (production) or dev server (development)
- MUST check `bridge.isAvailable()` before sending
- Promise keys MUST match C# response handler calls
- Post-processing (censoring) happens AFTER C# returns

**Request Pattern:**
```typescript
// CORRECT - create promise, send, await
async getTree() {
    if (this.isWebViewAvailable()) {
        const promise = this.csharp.createRequest<TreeData>('GetTree')
        this.csharp.send('GetTree', [])
        return await promise
    }
    // fallback to dev server
}

// WRONG - missing promise creation
async getTree() {
    this.csharp.send('GetTree', [])
    // no way to receive response!
}
```

**Promise Key Patterns:**
- Simple: `'GetTree'`
- With ID: `'GetToc:${bookId}'`
- With multiple IDs: `'GetLinks:${tabId}:${bookId}'`

---

## C# Backend Side

### File: `KleiKodeshWebView.cs` (Base Class)

**CRITICAL RULES:**
- Handles WebView2 initialization and message reception
- Virtual host mapping MUST be set before navigation
- Message handler MUST deserialize to `JsCommand` type
- Reflection dispatch MUST find method by name (case-insensitive)

**Virtual Host Setup:**
```csharp
// CORRECT - maps folder to virtual host
CoreWebView2.SetVirtualHostNameToFolderMapping(
    "zayitHost", 
    folderPath, 
    CoreWebView2HostResourceAccessKind.Allow
);
Source = new Uri("https://zayitHost/index.html");

// WRONG - file:// protocol breaks JavaScript modules
Source = new Uri($"file:///{folderPath}/index.html");
```

**Message Reception:**
```csharp
// CORRECT - deserialize to JsCommand
var cmd = JsonSerializer.Deserialize<JsCommand>(
    json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
);

// JsCommand MUST have these properties
public class JsCommand {
    public string Command { get; set; }
    public JsonElement[] Args { get; set; }
}
```

**Reflection Dispatch:**
```csharp
// Finds method by name (case-insensitive)
var method = _commandHandler.GetType()
    .GetMethod(cmd.Command, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

// Method name MUST match command string exactly
// Vue sends: 'GetTree' → C# method: GetTree()
// Vue sends: 'GetToc' → C# method: GetToc(int bookId)
```

### File: `ZayitViewer.cs`

**CRITICAL RULES:**
- Extends WebView2 with Zayit-specific initialization
- Virtual host MUST be "zayitHost" (not "appHost")
- Command handler MUST be `ZayitViewerCommands` instance
- Message handler MUST be wired in `CoreWebView2InitializationCompleted`

**Initialization Order:**
```csharp
// 1. Create shared environment
var env = await GetSharedEnvironmentAsync();

// 2. Initialize CoreWebView2
await EnsureCoreWebView2Async(env);

// 3. Set virtual host mapping
CoreWebView2.SetVirtualHostNameToFolderMapping("zayitHost", folderPath, ...);

// 4. Wire message handler
WebMessageReceived += ZayitViewer_WebMessageReceived;

// 5. Navigate to index.html
Source = new Uri("https://zayitHost/index.html");

// WRONG - navigate before virtual host setup
Source = new Uri("https://zayitHost/index.html");
CoreWebView2.SetVirtualHostNameToFolderMapping(...); // TOO LATE
```

### File: `ZayitViewerCommands.cs`

**CRITICAL RULES:**
- Receives all commands via reflection dispatch
- Method names MUST match Vue command strings exactly
- Methods MUST be private (reflection finds them)
- Methods delegate to specialized handlers

**Command Method Pattern:**
```csharp
// CORRECT - matches Vue command 'GetTree'
private async void GetTree() => _dbCommands.GetTree();

// CORRECT - matches Vue command 'GetToc' with bookId arg
private async void GetToc(int bookId) => _dbCommands.GetToc(bookId);

// WRONG - name mismatch
private async void GetTreeData() => _dbCommands.GetTree(); // Vue sends 'GetTree'

// WRONG - wrong access modifier
public async void GetTree() => _dbCommands.GetTree(); // Should be private
```

### File: `ZayitViewerDbCommands.cs`

**CRITICAL RULES:**
- Executes database queries
- Serializes results to JSON with camelCase properties
- Sends response via `ExecuteScriptAsync()` calling global window function
- Response handler name MUST match Vue's global handler

**Response Pattern:**
```csharp
// 1. Execute query
var treeData = new {
    categoriesFlat = _db.ExecuteQuery(SqlQueries.GetAllCategories),
    booksFlat = _db.ExecuteQuery(SqlQueries.GetAllBooks)
};

// 2. Serialize with camelCase
string json = JsonSerializer.Serialize(treeData, new JsonSerializerOptions {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

// 3. Call Vue handler - MUST match global handler name
string js = $"window.receiveTreeData({json});";
await _webView.ExecuteScriptAsync(js);

// WRONG - handler name mismatch
string js = $"window.onTreeData({json});"; // Vue expects receiveTreeData
```

**Response Handler Mapping:**
| C# Calls | Vue Handler | Promise Key |
|----------|-------------|-------------|
| `window.receiveTreeData(data)` | `win.receiveTreeData` | `'GetTree'` |
| `window.receiveTocData(bookId, data)` | `win.receiveTocData` | `'GetToc:${bookId}'` |
| `window.receiveLinks(tabId, bookId, links)` | `win.receiveLinks` | `'GetLinks:${tabId}:${bookId}'` |
| `window.receiveTotalLines(bookId, total)` | `win.receiveTotalLines` | `'GetTotalLines:${bookId}'` |
| `window.receiveLineContent(bookId, lineIndex, content)` | `win.receiveLineContent` | `'GetLineContent:${bookId}:${lineIndex}'` |
| `window.receiveLineId(bookId, lineIndex, lineId)` | `win.receiveLineId` | `'GetLineId:${bookId}:${lineIndex}'` |
| `window.receiveLineRange(bookId, start, end, lines)` | `win.receiveLineRange` | `'GetLineRange:${bookId}:${start}:${end}'` |
| `window.receiveSearchResults(bookId, results)` | `win.receiveSearchResults` | `'SearchLines:${bookId}'` |

---

## Complete Command Reference

### Database Commands

**GetTree**
- Vue: `send('GetTree', [])`
- C#: `GetTree()` → `_dbCommands.GetTree()`
- Response: `window.receiveTreeData({ categoriesFlat, booksFlat })`
- Promise: `'GetTree'`

**GetToc**
- Vue: `send('GetToc', [bookId])`
- C#: `GetToc(int bookId)` → `_dbCommands.GetToc(bookId)`
- Response: `window.receiveTocData(bookId, { tocEntriesFlat })`
- Promise: `'GetToc:${bookId}'`

**GetLinks**
- Vue: `send('GetLinks', [lineId, tabId, bookId])`
- C#: `GetLinks(int lineId, string tabId, int bookId)` → `_dbCommands.GetLinks(...)`
- Response: `window.receiveLinks(tabId, bookId, links)`
- Promise: `'GetLinks:${tabId}:${bookId}'`

**GetTotalLines**
- Vue: `send('GetTotalLines', [bookId])`
- C#: `GetTotalLines(int bookId)` → `_dbCommands.GetTotalLines(bookId)`
- Response: `window.receiveTotalLines(bookId, totalLines)`
- Promise: `'GetTotalLines:${bookId}'`

**GetLineContent**
- Vue: `send('GetLineContent', [bookId, lineIndex])`
- C#: `GetLineContent(int bookId, int lineIndex)` → `_dbCommands.GetLineContent(...)`
- Response: `window.receiveLineContent(bookId, lineIndex, content)`
- Promise: `'GetLineContent:${bookId}:${lineIndex}'`

**GetLineId**
- Vue: `send('GetLineId', [bookId, lineIndex])`
- C#: `GetLineId(int bookId, int lineIndex)` → `_dbCommands.GetLineId(...)`
- Response: `window.receiveLineId(bookId, lineIndex, lineId)`
- Promise: `'GetLineId:${bookId}:${lineIndex}'`

**GetLineRange**
- Vue: `send('GetLineRange', [bookId, start, end])`
- C#: `GetLineRange(int bookId, int start, int end)` → `_dbCommands.GetLineRange(...)`
- Response: `window.receiveLineRange(bookId, start, end, lines)`
- Promise: `'GetLineRange:${bookId}:${start}:${end}'`

**SearchLines**
- Vue: `send('SearchLines', [bookId, searchTerm])`
- C#: `SearchLines(int bookId, string searchTerm)` → `_dbCommands.SearchLines(...)`
- Response: `window.receiveSearchResults(bookId, results)`
- Promise: `'SearchLines:${bookId}'`

### PDF Commands

**OpenPdfFilePicker**
- Vue: `send('OpenPdfFilePicker', [])`
- C#: `OpenPdfFilePicker()` → `_pdfManager.OpenPdfFilePicker()`
- Response: `window.receivePdfPath(path)` or `window.receivePdfPath(null)`
- Promise: `'OpenPdfFilePicker'`

**LoadPdfFromPath**
- Vue: `send('LoadPdfFromPath', [path])`
- C#: `LoadPdfFromPath(string path)` → `_pdfManager.LoadPdfFromPath(path)`
- Response: `window.receivePdfUrl(url)` or `window.receivePdfUrl(null)`
- Promise: `'LoadPdfFromPath'`

### UI Commands

**TogglePopOut**
- Vue: `send('TogglePopOut', [])`
- C#: `TogglePopOut()` → `_popOutToggleAction?.Invoke()`
- Response: None (action-only command)
- Promise: None

---

## Breaking Changes to Avoid

### DO NOT Change These

**Message Format:**
```typescript
// NEVER change this structure
{ command: string, args: any[] }
```

**Response Handler Names:**
```typescript
// NEVER rename these global functions
window.receiveTreeData
window.receiveTocData
window.receiveLinks
// etc.
```

**C# Method Names:**
```csharp
// NEVER rename these methods
private async void GetTree()
private async void GetToc(int bookId)
private async void GetLinks(int lineId, string tabId, int bookId)
// etc.
```

**Promise Keys:**
```typescript
// NEVER change these key patterns
'GetTree'
'GetToc:${bookId}'
'GetLinks:${tabId}:${bookId}'
// etc.
```

**Virtual Host Name:**
```csharp
// NEVER change this host name
"zayitHost"
```

### Safe Changes

**Adding New Commands:**
1. Add method to `ZayitViewerCommands.cs`: `private async void NewCommand(args) => ...`
2. Add global handler to `csharpBridge.ts`: `win.receiveNewData = (data) => { ... }`
3. Add method to `dbManager.ts`: `async newCommand() { ... }`
4. Call from Vue component: `await dbManager.newCommand()`

**Changing Database Queries:**
- Safe to modify SQL in `SqlQueries.cs`
- Safe to change query logic in `DbQueries.cs`
- MUST keep response format compatible with Vue types

**Changing Response Data:**
- Safe to add new properties to response objects
- Safe to change property values
- MUST keep existing properties (Vue may depend on them)
- MUST use camelCase property names

---

## Debugging Communication Issues

### Vue Side

**Check Bridge Availability:**
```typescript
console.log('WebView available:', bridge.isAvailable())
console.log('chrome.webview:', (window as any).chrome?.webview)
```

**Check Pending Requests:**
```typescript
console.log('Pending requests:', Array.from(bridge.pendingRequests.keys()))
```

**Check Response Handler:**
```typescript
console.log('Handler exists:', typeof window.receiveTreeData === 'function')
```

### C# Side

**Check Message Reception:**
```csharp
Console.WriteLine($"[WebMessage] Received: {json}");
```

**Check Command Dispatch:**
```csharp
Console.WriteLine($"[Dispatch] Command: {cmd.Command}, Args: {cmd.Args.Length}");
```

**Check Method Found:**
```csharp
if (method == null) {
    Console.WriteLine($"[Dispatch] Method not found: {cmd.Command}");
}
```

**Check Response Sent:**
```csharp
Console.WriteLine($"[Response] Calling: {js}");
string result = await _webView.ExecuteScriptAsync(js);
Console.WriteLine($"[Response] Result: {result}");
```

---

## File Dependency Map

```
Vue Components (*.vue)
    ↓ import
tabStore.ts, settingsStore.ts (Pinia stores)
    ↓ import
dbManager.ts (router)
    ↓ import
csharpBridge.ts (singleton)
    ↓ uses
window.chrome.webview (WebView2 API)
    ↓ message passing
CoreWebView2.WebMessageReceived (C# event)
    ↓ handled by
KleiKodeshWebView.HandleWebMessage()
    ↓ dispatches to
ZayitViewerCommands (command handler)
    ↓ delegates to
ZayitViewerDbCommands (database)
CSharpPdfManager (PDF)
HebrewBooksDownloadManager (downloads)
    ↓ uses
DbQueries.ExecuteQuery() (Dapper ORM)
    ↓ queries
Seforim.db (SQLite database)
```

---

## Performance Considerations

**Batch Loading:**
- Use `GetLineRange(start, end)` instead of multiple `GetLineContent()` calls
- Use `dbManager.startBackgroundLoad()` for large datasets

**Promise Cleanup:**
- Promises are deleted from `pendingRequests` after resolution
- Prevents memory leaks from abandoned requests

**Shared Environment:**
- Single `CoreWebView2Environment` shared across all instances
- Reduces memory footprint and initialization time

**Virtual Host:**
- Avoids CORS overhead of file:// protocol
- Enables proper JavaScript module loading

---

## Security Considerations

**String Escaping:**
```csharp
// CORRECT - JSON serialization handles escaping
string json = JsonSerializer.Serialize(data);
string js = $"window.receiveData({json});";

// WRONG - manual string concatenation
string js = $"window.receiveData('{data}');"; // Injection risk
```

**Type Safety:**
```csharp
// CORRECT - strong typing prevents injection
var cmd = JsonSerializer.Deserialize<JsCommand>(json);

// WRONG - dynamic typing
dynamic cmd = JsonSerializer.Deserialize<dynamic>(json);
```

**Error Handling:**
```csharp
// CORRECT - catch and log, don't expose to UI
try {
    // command execution
} catch (Exception ex) {
    Console.WriteLine($"Error: {ex}");
    // Don't send error details to Vue
}
```

---

## Summary

This bridge is a custom, fragile connection between Vue and C#. Every piece must work together:

1. **Vue sends** exact message format via `window.chrome.webview.postMessage()`
2. **C# receives** via `CoreWebView2.WebMessageReceived` event
3. **C# dispatches** via reflection to matching method name
4. **C# executes** database query or other operation
5. **C# responds** via `ExecuteScriptAsync()` calling global window function
6. **Vue receives** via global handler resolving promise
7. **Vue returns** data to component

Break any link in this chain and the entire system fails. Always test communication after changes.
