# Hebrew Book Download Architecture - COMPLETED IMPLEMENTATION

## Overview

Hebrew book downloads use a **direct file serving** approach where files are downloaded directly to the PDF.js web directory and served via zayitHost virtual host mapping for optimal performance.

## ✅ COMPLETED Architecture

### Direct File Serving

- **Files download directly to PDF.js web directory** (`Html/pdfjs/web/`)
- **Served via zayitHost virtual host** as `https://zayitHost/pdfjs/web/{bookId}.pdf`
- **No blob conversion** - eliminates memory overhead
- **PDF.js origin validation disabled** - allows seamless file loading
- **Smart cache management** - maintains max 10 Hebrew book PDFs

### Two Download Modes

#### ✅ View Mode (Hebrew Book List Item Click)

1. Vue calls `PrepareHebrewBookDownload` with action "view"
2. C# checks if file exists in PDF.js web directory
3. **If cached**: C# sends `receiveHebrewBookDownloadComplete` immediately
4. **If not cached**: C# sends `receiveHebrewBookDownloadReady` → Vue triggers download → C# downloads to PDF.js web directory → C# sends `receiveHebrewBookDownloadComplete`
5. Vue constructs URL `https://zayitHost/pdfjs/web/{bookId}.pdf` and loads in PDF.js
6. **Download dialog auto-closed** for seamless viewing experience

#### ✅ Download Mode (Download Button Click)

1. Vue calls `PrepareHebrewBookDownload` with action "download"
2. C# shows save dialog for user to choose location
3. Vue triggers browser download
4. C# captures download to user-selected location
5. **Download dialog remains open** for user feedback
6. C# notifies Vue of completion via `receiveHebrewBookDownloadComplete`

## ✅ Implementation Details

### Vue Frontend (`hebrewBooksStore.ts`)

```typescript
const openHebrewBookViewer = async (bookId: string, title: string) => {
  // Create promises for both ready and completion signals
  const readyPromise = csharp.createRequest(`PrepareHebrewBookDownload:${bookId}:view`);
  const downloadCompletePromise = csharp.createRequest(`HebrewBookDownloadComplete:${bookId}`);

  // Send prepare command
  csharp.send('PrepareHebrewBookDownload', [bookId, title, 'view']);

  // Race between ready (non-cached) and completion (cached)
  const result = await Promise.race([readyPromise, downloadCompletePromise]);

  if (result.type === 'ready') {
    // Trigger download and wait for completion
    triggerBrowserDownload(bookId, title);
    await downloadCompletePromise;
  }

  // Construct PDF.js URL and load
  const fileUrl = `https://zayitHost/pdfjs/web/${bookId}.pdf`;
  tab.pdfState = { fileName: `${title}.pdf`, fileUrl, source: 'hebrewbook', bookId, bookTitle: title };
};
```

### C# Backend (`HebrewBooksDownloadManager.cs`)

```csharp
public HebrewBooksDownloadManager(CoreWebView2 webView)
{
    _webView = webView;
    // Download directly to PDF.js web directory
    _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Html", "pdfjs", "web");
    Directory.CreateDirectory(_cacheDirectory);
}

public async Task PrepareHebrewBookDownload(string bookId, string title, string action)
{
    if (action == "view")
    {
        string pdfFilePath = Path.Combine(_cacheDirectory, $"{bookId}.pdf");
        if (File.Exists(pdfFilePath))
        {
            // Cached - send completion immediately
            await SendDownloadComplete(bookId);
            return;
        }
    }
    
    // Not cached or download action - send ready signal
    await SendReady(bookId, action);
}

// Download handling with auto-close for view mode
e.DownloadOperation.StateChanged += async (sender, args) => {
    if (download.State == CoreWebView2DownloadState.Completed)
    {
        if (action == "view")
        {
            // Close download dialog for seamless viewing
            _webView.CloseDefaultDownloadDialog();
        }
        await SendDownloadComplete(bookId);
    }
};
```

### WebView2 Configuration (`ZayitViewer.cs`)

```csharp
// Virtual host mapping with DenyCors to avoid CORS issues
this.CoreWebView2.SetVirtualHostNameToFolderMapping("zayitHost", HtmlPath,
    CoreWebView2HostResourceAccessKind.DenyCors);
```

### PDF.js Security (`viewer.mjs`)

```javascript
// Completely disabled origin validation for zayitHost
var validateFileURL = function (file) {
    // Modified for Zayit: Always allow file access
    return;
};
```

## ✅ Communication Protocol

### Commands

| Command                     | Args                      | Purpose                         | Response                         |
| --------------------------- | ------------------------- | ------------------------------- | -------------------------------- |
| `PrepareHebrewBookDownload` | `[bookId, title, action]` | Prepare for download/view       | `receiveHebrewBookDownloadReady` or `receiveHebrewBookDownloadComplete` |

### Responses

| Response                            | Args                      | Purpose                    |
| ----------------------------------- | ------------------------- | -------------------------- |
| `receiveHebrewBookDownloadReady`    | `(bookId, action)`        | C# ready for download      |
| `receiveHebrewBookDownloadComplete` | `(bookId, success)`       | File ready for viewing     |

## ✅ Session Persistence

Hebrew books maintain session persistence through:

- **PdfState interface** extended with `bookId` and `bookTitle` fields
- **Auto-reload mechanism** in HebrewBooksViewPage.vue
- **Cached file detection** - files persist in PDF.js web directory across sessions

## ✅ Cache Management

```csharp
private void ManageCache()
{
    // Only manage Hebrew book PDF files (numeric filenames like "14304.pdf")
    var hebrewBookFiles = Directory.GetFiles(_cacheDirectory, "*.pdf")
        .Where(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out _))
        .Select(f => new FileInfo(f))
        .OrderBy(f => f.LastAccessTime)
        .ToArray();

    // Delete oldest files if more than MAX_CACHE_SIZE (10)
    while (hebrewBookFiles.Length > MAX_CACHE_SIZE)
    {
        hebrewBookFiles[0].Delete();
        hebrewBookFiles = hebrewBookFiles.Skip(1).ToArray();
    }
}
```

## ✅ Key Benefits Achieved

1. **Performance**: No memory-intensive blob conversion
2. **Simplicity**: Direct file serving eliminates complex path processing
3. **Security**: Files only accessible within WebView2 environment
4. **Reliability**: zayitHost virtual host eliminates CORS issues
5. **User Experience**: Auto-close download dialog for viewing, keep open for downloads
6. **Maintainability**: Clean, minimal codebase

## ✅ File Structure

```
Html/
├── pdfjs/
│   └── web/
│       ├── 14304.pdf          # Hebrew book cache
│       ├── 14318.pdf          # Hebrew book cache
│       ├── viewer.html        # PDF.js viewer
│       └── viewer.mjs         # Modified for origin bypass
├── index.html                 # Main app
└── ...
```

## ✅ URL Pattern

- **PDF.js Viewer**: `https://zayitHost/pdfjs/web/viewer.html?file=https://zayitHost/pdfjs/web/14304.pdf`
- **Direct File Access**: `https://zayitHost/pdfjs/web/14304.pdf`
- **Virtual Host Root**: `https://zayitHost/` → `Html/` directory

## File Locations

- **Vue Store**: `zayit-vue/src/stores/hebrewBooksStore.ts` ✅
- **C# Download Manager**: `Zayit-cs/Zayit/Viewer/HebrewBooksDownloadManager.cs` ✅
- **C# Commands**: `Zayit-cs/Zayit/Viewer/ZayitViewerCommands.cs` ✅
- **WebView2 Setup**: `Zayit-cs/Zayit/Viewer/ZayitViewer.cs` ✅
- **Bridge**: `zayit-vue/src/data/csharpBridge.ts` ✅
- **PDF.js Viewer**: `Zayit-cs/Zayit/bin/Debug/Html/pdfjs/web/viewer.mjs` ✅
- **Hebrew Books View**: `zayit-vue/src/components/pages/HebrewBooksViewPage.vue` ✅

## Status: ✅ COMPLETE AND WORKING

All functionality implemented and tested:
- ✅ Direct file serving to PDF.js web directory
- ✅ zayitHost virtual host mapping with DenyCors
- ✅ PDF.js origin validation bypass
- ✅ Smart cache management (max 10 files)
- ✅ Session persistence with bookId/bookTitle
- ✅ Auto-close download dialog for viewing
- ✅ Separate download functionality for saving
- ✅ Promise-based C# bridge communication
- ✅ Race condition handling for cached vs non-cached files
