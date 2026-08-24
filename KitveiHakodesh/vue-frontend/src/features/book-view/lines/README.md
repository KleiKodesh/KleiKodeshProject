# lines

Virtual-scrolled main text display for the book view. Handles line rendering, selection, and persistence.

## Components

**BookViewLinesContent.vue** - virtual-scrolled main text. Handles line selection, scroll position persistence, and communicates the selected line to the commentary panel. Any change to how lines are rendered or selected belongs here.

**BookViewAnnotationMenuRow.vue** - context menu row for highlight color selection and note editing. Uses `HIGHLIGHT_COLORS_LIST` from `bookViewAnnotationColors.ts`. Shows color swatches, an edit button for existing notes, and an eraser to clear highlights.

**BookViewNoteBubble.vue** - editable note bubble anchored to a note marker in the scroller. Positioned relative to the marker's DOMRect with smart viewport-edge flip.

**BookViewAbbrevTooltip.vue** - compact tooltip showing dictionary expansions for a selected abbreviation. All senses on one wrapped line, divider-separated; positioned above the selection with viewport flip. Keyed by lookup id so each new selection remounts and re-measures.

## Composables

**useBookViewLinesTable.ts** - paginated line fetching in chunks of 200. Pre-allocates placeholder slots for correct virtualizer height. Use `prioritise(lineIndex)` to move a chunk to the front of the queue when the user jumps to a specific position. `holdBackfill()`/`releaseBackfill()` pause and resume the full-book backfill queue (out-of-band `prefetch`/`prioritise` fetches keep working while held). The backfill queue and its workers stop on unmount — a switched-away tab must not keep flooding the data channel while the next mount loads.

**useBookViewLinesBackfillGate.ts** - makes the full-book backfill yield to commentary loading (the "commentary loads slowly on tab return" bug). Held from setup so the tab-return flood cannot start before the restored commentary panel gets on the wire; re-held whenever a commentary load starts; released a short grace after each load settles, when no commentary panel is being restored, when the panel closes, or on a per-hold safety timeout (in-book search must never be starved of the full book).

**useBookViewLineRenderer.ts** - line content rendering with diacritics filtering, divine name censoring, and search highlighting. Caches rendered HTML per line to avoid re-running expensive transformations on every render cycle.

**useBookViewLineCopyMenu.ts** - context menu for copying selected lines with optional source attribution. Provides three copy modes: block copy (selected HTML as-is), copy with source appended inline as "(Book Title, TOC Path)", and copy with source prepended as an `<h2>` block. The source is built from `getActiveTocEntry()` and `getTocPath()` for the selection's first line, falling back to `tabStore.activeTab.tocPath` for the live scroll position.

**useBookViewLineLink.ts** - the "העתק קישור לקטע זה" context menu action. Builds the deep link (`buildLineLink` in `@/utils/appDeepLink`, the one place the
scheme and URL shape are defined: `kitveihakodeshapp://book/<bookId>?index=<lineIndex>`) — deliberately shaped like the `otzaria://open/book/<bookId>?index=<lineIndex>` links `HostLink.cs` already parses, with `index` meaning a 0-based positional line index in both — for the line the user pressed to open the menu (recorded on pointerdown, since both right-click and long-press begin with one) and copies it to the clipboard with a toast. The app does not register a handler for the scheme — the format is future-proofing only. `HostLink.cs` still parses the previous `seforimapp://` spelling so links already copied keep opening.

**useBookViewAbbrevTooltip.ts** - shows the abbreviation tooltip when the user selects a single complete word shaped like a Hebrew abbreviation (gershayim in the middle — רשב"א — or geresh at the end — מת'). Normalizes ״/׳/curly quotes to ASCII (the dictionary stores abbreviations with ASCII quotes only), strips nikud and surrounding punctuation, and rejects partial-word or multi-word selections. Attached prefix letters are handled via stripped candidates (מהשי"ת → השי"ת → שי"ת). Looks up via `dictAbbrevSenses` (dictionary DB only, never the seforim DB): exact headword matches for all candidates first, then `%candidate%` LIKE fallbacks. Produces display-ready sense labels (translations only — the term itself is not repeated). Dismissed when the selection collapses or the scroller scrolls.

**useBookViewAnnotations.ts** - orchestrator that encapsulates all annotation state: highlights, notes, note bubble overlay, and selection-to-line-offset conversion. Exposes `onHighlight`, `onClearHighlight`, `onAddNote`, `onMarkerClick` handlers. Most annotation entry points go through here rather than the sub-composables directly.

**useBookViewHighlights.ts** - manages user highlights for the currently open book. Loads highlights on mount, handles apply/clear with overlap rules, persists to `user_settings.db` via `userSettingsDb.ts`. Called internally by useBookViewAnnotations.

**useBookViewNotes.ts** - manages user notes for the currently open book. Lazy viewport-driven loading, create/update/delete mutations with immediate in-memory map updates. Called internally by useBookViewAnnotations.

**useBookViewLinesNavigation.ts** - programmatic scroll navigation. `scrollToLine(index, {occurrence, force, skipIfVisible})` is the one scroller every jump goes through (TOC click, section nav, search match, commentary jump); `scrollToLineId(id, fallbackIndex, options)` resolves a line id to its index over the same core. The slow path stabilizes the landing while late chunk loads shift the target.

**useBookViewLinesScroll.ts** - scroll position save and restore. Save: captures `firstVisible.index` (not the overscan item) and the pixel offset within that item; skips saves where the gap between scrollTop and firstVisible.start exceeds 2000px (stale mid-restore state). Restore: two-stage virtualizer-API-only approach — stage 1 calls `scrollToIndex` immediately with estimated heights, stage 2 fires when the target chunk loads, re-issues `scrollToIndex`, then tracks `item.start` in a rAF loop until stable (correcting each time background chunks above shift the layout), then applies the saved sub-line offset via `scrollToOffset` if it fits within the item's actual height. A post-stabilization watch handles late-loading chunks. Never sets `scrollTop` directly.

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
