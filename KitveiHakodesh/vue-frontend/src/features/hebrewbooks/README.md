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
   - **Local folder** — if `hebrewBooksLocalFolder` is set in settings, checks `{localFolder}/{bookId}.pdf`. If found, opens immediately (no download) via a WebView2 virtual host. The hostname is allocated in a process-global map shared across all AppViewer instances, so the same folder always maps to the same stable hostname (e.g. `kitvei-hb-local-1`). Each AppViewer's WebView2 registers the mapping independently on first use and reuses it for all subsequent opens. I/O errors (e.g. disconnected drive, permissions, invalid path) are logged to the WebView debug console with the error message and folder path, then fall through to the next path.
   - **Download cache** — checks `bin/.../KitveiHakodesh/cache/hebrewbooks/{bookId}.pdf`. If found, opens immediately. Cache files are named by book ID only.
   - **Download** — navigates the WebView2 browser to `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}`. The download destination depends on whether a local folder is configured: if yes, the file goes to `{localFolder}/{bookId}.pdf` and LRU eviction is skipped; if no, the file goes to the app cache dir as `{bookId}.pdf` and LRU eviction runs (max 10 PDFs kept by last-access time).

All book files — both in the user's local folder and in the app cache — are named `{bookId}.pdf` with no title in the filename. This is consistent between the local folder hit path, the cache hit path, and fresh downloads.

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
