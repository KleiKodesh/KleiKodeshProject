# Plan: PDF & HebrewBooks — Vue ↔ C# Integration

## How PDF.js gets its file

PDF.js requires a URL. The Vue app itself is already served from a virtual host (`kezayit-vue-app` → app folder). Any file placed inside the app folder is therefore already accessible via that host — no additional virtual host mapping needed for those files.

For files outside the app folder (arbitrary local files), C# uses `SetVirtualHostNameToFolderMapping` to serve them. C# maintains a dictionary of `folderPath → (virtualHostname, refCount)`. On open, it looks up the folder — reuses the existing mapping if found, otherwise registers a new one with a generated name (e.g. `kezayit-pdf-{n}`). C# constructs the full URL and returns it ready-to-use. Vue passes it directly to PDF.js via the `file=` param.

When a tab closes, Vue sends `disposePdfHost` with the folder path. C# decrements the ref count — only when it reaches zero does it clear the mapping and remove it from the dictionary. If another tab from the same folder is still open, the mapping is left untouched.

---

## HebrewBooks — Two Actions

HebrewBooks restricts direct HTTP downloads — files must be triggered via the browser. The WebView2 browser engine handles the actual download; C# intercepts it.

### List item clicked — open in viewer
1. Vue shows a loading animation and triggers the hebrewbooks.org download URL directly in the WebView2 (no page navigation — just the download link).
2. The browser's default download dialog appears while the file downloads.
3. C# intercepts the download via the `DownloadStarting` event and redirects the save path to the **HebrewBooks cache folder** (`<app folder>/cache/hebrewbooks/`).
4. Once the download completes, C# calls `webView.CoreWebView2.CloseDefaultDownloadDialog()` to dismiss the browser dialog.
5. C# sends a push event to Vue with the cached file's URL (served via `kezayit-vue-app`).
6. Vue hides the loading animation and opens the file in PDF.js.

### Download button clicked — save to disk
1. Same interception flow as above.
2. Instead of redirecting to the cache folder, C# shows a native **Save As** dialog so the user can choose where to save.
3. The file is saved to the user's chosen location only — no cache involvement.

### HebrewBooks cache
- Location: `<kezayit folder>/cache/hebrewbooks/` — the `kezayit` folder is the C# host's Vue app folder, already mapped to `kezayit-vue-app`. Files here are served directly via that existing virtual host with no extra registration needed.
- LRU eviction, capped at 10 files (by last-access time).
- Cache is keyed by book title + book ID. On tab restore, if the cached file exists the viewer opens immediately without re-downloading.

---

## Open Local Files (Word / PDF)

The home "פתח קובץ" tile opens a native file picker. Supported formats are anything Microsoft Word can open (`.docx`, `.doc`, `.rtf`, `.txt`, etc.) plus `.pdf`.

- **PDF files** — opened directly. C# registers a virtual host for the file's parent folder (dictionary + ref count as above), returns the URL to Vue.
- **Word-compatible files** — C# uses Word Interop to convert the file to PDF, saves the output to the **Word conversion cache** (`<app folder>/cache/word/`), then returns the cache URL to Vue.

### Word conversion cache
- Location: `<kezayit folder>/cache/word/` — same `kezayit` folder, served by `kezayit-vue-app`. No extra host needed.
- LRU eviction, capped at 10 files.
- Cache key is the source file path + last-modified timestamp. On tab restore, if the cached PDF still matches the source file it is used immediately without re-converting.

---

## Session Persistence

`pdfBlobUrl` is removed from the `Tab` interface and replaced with:
- `pdfVirtualUrl` — in-memory only, not persisted; reconstructed on restore
- `pdfFilePath` — absolute path to the source file (local PDF or Word-compatible file)
- `pdfHbBookId` + `pdfHbBookTitle` — HebrewBooks book identity, for cache lookup and re-download fallback

On app init, any restored `/pdf-view` tab is restored according to its type:

### Type 1 — Local PDF
Persisted: `pdfFilePath`

C# registers a virtual host for the file's parent folder (dictionary + ref count) and returns the URL. If the file no longer exists on disk, show an error.

### Type 2 — HebrewBooks
Persisted: `pdfHbBookId` + `pdfHbBookTitle`

C# checks whether `<kezayit>/cache/hebrewbooks/{title}-{id}.pdf` exists. If yes, returns the `kezayit-vue-app` URL directly — instant open. If the file has been evicted from the cache (LRU limit reached), C# triggers a re-download using the book ID, saves to cache, and returns the URL once complete.

### Type 3 — Local Word-compatible file
Persisted: `pdfFilePath`

C# checks whether a converted PDF exists in `<kezayit>/cache/word/` for that source path + last-modified timestamp. If yes, returns the cache URL directly. If the converted file has been evicted, C# re-converts via Word Interop and saves to cache before returning the URL.

---

## New C# Bridge Actions

A generic `window.__webviewAction(action, args)` is added to `JsBridge.cs`.

- `pickFile` — opens file picker (PDF + Word formats), handles conversion if needed, returns virtual URL + cache key
- `restorePdf(cacheKey | filePath)` — resolves the URL for a persisted tab on restore
- `disposePdfHost(folderPath)` — ref-count decrement; clears virtual host mapping when count hits zero
- Download interception is handled entirely in C# via the WebView2 download event — no bridge action needed for HebrewBooks downloads

## Vue Changes

- New `src/host/bridge.ts` — exports `pickFile()`, `restorePdf()`, `disposePdfHost()`. In dev mode (no C# host), `pickFile` uses a native `<input type="file" accept=".pdf">` element to let the user pick a PDF, creates a blob URL, and passes it directly to PDF.js — no virtual host, no persistence, no Word conversion. All other bridge functions are no-ops in dev mode.
- `pdfStore.ts` — uses virtual URLs, calls `disposePdfHost` on tab close, calls `restorePdf` on session restore
- Home "פתח קובץ" tile → `bridge.pickFile()` → `pdfStore.open()`
- `HebrewBooksPage` — list item click navigates WebView2 to book page; download button triggers the same but with Save As; both result in a push event from C# that Vue handles to open the viewer

---

## Open Questions

- Does WebView2 (version in use) support `ClearVirtualHostNameToFolderMapping`? If not, orphaned mappings are harmless for the session.
- Word Interop requires Word to be installed on the machine — need a graceful error if it is not.
- HebrewBooks download interception: confirm the WebView2 `DownloadStarting` event is available in the .NET 4.8 WebView2 SDK version in use.
