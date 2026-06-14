# Commentary Pinned Book Scroll — Debug Investigation Notes

This file records the diagnostic logging that was used to trace and fix the pinned commentary scroll bugs. Keep for future reference if similar issues resurface.

## Problems Fixed

### 1. Line click doesn't stay on same commentary book

**Root cause:** `useCommentary`'s `watch(selectedLineId)` fires before `usePinnedCommentary`'s watcher in the same flush. It synchronously sets `groups.value = []`, which makes `activePinnedGroup` (derived from `flatItems`) return null before the pin watcher even runs. The old code tried to read `activePinnedGroup` as a "last known" value but it was already null.

**Fix:** Capture the pinned group imperatively in `onLineSelected` and `onNavigateSection` via `setPendingPin()`, called synchronously before any state changes. The `commentaryLineId` watcher then reads `pendingPin` instead of reading from the scroll state.

### 2. קטע הבא doesn't stay on the commentary book that emitted it

**Root cause:** Same as above — `setPendingPin` was not called for navigation, so the watcher fell back to the default commentator.

**Fix:** `onNavigateSection` in `useBookView.ts` calls `setPendingPin` with the navigated book before delegating to `navigateSection`.

### 3. Auto-scroll sync (useBookViewScrollSync) bypassed setPendingPin

**Root cause:** `useBookViewScrollSync.onLinesScrolled` set `commentaryLineId` via a 120ms timer without going through `onLineSelected`, so `setPendingPin` was never called.

**Fix:** Capture `activePinnedGroup` synchronously in `onLinesScrolled` when the timer starts, then call `setPendingPin` with the captured value inside the timer callback, right before setting `commentaryLineId`.

### 4. scrollToGroup lands on wrong item (stale measurementsCache)

**Root cause A — partial loads:** `setupGroupReloadScroll` fired on partial group loads (single-line fallback before section-range refetch). The virtualizer's measurement cache reflected the old list — positions from the previous groups list were used to scroll to the wrong item.

**Fix:** Added `isLoading()` guard — skip when `loading` is still true. Only fire scroll when the final complete groups are loaded.

**Root cause B — stale estimated positions:** Even after `loading=false`, the virtualizer uses `estimateSize` for unrendered items. Positions computed from estimated sizes don't match real DOM measurements.

**Fix:** Added `nextTick + rAF` delay before calling `scrollToGroup` to give the virtualizer one full render cycle to populate `measurementsCache` with real DOM measurements.

**Root cause C — stale estimated positions after scroll:** Even after the virtualizer reports a scroll as successful (diff=0 in the verify rAF), it continues re-measuring items in subsequent frames as they render for the first time. When estimated sizes differ significantly from real DOM sizes, `m.start` shifts by thousands of pixels. The verify rAF in `scrollToIndexWithRetry` fires too early — before this re-measurement completes — so it reports success at the wrong position.

**Fix:** `scrollToGroup` runs `waitForStableAndVerify` for 8 frames after `scrollToIndexWithRetry` reports success. On each frame it re-reads `m.start` for the target header and re-applies `scrollTop` if drift exceeds 4px. Since `topReserved=-8` makes `gap=0`, the expected position is simply `m.start`.

### 5. Multiple concurrent scrollToGroup calls fighting each other

**Root cause:** `setupGroupReloadScroll` fired multiple times for the same line (partial + final load) before the generation counter was added; and separately, `CommentaryHeaderNav.handleSelect` fired spuriously when the virtualizer re-rendered the `<datalist>` options during a scroll.

**Fix — generation counter:** Each watcher invocation increments `scrollGeneration`. After each `await nextTick()`, checks if the generation is still current — bails if a newer invocation superseded it.

**Fix — cancellation token:** `scrollToGroup` increments `scrollToGroupToken` on each call. The `isCancelled` function passed to `scrollToIndexWithRetry` checks whether the token is still current — aborts all rAF callbacks if a newer `scrollToGroup` call started.

**Fix — handleSelect spurious fire:** `CommentaryHeaderNav.handleSelect` guarded with `userHasTyped` flag — only processes `@change` if a real `@input` event preceded it. Datalist re-renders don't fire `@input`.

### 6. Session restore / tab-switch restore overwritten by scrollToGroup

**Root cause:** `restoreCommentaryScrollPos` is async (rAF retries). `setupGroupReloadScroll` started its `nextTick + rAF` chain concurrently. Even though `isRestoringScrollPos` was true during the setup phase, by the time `scrollToGroup` actually fired, restore had already completed and `isRestoringScrollPos` was false.

**Fix:** In `restoreCommentaryScrollPos`.finally(), bump `scrollToGroupToken` before clearing `isRestoringScrollPos`. Any `scrollToGroup` call that started before restore completed has a stale token and gets cancelled by `isCancelled()`.

### 7. Panel close/reopen loses scroll position

**Root cause:** The `commentaryVisible` watcher in `useBookView.ts` explicitly cleared `commentaryScrollIndex` and `commentaryScrollOffset` to `null` when the panel closed, under the mistaken assumption that scroll restore was only needed across page reloads. Since `CommentaryView` is v-if'd, every close/reopen is a full remount — the scroll position must be re-applied on every reopen, not just on initial session restore.

Additionally, `lastRestoredCommentaryKey` was not reset on close. Even if the scroll values were present, the dedup guard would have blocked the restore since the key matched the previous restore.

**Fix:** On panel close, keep `commentaryScrollIndex`/`commentaryScrollOffset` as-is (they continue to be updated by `onCommentaryScroll` while the panel is open). Reset `lastRestoredCommentaryKey` to `null` on close so the restore always runs on reopen against a freshly mounted `CommentaryView`.

---

## Key Log Prefixes Used

- `[PinnedCommentary]` — `useBookViewPinnedCommentary.ts`: setPendingPin, commentaryLineId watcher, groups watcher
- `[CommentaryScroll]` — `useCommentaryScroll.ts`: scrollToGroup calls, setupGroupReloadScroll watcher, restore start/done
- `[scrollToIndexWithRetry]` — `scrollToIndexWithRetry.ts`: each rAF attempt, verify, drift, confirm
- `[BookView]` — `useBookView.ts`: onNavigateSection, restore watcher

## Key Diagnostic Patterns

**MISMATCH log:** `scrollToGroup DONE ... match=false` — scroll confirmed at target scrollTop but the header visible at that position is not the target book. Causes: stale measurementsCache, flatItems changed between scroll and callback, detection threshold issue.

**Headers dump:** `headers in flatItems: [idx]bookId=N start=X end=Y ...` — full list of all headers with their measured positions at callback time. Used to identify whether the target book is present, where it is, and why the detection failed.

**Generation superseded:** `setupGroupReloadScroll gen=N superseded` — a newer groups change arrived while the watcher was awaiting, so this invocation correctly aborted.

**Token cancelled:** `scrollToGroup token=N CANCELLED` — a newer `scrollToGroup` (or `restoreCommentaryScrollPos`) invalidated this call's token.
