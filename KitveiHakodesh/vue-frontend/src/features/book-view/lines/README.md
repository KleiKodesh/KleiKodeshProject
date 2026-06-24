# lines

Virtual-scrolled main text display for the book view. Handles line rendering, selection, and persistence.

## Components

**BookViewLinesContent.vue** - virtual-scrolled main text. Handles line selection, scroll position persistence, and communicates the selected line to the commentary panel. Any change to how lines are rendered or selected belongs here.

**BookViewAnnotationMenuRow.vue** - context menu row for highlight color selection and note editing. Uses `HIGHLIGHT_COLORS_LIST` from `bookViewAnnotationColors.ts`. Shows color swatches, an edit button for existing notes, and an eraser to clear highlights.

**BookViewNoteBubble.vue** - editable note bubble anchored to a note marker in the scroller. Positioned relative to the marker's DOMRect with smart viewport-edge flip.

## Composables

**useBookViewLinesTable.ts** - paginated line fetching in chunks of 200. Pre-allocates placeholder slots for correct virtualizer height. Use `prioritise(lineIndex)` to move a chunk to the front of the queue when the user jumps to a specific position.

**useBookViewLineRenderer.ts** - line content rendering with diacritics filtering, divine name censoring, and search highlighting. Caches rendered HTML per line to avoid re-running expensive transformations on every render cycle.

**useBookViewLineCopyMenu.ts** - context menu for copying selected lines with optional source attribution. Provides three copy modes: block copy (selected HTML as-is), copy with source appended inline as "(Book Title, TOC Path)", and copy with source prepended as an `<h2>` block. The source is built from `getActiveTocEntry()` and `getTocPath()` for the selection's first line, falling back to `tabStore.activeTab.tocPath` for the live scroll position.

**useBookViewAnnotations.ts** - orchestrator that encapsulates all annotation state: highlights, notes, note bubble overlay, and selection-to-line-offset conversion. Exposes `onHighlight`, `onClearHighlight`, `onAddNote`, `onMarkerClick` handlers. Most annotation entry points go through here rather than the sub-composables directly.

**useBookViewHighlights.ts** - manages user highlights for the currently open book. Loads highlights on mount, handles apply/clear with overlap rules, persists to `user_settings.db` via `userSettingsDb.ts`. Called internally by useBookViewAnnotations.

**useBookViewNotes.ts** - manages user notes for the currently open book. Lazy viewport-driven loading, create/update/delete mutations with immediate in-memory map updates. Called internally by useBookViewAnnotations.

**useBookViewLinesNavigation.ts** - programmatic scroll navigation. `scrollToLineId(id, fallbackIndex)` scrolls to a line by id, skipping if visible. `scrollToLineIndex(index, occurrenceOffset)` scrolls to a specific index with search match highlighting.

**useBookViewLinesScroll.ts** - scroll position management. `captureScrollPos()` / `restoreScrollPos()` for session restore. Saves position to tabStore IDB on visibilitychange, beforeunload, and unmount.

## Utilities

**bookViewAnnotationColors.ts** — highlight color palette matching Zayit's HighlightColors object. Exports `HIGHLIGHT_COLORS` (named map) and `HIGHLIGHT_COLORS_LIST` (ordered array). Colors are Material Design: yellow, green, blue, pink, orange.

## Imports

Import from this subfolder:

```typescript
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import { useLines } from './lines/useBookViewLinesTable'
import { useBookViewLineRenderer } from './lines/useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './lines/useBookViewLineCopyMenu'
```
