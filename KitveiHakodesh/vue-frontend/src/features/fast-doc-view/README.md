# src/features/fast-doc-view

The FastDocView page (route `/fast-doc-view`, multi-instance, keyed by tab id + file path): a Word-family / OpenDocument / RTF / text file read in windows of blocks from the service, never whole. It is NOT the book view - it borrows only the book view's design and the shared pieces (`WindowedScroller`, `SplitPane`, `SidePanel`, `TreeView`, `useZoomHandler`, `useScopedKeys`, `useVirtualizedTextSelection`, the reading-column stylesheet `assets/styles/readingColumn.css`). Never import from `features/book-view`; move a shared piece out first.

Routing is behind ONE switch: `OPEN_DOCUMENTS_IN_FAST_DOC_VIEW` in `utils/fastDocViewFiles.ts` (off until the service ops exist). Every file-opening path asks `opensInFastDocView(path)`: the file picker (`webview-host/bridge.ts`), home search, file search, address bar, recents (`localFileStore.openFromHistory`) and the `localFileTxtReady` push event. Not covered yet: the C# host's own Word open push (`localFileConversionStarted`) and an "open as PDF" choice - both needed when the switch is turned on. A restored `/fast-doc-view` tab needs no lazy restore: the page reads `localFilePath` itself.

## Files

**FastDocViewPage.vue** - page shell: toolbar, side panel (TOC over the headings), `SplitPane` with the text (WindowedScroller, rows `.reading-column-line.fast-doc-view-block` with the block's HTML) above and the notes pane below. Content root `.fast-doc-view-content` carries `dir` from the summary and `data-fast-doc-view-root`; the library's stylesheet is injected wrapped in `@scope ([data-fast-doc-view-root=...])`. Position (`fastDocViewBlockIndex` / `fastDocViewBlockOffset`) and zoom (`fastDocViewZoom`) are per tab in `TabState`; the opening position goes through `initialIndex`, navigation only through `scrollToIndex`.

**FastDocViewToolbar.vue** - TOC toggle, notes-pane toggle, zoom out / in. Same look as the book-view toolbar (top position). Zoom keys (Ctrl +/-/0, Ctrl+wheel, pinch) are the shared `useZoomHandler` on the scroller.

**FastDocViewNotesPane.vue** - the footnotes / endnotes of the blocks mounted in the window.

**useFastDocViewDocument.ts** - one open document: `open(path)`, `ensureBlocks(start, end)` (the scroller's `ensureRows`), `blockAt`, `notesInRange`, `landmarkTiers` (headings by level), `rowWeights` (only while their length equals the count), `contentVersion` (the scroller's `rowSourceVersion`, bumped on a reopen). Closes the document on dispose. Takes the API as a parameter (tests pass a fake).

**fastDocViewBlockCache.ts** - the bounded block cache (400 blocks): missing-span detection, eviction farthest-from-focus first, retained ranges (window, pinned selection rows, selection) never evicted.

**fastDocViewOutline.ts** - headings -> `TreeNodeItem` rows (parent = nearest earlier heading of a lower level).

**useFastDocViewDocument.test.ts** - cache/window logic against a fake API.

## Service contract (C# must match exactly)

Ops go through `serviceCall` (MessagePack envelope, camelCase keys) - see `webview-host/fastDocViewApi.ts`.

- `fastDocViewOpen` `{ path: string }` -> `{ documentId: string, blockCount: int, knownBlockCount: int, isIndexComplete: bool, isRightToLeft: bool, stylesheet: string, headingCount: int, footnoteCount: int, endnoteCount: int }`. `blockCount` is the scroller count; while the index is incomplete it is the estimate (`knownBlockCount` = blocks found so far). Access through `LocalFileGrants`.
- `fastDocViewBlocks` `{ documentId, first: int, count: int }` -> array of `{ index: int, kind: string, html: string, notes: [{ kind: "footnote" | "endnote", number: int, label: string, html: string }] }`, in index order. Returning fewer than `count` means the document ends there: the page shrinks its count. `count` is at most 100.
- `fastDocViewHeadings` `{ documentId }` -> array of `{ blockIndex: int, level: int, text: string }` (level 1 = top).
- `fastDocViewWeights` `{ documentId }` -> bytes (MessagePack bin): Int32 little-endian per block, the estimated text length. Used only when the length equals `blockCount`.
- `fastDocViewClose` `{ documentId }` -> nothing.
- Errors: the file changed on disk, or an unknown / idle-closed `documentId`, must fail with a message containing `FastDocViewSourceChanged`; the page reopens the path and retries once.
- `stylesheet` is plain CSS for the rendered blocks; the page scopes it, so it must not rely on `html` / `body` selectors.
