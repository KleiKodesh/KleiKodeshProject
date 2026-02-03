# Quick Start Guide

## ✅ PROJECT STATUS: COMMUNICATION PIPELINE VERIFIED

All communication pipelines between Vue and C# have been verified and are fully functional:

- **Category Tree Loading** ✅ Working
- **TOC Loading** ✅ Working
- **Book Lines Loading** ✅ Working (virtualized with smart batching)
- **Commentary Loading** ✅ Working
- **PDF Files** ✅ Working (SetVirtualHostNameToFolderMapping + session persistence)
- **Hebrew Books** ✅ Working (download capture + cache management)

## Project Structure

```
/
├── zayit-vue/         # Vue 3 frontend (PRIMARY) - Clean services architecture
├── Zayit-cs/          # C# desktop application - Modern services architecture
│   └── ZayitLib/
│       └── Services/  # ✅ NEW: Clean service layer
└── shared-pdfjs/      # PDF.js distribution
```

## Development

### Vue Frontend (zayit-vue/)

```bash
# Install dependencies
npm install

# Development server with hot reload
npm run dev
# Opens at http://localhost:5173
# Uses direct SQLite access via Vite plugin

# Build for production
npm run build
# Outputs single HTML file to dist/index.html

# Deploy to C# project
build-and-deploy.bat
```

**IMPORTANT BUILD RULE**: Only build and deploy when explicitly requested by the user or when making final changes. The C# project has smart pre-build that automatically rebuilds Vue when needed. Avoid unnecessary builds during development iterations.

**TESTING RULE**: Never run development servers (`npm run dev`) during testing. Use `npm run build` to test compilation and catch errors without starting long-running processes.

### C# Backend (Zayit-cs/)

```bash
# Build with dotnet
dotnet build "Zayit-cs/ZayitSolution.sln" --configuration Debug

# Or use MSBuild if available
msbuild "Zayit-cs/ZayitSolution.sln" /p:Configuration=Debug
```

## ✅ VERIFIED ARCHITECTURE

### Modern Services Architecture (C#)

```
Vue Component → webviewBridge → WebViewBridgeService → ServiceProvider → Specialized Services
                                                                        ├── DbService
                                                                        ├── PdfService
                                                                        └── HebrewBooksService
```

### Communication Protocol

**Vue → C# (JSON Messages)**:

```typescript
// Modern bridge with lazy-loaded message listener
await webviewBridge.call(
  "GetTree",
  SqlQueries.getAllCategories,
  SqlQueries.getAllBooks,
);
await webviewBridge.call("OpenPdfFilePicker");
await webviewBridge.call("PrepareHebrewBookDownload", bookId, title, "view");
```

**C# → Vue (JSON Responses)**:

```csharp
// ServiceProvider delegates to specialized services
public object GetTree(string cq, string bq) => _db.GetTree(cq, bq);
public object OpenPdfFilePicker() => _pdf.OpenPdfFilePicker();
public object PrepareHebrewBookDownload(string id, string title, string action) =>
    _hebrewBooks.PrepareDownload(id, title, action).GetAwaiter().GetResult();
```

### SQL Queries - Single Source of Truth

- **Defined in**: `zayit-vue/src/services/sqlQueries.ts`
- **Used by**: Both development (Vite) and production (C#)
- **Rule**: Never define SQL elsewhere

### Data Flow

**Development Mode**:

```
Component → dbService → devQuery → Vite Plugin → SQLite
```

**Production Mode**:

```
Component → dbService → webviewBridge → C# ServiceProvider → DbService → SQLite
```

## ✅ VERIFIED FILE OPERATIONS

### PDF Files with SetVirtualHostNameToFolderMapping

```csharp
// C# creates secure virtual URLs
webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "zayitHost", htmlPath, CoreWebView2HostResourceAccessKind.Allow);
// Returns: https://zayitHost/temp/uniqueId_filename.pdf
```

### Hebrew Books with Download Capture

```csharp
// Captures downloads and manages cache
webView.CoreWebView2.DownloadStarting += (sender, e) => {
    // Redirect to cache directory
    e.ResultFilePath = Path.Combine(cacheDir, fileName);
};
// Auto-closes download dialog: webView.CoreWebView2.CloseDefaultDownloadDialog()
```

## Common Tasks

### Add New Database Operation

1. **Add SQL** to `zayit-vue/src/services/sqlQueries.ts`:

```typescript
export const SqlQueries = {
  getMyData: (id: number) => `SELECT * FROM myTable WHERE id = ${id}`,
};
```

2. **Add Vue service method** to `zayit-vue/src/services/dbService.ts`:

```typescript
async getMyData(id: number) {
  if (this.isWebViewAvailable()) {
    return await webviewBridge.call('GetMyData', id, SqlQueries.getMyData(id))
  } else {
    return await devQuery(SqlQueries.getMyData(id))
  }
}
```

3. **Add C# service method** to `Zayit-cs/ZayitLib/Services/DbService.cs`:

```csharp
public object GetMyData(int id, string query)
{
    return _dbQueries.ExecuteQuery(query);
}
```

4. **Expose in ServiceProvider** `Zayit-cs/ZayitLib/Services/ServiceProvider.cs`:

```csharp
public object GetMyData(int id, string q) => _db.GetMyData(id, q);
```

### Add New PDF Operation

1. **Add to PdfService.cs**:

```csharp
public object MyPdfOperation() {
    // Use SetVirtualHostNameToFolderMapping for file access
    return new { success = true, virtualUrl = "https://zayitHost/..." };
}
```

2. **Expose in ServiceProvider**:

```csharp
public object MyPdfOperation() => _pdf.MyPdfOperation();
```

3. **Add Vue service call**:

```typescript
await webviewBridge.call("MyPdfOperation");
```

### Add New Hebrew Books Operation

1. **Add to HebrewBooksService.cs**:

```csharp
public async Task<object> MyHebrewBooksOperation() {
    // Use download capture and cache management
    return new { success = true };
}
```

2. **Expose in ServiceProvider with proper async handling**:

```csharp
public object MyHebrewBooksOperation() =>
    _hebrewBooks.MyHebrewBooksOperation().GetAwaiter().GetResult();
```

## ✅ VERIFIED SERVICES

### Vue Services (Clean Architecture)

- `webviewBridge.ts` - Singleton bridge with lazy message listener
- `dbService.ts` - Database operations (cleaned of legacy PDF/Hebrew Books methods)
- `pdfService.ts` - Unified PDF operations with session persistence
- `webviewHebrewBooks.ts` - Clean Hebrew Books operations
- `hebrewBooksHandlers.ts` - Event handlers using unified service

### C# Services (Modern Architecture)

- `ServiceProvider.cs` - Central hub with all Vue-required methods
- `WebViewBridgeService.cs` - JSON message parsing and method invocation
- `DbService.cs` - Database operations via DbQueries
- `PdfService.cs` - SetVirtualHostNameToFolderMapping operations
- `HebrewBooksService.cs` - Download capture and cache management

## Troubleshooting

### Build Fails

```bash
# Vue build
cd zayit-vue
npm install
npm run build

# C# build
dotnet build "Zayit-cs/ZayitSolution.sln" --configuration Debug
```

### Communication Issues

1. Check browser DevTools Console for Vue errors
2. Check Visual Studio Output window for C# errors
3. Verify WebView2 Runtime is installed
4. Test `webviewBridge.isAvailable()` in Vue

### File Operations Not Working

1. Verify `SetVirtualHostNameToFolderMapping` is set up
2. Check file permissions and paths
3. Ensure virtual URLs use correct host name (`zayitHost`)

### Hebrew Books Cache Issues

1. Check cache directory exists: `Html/pdfjs/web/hebrewbookscache/`
2. Verify download event handlers are registered
3. Test cache stats: `GetHebrewBooksCacheStats`

## Resources

- Vue 3 Docs: https://vuejs.org/
- Vite Docs: https://vitejs.dev/
- WebView2 Docs: https://learn.microsoft.com/en-us/microsoft-edge/webview2/
- Pinia Docs: https://pinia.vuejs.org/
