# PDF Implementation Documentation

## Overview

This document explains the PDF functionality implementation in the Zayit project. **IMPORTANT**: There are TWO separate PDF systems that must NOT be confused.

## 🚨 CRITICAL: Two Separate PDF Systems

### 1. Hebrew Books System (`HebrewBooksCommands` + `HebrewBooksDownloadManager`)
- **Purpose**: Downloads PDFs from hebrewbooks.org website
- **File Pattern**: `hebrewbooks-{bookId}.pdf`
- **Location**: `Html/pdfjs/web/hebrewbooks-*.pdf`
- **Trigger**: User clicks Hebrew book in the library
- **Process**: Downloads from website → Caches in PDF.js directory → Opens in viewer

### 2. Local PDF System (`LocalPdfCommands` + `LocalPdfManager`) ✅ **INTEGRATED WITH VUE**
- **Purpose**: Opens local PDF files from user's computer
- **File Pattern**: `local-{guid}.pdf`
- **Location**: `Html/pdfjs/web/local-*.pdf`
- **Trigger**: User clicks "PDF" tile on homepage OR "Open PDF" in tab header
- **Process**: C# file dialog → Copy to PDF.js directory → Vue creates PDF tab

## Implementation Details

### File Structure
```
Viewer/
├── HebrewBooksCommands.cs      # Hebrew books operations ONLY
├── HebrewBooksDownloadManager.cs # Hebrew books download logic
├── LocalPdfCommands.cs         # Local PDF operations ONLY  
├── LocalPdfManager.cs          # Local PDF file handling
├── ZayitViewerCommands.cs      # Main dispatcher (coordinates both)
└── ZayitViewer.cs             # WebView2 host
```

### Vue Integration ✅ **COMPLETED**

Vue now uses the new C# system via the `OpenLocalPdfDialog` command:

#### Updated Vue Files:
- `HomePage.vue` - PDF tile uses new command
- `TabHeader.vue` - PDF menu item uses new command  
- `dbManager.ts` - Updated to call new command

#### Command Flow:
1. User clicks "PDF" tile or menu item in Vue
2. Vue sends `OpenLocalPdfDialog` command to C#
3. C# shows `OpenFileDialog` 
4. C# copies selected file to `Html/pdfjs/web/local-{guid}.pdf`
5. C# sends response back to Vue with file info
6. Vue creates PDF tab with virtual host URL

### Key Features Achieved

#### ✅ Local PDF Requirements Met:
1. **Immediate Loading**: Files served via virtual host - same speed as PDF.js native
2. **Memory Efficient**: WebView2 streams from disk, no full file loading
3. **Session Persistence**: Automatically restores last opened PDF on startup
4. **Vue Integration**: Seamlessly integrated with Vue's tab system

#### ✅ Dual System Support:
- **Vue Integration**: Vue calls C# for file selection, creates its own PDF tabs
- **PDF.js Override**: C# also overrides PDF.js built-in "Open" button for direct access
- **Both work together**: Users can open PDFs from Vue UI or directly in PDF.js viewer

### Command Flow

#### Local PDF Opening (Vue Integration):
1. User clicks "PDF" tile in Vue → Vue sends `OpenLocalPdfDialog` command
2. C# shows `OpenFileDialog`
3. Selected file copied to `Html/pdfjs/web/local-{guid}.pdf`
4. C# sends response: `{type: "pdfFilePicker", filePath: "...", fileName: "..."}`
5. Vue receives response and creates PDF tab with virtual host URL
6. Session info saved for persistence

#### Local PDF Opening (PDF.js Direct):
1. User clicks "Open" in PDF.js viewer → JavaScript calls overridden `webViewerOpenFile`
2. JavaScript sends `OpenLocalPdfDialog` command to C#
3. Same C# process as above
4. PDF.js opens directly via `PDFViewerApplication.open()`

#### Hebrew Book Opening:
1. User clicks Hebrew book in library
2. Vue sends `PrepareHebrewBookDownload` command to C#
3. C# downloads from hebrewbooks.org to `Html/pdfjs/web/hebrewbooks-{bookId}.pdf`
4. PDF.js opens via virtual host URL: `https://zayitHost/pdfjs/web/hebrewbooks-{bookId}.pdf`

### Session Persistence

#### Local PDFs:
- Session file: `Html/pdfjs/web/local-pdf-session.json`
- Stores: original path, filename, virtual filename, last opened time
- Restoration: Checks virtual file exists, falls back to original path if needed

#### Hebrew Books:
- Handled by existing `HebrewBooksDownloadManager`
- Uses different session mechanism (not covered in this implementation)

### Cache Management

#### Local PDFs:
- Keeps last 10 files in `Html/pdfjs/web/local-*.pdf`
- Automatic cleanup on cache overflow
- Files named with GUID to avoid conflicts

#### Hebrew Books:
- Keeps last 10 files in `Html/pdfjs/web/hebrewbooks-*.pdf`
- Managed by `HebrewBooksDownloadManager`
- Files named with book ID for easy identification

## Usage Examples

### Initialize Both Systems:
```csharp
// In ZayitViewer initialization
var commands = _commandHandler as ZayitViewerCommands;
commands?.InitializeHebrewBooksDownloadManager(CoreWebView2);  // Hebrew books
await commands?.InitializeLocalPdfManager(HtmlPath);           // Local PDFs
```

### Vue Integration:
```typescript
// Vue sends command (automatically handled)
chrome.webview.postMessage({
    command: 'OpenLocalPdfDialog',
    args: []
});

// Vue receives response
window.addEventListener('message', (event) => {
    const data = JSON.parse(event.data);
    if (data.type === 'pdfFilePicker' && data.success) {
        // Create PDF tab with file info
        tabStore.openPdfWithFilePath(data.fileName, data.filePath);
    }
});
```

## File Naming Conventions

| System | Pattern | Example | Purpose |
|--------|---------|---------|---------|
| Hebrew Books | `hebrewbooks-{bookId}.pdf` | `hebrewbooks-12345.pdf` | Downloaded from website |
| Local PDFs | `local-{guid}.pdf` | `local-a1b2c3d4.pdf` | User's local files |

## Important Notes

1. **Never mix the two systems** - they have different purposes and file patterns
2. **Virtual host mapping** enables immediate loading without memory overhead
3. **Session persistence** works differently for each system
4. **Cache management** is separate for each system
5. **Vue integration** maintains compatibility with existing Vue PDF tab system
6. **Dual access points**: Users can open PDFs from Vue UI or PDF.js viewer directly

## Future Maintenance

When working on PDF functionality:

1. **Identify the system first**: Hebrew books vs Local PDFs
2. **Use the correct class**: `HebrewBooksCommands` vs `LocalPdfCommands`
3. **Check file patterns**: `hebrewbooks-*` vs `local-*`
4. **Maintain separation**: Don't mix the two systems
5. **Update both Vue and C#** when changing command interfaces
6. **Test both access points**: Vue UI and PDF.js direct access

This separation ensures clean code, easier maintenance, and prevents confusion between the two distinct PDF workflows while providing seamless integration with Vue's existing tab system.