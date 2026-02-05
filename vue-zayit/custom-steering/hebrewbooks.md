# Hebrew Book Download Architecture - UPDATED IMPLEMENTATION

## Overview

Hebrew book downloads use a unified origin architecture that eliminates cross-origin restrictions by placing Hebrew book cache files directly within the PDF.js web directory structure, sharing the same origin as the PDF.js viewer.

## ✅ MAJOR ARCHITECTURAL CHANGES (Latest Update)

### Unified Origin Architecture

- **Hebrew books cache moved to PDF.js web directory** (`Html/pdfjs/web/hebrewbookscache/`)
- **Same origin as PDF.js viewer** - eliminates cross-origin restrictions
- **No separate virtual host needed** - uses existing PDF.js viewer origin
- **Browser-initiated downloads** - C# captures protected Hebrew Books URLs
- **Two distinct flows** - viewing (cache) vs download (SaveAs dialog)

### Cache Management Architecture

- **Simple direct deletion** - Files deleted immediately when tabs close
- **LRU eviction** - Oldest files removed when cache exceeds 10 files
- **No active file tracking** - Cache manager works directly with files on disk
- **Automatic re-download** - Missing files are detected and re-downloaded on tab switch/session reload

### Cross-Origin Research Findings

**PDF.js Origin Validation Discovery:**

- PDF.js has built-in origin validation in `viewer.mjs` (lines 18522-18531)
- Validates that file origin matches viewer origin
- Blocks cross-origin file loading with error: "file origin does not match viewer's"
- **Solution**: Place Hebrew books in same directory structure as PDF.js viewer

**PDF.js File Parameter Research:**

- File parameter accepts both relative and absolute paths
- Key constraint is **same-origin policy**, not path format
- Both work: `file=hebrewbookscache/book.pdf` (relative) or `file=/pdfjs/web/hebrewbookscache/book.pdf` (absolute)
- Must be served from same origin as viewer.html

## ✅ COMPLETED Two-Flow Architecture

### Flow 1: Open Book for Viewing (Cache if needed, no SaveAs)

**Vue Side:**

1. User clicks book → `store.openHebrewBookViewer(bookId, title)`
2. Creates tab and sets title immediately
3. Calls `webviewHebrewBooks.prepareForViewing(bookId, title)`
4. If cached → Sets PDF state immediately with relative URL
5. If not cached → Creates hidden `<a>` link and triggers browser download
6. Waits for C# notification via `handleHebrewBookViewingReady`

**C# Side:**

1. `PrepareForViewing` checks cache in `Html/pdfjs/web/hebrewbookscache/`
2. If cached → Returns relative URL `hebrewbookscache/{fileName}` immediately
3. If not cached → Sets up `_pendingViewingContext` and waits
4. Download handler captures browser-initiated download from Hebrew Books
5. Saves to `Html/pdfjs/web/hebrewbookscache/` directory
6. Enforces cache file limit (removes oldest files if > 10)
7. Calls `NotifyViewingReady` with relative URL `hebrewbookscache/{fileName}`
8. Vue receives notification and sets PDF state

### Flow 2: Download Book with SaveAs (User chooses location)

**Vue Side:**

1. User clicks download button → `store.downloadHebrewBook(bookId, title)`
2. Calls `webviewHebrewBooks.prepareForDownload(bookId, title)`
3. If cached → C# shows SaveAs and copies file immediately
4. If not cached → C# shows SaveAs first, then Vue triggers browser download
5. Waits for C# notification via `handleHebrewBookDownloadComplete`

**C# Side:**

1. `PrepareForDownload` checks cache in `Html/pdfjs/web/hebrewbookscache/`
2. If cached → Shows SaveAs dialog and copies file immediately
3. If not cached → Shows SaveAs dialog first, stores target path, returns to Vue
4. Download handler captures browser-initiated download from Hebrew Books
5. Saves to cache, then copies to user's chosen location
6. Calls `NotifyDownloadComplete`
7. Vue receives notification (just for logging)

### Flow 3: Tab Closure Cleanup

**Vue Side:**

1. Tab closes → `tabStore.closeTab()` or `tabStore.closeTabById()`
2. Calls `handleTabCleanup(tab)` before removing tab
3. If Hebrew book tab → Calls `handleHebrewBookTabClosed(tab)`
4. Calls `webviewHebrewBooks.notifyTabClosed(fileName)`

**C# Side:**

1. `HandleTabClosed(fileName)` receives notification
2. Directly deletes file from cache directory
3. No complex state tracking needed

### Flow 4: Tab Switch / Session Reload (Missing File Recovery)

**Vue Side:**

1. Tab becomes active or session loads with Hebrew book tab
2. `HebrewBooksViewPage` detects missing `fileUrl` in tab state
3. Calls `webviewHebrewBooks.checkInCache(bookId, title)` first
4. If cached → Updates tab state immediately with cached URL (instant load)
5. If not cached → Calls `store.openHebrewBookViewer()` to re-download
6. Download completes and tab state updates

**C# Side:**

1. `CheckFileInCache(bookId, title)` checks if file exists on disk
2. If exists → Returns relative URL `hebrewbookscache/{fileName}`
3. If not exists → Returns `exists: false`
4. Vue then triggers re-download if needed

## ✅ CACHE MANAGEMENT IMPLEMENTATION

### HebrewBooksCacheManager (Simplified)

**No Active File Tracking:**

- Removed `_activeFiles` HashSet
- Removed `RegisterActive()` and `UnregisterActive()` methods
- Cache manager works directly with files on disk

**Simple Operations:**

- `EnforceFileLimit()` - Removes oldest files when cache exceeds 10 files
- `GetStats()` - Calculates statistics directly from disk
- `ClearAll()` - Deletes all PDF files in cache directory

**LRU Eviction:**

- Uses `FileInfo.LastAccessTime` to determine oldest files
- Automatically removes oldest files when new files are added
- No complex state management needed

### Tab Closure Cleanup

**Direct File Deletion:**

```csharp
public void HandleTabClosed(string fileName)
{
    var filePath = Path.Combine(GetCacheDirectory(), fileName);
    if (File.Exists(filePath))
    {
        File.Delete(filePath);
        Console.WriteLine($"Deleted Hebrew book from cache: {fileName}");
    }
}
```

**Benefits:**

- Simple and reliable
- No state synchronization issues
- Files are immediately deleted when tabs close
- Cache stays clean automatically

### Missing File Recovery

**Cache Check Method:**

```csharp
public object CheckFileInCache(string bookId, string title)
{
    var fileName = SanitizeFileName($"{title}_{bookId}") + ".pdf";
    var cachedPath = GetCachedFilePath(fileName);
    var exists = File.Exists(cachedPath);

    if (exists)
    {
        var relativeUrl = $"hebrewbookscache/{fileName}";
        return new { exists = true, fileName = fileName, url = relativeUrl };
    }

    return new { exists = false, fileName = fileName };
}
```

**Vue Integration:**

```typescript
// Check cache first before re-downloading
const cacheResult = await webviewHebrewBooks.checkInCache(bookId, title);

if (cacheResult.exists && cacheResult.url) {
  // File exists in cache, update tab state directly
  tab.pdfState = {
    ...pdfState,
    fileUrl: cacheResult.url,
    fileName: cacheResult.fileName,
  };
} else {
  // File not in cache, trigger re-download
  await hebrewBooksStore.openHebrewBookViewer(bookId, title);
}
```

## ✅ CRITICAL IMPLEMENTATION DETAILS

### Protected URL Handling

**Hebrew Books URLs are protected** and require browser-initiated downloads:

- C# cannot download directly from `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}`
- Vue creates hidden `<a>` link and triggers click to initiate browser download
- C# captures the browser-initiated download via `DownloadStarting` event
- **NOT using `window.location.href`** - uses proper `<a>` link click

### Directory Structure

```
Html/
├── pdfjs/
│   └── web/
│       ├── hebrewbookscache/          # ← C# creates and manages this
│       │   ├── BookTitle_12345.pdf    # Hebrew book cache files (max 10)
│       │   └── AnotherBook_67890.pdf
│       ├── viewer.html                # PDF.js viewer
│       ├── viewer.mjs                 # PDF.js viewer logic
│       └── ...
├── index.html
└── ...
```

### URL Construction

**Hebrew Books View Page constructs PDF.js URL:**

```javascript
const hebrewBookUrl = computed(() => {
  if (tab?.pdfState?.source === "hebrewbook" && tab.pdfState.fileUrl) {
    const params = new URLSearchParams();
    params.set("file", tab.pdfState.fileUrl); // e.g., "hebrewbookscache/BookTitle_12345.pdf"
    params.set("locale", "he");

    return `/pdfjs/web/viewer.html?${params.toString()}`;
  }
  return "";
});
```

**Final URL Example:**
`/pdfjs/web/viewer.html?file=hebrewbookscache%2FBookTitle_12345.pdf&locale=he`

## ✅ IMPLEMENTATION FILES UPDATED

### Vue Frontend

**Tab Store** (`zayit-vue/src/stores/tabStore.ts`):

- ✅ `handleTabCleanup()` - Cleanup helper for tab closure
- ✅ Calls `handleHebrewBookTabClosed()` when Hebrew book tabs close
- ✅ Integrated into `closeTab()` and `closeTabById()`

**Hebrew Books Store** (`zayit-vue/src/stores/hebrewBooksStore.ts`):

- ✅ Two distinct methods: `openHebrewBookViewer()` and `downloadHebrewBook()`
- ✅ Browser-initiated downloads using hidden `<a>` links
- ✅ WebView availability checks
- ✅ Tab management with immediate title setting

**Hebrew Books View Page** (`zayit-vue/src/components/pages/HebrewBooksViewPage.vue`):

- ✅ Constructs PDF.js viewer URL with file parameter
- ✅ Handles Hebrew book PDF state
- ✅ Auto-reload mechanism for session persistence
- ✅ **Cache check before re-download** - Checks cache first, only downloads if missing
- ✅ **Retry with cache check** - Retry button also checks cache first

**Hebrew Books Handlers** (`zayit-vue/src/services/hebrewBooksHandlers.ts`):

- ✅ Event handlers for C# notifications
- ✅ `handleHebrewBookTabClosed()` - Notifies C# for cache cleanup
- ✅ Reactive PDF state updates

### C# Backend

**Hebrew Books Service** (`Zayit-cs/ZayitLib/Services/HebrewBooksService.cs`):

- ✅ **No separate virtual host** - uses same origin as PDF.js
- ✅ Cache directory: `Html/pdfjs/web/hebrewbookscache/`
- ✅ Three methods: `PrepareForViewing()`, `PrepareForDownload()`, `CheckFileInCache()`
- ✅ Download event handler captures browser-initiated downloads
- ✅ Context tracking: `_pendingViewingContext`, `_pendingDownloadContext`
- ✅ Returns relative URLs: `hebrewbookscache/{fileName}`
- ✅ `HandleTabClosed()` - Direct file deletion on tab closure
- ✅ Proper directory creation and validation

**Hebrew Books Cache Manager** (`Zayit-cs/ZayitLib/Services/HebrewBooksCacheManager.cs`):

- ✅ **Simplified architecture** - No active file tracking
- ✅ `EnforceFileLimit()` - LRU eviction when cache exceeds 10 files
- ✅ `GetStats()` - Statistics calculated directly from disk
- ✅ `ClearAll()` - Deletes all cached files
- ✅ Works directly with files on disk

**Service Provider** (`Zayit-cs/ZayitLib/Services/ServiceProvider.cs`):

- ✅ Method mappings: `PrepareHebrewBookForViewing`, `PrepareHebrewBookForDownload`, `CheckHebrewBookInCache`
- ✅ Cache management methods: `HandleHebrewBookTabClosed`, `GetHebrewBooksCacheStats`, `ClearHebrewBooksCache`

### WebView Bridge

**WebView Bridge** (`zayit-vue/src/services/webviewBridge.ts`):

- ✅ Hebrew Books methods: `prepareHebrewBookForViewing`, `prepareHebrewBookForDownload`, `checkHebrewBookInCache`
- ✅ Tab closure notification: `notifyHebrewBookTabClosed`
- ✅ Lazy loading singleton pattern
- ✅ Global message listener

**WebView Hebrew Books Service** (`zayit-vue/src/services/webviewHebrewBooks.ts`):

- ✅ Thin helper that delegates to bridge
- ✅ Three flow methods: `prepareForViewing`, `prepareForDownload`, `checkInCache`
- ✅ Tab closure notification: `notifyTabClosed`

## ✅ KEY RESEARCH INSIGHTS

### PDF.js Cross-Origin Restrictions

**Discovery**: PDF.js has built-in origin validation that blocks cross-origin file loading:

```javascript
// From viewer.mjs lines 18522-18531
const HOSTED_VIEWER_ORIGINS = new Set([
  "null",
  "http://mozilla.github.io",
  "https://mozilla.github.io",
]);
var validateFileURL = function (file) {
  const viewerOrigin = URL.parse(window.location)?.origin || "null";
  const fileOrigin = URL.parse(file, window.location)?.origin;
  if (fileOrigin === viewerOrigin) {
    return; // Same origin - allowed
  }
  const ex = new Error("file origin does not match viewer's");
  // ... throws error
};
```

**Solution**: Place Hebrew books cache in same directory as PDF.js viewer to share origin.

### Hebrew Books URL Protection

**Discovery**: Hebrew Books URLs are protected and cannot be downloaded directly by C#:

- URLs like `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}` require browser context
- C# must capture browser-initiated downloads, not initiate downloads directly
- Vue triggers download via hidden `<a>` link click, C# captures via `DownloadStarting` event

### PDF.js File Parameter Flexibility

**Research Finding**: PDF.js file parameter accepts both relative and absolute paths:

- `file=hebrewbookscache/book.pdf` (relative to viewer.html)
- `file=/pdfjs/web/hebrewbookscache/book.pdf` (absolute path)
- Key constraint is same-origin policy, not path format

## ✅ COMMUNICATION FLOW

### Viewing Flow

```
User Click → Vue Store → C# PrepareForViewing →
If Cached: Immediate PDF State with relative URL
If Not Cached: Browser Download → C# Capture → Cache → Enforce Limit → Notify Vue → PDF State
```

### Download Flow

```
Download Click → Vue Store → C# PrepareForDownload → SaveAs Dialog →
If Cached: Copy to User Location
If Not Cached: Browser Download → C# Capture → Copy to User Location → Notify Vue
```

### Tab Closure Flow

```
Tab Close → Vue Cleanup → Notify C# → Direct File Deletion
```

### Missing File Recovery Flow

```
Tab Switch/Session Load → Vue Detects Missing URL → Check Cache →
If Cached: Immediate Update (instant load)
If Not Cached: Re-download → Cache → Update Tab State
```

## ✅ BENEFITS ACHIEVED

1. **Cross-Origin Elimination**: Same origin as PDF.js viewer
2. **Protected URL Handling**: Proper browser-initiated downloads
3. **Clean Architecture**: No separate virtual host needed
4. **Reliable Caching**: Files persist in PDF.js web directory
5. **Simple Cache Management**: Direct file operations, no complex state tracking
6. **Automatic Cleanup**: Files deleted immediately when tabs close
7. **LRU Eviction**: Oldest files removed automatically when cache exceeds limit
8. **Smart Recovery**: Missing files detected and re-downloaded automatically
9. **Fast Tab Switching**: Cached files load instantly without re-download
10. **Session Persistence**: Hebrew books reload properly across sessions
11. **Error Resilience**: Comprehensive error handling and fallback mechanisms

## Status: ✅ COMPLETE AND TESTED

All functionality implemented with unified origin architecture and simplified cache management:

- ✅ Hebrew books cache in PDF.js web directory (`hebrewbookscache/`)
- ✅ Same origin as PDF.js viewer (no cross-origin restrictions)
- ✅ Browser-initiated downloads for protected Hebrew Books URLs
- ✅ Two distinct flows: viewing (cache) vs download (SaveAs)
- ✅ Simple direct file deletion on tab closure
- ✅ LRU eviction when cache exceeds 10 files
- ✅ Missing file detection and automatic re-download
- ✅ Cache check before re-download for fast tab switching
- ✅ Proper directory creation and management by C#
- ✅ Relative URL construction for PDF.js file parameter
- ✅ Session persistence and auto-reload functionality
- ✅ Comprehensive error handling and logging
