# book-view

Main book reader. Split pane with text above and commentary below, shared side panel for tools, search bar, and toolbar.

## Top-Level Components

**BookViewPage.vue** - top-level orchestrator. The right place to add new panels or cross-cutting book-view behavior.

**BookViewToolbar.vue** - zoom, search, TOC toggle, and bottom panel toggle. Add new toolbar actions here.

**BookViewSplitPane.vue** - thin wrapper around `SplitPane` for the text/commentary split. Do not add logic here.

**BookViewSidePanel.vue** - shared side-panel shell for book-view tools such as TOC and commentary filters.

**BookViewSearchBar.vue** - inline search bar. Query input, mode selection, and match navigation.

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

## Composables

**useBookViewSearch.ts** - in-book content search, line-based.

**useBookViewHighlights.ts** - user-applied text highlights for the active book. Loads on mount, manages highlight state, handles apply/clear operations via context menu. Highlights persist in `user_settings.db` table `user_highlights`.

**useBookViewNotes.ts** - user-written footnote annotations tied to text ranges. Lazy-loaded viewport-driven: only notes for visible lineIds are fetched. Manages note creation/editing/deletion via context menu. Notes persist in `user_settings.db` table `user_notes`.

## Data Structures

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


