# Scroll persistence & commentary positioning — intended behavior

This documents the complete intended behavior of scroll-position persistence and
commentary-panel positioning, as specified by the product owner. It exists so the
behavior never has to be re-explained: **any change to data loading, virtualization,
or commentary rendering must preserve every rule below.**

Since 2026-08 there are **three** commentary panels ('bottom', 'side', 'side-left'),
and every rule below applies to each of them independently: each keeps its own pin,
filter, saved scroll position and restore lifecycle. Where this document says "the
commentary pane", read "each commentary panel" - the composables named here are
instantiated once per panel by `commentary/useCommentaryPanelSlot.ts`, which is
driven by `COMMENTARY_SLOTS`, not by any hardcoded pair.

## The positioner (2026-08-11): one owner per panel

All programmatic commentary positioning goes through a single per-panel GOAL slot in
`useCommentaryScroll`. Six code paths used to write to the scroller directly
(restore, groups-reload pin-follow, pin-arrived-late, panel-mount, header nav,
search jump), coordinated through shared flags (`scrollToGroupToken`,
`isRestoringScrollPos`, `restoreIntentClaimed`) - and every new panel or new load
timing multiplied their races; the git history is four generations of "finally
fixed". Now every path REQUESTS a goal (`group` / `restore` / `flatIndex` /
`restore-intent`), and one rAF loop executes whichever goal is current:

- **Priority**: a user request always replaces the current goal; a restore always
  wins over auto pin-follow; an AUTO request (reload/mount) can never displace a
  restore or a claimed restore intent. `setGoal` returns acceptance so auto callers
  can keep their pin-scroll debt.
- **Completion is condition-based, never wall-clock**: a goal holds until its target
  is measured, its content present (restore with offset), its position stable for
  SETTLE_FRAMES consecutive frames, **and nothing above the target is still waiting
  for content**. The old fixed windows (800ms → 2.5s → 6s) each encoded an
  assumption about how long loading takes and each broke on the next slower
  environment; frame-based settling stretches with machine load instead. A generous
  SAFETY_MS valve force-applies and ends a genuinely unachievable goal.
- **Stable frames are not proof a goal is finished (2026-08-14).** `loading` goes
  false when the group STRUCTURE lands; `useCommentary` then backfills line content
  in batches for as long as it takes, and every still-empty line above the target
  grows later and pushes the target down. Settling on frames alone made
  SETTLE_FRAMES a fixed window in disguise (~500ms at 60fps): the goal declared
  itself done mid-backfill, tore down its rAF loop, and the content then landed with
  nothing left to re-anchor — "it scrolls to the right place and then jumps away
  once the content loads", the symptom that outlived the positioner rewrite. The
  loop now also requires `contentPendingAbove(goal)` to be false, which scans the
  flat list for `type === 'line' && lineId > 0 && content === ''` before the target
  index. Only lines ABOVE it matter (those are the ones that move it); `lineId > 0`
  skips the injected placeholder rows, which are never backfilled and would
  otherwise hold every goal open until the safety valve. The mask is still released
  at first arrival, so the extra hold is invisible and stays interruptible.
- **`isPositioning`** is true from goal start to FIRST arrival; CommentaryView
  shows an opaque positioning mask (spinner after the usual delay) over the
  scroller while it is true, so the reader never watches content sitting at the
  wrong offset while backfill reshapes the list. The mask is `pointer-events:none`
  and any wheel/pointer/key cancels the goal, so it is always interruptible - and
  it must stay an OVERLAY: putting it in the v-if chain would unmount the scroller
  and deadlock the goal it masks.
- **A derived active group is not a preference.** `activePinnedGroup` falls back to
  the FIRST header when nothing has scrolled under the nav - which is exactly a
  panel's state mid-transit. A click landing in that window used to capture the
  first group as "what the reader was looking at" and permanently switch the pin
  (the T4 flip: reliable at 0ms, intermittent under host latency). Pin capture now
  uses `activePinnedGroupForCapture`, which answers null unless the reader has
  personally moved the panel (wheel/touch/pointer/key) since the last programmatic
  positioning - `captureActivePins` then falls back to the held pin, which is
  authoritative. Note this also means a PROBE's `el.scrollTop =` is deliberately
  not a preference: no user gesture, no pin-follow.

Verified with a latency-injected Playwright matrix (0 / 700 / 1500 ms on the
service port): default-commentator scroll, keep-place across line switches,
close/reopen restore at shallow and ~80% depth, section-mode loads, repeated
same-line determinism, and rapid unthrottled click bursts - all green at every
latency, where the pre-rewrite code failed restore beyond ~8k px drift at 700ms.

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
3. **"זכרו מיקום אחרון בספר"** (Settings → resumeLastRead, default on) — opening the
   same book in a *new* tab restores the last position saved for that book
   (per-book `lastRead` record). When the setting is off, a brand-new tab starts
   from the top, but an existing tab's own saved state still restores.

**How the position is stored:** not as a raw `scrollTop` — that is meaningless in a
virtualized list whose sizes are estimates — but as
`(scrollIndex, scrollOffset)` = the index of the item at the top of the viewport
plus the pixel offset *within* that item.

- Lines pane: captured in `useBookViewLinesScroll.captureScrollPos`, saved through
  the tab store (per-tab `bookViewState` + per-book `lastRead` in IDB), restored via
  `initialScrollTop`/`initialScrollOffset` (`useBookViewSessionRestore`).
- Commentary panes: captured per panel in `useCommentaryScroll.captureScrollPos` on
  every scroll (emitted to that panel's `useBookViewCommentaryPanel.onCommentaryScroll`),
  restored by `restoreCommentaryScrollPos(index, offset)` -- on session restore
  (`useBookViewSessionRestore.restore`, which seeds each panel from
  `commentaryPanels[slot]`) and on every panel remount (`onCommentaryPanelMounted`:
  v-if toggle, tab return). Each panel's position is stored under its own slot, so
  no panel can overwrite another's.

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

**Async-watcher rule (learned the hard way, twice):** any watcher that awaits
before acting on a virtualized list must

1. bump its generation counter **before** every early return, not after the guards.
   A callback that bails (empty list, still loading) still has to invalidate a
   callback already sitting on its `await`; and
2. re-read the list **live** after the await instead of trusting the value the
   watcher was handed.

A single line tap runs `useCommentary.load()` twice (the `selectedLineId` and
`selectedLineIds` watchers both fire), so a panel's list goes
`groups -> [] -> groups`. With the bump after the guards, the first
`setupGroupReloadScroll` callback resumed while the list was empty, still saw its
own captured non-empty array, and called `scrollToGroup` on a panel whose scroller
the empty-state branch had already unmounted: `ABORT_no_scroller`, no scroll, and
the panel silently kept whatever offset the virtualizer left it at. That is what
"the commentary panel loses its place when I switch lines" was.

**Pin matching rule:** a pin is `(bookId, sectionLabel, subSectionLabel)`, but the
labels only disambiguate a book that appears in several sections. They are captured
on the PREVIOUS line, and the same book can sit under a different section on the
next one, so `scrollToGroup` matches exact-first then falls back to `bookId` alone
(traced as `resolveIndex_label_fallback`). Refusing to scroll on a label mismatch
looked identical to losing the position.

**Pin-staging rule:** a pin is staged (`setPendingPin`) so that the next
`commentaryLineId` change consumes it; a panel whose pin was NOT staged falls back
to its default commentator. So stage it synchronously at the instant the anchor line
changes — never before an `await`.

Section navigation runs DB queries before it knows its target, and staging up-front
left the pins in flight across the round trip. Two ways that broke:
a navigation that found no target left them staged indefinitely, for some later,
unrelated `commentaryLineId` change to consume; and anything that changed the anchor
during the round trip (a line tap, the auto-select timer) consumed them early, so the
navigation's own change found nothing staged and reset the panel to its default.
`useCommentaryNavigation` therefore takes an `onBeforeNavigate` hook and calls it
inside `afterNavigate`, immediately before mutating the refs.

**(Superseded by the positioner - kept for history.) The correction window must outlive the load, not the animation.** A pin scroll is
not done when it lands: items ABOVE the target render as near-empty stubs and grow as
their text, TOC-path labels, notes and highlights arrive, and each one pushes the
target down. `scrollToGroup` re-anchors on every DOM mutation, but only for
`CORRECTION_WINDOW_MS`. At 800ms that window closed while a cold section-mode load
was still filling in, so the panel landed on the pinned commentator and then drifted
off it with nothing left to re-anchor — "it lands on Rashi and then jumps away". It is
now 6000ms, matching the lines pane's 10s post-restore window and for the same
reason.

A long window is only safe because it yields immediately: `finish()` runs on wheel,
touchstart, pointerdown AND keydown (the scroller is focusable and arrow keys scroll
it), and any newer `scrollToGroup` / `restoreCommentaryScrollPos` / `scrollToFlatIndex`
bumps `scrollToGroupToken` and cancels it. `scrollToFlatIndex` had to start bumping
the token for this: jumping to a search match is a competing programmatic scroll, and
at 800ms the overlap was too short to matter while at 6s it would have dragged the
panel back off the match.

**Payload size decides whether any of this is observable, and the dev corpus is too
small.** Measured on this machine: the heaviest chapter reaches ~7.8k px with 8
commentators, and the content settles inside 800ms (`correct_applied=0`,
`correct_noop=3`). A rich book's chapter is an order of magnitude larger. A probe that
passes here says nothing about a large corpus — see
[[dev-latency-hides-races-2026-08-06]] for the latency-injection technique, and use it
together with a section-mode (TOC-heading) click, which is the heaviest load the
reader can trigger.

**A pin can arrive AFTER its groups — owe the scroll and settle it later.**
`usePinnedCommentary` awaits `getDefaultCommentators`, and that query runs ONCE per
book. So on the FIRST commentary load of a book it can still be in flight when the
groups land: `setupGroupReloadScroll` finds no pin, returns, and nothing re-triggers
it milliseconds later when the pin appears — the panel never scrolls to the default
commentator at all. Every later load has the list cached and works, which is why it
reads as "the FIRST time I open a chapter it doesn't scroll to Rashi".

The `!pinned` branch therefore records `pinScrollOwed`, and `settlePinDebt` pays it
once every precondition holds. Reproduce by delaying ONLY the
`getDefaultCommentators` service call, in a fresh page, and arm the delay BEFORE
opening a panel — opening one syncs `commentaryLineId` from the selected line, which
already asks for the pin. Without the fix: `begins=0`, `scrollTop=0`, panel on the
first group. With it: `begins=1`, panel on its pin.

**The debt must be re-armable, and must outlive the list blinking empty (2026-08-14).**
The one-shot version above still lost the first-load scroll in three ways, which is
why "it doesn't scroll to the default commentary on first load" survived the
positioner rewrite:

- **A `watch(pinnedGroup)` fires once.** When the pin resolved while a partial load
  was still running, the callback hit its `isLoading()` guard and returned — and the
  pin ref never changed again, so nothing re-asked and the debt stayed unpaid for the
  whole load. The pin is NOT reliably the last precondition to arrive. `settlePinDebt`
  is therefore called from watchers on *every* precondition — the pin
  (`pin-arrived-late`), the loading edge (`pin-owed-load-done`) and the groups
  (`pin-owed-groups-arrived`) — so whichever lands last does the work. All three are
  AUTO reasons; the guards make every redundant call a no-op.
- **`groups = []` does not mean a new load.** One line tap runs `load()` twice, so the
  list goes `groups → [] → groups` for the SAME anchor. Voiding the debt on every
  empty list wiped it inside that blink; if the pin query resolved in that window the
  debt was already gone. The debt now carries the anchor it belongs to
  (`owedForAnchor`, from `selectedLineId`) and only a genuinely different anchor
  voids it.
- **"Pin decided but its book isn't in the list" is owed, not done.** That branch used
  to return without recording anything, leaving the panel on the first group with no
  later event to correct it. It now records the debt like the `!pinned` branch.

**Consume `isFirstLoad` only when ready to position.** It used to be consumed by the
watcher's FIRST fire, which is the `groups = []` that `load()` starts with — so the
"a restore owns first positioning" skip it guards never actually applied to a real
load. The flag is now consumed after the empty/loading/restoring guards.

**Serialize section navigation.** `onNavigateSection` resolves its target with a
DB query, reading the CURRENT anchor to decide where "next" is. Two clicks that
overlap therefore both read the pre-click anchor, resolve to the SAME target, and
the reader advances one section instead of two — and the second `afterNavigate`
assigns `commentaryLineId` a value it already holds, so no watcher fires. Clicks are
chained (capped at a few queued steps) so each starts from where the previous
landed. Reproduced under injected latency: 4 clicks 120ms apart advanced 1 section
with 3 duplicate navigations and 3 × `ABORT_no_scroller`; chained, the same input
advances 4 sections with zero aborts.

**A null "active group" is not a preference.** `captureActivePins` must fall back to
the pin a panel already holds when the live view cannot report one — a panel that is
mid-load has an empty list and no active group, and staging null makes the pin
watcher fall back to the DEFAULT commentator, discarding the reader's choice. Only a
panel that has never had a pin should get the default. Under bridge latency this hit
the non-navigating panel on essentially every consecutive nav click.

**This class of bug is invisible in dev.** The local service answers in single-digit
ms, so consecutive clicks never overlap; the WebView2 host round-trips through
postMessage and they always do. To reproduce in a Playwright probe, delay the
service traffic (`context.route` on the non-vite port) — 700ms is enough.

**Never navigate "book 0":** the sticky nav derives its section-nav target from
`activePinnedGroup`, which is null while a panel is empty. It used to emit
`?? 0`, which pinned the panel to a nonexistent book and then silently refused to
scroll on every later line change. Those buttons are now disabled without an active
group, and `onNavigateSection` rejects a falsy bookId.

**Identity in the DOM:** `.commentary-header` carries `data-book-id`. Its rendered
label includes the TOC path, so the label changes whenever the anchor line moves even
though the commentator has not — compare `data-book-id`, never the text, when asking
"is this panel still on the same commentator?".

**Debugging several panels:** trace flows are slot-tagged (`scrollToGroup:bottom`,
`restore:side`) and each flow keeps its own relative clock, so filter a dump by
`flow` to read one panel and order across panels by `seq`, never by `t`. Every
`scrollToGroup` records a `reason` (`groups-reload`, `panel-mounted`,
`same-line-reclick`, `header-nav-picker`, `already-restored`) — one `BEGIN` per
panel per line switch is correct; two means a stale callback is firing.

## B. Blank slate → default commentary

When a line is selected and there is **no pending pin** (fresh open, no saved
state), the panel must position itself on the book's **default commentator**
(`default_commentator` table, lowest `position`). Implemented in
`usePinnedCommentary`: the `commentaryLineId` watcher falls back to
`defaultCommentatorBookIds[defaultRank]` (bottom panel 0, side panel 1) when no pin was captured; if that book has no group
for this line, the next default that does is used (groups watcher).

Note: line clicks are intentionally inert while the commentary panel is closed
(`onLineClick` checks whether either panel is open), so the blank-slate flow is always
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
  `useGroupsForDisplay` in `useCommentary.ts`, which each panel calls with its own
  pin. (The shared fetch deliberately injects nothing: it would have to pick one
  panel's pin, and injecting there also made `usePinnedCommentary`'s groups watcher
  believe the pin had real links for the line.)
- Placeholder lines use `lineId: -1` and must never be content-backfilled.

## D. Header next/prev navigation scrolls to the target commentary

The commentary header (and `CommentaryHeaderNav`) has next/previous-section buttons
(`onNavigateSection` in `useCommentaryNavigation`). They:

1. Find the next/prev line (normal mode), TOC section (TOC mode), or adjacent line
   (multi-select mode) that has commentary for the given book.
2. Set `selectedLineId`/`commentaryLineId`, scroll the **lines pane** to the target
   line (`scrollToLineId`).
3. Pin the target book in the **navigating panel only** (`setPendingPin` on that
   slot, via `useBookView.onNavigateSection(slot, ...)`), so when the new groups load
   `setupGroupReloadScroll` scrolls that panel to the book's group -- the same path as
   a line click. No manual scroll wiring. The other panel keeps what it was showing.

`scrollToGroup` itself is two-stage like restore: `scrollToIndex` first, then a
MutationObserver correction that re-reads the header's measured `start`. Because
content backfill keeps resizing items for a short while after groups render, the
correction must stay active (bounded, cancellable by a newer scroll request via the
token) until the target's position is stable — a single one-shot correction lands
wrong if a batch arrives right after it.

## E. Lines pane keeps its position when a side commentary opens or closes

All three commentary panels are rendered by one nested layout (a side column on each
side of the text, a SplitPane row beneath it), so opening the bottom panel remounts
nothing. Opening or closing **either** side column does change the text column's
width, which re-wraps every line, so BookViewLinesContent is re-keyed on
`sideColumnsKey` (both columns' open state) and restored deliberately.

The remounted instance re-runs its initial-scroll restore from
`initialLineIndex`/`initialScrollTop`/`initialScrollOffset` — refs frozen at
session-restore time — so without intervention it jumps to the stale position (or
the top when nothing was saved). A pre-flush `watch(sideColumnsKey)` in
BookViewPage captures the live position (`linesContentRef.captureScrollPos()`, old
instance still mounted at pre-flush time) and writes it into those same refs,
clearing `initialLineIndex` so the captured index wins over a TOC-open index.
The capture is skipped while `idbResolved` is still false: session restore reopens
a side panel BEFORE the lines instance has applied its restore, and capturing at
that moment would read `{0,0}` and overwrite the seeded position (the whole view
then lands at the top). The seeded values are exactly what the remounted instance
should restore from, so restore-driven remounts capture nothing.
Dragging either divider does not remount anything and needs no handling.

Known limitation: the restore is (index, offset)-based, and the two widths give
different line heights — the same line is kept at the top, but the sub-line pixel
offset may land slightly differently. A no-remount single-container layout for the
lines pane was tried (2026-07-13) and reverted at the product owner's request.

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
