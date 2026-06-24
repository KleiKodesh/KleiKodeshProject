# book-view

Main book reader. Split pane with text above and commentary below, shared side panel for tools, search bar, and toolbar.

## Top-Level Components

**BookViewPage.vue** - top-level orchestrator. The right place to add new panels or cross-cutting book-view behavior.

**BookViewToolbar.vue** - zoom, search, TOC toggle, and bottom panel toggle. Add new toolbar actions here.

**BookViewSidePanel.vue** - shared side-panel shell for book-view tools such as TOC and commentary filters.

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

**useBookViewSessionRestore.ts** - restores per-book view state from IDB on mount: scroll position, selected line, commentary scroll, zoom, and divider fraction.

**useBookViewPinnedCommentary.ts** - manages the pinned commentary group for the split-pane bottom panel. Tracks which commentary group is visible and handles pin transitions on line navigation.

**useBookViewHighlights.ts** - lives in `lines/`. See `lines/README.md`.

**useBookViewNotes.ts** - lives in `lines/`. See `lines/README.md`.

## Data Structures

### bookViewTypes.ts

Shared types for the book-view feature: `SearchMode`, `SidePanelMode`, and `CommentaryEntryVisibility`.

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
