# Local File Handling

This describes how the app opens local files — whether picked by the user inside the app, or launched via the Windows "Open With" context menu.

## Supported file types and how each is handled

| Extension | Route | Mechanism |
|---|---|---|
| `.pdf` | `/pdf-view` | Folder registered as a WebView2 virtual host. URL served as `http://kitvei-localfile-N/filename.pdf`. |
| `.htm` / `.html` | `/html-view` | Same virtual host registration. Rendered in an iframe with theme injection and scroll persistence. |
| `.txt` | `/html-view` | Same virtual host registration. Browser renders as `text/plain`; injected script applies RTL styles. |
| `.doc` / `.docx` / `.rtf` | `/pdf-view` | Converted to PDF via `WordToPdfConverter` (Word COM interop). Cached under `KitveiHakodesh/cache/word/`. Tab shows a converting placeholder until done. |

## Event sequence

All file-open paths go through `LocalFileHandler` in C# and produce push events consumed by `localFileStore.ts` in Vue. Never push file-open events from anywhere else.

**PDF / HTML / TXT** — one event: `localFileReady { url, fileName, filePath }`. Vue routes to the correct page based on extension.

**Word / RTF** — two events in order:
1. `localFileConversionStarted { fileName, filePath }` — Vue immediately navigates to `/pdf-view` with the converting placeholder.
2. `localFileConversionReady { url, fileName, filePath }` — Vue calls `finishLocalFileConversion`, which sets `localFilePath` to the **original source path** (not the cache path). This is critical for session restore.

**Errors** — `localFileError { message, filePath }`. If a Word tab is stuck on the converting placeholder, the store resets it to home before showing the alert.

## Persistence and session restore

`localFilePath` on the tab stores the original file path on disk. On session restore, `restoreLocalFile(filePath)` is called: for PDF/HTML/TXT it re-registers the virtual host; for Word/RTF it reconverts from the source (or hits the cache if present).

`localFileVirtualUrl` is in-memory only — never persisted. It is always reconstructed on restore.

## Virtual host lifecycle

`LocalFileHandler` ref-counts virtual host mappings per folder. `disposeLocalFileHost(filePath)` is called on tab close to decrement the count; the mapping is cleared when it reaches zero. `DisposeAllHosts()` is called on app shutdown.

## "Open With" entry point

`AppViewer.OpenFileFromPath(filePath)` is the entry point for files opened via the Windows context menu or command-line argument. It queues the path if the WebView2 bridge is not yet ready, then calls `LocalFileHandler.OpenFileFromPathAsync` which produces the same event sequence as `HandlePickFile`. The single-instance pipe in `Program.cs` forwards the path to the already-running instance if one exists.
