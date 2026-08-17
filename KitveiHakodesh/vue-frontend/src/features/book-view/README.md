# book-view

Main book reader: the text, up to three independent commentary panels, a side panel for the table of contents, a search bar, and a toolbar.

## Three commentary panels

The reader can open a **bottom** commentary panel (stacked under the text) and a column on each side of it — **side** on the RTL start edge (physically right) and **side-left** on the end edge — together or in any combination, from three independent toolbar toggles (`Ctrl+J` / `Ctrl+Shift+J` / `Ctrl+Alt+J`). Both side columns need a wide pane; the bottom panel works at any width. All are anchored to the same clicked line and share one `useCommentary` fetch, because re-querying the same line would be byte-identical work on the app's heaviest payload.

Everything downstream of the fetch is **per panel**, assembled by `commentary/useCommentaryPanelSlot.ts`:

| Per panel | Shared |
| --- | --- |
| pinned book (and which default commentator it opens on — see below) | the fetched `groups` for the current line |
| the filter tree: whether it is open, its state, its check-tree scope (`commentaryScopeKey(tabId, slot)`) | highlights, notes, word-link anchors, TOC paths |
| scroll position and its save/restore | `staticFilterGroups` / `filterGroups` |
| in-panel search (Ctrl+F) and render cache | the anchor line (`selectedLineId` / `commentaryLineId`) |
| divider position | |

Each panel opens on a different one of the book's default commentators (bottom takes the first, side the second, side-left the third), so opening several shows several commentators rather than one repeated. Bottom and side fall back to the first default when the book has fewer; **side-left does not** — with no third default it stays unpinned and simply renders the list from the top without scrolling anywhere.

`CommentarySlot` (`'bottom' | 'side' | 'side-left'`) keys all of it, and every panel persists under `BookState.commentaryPanels` / `LastReadState.commentaryPanels`.

### Filter trees are per panel

Each panel renders its own `CommentaryTreePanel` as a dropdown — floating over the content, anchored to that panel's own filter button, closed by clicking outside it, and reflowing nothing. All three can be open at once, each with its own search and its own expanded nodes, and opening one no longer closes the TOC.

The panels differ only in how tall their dropdown is, and therefore in who renders it:

- **Side columns** — `CommentaryPanelHost` renders it, clipped to that column so it stays over its own commentary and never touches the text.
- **Bottom panel** — `BookViewPage` renders it, anchored to `.content-area` so it runs the full height of the book-view body. It cannot live in the host: `SplitPane` and `.side-lines` both clip, so a dropdown mounted inside the bottom panel could only be as tall as that panel.

The trees used to share `BookViewSidePanel` with the TOC, which is why only one could be open and why opening one closed the TOC. That panel is now the TOC's alone.

Adding a fourth panel means adding a slot to `COMMENTARY_SLOTS` plus its layout and toggle: the composables, the store, persistence and session restore all loop over the constant and need no edit.

## Top-Level Components

**BookViewPage.vue** - top-level orchestrator. The right place to add new panels or cross-cutting book-view behavior.

**BookViewToolbar.vue** - zoom, search, TOC toggle, and one toggle per commentary panel. Add new toolbar actions here.

**BookViewSidePanel.vue** - the side-panel shell, holding the table of contents. Commentary filters used to share it; they now live in their own panels (see above).

**BookViewSearchBar.vue** - inline search bar. Query input, mode selection, and match navigation.

**BookViewRelatedBooksDropdown.vue** - "ספרים קרובים" dropdown in the toolbar. Shows books linked to the current book (SOURCE / TARGUM / COMMENTARY / LINKED) and navigates on click.

## Lines Subfolder

All line-display logic lives in `lines/`. See `lines/README.md` for details.

Import line components from the subfolder:

```typescript
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import { useLines } from './lines/useBookViewLinesTable'
```

## TOC Subfolder

All table-of-contents logic lives in `toc/`. See `toc/README.md` for details.

Import TOC components from the subfolder:

```typescript
import BookViewTocTree from './toc/BookViewTocTree.vue'
import { useToc } from './toc/useBookViewToc'
```

## Commentary Subfolder

All commentary-related components, composables, and utilities live in `commentary/`. See `commentary/README.md` for details.

Import commentary components from the subfolder:

```typescript
import CommentaryView from './commentary/CommentaryView.vue'
import { useCommentary } from './commentary/useCommentary'
```

## Line Selection Modes

The book view supports two selection modes for loading commentary:

### TOC Section Mode

Clicking a line that is a TOC entry loads commentary for all lines in that TOC section. The commentary headers display the deepest common ancestor TOC path (one level up) that encompasses all lines in the range, rather than the specific path of the first line. Navigation via "next/prev section" in the commentary headers moves to the next/previous TOC section.

### Multi-Select Mode

Ctrl+Click (or Cmd+Click on Mac) a line to start a consecutive range selection, similar to Windows Explorer behavior. The first Ctrl+Click sets an anchor. Subsequent Ctrl+Clicks extend or modify the range. Once a range is selected, commentary loads for all lines in that range, using the same "one level up" TOC path adjustment as TOC section mode.

**Visual Distinction:** Multi-selected lines display a full-opacity accent bar on the left side and a background tint. The side bar is more prominent than the faint accent used for TOC section ranges to clearly indicate manual selection.

**Navigation in Multi-Select Mode:** The "next/prev section" buttons in the commentary headers navigate to the next/previous line *outside* the selected range, then clear the selection. This differs from TOC section mode, where "next/prev" navigates within the TOC hierarchy. After navigation, the newly selected single line becomes the anchor for the next potential Ctrl+Click.

**Implementation Notes:**
- `manualSelectionAnchorLineId` stores the first line Ctrl+Clicked
- `manualSelectionLineIds` stores all lines in the current range (derived from anchor + last Ctrl+Click)
- `selectedSectionLineIds` computed prioritizes manual selection, falling back to TOC range if no manual selection is active
- Commentary headers use the same `getTocPath` logic for both modes, invoked when `selectedSectionLineIds.length > 1`

## Composables

**useBookView.ts** - central composable for the book view page. Owns data loading, state, event handlers, watchers, and exposes everything `BookViewPage.vue` needs.

**useBookViewSearch.ts** - in-book content search, line-based.

**useBookViewScrollSync.ts** - syncs the active TOC entry and auto-selects commentary as the user scrolls. Updates `activeTocEntryId` and triggers commentary load on scroll.

**useBookViewSessionRestore.ts** - restores per-book view state from IDB on mount: lines scroll position, selected line, zoom, and each commentary panel's visibility, scroll position, filter and pin.

**useBookViewPinnedCommentary.ts** - manages one commentary panel's pinned group. Tracks which group is visible and handles pin transitions on line navigation. Instantiated once per panel, with a `defaultRank` deciding which of the book's default commentators that panel opens on.

**useBookViewCommentaryPanel.ts** - one commentary panel's visibility and scroll save/restore lifecycle. Instantiated once per panel by `useCommentaryPanelSlot`.

**useBookViewHighlights.ts** - lives in `lines/`. See `lines/README.md`.

**useBookViewNotes.ts** - lives in `lines/`. See `lines/README.md`.

## Data Structures

### bookViewTypes.ts

Shared types for the book-view feature: `CommentarySlot`, `SearchMode` (`'content' | 'commentary-bottom' | 'commentary-side'`), `SidePanelMode`, `CommentaryVisibilityItem`, `CommentaryTreeState`, `CommentaryPinSnapshot`, and the persisted `CommentaryPanelPersistState(s)`.

Live and stored forms are deliberately distinct: panels hand over `CommentaryPanelLiveState(s)` (whose `filterState` is the live reactive `CommentaryTreeState`), and the save path converts that to `CommentaryPanelPersistState(s)` with a `CommentaryTreeStatePersist` — the same thing minus the derived `isChecked`, which is recomputed on restore from the separately-stored `CommentaryCheckStateSnapshot`.

### Highlights

Highlights are text ranges with a color applied to specific lines.

```typescript
interface Highlight {
  id: number
  bookId: number
  lineId: number
  startOffset: number      // stripped character offset (no diacritics)
  endOffset: number        // stripped character offset (no diacritics)
  colorArgb: number        // signed 32-bit int (Material Design colors)
  createdAt: number        // UNIX timestamp (ms)
}
```

**Colors:** Material Design palette stored in `bookViewAnnotationColors.ts` (yellow, green, blue, pink, orange). Display colors are theme-adjusted (desaturated, semi-transparent) via `highlightColorToThemeColor()` for VSCode/Fluent design fit.

**Overlap Rules** when applying a new highlight with a different color:
- Same color, existing highlight fully covered: merge
- Different color, existing fully covered: delete existing
- Different color, existing fully spans new: split into left and right stubs
- Different color, partial overlap on left: trim existing's end
- Different color, partial overlap on right: trim existing's start

**Clear Rules** when erasing a range:
- Fully inside erased range: delete
- Fully spans erased range: split into stubs
- Partial overlap: trim appropriately

Storage: `user_settings.db` table `user_highlights`. All access via `userSettingsDb.ts` (web-host layer).

### Notes

Notes are user-written annotations anchored to a text range and line. A note captures the selected text as a snapshot at creation time.

```typescript
interface Note {
  id: number
  bookId: number
  lineId: number
  startOffset: number      // stripped character offset (no diacritics)
  endOffset: number        // stripped character offset (no diacritics)
  note: string            // user-written text (any length, any language)
  quote: string           // snapshot of selected text at creation time
  createdAt: number       // UNIX timestamp (ms)
  updatedAt: number       // UNIX timestamp (ms)
}
```

**Loading Strategy:** Lazy, viewport-driven. Only notes for visible lineIds are fetched. `getVisibleLineIds()` callback from the renderer provides the current viewport's lineIds; a 100ms debounce triggers DB queries for any lineIds not yet loaded. This keeps initial render instant and avoids loading notes for lines the user never sees.

**Mutations:** Create/update/delete are fire-and-forget DB writes with immediate in-memory map updates. Map is keyed by lineId, storing `Note[]` sorted by `startOffset`.

Storage: `user_settings.db` table `user_notes`. All access via `userSettingsDb.ts` (web-host layer).

## Text selection keyboard handling

**useTextSelectionKeys.ts** — `Ctrl+A` (select all), `Ctrl+F` (open search), `Ctrl+V` and `Ctrl+Shift+C`, scoped to one element. Used by `lines/BookViewLinesContent.vue` and `commentary/CommentaryView.vue`, which is why it sits at the feature root rather than in either subfolder. `Ctrl+A` drives `useSelectAllInContainer`, whose `isSelectAll` / `selectAllInContainer` it re-exports.

**useSelectAllInContainer.ts** — tracks whether the current selection is a whole-container "select all" as a reactive `isSelectAll` boolean, plus a `selectAll()` trigger. The flag matters for copy/export: with virtualized content, "select all" must grab the ENTIRE line set rather than only the DOM range that happens to be mounted (see `useLineCopy`). Auto-clears on the next `selectionchange` that collapses or replaces the selection.

Both lived in `src/composables/` until 2026-07-29 and moved here because book-view is their only consumer. If a second feature needs them, move them back to `src/composables/`.
