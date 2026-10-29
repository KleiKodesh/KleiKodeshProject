# src/features/fast-doc-view

The FastDocView page (route `/fast-doc-view`, multi-instance, keyed by tab id + file path): a Word-family / OpenDocument / RTF / text file read in windows of blocks from the service, never whole. It is NOT the book view - it borrows only the book view's design and the shared pieces (`WindowedScroller`, `SplitPane`, `SidePanel`, `TreeView`, `useZoomHandler`, `useScopedKeys`, `useVirtualizedTextSelection`, the reading-column stylesheet `assets/styles/readingColumn.css`). Never import from `features/book-view`; move a shared piece out first.

Routing is behind ONE switch: `OPEN_DOCUMENTS_IN_FAST_DOC_VIEW` in `utils/fastDocViewFiles.ts` (on). Every file-opening path asks `opensInFastDocView(path)`: the file picker (`webview-host/bridge.ts`), home search, file search, address bar, recents (`localFileStore.openFromHistory`) and the `localFileTxtReady` push event. The hosted C# open (`KitveiHakodeshLib` `LocalFileHandler`, pick and Open-With) pushes `localFileTxtReady` with the path only for .txt and the Word family, so no Word -> PDF conversion starts by default. "Open as PDF" (toolbar button) converts on request through `restoreLocalFile` and turns the tab into a PDF tab. A restored `/fast-doc-view` tab needs no lazy restore: the page reads `localFilePath` itself.

## Files

**FastDocViewPage.vue** - page shell: toolbar, side panel (TOC over the headings), `SplitPane` with the text (WindowedScroller, rows `.reading-column-line.fast-doc-view-block` with the block's HTML) above and the notes pane below. Content root `.fast-doc-view-content` carries `dir` from the summary and `data-fast-doc-view-root`; the library's stylesheet is injected wrapped in `@scope ([data-fast-doc-view-root=...])`. Position (`fastDocViewBlockIndex` / `fastDocViewBlockOffset`) and zoom (`fastDocViewZoom`) are per tab in `TabState`; the opening position goes through `initialIndex`, navigation only through `scrollToIndex`.

**FastDocViewToolbar.vue** - TOC toggle, notes-pane toggle, zoom out / in, open as PDF. Same look as the book-view toolbar (top position). Zoom keys (Ctrl +/-/0, Ctrl+wheel, pinch) are the shared `useZoomHandler` on the scroller.

**FastDocViewNotesPane.vue** - the footnotes / endnotes of the blocks mounted in the window.

**useFastDocViewDocument.ts** - one open document: `open(path)`, `ensureBlocks(start, end)` (the scroller's `ensureRows`), `blockAt`, `notesInRange`, `landmarkTiers` (headings by level), `rowWeights` (only while their length equals the count), `contentVersion` (the scroller's `rowSourceVersion`, bumped on a reopen), `isIndexComplete`. After an open with an incomplete index it calls `fastDocViewScan` in a loop (one call at a time, and none while a block fetch is pending, so the screen comes first; stops on a document switch, dispose or a changed file; any other failure is retried after 0.5/1/2/4/8 s, then `indexBuildFailed` is set and the page shows a warning); the count only grows; on completion headings and weights are fetched again. A restored position (`open(path, { reachIndex })`) is scanned to first - scans only, counts come back - so the scroller mounts with that block known, or the reader would land elsewhere. Closes the document on dispose. Takes the API as a parameter (tests pass a fake).

**fastDocViewBlockCache.ts** - the bounded block cache (400 blocks): missing-span detection, eviction farthest-from-focus first, retained ranges (window, pinned selection rows, selection) never evicted.

**fastDocViewOutline.ts** - headings -> `TreeNodeItem` rows (parent = nearest earlier heading of a lower level).

**useFastDocViewDocument.test.ts** - cache/window logic against a fake API.

## Service contract (C# must match exactly)

Ops go through `serviceCall` (MessagePack envelope, camelCase keys) - see `webview-host/fastDocViewApi.ts`.

- `fastDocViewOpen` `{ path: string }` -> `{ documentId: string, blockCount: int, knownBlockCount: int, isIndexComplete: bool, isRightToLeft: bool, stylesheet: string, headingCount: int, footnoteCount: int, endnoteCount: int }`. `blockCount` is the scroller count: the blocks known so far, never an estimate - while the index is incomplete (first open of a big file) it only grows (`knownBlockCount` is the same number). Access through `LocalFileGrants`.
- `fastDocViewBlocks` `{ documentId, first: int, count: int }` -> array of `{ index: int, kind: string, html: string, notes: [{ kind: "footnote" | "endnote", number: int, label: string, html: string }] }`, in index order, clamped to the known blocks. `count` is at most 100.
- `fastDocViewHeadings` `{ documentId }` -> array of `{ blockIndex: int, level: int, text: string }` (level 1 = top); the ones known so far while the index builds.
- `fastDocViewWeights` `{ documentId }` -> bytes (MessagePack bin): Int32 little-endian per block, the estimated text length. Used only when the length equals `blockCount`.
- `fastDocViewScan` `{ documentId, blocks: int }` -> `{ knownBlockCount: int, isIndexComplete: bool, footnoteCount: int, endnoteCount: int }`: builds up to `blocks` more of the index but stops after about 5 ms (Core `ScanTimeBudget`), so a block fetch never waits long for the document lock; renders and sends nothing else. Note counts grow with the index; the page updates its summary from every reply.
- `fastDocViewSections` `{ documentId }` -> array of `{ firstBlockIndex: int, textWidthTwips: int }`: page text width per Word section (page width - margins - gutter), block order; the page sizes each block's column from it, scaled with zoom.
- `fastDocViewBookmark` `{ documentId, name }` -> `{ blockIndex: int | null }`: the block of an internal link's target (`<a href="#bookmark-<escaped name>">`); the page jumps there windowed.
- `fastDocViewClose` `{ documentId }` -> nothing.
- Errors: the file changed on disk, or an unknown / idle-closed `documentId`, must fail with a message containing `FastDocViewSourceChanged`; the page reopens the path and retries once.
- `stylesheet` is plain CSS for the rendered blocks; the page scopes it, so it must not rely on `html` / `body` selectors.
