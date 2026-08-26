/**
 * Manages scroll position for the book view lines scroller.
 *
 * Scroll save:
 * - captureScrollPos saves firstVisible.index (not the overscan item) and the pixel
 *   offset within that item. If the gap between scrollTop and firstVisible.start exceeds
 *   2000px the virtualizer hasn't re-rendered after a programmatic scroll — skip the save.
 *
 * Scroll restore — two stages, virtualizer API only, never touch scrollTop directly:
 * Stage 1 — scrollToIndex(targetIndex, 'start') immediately when placeholders are allocated.
 *   Uses estimated item heights but gets the item on screen fast.
 * Stage 2 — once the target line's chunk loads (real DOM heights), scrollToIndex again.
 *   Tracks item.start in a rAF loop; whenever it shifts (background chunks above load and
 *   push item.start down), re-issues scrollToIndex. Exits once stable for 3 consecutive frames.
 *   After stabilization, applies the saved sub-line pixel offset via scrollToOffset — but only
 *   if the offset fits within the item's actual height (guards against stale saves).
 *   A post-stabilization MutationObserver keeps tracking for a bounded window (10s,
 *   cancelled by user input): late chunks above the target re-measure when they
 *   RENDER, which arrives as a DOM mutation, not as a lines-array change - a
 *   watch on lines() slept through exactly those shifts.
 */
import { watch, nextTick, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Virtualizer, VirtualItem } from '@tanstack/vue-virtual'
import type { LineItem } from './useBookViewLinesTable'
import { SCROLL_LANDING_GAP_PX } from '@/utils/scrollToIndexWithRetry'
import { SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'
import { COMMENTARY_SLOTS } from '../bookViewTypes'
import type {
  CommentaryPanelLiveStates,
  CommentaryPanelPersistStates,
  CommentarySlot,
  CommentaryTreeState,
  CommentaryTreeStatePersist,
  TocPersistState,
} from '../bookViewTypes'
import type { useTabStore } from '@/stores/tabStore'
import type { useBookViewStore } from '@/stores/bookViewStore'

// ── Props shape accepted by this composable ───────────────────────────────────

export interface BookViewLinesScrollProps {
  initialLineIndex?: number
  initialScrollIndex?: number
  initialScrollOffset?: number
  flashLineOnOpen?: boolean
  searchHighlightLineIndex?: number
  idbResolved?: boolean
  /** True when EITHER commentary panel is open. */
  commentaryVisible?: boolean
  /**
   * Both commentary panels' persistable state, read at save time.
   *
   * A getter rather than eight pass-through props: this composable owns none of it
   * and only ferries it to IDB alongside the scroll position it does own. The
   * panels themselves own it - see useCommentaryPanelSlot.
   */
  commentaryPersistState?: () => CommentaryPanelLiveStates
  /** The TOC panel's state, read at save time like the commentary panels'. */
  tocPersistState?: () => TocPersistState
  /**
   * The alternate version the text is being read in, by versionTitle — read at save
   * time like the panels above. null = the book's merged text.
   */
  versionPersistState?: () => string | null
  /**
   * The alternate version being rendered, or null for the merged text. Part of the
   * line renderer's cache key — a swap changes a line's text while its index and id
   * stay the same, which the cache would otherwise read as "nothing to re-render".
   */
  activeVersionId?: number | null
  selectedLineId?: number | null
  searchBarVisible?: boolean
}

export interface BookViewLinesScrollStoreRefs {
  tabStore: ReturnType<typeof useTabStore>
  bookViewStore: ReturnType<typeof useBookViewStore>
  autoSelectTopLine: Ref<boolean>
  zoom: Ref<number>
  tabId: string
  bookId: number
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLinesScroll(
  scrollerEl: Ref<HTMLElement | null>,
  virtualizer: () => Virtualizer<Element, Element>,
  virtualItems: () => VirtualItem[],
  lines: () => LineItem[],
  props: BookViewLinesScrollProps,
  storeRefs: BookViewLinesScrollStoreRefs,
  emit: (
    event: 'scrolled',
    firstVisible: number,
    firstFull: number,
    isUserScroll: boolean,
  ) => void,
  prioritise: (lineIndex: number) => void,
) {
  const { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId } = storeRefs

  // ── Scroll capture ──────────────────────────────────────────────────────────

  function captureScrollPos() {
    const items = virtualItems()
    const first = items[0]
    if (!first || !scrollerEl.value) return null
    const scrollTop = scrollerEl.value.scrollTop
    const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? first

    // If firstVisible is more than 2000px above scrollTop the virtualizer hasn't
    // re-rendered after a programmatic scroll yet — skip this save to avoid
    // persisting a stale overscan-item position.
    if (scrollTop - firstVisible.start > 2000) return null

    return {
      scrollIndex: firstVisible.index,
      scrollOffset: Math.max(0, scrollTop - firstVisible.start),
    }
  }

  // ── Scroll restore (public wrapper) ────────────────────────────────────────

  let saveSuppressTimer: ReturnType<typeof setTimeout> | null = null
  let saveSuppressed = false

  function restoreScrollPos(lineIndex: number, _scrollOffset = 0) {
    virtualizer().scrollToIndex(lineIndex, { align: 'start' })
  }

  // ── Initial scroll on load ──────────────────────────────────────────────────

  let cancelStabilize: (() => void) | null = null
  // Vue does not drop queued nextTick callbacks on unmount, and cancelStabilize is only
  // assigned INSIDE the one below — so an unmount in between leaves onBeforeUnmount with
  // nothing to call while the callback still runs and creates a watch nobody owns.
  let scrollTeardown = false
  // The post-restore correction window installs a MutationObserver and three listeners on
  // the scroller. They are only torn down by its own 10s timer, so without a handle up here
  // they stay attached to a detached element for up to 10s after unmount. Idempotent.
  let finishPostRestore: (() => void) | null = null
  // Timer for the deep-link line flash; cleared on cancel/unmount so it never fires
  // against a stale element.
  let flashTimer: number | null = null
  {
    let restored = false
    let stopContentWatch: (() => void) | null = null
    let stop: (() => void) | null = null

    stop = watch(
      () => [lines(), props.initialScrollIndex] as const,
      ([currentLines]) => {
        if (!currentLines.length) return
        const targetIndex = props.initialLineIndex ?? props.initialScrollIndex
        if (targetIndex == null) return
        if (targetIndex >= currentLines.length) return
        stop?.()
        stop = null
        prioritise(targetIndex)

        // Capture as const number — TypeScript loses the narrowing across nextTick closures.
        const target: number = targetIndex

        nextTick(() => {
          if (restored || scrollTeardown) return
          restored = true

          let cancelled = false
          cancelStabilize = () => {
            cancelled = true
            saveSuppressed = false
            if (flashTimer != null) { clearTimeout(flashTimer); flashTimer = null }
            // Created inside this nextTick, so it is outside the effect scope and unmount
            // won't dispose it. The `cancelled` flag already neuters its effects, but the
            // watcher itself would stay subscribed to lines() and retain the closure.
            stopContentWatch?.()
            stopContentWatch = null
            // Disconnects the observer and drops the scroller listeners now instead of
            // leaving them on a detached element until the 10s window expires.
            finishPostRestore?.()
            finishPostRestore = null
          }
          saveSuppressed = true

          // Stage 1 — estimated heights, gets item on screen immediately.
          virtualizer().scrollToIndex(target, { align: 'start' })

          // Stage 2 — once the target chunk loads, re-issue scrollToIndex with real heights,
          // then track item.start until stable, then apply the saved sub-line offset.
          stopContentWatch = watch(
            () => lines()[target]?.content,
            (content) => {
              if (content == null) return
              stopContentWatch?.()
              if (cancelled) return
              nextTick(() => {
                if (cancelled) return

                virtualizer().scrollToIndex(target, { align: 'start' })

                let lastStart = virtualizer().measurementsCache.find((m) => m.index === target)?.start ?? -1
                let stableFrames = 0
                let trackAttempts = 0
                const offsetToApply = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0

                function trackAndCorrect() {
                  if (cancelled) return
                  const fresh = virtualizer().measurementsCache.find((m) => m.index === target)
                  const freshStart = fresh?.start ?? lastStart
                  if (freshStart !== lastStart) {
                    lastStart = freshStart
                    stableFrames = 0
                    virtualizer().scrollToIndex(target, { align: 'start' })
                  } else {
                    stableFrames++
                  }

                  if (stableFrames >= 3) {
                    // Apply saved sub-line offset — only if it fits within the item's
                    // actual height (guards against stale saves from old code).
                    if (offsetToApply > 0) {
                      const offsetCache = virtualizer().measurementsCache.find((m) => m.index === target)
                      if (offsetCache && offsetToApply < offsetCache.end - offsetCache.start) {
                        saveSuppressed = true
                        virtualizer().scrollToOffset(offsetCache.start + offsetToApply)
                        requestAnimationFrame(() => { if (!cancelled) saveSuppressed = false })
                      } else {
                        saveSuppressed = false
                      }
                    } else {
                      saveSuppressed = false
                    }

                    // Post-stabilization: late chunks above the target re-measure when
                    // they render and push the content under the viewport. The shifts
                    // arrive with DOM mutations, NOT with lines-array changes (backfill
                    // mutates line objects in place), so a watch on lines() slept through
                    // them - the drift the reader saw as "restored to the wrong place".
                    // Mirror the commentary panels' startRestoreCorrection instead:
                    // observe the scroller and re-anchor whenever the target's measured
                    // start moves, for a bounded window. The reader taking over (wheel /
                    // touch / pointer) cancels immediately - their scroll wins.
                    const POST_RESTORE_WINDOW_MS = 10000
                    const postEl = scrollerEl.value
                    let postLastStart = lastStart
                    let postDone = false
                    let postObserver: MutationObserver | null = null
                    const postStartedAt = performance.now()

                    function postFinish() {
                      if (postDone) return
                      postDone = true
                      postObserver?.disconnect()
                      postEl?.removeEventListener('wheel', postFinish)
                      postEl?.removeEventListener('touchstart', postFinish)
                      postEl?.removeEventListener('pointerdown', postFinish)
                    }

                    function postCorrect() {
                      if (postDone) return
                      if (cancelled || performance.now() - postStartedAt > POST_RESTORE_WINDOW_MS) {
                        postFinish()
                        return
                      }
                      const postCache = virtualizer().measurementsCache.find((m) => m.index === target)
                      if (!postCache) return
                      if (postCache.start === postLastStart) return
                      postLastStart = postCache.start
                      virtualizer().scrollToIndex(target, { align: 'start' })
                      if (offsetToApply > 0) {
                        requestAnimationFrame(() => {
                          if (cancelled || postDone) return
                          const driftCache = virtualizer().measurementsCache.find((m) => m.index === target)
                          if (driftCache) virtualizer().scrollToOffset(driftCache.start + offsetToApply)
                        })
                      }
                    }

                    if (postEl) {
                      postEl.addEventListener('wheel', postFinish, { passive: true })
                      postEl.addEventListener('touchstart', postFinish, { passive: true })
                      postEl.addEventListener('pointerdown', postFinish, { passive: true })
                      postObserver = new MutationObserver(() => postCorrect())
                      postObserver.observe(postEl, { childList: true, subtree: true, attributes: false })
                      setTimeout(postFinish, POST_RESTORE_WINDOW_MS + 100)
                      finishPostRestore = postFinish // so teardown can close the window early
                    }

                    // FTS: scroll to the highlighted mark once positioned.
                    if (props.searchHighlightLineIndex != null) {
                      let markAttempts = 0
                      function tryScrollToMark() {
                        if (cancelled || !scrollerEl.value) return
                        const mark = scrollerEl.value.querySelector('mark.search-match') as HTMLElement | null
                        if (mark) { mark.scrollIntoView({ block: 'center' }); return }
                        if (++markAttempts < 30) requestAnimationFrame(tryScrollToMark)
                      }
                      requestAnimationFrame(tryScrollToMark)
                    }

                    // Deep-link open (otzaria:// / zayit://): momentarily flash the
                    // target line's background so the user sees where the link landed.
                    // The row may not be in the DOM yet (virtualizer), so poll by
                    // [data-index] with the same rAF-retry pattern as the mark scroll.
                    if (props.flashLineOnOpen) {
                      let flashAttempts = 0
                      function tryFlashLine() {
                        if (cancelled || !scrollerEl.value) return
                        const row = scrollerEl.value.querySelector(
                          `[data-index="${target}"] .line`,
                        ) as HTMLElement | null
                        if (row) {
                          row.classList.add('flash-open')
                          // Remove after the animation finishes (must match the CSS
                          // animation duration, ~3.5s) so the class doesn't linger and
                          // re-fire on virtualizer recycle. Cleared on cancel/unmount.
                          flashTimer = window.setTimeout(() => {
                            row.classList.remove('flash-open')
                            flashTimer = null
                          }, 3600)
                          return
                        }
                        if (++flashAttempts < 30) requestAnimationFrame(tryFlashLine)
                      }
                      requestAnimationFrame(tryFlashLine)
                    }
                    return
                  }

                  if (++trackAttempts < 200) {
                    requestAnimationFrame(trackAndCorrect)
                  } else {
                    saveSuppressed = false
                  }
                }

                requestAnimationFrame(trackAndCorrect)
              })
            },
            { immediate: true, flush: 'post' },
          )

          // Safety: clear saveSuppressed if content never loads.
          setTimeout(() => { if (!cancelled) saveSuppressed = false }, 5000)

          // No manual `scrolled` emit here any more. Restore scrolls the scroller,
          // and onScroll now reports every scroll — this used to be a hand-rolled
          // copy of onScroll's index maths, needed only because the old latch
          // swallowed the real event.
          scrollerEl.value?.focus({ preventScroll: true })
        })
      },
      { flush: 'post', immediate: true },
    )

    watch(
      () => [lines(), props.idbResolved] as const,
      ([currentLines, resolved]) => {
        if (!currentLines.length || restored || !resolved) return
        if (props.initialLineIndex == null && props.initialScrollIndex == null) {
          stop?.()
          stop = null
          nextTick(() => scrollerEl.value?.focus({ preventScroll: true }))
        }
      },
      { flush: 'post' },
    )
  }

  // ── Persist scroll position ─────────────────────────────────────────────────

  let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null

  /**
   * Copy for storage: the reader's search input only — see CommentaryTreeStatePersist.
   *
   * There used to be a `lastValidFilterState` backfill here, because a panel's
   * visibilityList empties whenever its tree closes and saving the live value blindly
   * would overwrite a good filter with an empty one. That list is no longer persisted
   * at all, and searchQuery has no such empty phase, so the backfill is gone with it.
   */
  function cloneFilterState(state: CommentaryTreeState): CommentaryTreeStatePersist {
    return {
      searchQuery: state.searchQuery,
      tokens: [...state.tokens],
    }
  }

  /** Every panel's state for this save. */
  function commentaryPanelsForSave(): CommentaryPanelPersistStates {
    const live = props.commentaryPersistState?.() ?? {}
    const result: CommentaryPanelPersistStates = {}
    for (const slot of COMMENTARY_SLOTS) {
      const panel = live[slot]
      if (!panel) continue
      result[slot] = {
        ...panel,
        filterState: panel.filterState ? cloneFilterState(panel.filterState) : undefined,
      }
    }
    return result
  }

  function savePos() {
    // Mid-flight programmatic positions are transient — persisting one would
    // overwrite the reader's real place with wherever a jump happened to be
    // passing through.
    if (saveSuppressed) return
    const position = lastKnownPos ?? captureScrollPos()
    if (!position) return

    const commentaryPanels = commentaryPanelsForSave()
    const toc = props.tocPersistState?.()
    const versionTitle = props.versionPersistState?.() ?? null

    tabStore.setBookViewState(tabId, bookId, {
      ...position,
      selectedLineId: props.selectedLineId,
      zoom: zoom.value,
      autoSelectTopLine: autoSelectTopLine.value,
      commentaryPanels,
      toc,
      versionTitle,
    })
    tabStore.setLastReadPos(bookId, {
      ...position,
      selectedLineId: props.selectedLineId,
      // Zoom rides along here too, so reopening a book in a new tab restores the
      // text zoom and not just the commentary panels'.
      zoom: zoom.value,
      commentaryPanels,
      toc,
      versionTitle,
    })
  }

  watch(
    () => props.commentaryVisible,
    (visible) => { if (!visible) savePos() },
  )

  useEventListener(document, 'visibilitychange', () => {
    if (document.visibilityState === 'hidden') savePos()
  })
  useEventListener(window, 'beforeunload', savePos)

  onBeforeUnmount(() => {
    scrollTeardown = true
    cancelStabilize?.()
    saveSuppressed = false
    if (saveSuppressTimer) {
      clearTimeout(saveSuppressTimer)
      saveSuppressTimer = null
    }
    savePos()
  })

  // ── Scroll handler ──────────────────────────────────────────────────────────

  // Reports the scroller's position on EVERY scroll, programmatic ones included.
  //
  // This deliberately does not consult saveSuppressed. That flag means "do not
  // persist a reading position right now", which is a statement about saving, not
  // about where the reader actually is. The `scrolled` consumers — the breadcrumb
  // (tocPath), the active TOC entry, auto-select — track the CURRENT position and
  // are just as right after a programmatic jump as after a wheel. Gating them here
  // is what forced every programmatic caller (TOC click, section nav, restore,
  // search) to re-derive and re-publish the position itself.
  //
  // Consumers that must ignore a jump's in-transit hops still can: the TOC sync
  // waits for programmatic events to settle before syncing (see
  // useBookViewScrollSync), keyed off the isUserScroll argument below.
  /**
   * Derives which line is at the top of the view RIGHT NOW: the first line
   * meaningfully visible below the top of the view — below the search bar when it
   * overlays the scroller, and past a sliver allowance slightly larger than the
   * landing gap.
   *
   * The sliver allowance fixes the TOC flicker that appeared only at section
   * boundaries: a programmatic landing deliberately stops SCROLL_LANDING_GAP_PX
   * above its target, leaving that many pixels of the PREVIOUS line at the top.
   * Counting that sliver made this derivation report the previous line, whose TOC
   * entry is the previous section — a click on "daf 142a" answered by a sync
   * announcing "daf 141b". For reader scrolling the allowance is imperceptible:
   * the active section flips a few pixels later than it used to.
   *
   * The fallback chain matters at the very bottom of the book, where the last
   * line itself may have less than the allowance visible: fall back to the
   * sliver-counting find rather than items[0], which is the top overscan row.
   *
   * CAVEAT — exposed as readCurrentPosition for exactly this: during a long jump
   * the scroll event fires BEFORE the virtualizer re-renders, so the items here
   * still describe the window around the OLD position and the derivation returns
   * old-position garbage (live-verified: every backward TOC jump reported the
   * previous location minus one row). Consumers acting LATER — the scroll sync's
   * settle pass — must re-derive fresh instead of trusting an event-time value.
   */
  function readCurrentPosition(): { lineIndex: number; fullLineIndex: number } | null {
    if (!scrollerEl.value) return null
    const scrollTop = scrollerEl.value.scrollTop
    const items = virtualItems()
    const inset = props.searchBarVisible ? SEARCH_BAR_INSET_PX : 0
    const visibleTop = scrollTop + inset + SCROLL_LANDING_GAP_PX + 4
    const firstVisible =
      items.find((v) => v.start + v.size > visibleTop) ??
      items.find((v) => v.start + v.size > scrollTop) ??
      items[0]
    if (!firstVisible) return null
    const lineIndex = firstVisible.index
    // Fully visible means fully visible BELOW the search bar too — a short line
    // hidden entirely under the bar is not something auto-select should act on.
    const firstFull = items.find((v) => v.start >= scrollTop + inset) ?? firstVisible
    return { lineIndex, fullLineIndex: firstFull.index }
  }

  function onScroll() {
    if (!scrollerEl.value) return
    const position = readCurrentPosition()
    if (!position) return
    const { lineIndex, fullLineIndex } = position
    lastKnownPos = captureScrollPos()
    prioritise(lineIndex)
    // The third argument says whether the READER moved the view, as opposed to a
    // jump the app performed (TOC click, search match, restore). saveSuppressed
    // already draws exactly that line and every programmatic scroller sets it, so
    // it is reused here rather than tracked a second time — and unlike a `wheel`
    // listener it also catches scrollbar drags and keyboard scrolling.
    emit('scrolled', lineIndex, fullLineIndex, !saveSuppressed)
  }

  // ── Position-save suppression ───────────────────────────────────────────────

  function suppressPositionSave() {
    saveSuppressed = true
    if (saveSuppressTimer) clearTimeout(saveSuppressTimer)
    saveSuppressTimer = setTimeout(() => { saveSuppressed = false }, 300)
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * Closes the post-restore correction window early. Restore keeps re-anchoring
   * its OWN target for up to 10s while late chunks shift it — right up until the
   * reader goes somewhere else. A new programmatic jump is exactly that, and
   * without this the two positioners fight: the jump lands, a late chunk shifts
   * the old restore target, and the observer yanks the view back to the restored
   * position.
   */
  function cancelRestoreCorrection() {
    finishPostRestore?.()
    finishPostRestore = null
  }

  return {
    captureScrollPos,
    restoreScrollPos,
    readCurrentPosition,
    cancelRestoreCorrection,
    isPositionSaveSuppressed: () => saveSuppressed,
    suppressPositionSave,
    onScroll,
  }
}
