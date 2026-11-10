# Scroll position & commentary panels — intended behavior

This documents the intended behavior of scroll-position persistence and of where the
commentary panels open, as specified by the product owner, and the ONE model all of
it is built on. It exists so neither has to be re-explained: **any change to data
loading, the scroller, or commentary rendering must preserve every rule below.**

There are **three** commentary panels ('bottom', 'side', 'side-left'), and every rule
applies to each of them independently: each keeps its own commentator, filter and
place. Where this document says "the commentary panel", read "each commentary panel"
— the composables named here are instantiated once per panel by
`commentary/useCommentaryPanelSlot.ts`, driven by `COMMENTARY_SLOTS`.

## The model — read this first

The lines pane and the commentary panels are `WindowedScroller` lists
(`src/components/WindowedScroller.vue`).

1. **A windowed scroller shows "the rows from index N". That is all it knows.** The
   top of the list is not special: row 0 and row 5000 are the same kind of window.
2. **So where a list opens is an ARGUMENT, not an action.** The start row
   (`initialIndex`, `initialOffsetPx`) is given when the scroller is *created*, read
   once, and the first window is built there. A list is never shown at its top and
   moved to a position afterwards.
3. **A caller that does not know its start row yet does not create the scroller
   yet.** A saved position is read FIRST; a commentary list's commentator is settled
   FIRST. To open somewhere else from scratch, a NEW scroller is created (`v-if` /
   `:key`).
4. **Rows that do not exist yet are the row source's business.** The scroller asks
   `ensureRows` for its start row like for any other; a list that is still streaming
   in answers when the row has arrived.
5. **Going elsewhere in a live list is `await scrollToIndex(index, { offsetPx })`.**
   Exact, one render, resolves when the row and its content are in the DOM.

What follows from it, and is therefore **forbidden** anywhere around these lists:

- "restore" code that runs after a list has mounted and moves it to a saved place;
- a mask or spinner hiding a list that opened in the wrong place;
- goals, intents, debts or flags that wait for something to arrive and then scroll;
- retries, settle loops, rAF polling, `MutationObserver` corrections, timeouts;
- any guard that cancels a move because the reader pressed a key, clicked or scrolled.

All of these existed here. They were the price of the virtualizer this app used
before (`@tanstack/vue-virtual`), which could only open a list at its top and be
scrolled afterwards, on estimated row heights. Four generations of "finally fixed"
commentary positioning bugs were that price. **Do not bring any of it back.** If a
list opens in the wrong place, the fix is in *what start row it is created with* or
*when it is created* — never in something that runs afterwards.

(The bug that settled this, 2026-11-10: the scroller mounted at the top, waited for
its start row, and protected the late move with a "has the reader moved?" flag set by
any keydown. Ctrl+click on a search result opens a new tab with Ctrl still held and
auto-repeating — which cancelled the move, and the book opened at the wrong place.)

## A. The reading position is persisted exactly (lines pane AND commentary panels)

The exact reading position must survive:

1. **Tab switches** — leaving a book-view tab and returning to it (the page component
   is keyed by tab and remounts).
2. **Session reload** — closing and reopening the app.
3. **"זכרו מיקום אחרון בספר"** (Settings → resumeLastRead, default on) — opening the
   same book in a *new* tab opens at the last position saved for that book (per-book
   `lastRead` record). When the setting is off, a brand-new tab starts from the top,
   but an existing tab's own saved state still applies.

**How a position is stored:** `(scrollIndex, scrollOffset)` = the row at the top of
the viewport plus the pixel offset *within* that row. Rows are real DOM, so both are
measured, never estimated.

**How it is used — the same way on every path** (tab switch, reload, Back, recents,
a new tab): it is the start row the next scroller is created with.

- **Lines pane.** `useBookViewSessionRestore` reads the tab's state and the book's
  last-read record from IndexedDB and sets `initialScrollTop` / `initialScrollOffset`
  (or keeps `initialLineIndex`, a requested line — a search result, a TOC entry, a
  deep link — which outranks a saved place). `BookViewPage` creates
  `BookViewLinesContent` only once that read has finished (`v-if="idbResolved"`), so
  its scroller's start row is final at creation. `useBookViewLinesScroll` puts the
  list nowhere; on the scroller's `opened` event it only does what needs the rows in
  the DOM (focus, the caret, the search mark, the deep-link flash).
- **Commentary panels.** The panel reports where it is on every scroll
  (`useCommentaryScroll.captureScrollPos` → `useBookViewCommentaryPanel
  .onCommentaryScroll`), and that place is kept by the panel's owner so it outlives
  the view (closed, reopened, remounted, saved to the session and read back —
  `useBookViewSessionRestore._seedPanel`). It reaches `CommentaryView` as
  `savedScrollIndex` / `savedScrollOffset` and is what the panel's scroller is
  created at — see C.
- **A requested line is one navigation's argument.** `openTocLineIndex` (and
  `openTocLineOffsetPx`, set when Back or a recents row returns to an exact place)
  is read by `useBookView` at setup and cleared from the tab at once, whatever set
  it. Left on the tab, the next remount (a tab switch) would open at that line
  again instead of where the reader has since got to.
- **Saving** happens when a view goes away (`registerPositionCapture` in
  `persistenceFlush.ts`, and `onBeforeUnmount`). There is no restore for a save to
  race with.

## A2. The KEYBOARD CARET is part of the position

Caret browsing (F7, "הפעלת סמן טקסט לניווט ובחירה" in the shortcuts list) is Chromium's own —
there is no app code behind the key. It navigates and selects from the DOCUMENT's
caret: the collapsed `window.getSelection()`. So the lines pane has TWO positions:

- **which rows the view shows** — the scroller's window; and
- **where the caret is** — the document selection.

A click sets the second for free. An opening does not click: the scroller is created
at its start row and the viewport is given focus, and **focus is not a caret**. With
none inside the focused element the browser picks its own starting point — the start
of the MOUNTED WINDOW, which begins some rows above the start row. The view showed
the right line while the caret sat higher, so the first arrow key left it.

`placeCaretAtViewportStart` (`lines/lineCaret.ts`) therefore seats a collapsed caret
on the first line in view, from `onScrollerOpened` — the rows are in the DOM by then,
so it is one call, not a retry. Collapsed matters: `hasActiveTextSelection`,
`useSelectAllInContainer` and `useVirtualizedTextSelection` all ignore a collapsed
selection. A real (non-collapsed) selection is never replaced.

Not covered, and a different situation: a jump WITHIN an open book (`scrollToLine` — a
TOC click, a section step, a search match) leaves the caret where it was. Those are
driven from another panel or an input, which keeps the focus.

## B. Blank slate → the default commentator

When a line is selected and the reader has not chosen a commentator (fresh open, no
saved state), the panel opens on the book's **default commentator**
(`default_commentator` table, lowest `position`). `usePinnedCommentary` decides it:
the `commentaryLineId` watcher falls back to `defaultCommentatorBookIds[defaultRank]`
(bottom panel 0, side panel 1, left panel 2) when no commentator was captured; if
that book has no group for this line, the next default that does is used (groups
watcher).

Line clicks are intentionally inert while every commentary panel is closed, so the
blank-slate flow is always *open panel → click line*.

## C. Where a commentary list opens

"The pin" is a product rule, not a scroll mechanism: it is **the commentator a panel
stays on** (`pinnedCommentaryGroup`: book + section labels). Switching lines must keep
the reader on the commentary book they were reading.

Every commentary list that loads gets a **new scroller** (`CommentaryView` has none
while loading or empty), created at the row `useCommentaryScroll.startRow` returns —
decided once, when the list is ready. In order:

1. **what the reader just asked for** while there was no scroller to show it (a
   commentator picked, a search hit stepped to, while the list was loading);
2. **the same place**, when the list coming up is the one the saved place belongs to:
   the first scroller of a view (a reopened or remounted panel, a restored session),
   or the same line's list loading again. An index past the end of a list that has
   since shortened opens on the last row;
3. **the panel's commentator** — the pinned group's header;
4. the top.

"The list is ready" means: loaded, non-empty, **and its commentator settled**
(`pinSettled`). `usePinnedCommentary` writes the pin a moment *after* a line change
and again *after* the list arrives (both watchers await the book's default
commentators), so the pin a panel holds when a new list lands may still be the
previous line's. `isPinSettled` says the pin now belongs to this list; the scroller
is created only then. This one wait replaces everything that used to chase a pin that
"arrived late".

How the commentator follows the reader:

- At the moment of a line click — *synchronously, before any reactive state changes*
  — each panel's current commentator is captured and staged (`captureActivePins` /
  `setPendingPin`, see the rules below). An explicit line click also voids the saved
  place: it belonged to the previous line's list.
- If the pinned book has **no commentary on the new line**, a placeholder group
  ("אין טקסט לשורה זו") is injected at the pinned book's canonical position
  (`useGroupsForDisplay`), so the panel opens there rather than elsewhere. Placeholder
  lines use `lineId: -1`.

## D. Navigation inside a live panel

The reader picking a commentator (header toolbar next/previous and its search, the
filter tree) or stepping to an in-panel search match is plain navigation:
`scrollToGroup` / `scrollToFlatIndex` → one `scroller.scrollToIndex`. When no
scroller can take it (the list is loading), the request becomes the row the next
scroller is created at.

The header's next/previous-**section** buttons (`onNavigateSection` in
`useCommentaryNavigation`) find the next/previous line or TOC section with commentary
for that book, move the anchor line there, scroll the **lines pane** to it, and pin
the book in the navigating panel only — so the new list opens on it by C. No scroll
wiring of their own.

## E. Lines pane keeps its position when a side commentary opens or closes

Opening or closing **either** side column changes the text column's width, which
re-wraps every line, so `BookViewLinesContent` is re-keyed on `sideColumnsKey`: a new
scroller is created. A `watch(sideColumnsKey)` in `BookViewPage` captures the old
one's position first and makes it the new one's start row (clearing
`initialLineIndex`, so the captured place outranks the line the book was opened at).
Dragging a divider remounts nothing.

Known limitation: the place is (index, offset)-based, and the two widths give
different line heights — the same line is kept at the top, but the pixel offset into
it may differ slightly.

## Rules about the commentator (pin) — learned the hard way, still true

- **Pin matching.** A pin is `(bookId, sectionLabel, subSectionLabel)`, but the labels
  only disambiguate a book that appears in several sections. They are captured on the
  PREVIOUS line, and the same book can sit under a different section on the next one,
  so the group is matched exact-first, then by `bookId` alone (`resolveGroupIndex`).
- **Pin staging.** A pin is staged (`setPendingPin`) so that the next
  `commentaryLineId` change consumes it; a panel whose pin was NOT staged falls back
  to its default. Stage it synchronously at the instant the anchor line changes —
  never before an `await`. `useCommentaryNavigation` takes an `onBeforeNavigate` hook
  and calls it immediately before mutating the refs for exactly this.
- **A derived active group is not a preference.** `activePinnedGroup` falls back to
  the FIRST header when no rows are on screen. Pin capture uses
  `activePinnedGroupForCapture`, which answers null unless the reader has personally
  moved the panel (wheel / touch / pointer / a scrolling key) since the app last put
  it somewhere at their request, or picked a commentator by name from the toolbar
  (`explicitPick`). `captureActivePins` then falls back to the pin the panel holds.
  A probe's `el.scrollTop =` is deliberately not a preference.
- **A null "active group" is not a preference either.** A panel that is mid-load has
  no active group; staging null would reset it to the default commentator. Only a
  panel that has never had a pin gets the default.
- **Async watchers** that await before writing (both pin watchers) bump their
  generation counter before every early return and re-read the list live after the
  await. One line tap runs `useCommentary.load()` twice, so a panel's list goes
  `groups → [] → groups` for the same line.
- **Serialize section navigation.** `onNavigateSection` reads the CURRENT anchor to
  decide where "next" is, after a DB query; overlapping clicks would both resolve to
  the same target. Clicks are chained (capped) so each starts where the previous
  ended.
- **Never navigate "book 0".** The sticky nav's section buttons are disabled without
  an active group, and `onNavigateSection` rejects a falsy bookId.
- **Identity in the DOM.** `.commentary-header` carries `data-book-id`. Its label
  includes the TOC path and changes with the anchor line; compare `data-book-id`,
  never the text, when asking "is this panel still on the same commentator?".
- **This class of bug is invisible in dev.** The local service answers in single-digit
  ms; the WebView2 host round-trips through postMessage. To reproduce ordering bugs in
  a Playwright probe, delay the service traffic (`context.route` on the non-vite
  port) — 700 ms is enough. Use a section-mode (TOC-heading) click for the heaviest
  load.

## Other facts

- Commentary groups are replaced wholesale on every line change (`groups.value =`).
- A commentary line has its text before its row mounts (`ensureLineContents` is the
  scroller's row source); there are no empty rows that grow later.
- `useCommentaryRender`'s render cache is keyed by flat index and validated against
  the *source content string* (`renderSource`).
