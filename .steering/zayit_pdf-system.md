---
inclusion: fileMatch
fileMatchPattern: '**/CSharpPdfManager*|**/PdfView*|**/pdfService*|**/LocalPdf*|**/HebrewBooks*'
---

# Zayit PDF System

## Two PDF Systems (Don't Mix!)

### Local PDF System
**Purpose**: User-selected PDF files via file dialog
**Pattern**: `local-{guid}.pdf`
**Cache**: `Html/pdfjs/web/local-*.pdf`
**URL**: `https://app.local/{filename}`
**Commands**: `OpenLocalPdfDialog`, `LocalPdfCommands`

### Hebrew Books System  
**Purpose**: hebrewbooks.org PDF downloads
**Pattern**: `{sanitized-title}_{bookId}.pdf`
**Cache**: `Html/pdfjs/web/hebrewbookscache/`
**URL**: `https://zayitHost/pdfjs/web/hebrewbookscache/{filename}`
**Commands**: `PrepareHebrewBookDownload`, `HebrewBooksCommands`

## Virtual Host System

### Local PDFs
```csharp
// Create unique virtual host for each file
string virtualHost = $"app.local";
_webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    virtualHost, directory, CoreWebView2HostResourceAccessKind.Allow);
```

### Hebrew Books
```csharp
// Single virtual host for cache directory
_webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "zayitHost", htmlDirectory, CoreWebView2HostResourceAccessKind.Allow);
```

## Cache Management

### Local PDF Cache
- **Max Files**: 10 PDFs
- **Pattern**: `local-*.pdf`
- **Strategy**: Delete oldest by last access time
- **Session**: `local-pdf-session.json`

### Hebrew Books Cache
- **Max Files**: 10 PDFs  
- **Pattern**: `{title}_{id}.pdf`
- **Strategy**: Delete oldest by last access time
- **No Session**: Managed by Vue tab store

## PDF State Structure
```typescript
tab.pdfState = {
    fileName: string,     // Display name
    fileUrl: string,      // Virtual URL for PDF.js
    filePath?: string,    // Original path (local PDFs only)
    source: 'local' | 'hebrewbooks'
}
```

## Common Patterns

### File Dialog (Local PDFs)
```csharp
var result = await WebViewDialogHelper.ShowOpenFileDialogAsync(
    _webView, 
    "PDF Files (*.pdf)|*.pdf", 
    "Select PDF File"
);
```

### Download Progress (Hebrew Books)
```csharp
await SendReady(bookId, action);
// ... download happens ...
await SendDownloadComplete(bookId, fileName);
```

### Error Handling
```csharp
try {
    // PDF operation
} catch (Exception ex) {
    Console.WriteLine($"[PDF] Error: {ex}");
    // Send null response to indicate failure
}
```