# HebrewBooks Feature

Browse and download Hebrew books from the HebrewBooks.org catalog with support for local offline collections and download caching.

## Files

- `HebrewBooksPage.vue` — main catalog page with virtual scroller search
- `HebrewBooksListItem.vue` — individual book list row
- `useHebrewBooks.ts` — catalog search, book opening, and download triggers
- `hebrewBooksCatalog.ts` — catalog search engine and URL generation

## How Books Open

When a user clicks a book:

1. **Frontend** — `openBook()` immediately shows a `/pdf-view` placeholder with a downloading spinner
2. **Bridge** — calls C# action `triggerHbDownload` with the book ID, title, URL, and user's configured local folder path (if set)
3. **C# resolution** — tries three paths in order:
   - **Local folder** — if `hebrewBooksLocalFolder` is set in settings, checks `{localFolder}/{bookId}.pdf`. If found, registers a WebView2 virtual host and opens immediately (no download). I/O errors (e.g. disconnected drive, permissions, invalid path) are logged to the WebView debug console with the error message and folder path, then fall through to the next path.
   - **Download cache** — checks `bin/.../KitveiHakodesh/cache/hebrewbooks/`. If the book is already cached, opens immediately.
   - **Download** — navigates the WebView2 browser to `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}`. The download is intercepted and saved to cache. On completion, the PDF opens. Cache is evicted by last-access time, keeping max 10 PDFs.

## Save As Flow

`downloadBook()` triggers a different flow: the user gets a native Save As dialog, and the browser handles the download without any redirect. The downloaded file goes wherever the user chooses, not to the app cache.

## LocalFileStore Integration

`localFileStore` manages the `/pdf-view` tab state during download:

- `startHbDownload()` — shows the placeholder spinner (loadingType: 'downloading')
- `finishHbDownload()` — pushed by C# `hbPdfReady` event; sets the final URL and clears the spinner
- `cancelHbDownload()` — pushed by C# `hbPdfCancelled` event; closes the tab
- `restoreTab()` — on app init, restores cached/local books from persisted tab state

## Settings Integration

- `settingsStore.hebrewBooksLocalFolder` — user-configured folder path for offline PDF collection. If set, the local folder is checked first before any download attempt.
