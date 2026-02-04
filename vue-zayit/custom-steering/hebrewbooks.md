# Hebrew Book Download Architecture - UPDATED IMPLEMENTATION

## Overview

Hebrew book downloads have been **completely refactored** to use a unified origin architecture that eliminates cross-origin restrictions by placing Hebrew book cache files directly within the PDF.js web directory structure, sharing the same origin as the PDF.js viewer.

## ✅ MAJOR ARCHITECTURAL CHANGES (Latest Update)

### Unified Origin Architecture

- **Hebrew books cache moved to PDF.js web directory** (`Html/pdfjs/web/hebrewbookscache/`)
- **Same origin as PDF.js viewer** - eliminates cross-origin restrictions
- **No separate virtual host needed** - uses existing PDF.js viewer origin
- **Browser-initiated downloads** - C# captures protected Hebrew Books URLs
- **Two distinct flows** - viewing (cache) vs download (SaveAs dialog)

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
6. Calls `NotifyViewingReady` with relative URL `hebrewbookscache/{fileName}`
7. Vue receives notification and sets PDF state

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
│       │   ├── BookTitle_12345.pdf    # Hebrew book cache files
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

**Hebrew Books Store** (`zayit-vue/src/stores/hebrewBooksStore.ts`):

- ✅ Two distinct methods: `openHebrewBookViewer()` and `downloadHebrewBook()`
- ✅ Browser-initiated downloads using hidden `<a>` links
- ✅ WebView availability checks
- ✅ Tab management with immediate title setting

**Hebrew Books View Page** (`zayit-vue/src/components/pages/HebrewBooksViewPage.vue`):

- ✅ Constructs PDF.js viewer URL with file parameter
- ✅ Handles Hebrew book PDF state
- ✅ Auto-reload mechanism for session persistence

**Hebrew Books Handlers** (`zayit-vue/src/services/hebrewBooksHandlers.ts`):

- ✅ Event handlers for C# notifications
- ✅ Cache cleanup on tab closure
- ✅ Reactive PDF state updates

### C# Backend

**Hebrew Books Service** (`Zayit-cs/ZayitLib/Services/HebrewBooksService.cs`):

- ✅ **No separate virtual host** - uses same origin as PDF.js
- ✅ Cache directory: `Html/pdfjs/web/hebrewbookscache/`
- ✅ Two separate methods: `PrepareForViewing()` and `PrepareForDownload()`
- ✅ Download event handler captures browser-initiated downloads
- ✅ Context tracking: `_pendingViewingContext`, `_pendingDownloadContext`
- ✅ Returns relative URLs: `hebrewbookscache/{fileName}`
- ✅ Proper directory creation and validation

**Service Provider** (`Zayit-cs/ZayitLib/Services/ServiceProvider.cs`):

- ✅ Method mappings: `PrepareHebrewBookForViewing`, `PrepareHebrewBookForDownload`
- ✅ Cache management methods

### WebView Bridge

**WebView Bridge** (`zayit-vue/src/services/webviewBridge.ts`):

- ✅ Hebrew Books methods: `prepareHebrewBookForViewing`, `prepareHebrewBookForDownload`
- ✅ Lazy loading singleton pattern
- ✅ Global message listener

**WebView Hebrew Books Service** (`zayit-vue/src/services/webviewHebrewBooks.ts`):

- ✅ Thin helper that delegates to bridge
- ✅ Two flow methods with proper return types

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
If Not Cached: Browser Download → C# Capture → Cache → Notify Vue → PDF State
```

### Download Flow

```
Download Click → Vue Store → C# PrepareForDownload → SaveAs Dialog →
If Cached: Copy to User Location
If Not Cached: Browser Download → C# Capture → Copy to User Location → Notify Vue
```

## ✅ BENEFITS ACHIEVED

1. **Cross-Origin Elimination**: Same origin as PDF.js viewer
2. **Protected URL Handling**: Proper browser-initiated downloads
3. **Clean Architecture**: No separate virtual host needed
4. **Reliable Caching**: Files persist in PDF.js web directory
5. **Two Distinct Flows**: Clear separation between viewing and downloading
6. **Session Persistence**: Hebrew books reload properly across sessions
7. **Error Resilience**: Comprehensive error handling and fallback mechanisms

## Status: ✅ COMPLETE AND TESTED

All functionality implemented with unified origin architecture:

- ✅ Hebrew books cache in PDF.js web directory (`hebrewbookscache/`)
- ✅ Same origin as PDF.js viewer (no cross-origin restrictions)
- ✅ Browser-initiated downloads for protected Hebrew Books URLs
- ✅ Two distinct flows: viewing (cache) vs download (SaveAs)
- ✅ Proper directory creation and management by C#
- ✅ Relative URL construction for PDF.js file parameter
- ✅ Session persistence and auto-reload functionality
- ✅ Comprehensive error handling and logging
