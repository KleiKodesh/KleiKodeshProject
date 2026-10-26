# lines

The book view's main text: a `WindowedScroller` (src/components) mounting a sliding window of real line rows. Handles line rendering, selection, and persistence.

## Components

**BookViewLinesContent.vue** - the main text, rendered through `WindowedScroller.vue` (`src/components`): the scroller asks for each range of lines (`ensureLines`) before mounting it, so no row is ever a placeholder, and it opens directly on the saved line (`initialIndex`) — one render, no correction. Handles line selection, scroll position persistence, and communicates the selected line to the commentary panel. Any change to how lines are rendered or selected belongs here.

**BookViewAnnotationMenuRow.vue** - context menu row for highlight color selection and note editing. Uses `HIGHLIGHT_COLORS_LIST` from `bookViewAnnotationColors.ts`. Shows color swatches, an edit button for existing notes, and an eraser to clear highlights.

**BookViewNoteBubble.vue** - editable note bubble anchored to a note marker in the scroller. Positioned relative to the marker's DOMRect with smart viewport-edge flip.

**BookViewScrollbarTocTooltip.vue** - the current book-and-TOC-path label shown beside the pointer while it hovers the scrollbar track. Title leads on its own line, path beneath it in secondary colour; the title stands alone when no TOC entry covers the line. Height is observed via `useElementSize` with an explicit `{ box: 'border-box' }`, not measured once on mount — the component is not remounted per update, and the box resizes as a drag swaps in paths that wrap differently. The explicit border box matters twice over: it is the full painted height the centring wants, and it avoids the seed-vs-observed jump (`useElementSize` seeds from `offsetHeight`, then switches to the requested box on the first ResizeObserver fire) that would otherwise drop the label by half its chrome one frame in. Same trap as `useBookViewToolbarOverflow`. A plain Teleported label with `pointer-events: none`, clamped to the viewport and hung off the scrollbar's inner edge so it reads into the page.

**BookViewAbbrevTooltip.vue** - compact tooltip showing dictionary expansions for a selected abbreviation. All senses on one wrapped line, divider-separated; positioned above the selection with viewport flip. Keyed by lookup id so each new selection remounts and re-measures.

## Composables

**useBookViewLinesTable.ts** - the book's lines, fetched ON DEMAND only. `ensureLines(start, end)` resolves once a range has its text (consecutive missing 200-line slots are fetched in one request, up to 2000 lines) — the WindowedScroller asks for every range before mounting it, so no row is ever rendered without content. `ensureAllLines()` fetches the whole book, used only by whole-book actions (select-all copy, export to Word). Nothing downloads the book in the background: in-book search has its own source (`useBookViewSearch`).

**useBookViewLineRenderer.ts** - line content rendering with diacritics filtering, divine name censoring, and search highlighting. Caches rendered HTML per line to avoid re-running expensive transformations on every render cycle.

**useBookViewLineCopyMenu.ts** - context menu for copying selected lines with optional source attribution. Provides three copy modes: block copy (selected HTML as-is), copy with source appended inline as "(Book Title, TOC Path)", and copy with source prepended as an `<h2>` block. The source is built from `getActiveTocEntry()` and `getTocPath()` for the selection's first line, falling back to `tabStore.activeTab.tocPath` for the live scroll position.

**useBookViewLineLink.ts** - the "העתק קישור לקטע זה" context menu action. Builds the deep link (`buildLineLink` in `@/utils/appDeepLink`, the one place the
scheme and URL shape are defined: `kitveihakodeshapp://book/<bookId>?index=<lineIndex>`) — deliberately shaped like the `otzaria://open/book/<bookId>?index=<lineIndex>` links `HostLink.cs` already parses, with `index` meaning a 0-based positional line index in both — for the line the user pressed to open the menu (recorded on pointerdown, since both right-click and long-press begin with one) and copies it to the clipboard with a toast. The app does not register a handler for the scheme — the format is future-proofing only. `HostLink.cs` still parses the previous `seforimapp://` spelling so links already copied keep opening.

**useBookViewScrollbarTocTooltip.ts** - shows the book title and a TOC path while the pointer hovers the scroller's drawn scrollbar (`WindowedScrollerBar.vue`). On the thumb or an arrow it is the CURRENT path — "where am I?"; elsewhere on the track it is the path of the line a drag to that point would land on — "where would this take me?" — so the bar reads like an index. `getActiveTocEntry`/`getTocPath` turn a line index into the displayed path; the alternate TOC's label for the same line joins it. Title and path stay separate fields so the label can weight them, and so the title alone can answer when no TOC entry covers the line. `refreshScrollbarTocTooltip()` is called from the scroller's `@scroll` so a thumb drag — when the path changes fastest and the pointer is not moving — keeps the label current. Positioned off the bar element's own rect (side from its resolved `direction`), so it follows the RTL bar to the physical left. The bar itself reveals in hidden-scrollbars mode and keeps itself revealed while the pointer rests on it.

**useBookViewAbbrevTooltip.ts** - shows the abbreviation tooltip when the user selects a single complete word shaped like a Hebrew abbreviation (gershayim in the middle — רשב"א — or geresh at the end — מת'). Normalizes ״/׳/curly quotes to ASCII (the dictionary stores abbreviations with ASCII quotes only), strips nikud and surrounding punctuation, and rejects partial-word or multi-word selections. Attached prefix letters are handled via stripped candidates (מהשי"ת → השי"ת → שי"ת). Looks up via `dictAbbrevSenses` (dictionary DB only, never the seforim DB): exact headword matches for all candidates first, then `%candidate%` LIKE fallbacks. Produces display-ready sense labels (translations only — the term itself is not repeated). Dismissed when the selection collapses or the scroller scrolls.

**useBookViewAnnotations.ts** - orchestrator that encapsulates all annotation state: highlights, notes, note bubble overlay, and selection-to-line-offset conversion. Exposes `onHighlight`, `onClearHighlight`, `onAddNote`, `onMarkerClick` handlers. Most annotation entry points go through here rather than the sub-composables directly.

**useBookViewHighlights.ts** - manages user highlights for the currently open book. Loads highlights on mount, handles apply/clear with overlap rules, persists to `user_settings.db` via `userSettingsDb.ts`. Called internally by useBookViewAnnotations.

**useBookViewNotes.ts** - manages user notes for the currently open book. Lazy viewport-driven loading, create/update/delete mutations with immediate in-memory map updates. Called internally by useBookViewAnnotations.

**useBookViewLinesNavigation.ts** - programmatic scroll navigation. `scrollToLine(index, {occurrence, force, skipIfVisible, direction})` is the one scroller every jump goes through (TOC click, section nav, search match, commentary jump); `scrollToLineId(id, fallbackIndex, options)` resolves a line id to its index over the same core. A jump is `WindowedScroller.scrollToIndex`: the target's lines are fetched, the window is mounted around them, and the scroll is set from the row's real position — exact, no retry. Owns `SCROLL_LANDING_GAP_PX`, the sliver a landing leaves above its target.

**useBookViewLinesScroll.ts** - scroll position save and restore. Save: the first visible row's index and the pixel offset into it, read from the DOM. Restore: the scroller itself opens on the saved line (`initialIndex` / `initialOffsetPx` on `WindowedScroller`) — rows fetched first, one render, scroll set from the real row; an offset that no longer fits the row (zoom or width changed) lands at the row's top. This composable holds the position save through that landing, releases it after the landing's scroll event, and on the scroller's `opened` event runs the follow-ups: focus, scrolling an FTS mark into view, the deep-link flash.

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
