# Scroll persistence & commentary positioning — intended behavior

This documents the complete intended behavior of scroll-position persistence and
commentary-panel positioning, as specified by the product owner. It exists so the
behavior never has to be re-explained: **any change to data loading, virtualization,
or commentary rendering must preserve every rule below.**

All of this is delicate because both the lines pane and the commentary pane are
TanStack virtual lists with **dynamic item measurement** (`measureElement`): item
heights start as estimates and change when items render — and, since the two-phase
commentary loader (2026-07), also change when line *content* backfills into
already-rendered items.

## A. Exact scroll-position persistence (lines pane AND commentary pane)

The exact reading position must survive:

1. **Tab switches** — leaving a book-view tab and returning to it (the page component
   is keyed by tab and remounts).
2. **Session reload** — closing and reopening the app.
3. **Reopening a book in a new tab** — opening the same book in a *new* tab restores
   the last position saved for that book (per-book `lastRead` record). This is
   always on: it used to be the optional "זכור מיקום אחרון בספר" setting, which was
   removed in favor of hardcoded resume. Per-tab `bookViewState` still takes
   precedence over the per-book record when both exist.

**How the position is stored:** not as a raw `scrollTop` — that is meaningless in a
virtualized list whose sizes are estimates — but as
`(scrollIndex, scrollOffset)` = the index of the item at the top of the viewport
plus the pixel offset *within* that item.

- Lines pane: captured in `useBookViewLinesScroll.captureScrollPos`, saved through
  the tab store (per-tab `bookViewState` + per-book `lastRead` in IDB), restored via
  `initialScrollTop`/`initialScrollOffset` (`useBookViewSessionRestore`).
- Commentary pane: captured in `useCommentaryScroll.captureScrollPos` on every
  scroll (emitted to `useBookViewCommentaryPanel.onCommentaryScroll`), restored by
  `restoreCommentaryScrollPos(index, offset)` — on session restore
  (`useBookViewSessionRestore.restore`) and on every panel remount
  (`onCommentaryPanelMounted`: v-if toggle, bottom↔side layout switch, tab return).

**How restore must work (the two-stage pattern):**

1. Use TanStack's built-in API first: `virtualizer.scrollToIndex(index)` brings the
   item into the render window using estimated sizes — instantly, no waiting.
2. Then correct: once the target item is actually measured (and, since two-phase
   loading, once its *content* is present so its height is real), set
   `scrollTop = item.start + offset`. Corrections are driven by MutationObserver /
   rAF retries, NOT polling loops — virtualization means measurements land
   asynchronously as DOM mutates.
3. TanStack's built-in resize anchoring (`shouldAdjustScrollPositionOnItemSizeChange`
   default: adjust when a resized item is above the scroll offset) keeps the
   viewport anchored while items above it grow — do not fight it; re-read
   `item.start` fresh on every correction rather than caching a target scrollTop.

**Two-phase loading rule:** commentary line items may render with `content === ''`
and grow later when the backfill batch arrives. Therefore:

- The restore correction must NOT apply the saved `scrollOffset` while the target
  item's content is still pending — a 300px offset into a 20px-tall empty item
  lands in the wrong group. Wait (bounded) for the content, then apply.
- The content loader must prioritize the lines that are actually in the viewport
  (`requestContentPriority` from the CommentaryView virtual-items watcher), so the
  restore target and anything scrolled to fills within one small query instead of
  waiting for the display-order backfill to reach it.

## B. Blank slate → default commentary

When a line is selected and there is **no pending pin** (fresh open, no saved
state), the panel must position itself on the book's **default commentator**
(`default_commentator` table, lowest `position`). Implemented in
`usePinnedCommentary`: the `commentaryLineId` watcher falls back to
`defaultCommentatorBookIds[0]` when no pin was captured; if that book has no group
for this line, the next default that does is used (groups watcher).

Note: line clicks are intentionally inert while the commentary panel is closed
(`onLineClick` checks `commentaryVisible`), so the blank-slate flow is always
*open panel → click line*. That means the **first** groups load happens with the
panel already mounted — the first-load branch of `setupGroupReloadScroll` must
scroll to the pinned/default group when there is no saved scroll position
(`hasSavedScrollPos`); with a saved position the restore path owns positioning
and the first-load scroll must be skipped.

## C. Switching lines keeps the current commentary

When the user selects a different line, the panel must stay on the commentary book
they were reading:

- At the moment of the click — *synchronously, before any reactive state changes* —
  the active group (top sticky header) is captured via `setPendingPin`
  (`useBookViewLineSelection.onLineSelected` / `onNavigateSection`).
- After the new line's groups load, `setupGroupReloadScroll` (in
  `useCommentaryScroll`) scrolls to the pinned group's header.
- If the pinned book has **no commentary on the new line**, a placeholder group
  ("אין טקסט לשורה זו") is injected at the pinned book's canonical position
  (ordering taken from `staticFilterGroups`) so the panel doesn't jump — see
  `groupsForDisplay` / the pinned-placeholder branch in `useCommentary`.
- Placeholder lines use `lineId: -1` and must never be content-backfilled.

## D. Header next/prev navigation scrolls to the target commentary

The commentary header (and `CommentaryHeaderNav`) has next/previous-section buttons
(`onNavigateSection` in `useCommentaryNavigation`). They:

1. Find the next/prev line (normal mode), TOC section (TOC mode), or adjacent line
   (multi-select mode) that has commentary for the given book.
2. Set `selectedLineId`/`commentaryLineId`, scroll the **lines pane** to the target
   line (`scrollToLineId`).
3. Pin the target book (`setPendingPin` in `useBookView.onNavigateSection`), so when
   the new groups load, `setupGroupReloadScroll` scrolls the **commentary pane** to
   that book's group — the same path as a line click. No manual scroll wiring.

`scrollToGroup` itself is two-stage like restore: `scrollToIndex` first, then a
MutationObserver correction that re-reads the header's measured `start`. Because
content backfill keeps resizing items for a short while after groups render, the
correction must stay active (bounded, cancellable by a newer scroll request via the
token) until the target's position is stable — a single one-shot correction lands
wrong if a batch arrives right after it.

## E. Lines pane keeps its position across layout-mode switches

Switching commentary layout bottom ↔ side swaps template branches in BookViewPage
(SplitPane vs .side-by-side), which unmounts and **remounts BookViewLinesContent**.
The remounted instance re-runs its initial-scroll restore from
`initialLineIndex`/`initialScrollTop`/`initialScrollOffset` — refs frozen at
session-restore time — so without intervention it jumps to the stale position (or
the top when nothing was saved). A pre-flush `watch(sideBySide)` in BookViewPage
captures the live position (`linesContentRef.captureScrollPos()`, old instance
still mounted at pre-flush time) and writes it into those same refs, clearing
`initialLineIndex` so the captured index wins over a TOC-open index. Dragging the
divider does not remount anything and needs no handling.

Known limitation: the restore is (index, offset)-based, and the two modes have
different lines-pane widths, so line heights differ — the same line is kept at
the top, but the sub-line pixel offset may land slightly differently. A
no-remount single-container layout was tried (2026-07-13) and reverted at the
product owner's request.

## Interaction rules / gotchas (learned the hard way)

- **Restore wins over pin-scroll**: `restoreCommentaryScrollPos` sets
  `isRestoringScrollPos` and bumps `scrollToGroupToken` so `setupGroupReloadScroll`
  and in-flight `scrollToGroup` calls never overwrite an in-flight restore.
- `setupGroupReloadScroll` skips its very first groups load (`isFirstLoad`) — the
  panel-mount path (`onCommentaryPanelMounted`) owns positioning on first open.
- Commentary groups are replaced wholesale on every line change (`groups.value =`);
  mutation-in-place only happens for content backfill, which MUST go through the
  reactive proxy (`groups.value`), never the raw array, or already-rendered rows
  won't update.
- `useCommentaryRender`'s render cache is keyed by flat index and validated against
  the *source content string* (`renderSource`) — required because content changes
  in place without the groups array identity changing.
- Scroll capture during backfill fires on anchoring adjustments too; that's fine —
  the saved `(index, offset)` stays approximately right and restore corrects.
