---
inclusion: fileMatch
fileMatchPattern: '**/CSharpPdfManager*|**/PdfView*|**/pdfService*|**/LocalPdf*'
---

# Zayit Local PDF Management System

## CRITICAL: Local PDF File Handling Architecture

### System Overview
The PDF management system handles local PDF files selected by users through file dialogs and creates virtual URLs for PDF.js viewing.

**Components:**
- `CSharpPdfManager` - Virtual host mapping and blob URL creation
- `LocalPdfCommands` - Local PDF operations ONLY
- `LocalPdfManager` - Local PDF file handling
- `pdfService.ts` - Vue service for PDF operations
- `PdfViewPage.vue` - PDF viewer component

### File Naming Convention
**Pattern**: `local-{guid}.pdf`
**Examples**: 
- `local-a1b2c3d4.pdf`
- `local-e5f6g7h8.pdf`

### Cache Directory Structure
```
Html/pdfjs/web/
├── local-a1b2c3d4.pdf
├── local-e5f6g7h8.pdf
└── local-i9j0k1l2.pdf
```

### Command Flow - Local PDF Opening

#### Vue Integration:
1. User clicks "PDF" tile in Vue → Vue sends `OpenLocalPdfDialog` command
2. C# shows `OpenFileDialog`
3. Selected file copied to `Html/pdfjs/web/local-{guid}.pdf`
4. C# sends response: `{type: "pdfFilePicker", filePath: "...", fileName: "..."}`
5. Vue receives response and creates PDF tab with virtual host URL
6. Session info saved for persistence

#### PDF.js Direct Access:
1. User clicks "Open" in PDF.js viewer → JavaScript calls overridden `webViewerOpenFile`
2. JavaScript sends `OpenLocalPdfDialog` command to C#
3. Same C# process as above
4. PDF.js opens directly via `PDFViewerApplication.open()`

### Virtual Host System
**Purpose**: Create accessible URLs for local files without exposing file system paths
**Pattern**: `https://app.local/{guid}/{filename}`

### Bridge Communication Pattern

**C# → Vue Response:**
```csharp
// File picker response
string js = $"window.receivePdfFilePath && window.receivePdfFilePath('{virtualUrl}', '{fileName}', '{virtualUrl}');";
await _webView.ExecuteScriptAsync(js);
```

**Vue → C# Request:**
```typescript
// Request file picker
const promise = this.csharp.createRequest('OpenPdfFilePicker')
this.csharp.send('OpenPdfFilePicker', [])
const result = await promise
```

### Virtual Host Mapping
```csharp
// Create unique virtual host for each file
string virtualHost = $"app.local";
string directory = Path.GetDirectoryName(filePath);
_webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    virtualHost, directory, CoreWebView2HostResourceAccessKind.Allow);
```

### URL Construction
**Pattern**: `https://app.local/{filename}`
**Example**: `https://app.local/MyDocument.pdf`

### PDF State Management
```typescript
// Tab PDF state structure
tab.pdfState = {
    fileName: string,     // Display name
    fileUrl: string,      // Virtual URL for PDF.js
    filePath?: string,    // Original file path (for reference)
    source: 'local'       // Source type
}
```

### File Dialog Configuration
```csharp
dialog.Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*";
dialog.Title = "Select PDF File";
dialog.CheckFileExists = true;
dialog.CheckPathExists = true;
```

### Cache Management
- **Max Files**: 10 PDFs
- **Strategy**: Delete oldest by last access time
- **Scope**: Only manages files with `local-*.pdf` pattern
- **Automatic cleanup**: On cache overflow

### Session Persistence
- **Session file**: `Html/pdfjs/web/local-pdf-session.json`
- **Stores**: original path, filename, virtual filename, last opened time
- **Restoration**: Checks virtual file exists, falls back to original path if needed
- **Limitation**: Virtual host mappings don't persist across app restarts
- **UI Handling**: Show "Select file again" message for restored tabs

### Browser Fallback
**Development Mode**: Uses browser file picker with blob URLs
**Production Mode**: Uses C# file dialog with virtual host URLs

### Error Handling Pattern

**C# Side:**
```csharp
try {
    var (fileName, virtualUrl) = await _pdfManager.ShowFileDialogAndCreateUrl();
    // Send success response
} catch (Exception ex) {
    Console.WriteLine($"[PdfManager] Error: {ex}");
    // Send null response
    string js = "window.receivePdfFilePath && window.receivePdfFilePath(null, null, null);";
    await _webView.ExecuteScriptAsync(js);
}
```

**Vue Side:**
```typescript
try {
    const result = await pdfService.showFilePicker()
    if (result.dataUrl) {
        // Handle success
    }
} catch (error) {
    console.error('[PDF] File picker failed:', error)
}
```

### Integration with PDF.js
- Uses standard PDF.js viewer at `/pdfjs/web/viewer.html`
- Passes file URL as `?file=` parameter
- Supports all PDF.js features (zoom, search, etc.)

### Virtual Host Security
- Uses `CoreWebView2HostResourceAccessKind.Allow` for file access
- Each file gets its own virtual host mapping
- Mappings are cleaned up when no longer needed

### Key Features
1. **Immediate Loading**: Files served via virtual host - same speed as PDF.js native
2. **Memory Efficient**: WebView2 streams from disk, no full file loading
3. **Session Persistence**: Automatically restores last opened PDF on startup
4. **Vue Integration**: Seamlessly integrated with Vue's tab system
5. **Dual Access Points**: Users can open PDFs from Vue UI or PDF.js viewer directly

### Common Issues & Solutions

**Issue**: PDF not loading after file selection
**Check**:
1. Virtual host mapping was created successfully
2. File path is accessible and file exists
3. PDF.js viewer received correct URL

**Issue**: File picker not opening
**Check**:
1. C# bridge is available (`csharp.isAvailable()`)
2. Method signature matches: `OpenPdfFilePicker()` with no parameters
3. Bridge handler `receivePdfFilePath` is registered

**Issue**: Files not accessible after restart
**Expected**: This is normal behavior - virtual mappings don't persist
**Solution**: Show file reselection UI for restored tabs

### Development Notes
- Virtual host system provides security by not exposing real file paths
- Each file selection creates a new virtual host mapping
- Browser fallback uses standard File API with blob URLs
- PDF.js integration is identical for both virtual and blob URLs
- **NEVER mix with Hebrew Books system** - they have different purposes and file patterns
- Files named with GUID to avoid conflicts