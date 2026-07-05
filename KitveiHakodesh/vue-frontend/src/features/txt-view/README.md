# txt-view

Native Vue viewer for `.txt` files. Renders text content directly in a `<div>` — no iframe, no virtual host, no cross-origin restrictions.

**TxtViewPage.vue** — the only component file. Owns loading, parsing, rendering, search, zoom, and scroll persistence.

**useTxtViewSearch.ts** — search composable. Scans parsed line text asynchronously in chunks (2000 lines per tick) to stay responsive on large files. Returns match positions by line index, supports next/previous navigation.

## How content is loaded

**C# host mode**: on mount, calls `readTxtFileContent(filePath)` via the bridge. C# reads the file as UTF-8 and returns the raw string. `localFilePath` is always persisted on the tab and is the only thing needed for session restore.

**Dev mode**: `devPickPdf()` in `devFallbacks.ts` creates a blob URL from the raw file read via `FileReader`. The blob URL is stored as `localFileVirtualUrl` on the tab. `TxtViewPage` fetches the blob URL with `fetch()` to get the text.

## Line parsing

Each line of the raw text is processed:

- Whitespace-only lines (including `&nbsp;`) are stripped entirely
- Lines starting with `@`, `#`, or `$` → rendered as `<h2>` (prefix removed)
- Lines starting with `!` → prefix removed, content rendered as a `<div>`
- All other lines → rendered as a `<div>` as-is

All line content is set via `v-html` so any HTML markup in the file is parsed and rendered by the browser.

## Search

`Ctrl+F` opens a floating search bar (same visual style as `BookViewSearchBar`). Search scans `parsedLines[].rawText` (diacritics-stripped) asynchronously. Matches are highlighted inline via a tag-aware `highlightedHtml()` function that inserts `<mark>` around matched text without breaking HTML tags. The current match is highlighted differently and the view scrolls to it.

## Zoom

Uses `useZoomHandler` — same as book view and full-text search. `Ctrl++`/`Ctrl+-`/`Ctrl+0`, `Ctrl+scroll`, pinch. Font size = `(zoom / 100) × (settingsFontSize / 100) × 15px`. Zoom persisted to `TabState.txtViewZoom`.

## Font and paragraph settings

Uses the same CSS variables as the book view: `--text-font`, `--line-height`, `--header-font`. All respond live to user changes in settings.

## Scroll persistence

Scroll position is saved to `TabState.htmlViewScrollTop` via a debounced `@scroll` handler. Restored on mount after content loads.

## Session restore

`localFileStore.restoreTab()` detects `.txt` extension and sets `route: '/txt-view'`. `TxtViewPage` then loads the content itself on mount via `readTxtFileContent`.
