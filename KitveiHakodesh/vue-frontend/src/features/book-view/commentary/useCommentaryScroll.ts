import { computed, ref, watch, nextTick } from 'vue'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
import { commentaryScrollTrace as trace } from '@/utils/commentaryScrollTrace'
import type { Virtualizer } from '@tanstack/vue-virtual'

const NAV_HEIGHT = 32

/**
 * Manages scroll behavior for commentary: sticky header tracking, scroll position
 * capture/restore, and scroll-to-group navigation.
 */
export function useCommentaryScroll(
  flatItems: () => any[],
  visibleGroups: () => any[],
  virtualizer: () => Virtualizer<any, any>,
  scrollerEl: () => HTMLElement | null,
  /**
   * Which commentary panel this instance drives ('bottom' | 'side'). Only used to
   * tag trace flows: two panels scroll concurrently, and with a shared flow name
   * their BEGIN calls reset each other's relative clock, making a dump unreadable
   * and impossible to attribute. See utils/commentaryScrollTrace.ts.
   */
  traceSlot = 'panel',
) {
  const scrollTop = ref(0)
  const FLOW_SCROLL = `scrollToGroup:${traceSlot}`
  const FLOW_RESTORE = `restore:${traceSlot}`

  const stickyHeader = computed(() => {
    let active: any = null
    for (const m of virtualizer().measurementsCache) {
      const item = flatItems()[m.index]
      if (item?.type !== 'header') continue
      // Switch only when the header's bottom edge has scrolled past the nav
      if (m.end <= scrollTop.value + NAV_HEIGHT + 5) active = item
      else break
    }
    return active
  })

  const activeHeader = computed(
    () =>
      stickyHeader.value ??
      (flatItems().find((i) => i.type === 'header') as any) ??
      null,
  )

  const activePinnedGroup = computed<any>(() => {
    const header = activeHeader.value
    if (!header) return null
    return {
      bookId: header.bookId,
      sectionLabel: header.sectionLabel ?? '',
      subSectionLabel: header.subSectionLabel ?? '',
    }
  })

  // Set to true while restoreCommentaryScrollPos is running — suppresses
  // setupGroupReloadScroll so it doesn't overwrite the in-flight restore scroll.
  let isRestoringScrollPos = false

  // Restore INTENT latch. Set synchronously by the panel the instant it commits to
  // restoring a saved position (before the nextTick + await inside
  // restoreCommentaryScrollPos actually flips isRestoringScrollPos). Without this,
  // setupGroupReloadScroll and the panel-mount restore both wake on the same
  // "groups reloaded" tick and race: whichever watcher callback runs first wins,
  // so the panel non-deterministically lands on the pinned group (scrollToGroup)
  // instead of the saved position. The latch lets the reload-scroll watcher stand
  // down deterministically. Cleared when the restore promise settles.
  let restoreIntentClaimed = false
  function claimRestoreIntent() { restoreIntentClaimed = true }

  function onScroll(emitScroll: (scrollIndex: number, scrollOffset: number) => void) {
    scrollTop.value = scrollerEl()?.scrollTop ?? 0
    const pos = captureScrollPos()
    if (pos) emitScroll(pos.scrollIndex, pos.scrollOffset)
  }

  // Cancellation token for in-flight scrollToGroup calls. Each new call
  // increments this so any previous rAF callbacks know to bail out.
  let scrollToGroupToken = 0

  /**
   * @param reason which code path asked. Several independent paths scroll a panel
   *   to its pinned group (groups reload, panel mount, header nav, re-click) and a
   *   trace that does not say which one is unreadable when they overlap.
   */
  function scrollToGroup(
    bookId: number,
    sectionLabel?: string,
    subSectionLabel?: string,
    reason = 'unknown',
  ) {
    const el = scrollerEl()
    trace.begin(FLOW_SCROLL, {
      reason,
      bookId,
      sectionLabel,
      subSectionLabel,
      hasEl: !!el,
      flatItems: flatItems().length,
      groups: visibleGroups().length,
    })
    if (!el) { trace.push(FLOW_SCROLL, 'ABORT_no_scroller', {}); return }
    const token = ++scrollToGroupToken

    function resolveIndex(): number {
      const items = flatItems()
      const exact = items.findIndex(
        (item) =>
          item.type === 'header' &&
          item.bookId === bookId &&
          (sectionLabel == null || item.sectionLabel === sectionLabel) &&
          (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
      )
      if (exact !== -1 || (sectionLabel == null && subSectionLabel == null)) return exact
      // The labels only disambiguate a book that appears in several sections; the
      // reader asked for a COMMENTATOR. A pin carries the labels captured on the
      // PREVIOUS line, and the same book can sit under a different section there
      // (e.g. COMMENTARY on one line, REFERENCE on the next), so an exact-only
      // match refused to scroll and left the panel on a stale offset - read as
      // "the panel lost its place". Fall back to book identity.
      const byBook = items.findIndex((item) => item.type === 'header' && item.bookId === bookId)
      if (byBook !== -1) trace.push(FLOW_SCROLL, 'resolveIndex_label_fallback', { bookId, idx: byBook })
      return byBook
    }

    const idx = resolveIndex()
    trace.push(FLOW_SCROLL, 'resolveIndex', { idx, flatItems: flatItems().length })
    if (idx < 0) { trace.push(FLOW_SCROLL, 'ABORT_index_not_found', {}); return }

    // Step 1 — bring the target into the rendered range.
    virtualizer().scrollToIndex(idx, { align: 'start' })
    trace.push(FLOW_SCROLL, 'scrollToIndex', { idx, scrollTop: el.scrollTop })

    // Step 2 — correct to the header's measured position, and KEEP correcting for a
    // bounded window: the two-phase commentary loader backfills line content into
    // already-rendered items, so measurements above the header keep changing for a
    // short while after groups render. A one-shot correction lands wrong if a
    // content batch arrives right after it. MutationObserver fires after each DOM
    // mutation so corrections land as soon as measurements do. Cancelled by a newer
    // scrollToGroup/restore (token) or by the user scrolling (wheel/touch/pointer).
    const elCaptured = el
    // Long enough to outlive a cold, section-mode load. Items ABOVE the target
    // render as near-empty stubs and grow as their text, TOC-path labels, notes and
    // highlights arrive; every one of those pushes the target down. At 800ms the
    // window closed while a first load was still filling in, so the panel visibly
    // landed on the pinned commentator and then drifted off it - and nothing
    // re-anchored afterwards. Cancelled the instant the reader takes over (wheel /
    // touch / pointer / key) and by any newer scrollToGroup or restore, so a long
    // window costs nothing when it is not needed.
    const CORRECTION_WINDOW_MS = 6000
    const startedAt = performance.now()
    let done = false
    let observer: MutationObserver | null = null

    function finish() {
      if (done) return
      done = true
      observer?.disconnect()
      elCaptured.removeEventListener('wheel', finish)
      elCaptured.removeEventListener('touchstart', finish)
      elCaptured.removeEventListener('pointerdown', finish)
      elCaptured.removeEventListener('keydown', finish)
    }

    function correct(): void {
      if (done) return
      if (token !== scrollToGroupToken) { trace.push(FLOW_SCROLL, 'correct_cancelled_token', { token, current: scrollToGroupToken }); finish(); return }
      if (performance.now() - startedAt > CORRECTION_WINDOW_MS) { trace.push(FLOW_SCROLL, 'correct_window_expired', {}); finish(); return }
      const currentIdx = resolveIndex()
      if (currentIdx < 0) { trace.push(FLOW_SCROLL, 'correct_no_index', {}); return }
      const m = virtualizer().measurementsCache.find((c: any) => c.index === currentIdx)
      if (!m) { trace.push(FLOW_SCROLL, 'correct_no_measurement', { currentIdx }); return }
      const targetScrollTop = Math.max(0, m.start)
      const before = elCaptured.scrollTop
      if (Math.abs(elCaptured.scrollTop - targetScrollTop) > 2) {
        elCaptured.scrollTop = targetScrollTop
        scrollTop.value = targetScrollTop
        trace.push(FLOW_SCROLL, 'correct_applied', { before, target: targetScrollTop, mStart: m.start, currentIdx })
      } else {
        trace.push(FLOW_SCROLL, 'correct_noop', { scrollTop: before, target: targetScrollTop, currentIdx })
      }
    }

    elCaptured.addEventListener('wheel', finish, { passive: true })
    elCaptured.addEventListener('touchstart', finish, { passive: true })
    elCaptured.addEventListener('pointerdown', finish, { passive: true })
    // The scroller is focusable and arrow keys scroll it; without this the
    // correction would fight keyboard navigation for the whole window.
    elCaptured.addEventListener('keydown', finish, { passive: true })

    requestAnimationFrame(() => {
      if (done) return
      correct()
      if (done) return
      observer = new MutationObserver(() => correct())
      observer.observe(elCaptured, { childList: true, subtree: true, attributes: false })
      setTimeout(finish, CORRECTION_WINDOW_MS + 50)
    })
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    const el = scrollerEl()
    if (!el) return
    // Cancel any in-flight pin correction: jumping to a search match is a newer,
    // competing programmatic scroll, and with the correction window now measured in
    // seconds an un-cancelled one would drag the panel back off the match.
    scrollToGroupToken++

    const reserved = NAV_HEIGHT
    const virt = virtualizer() as any

    // Check if the item is already in the measurements cache
    const m = virt.measurementsCache.find((c: any) => c.index === flatIndex)

    if (m) {
      // Line is already measured by the virtualizer. Scroll to the line top first,
      // then wait for Vue to render the new currentMatchOccurrence (which invalidates
      // the render cache and re-renders the line HTML). Use MutationObserver to detect
      // when the <mark class="current"> actually appears in the DOM, then adjust.

      // Step 1: scroll to line top immediately so the line is visible.
      const targetScrollTop = m.start - reserved - 8
      if (Math.abs(el.scrollTop - targetScrollTop) > 2) {
        el.scrollTop = targetScrollTop
      }
      setCurrentMark(el, flatIndex, occurrence)

      // Step 2: wait for the current mark to appear/move in the DOM, then fine-adjust.
      let settled = false

      function adjustToMark() {
        if (settled || !el) return
        const mark = el.querySelector('mark.search-match.current') as HTMLElement | null
        if (!mark) return false
        const markRect = mark.getBoundingClientRect()
        const scrollerRect = el.getBoundingClientRect()
        const relativeTop = markRect.top - scrollerRect.top
        const relativeBottom = markRect.bottom - scrollerRect.top
        const alreadyVisible =
          relativeTop >= reserved + 4 && relativeBottom <= scrollerRect.height - 4
        if (!alreadyVisible) {
          el.scrollTop += relativeTop - reserved - 8
        }
        return true
      }

      // Try immediately after two rAFs (covers same-line occurrence changes where
      // the mark is already in the DOM and just needs its class updated).
      requestAnimationFrame(() =>
        requestAnimationFrame(() => {
          if (adjustToMark()) {
            settled = true
            return
          }

          // Mark not found yet — the render cache was just invalidated and Vue hasn't
          // re-rendered the line HTML yet. Watch for DOM mutations on the scroller.
          const observer = new MutationObserver(() => {
            if (adjustToMark()) {
              settled = true
              observer.disconnect()
            }
          })
          observer.observe(el, {
            childList: true,
            subtree: true,
            characterData: false,
            attributes: true,
            attributeFilter: ['class'],
          })
          // Safety timeout — disconnect after 500ms regardless.
          setTimeout(() => {
            if (!settled) {
              observer.disconnect()
            }
          }, 500)
        }),
      )
      return
    }

    // Line not yet rendered — use scrollToIndexWithRetry to bring it into range,
    // then scroll to the mark once it's in the DOM.
    scrollToIndexWithRetry(virt, el, flatIndex, reserved, 5, () => {
      // After scrollToIndexWithRetry positions the line, wait for the mark using
      // the same MutationObserver approach.
      const scroller = scrollerEl()
      if (!scroller) return
      setCurrentMark(scroller, flatIndex, occurrence)
      let settled = false

      function adjustToMark() {
        if (!scroller) return false
        const mark = scroller.querySelector('mark.search-match.current') as HTMLElement | null
        if (!mark) return false
        const markRect = mark.getBoundingClientRect()
        const scrollerRect = scroller.getBoundingClientRect()
        const relativeTop = markRect.top - scrollerRect.top
        const relativeBottom = markRect.bottom - scrollerRect.top
        const alreadyVisible =
          relativeTop >= reserved + 4 && relativeBottom <= scrollerRect.height - 4
        if (!alreadyVisible) {
          scroller.scrollTop += relativeTop - reserved - 8
        }
        return true
      }

      requestAnimationFrame(() =>
        requestAnimationFrame(() => {
          if (adjustToMark()) {
            settled = true
            return
          }
          const observer = new MutationObserver(() => {
            if (adjustToMark()) {
              settled = true
              observer.disconnect()
            }
          })
          observer.observe(scroller, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class'],
          })
          setTimeout(() => {
            if (!settled) {
              observer.disconnect()
            }
          }, 500)
        }),
      )
    })
  }

  function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
    const el = scrollerEl()
    if (!el) return null

    const items = virtualizer().getVirtualItems()
    if (!items.length) return null

    const scrollTopValue = el.scrollTop
    const measured = virtualizer().measurementsCache

    let first = measured.find((item) => item.start <= scrollTopValue && scrollTopValue < item.end)

    if (!first) {
      first = items.find((item) => item.start <= scrollTopValue && scrollTopValue < item.end) ?? items[0]
    }

    if (!first) return null

    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, scrollTopValue - first.start),
    }
  }

  /**
   * Keeps the restored position anchored while the two-phase loader backfills text.
   * The one-shot apply in restoreCommentaryScrollPos computes item.start from
   * measurements that are still settling — lines above the target are near-empty
   * stubs whose heights change as their content arrives, shifting the content under
   * the viewport by tens/hundreds of px ("near but not exact" restores). Mirrors
   * scrollToGroup's MutationObserver correction: on every DOM mutation, re-derive
   * the target from the CURRENT measurement of scrollIndex and re-apply. Cancelled
   * by user interaction, by a newer scrollToGroup/restore (token), or when the
   * window elapses.
   */
  const RESTORE_CORRECTION_WINDOW_MS = 2500
  function startRestoreCorrection(scrollIndex: number, scrollOffset: number) {
    const el = scrollerEl()
    if (!el) return
    const token = scrollToGroupToken
    const startedAt = performance.now()
    let done = false
    let observer: MutationObserver | null = null

    function finish() {
      if (done) return
      done = true
      observer?.disconnect()
      el!.removeEventListener('wheel', finish)
      el!.removeEventListener('touchstart', finish)
      el!.removeEventListener('pointerdown', finish)
      el!.removeEventListener('keydown', finish)
    }

    function correct(): void {
      if (done) return
      if (token !== scrollToGroupToken) { finish(); return }
      if (performance.now() - startedAt > RESTORE_CORRECTION_WINDOW_MS) { finish(); return }
      const m = virtualizer().measurementsCache.find((c) => c.index === scrollIndex)
      if (!m) return
      const maxScrollTop = Math.max(0, el!.scrollHeight - el!.clientHeight)
      const target = Math.min(Math.max(0, m.start + scrollOffset), maxScrollTop)
      if (Math.abs(el!.scrollTop - target) > 2) {
        const before = el!.scrollTop
        el!.scrollTop = target
        scrollTop.value = target
        trace.push(FLOW_RESTORE, 'correction_applied', { before, target, itemStart: m.start })
      }
    }

    el.addEventListener('wheel', finish, { passive: true })
    el.addEventListener('touchstart', finish, { passive: true })
    el.addEventListener('pointerdown', finish, { passive: true })
    el.addEventListener('keydown', finish, { passive: true })

    requestAnimationFrame(() => {
      if (done) return
      correct()
      if (done) return
      observer = new MutationObserver(() => correct())
      observer.observe(el, { childList: true, subtree: true, attributes: false })
      setTimeout(finish, RESTORE_CORRECTION_WINDOW_MS + 50)
    })
  }

  function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number): Promise<void> {
    isRestoringScrollPos = true
    restoreIntentClaimed = true
    trace.begin(FLOW_RESTORE, { scrollIndex, scrollOffset, flatItems: flatItems().length, hasEl: !!scrollerEl() })
    // Cancel any in-flight or queued scrollToGroup call — restore takes priority.
    scrollToGroupToken++
    // Set true when the position is actually applied — gates the post-restore
    // correction loop (no point correcting a restore that gave up).
    let applied = false
    return new Promise<void>((resolve) => {
      let attempts = 0
      const MAX_ATTEMPTS = 40

      function startRestore() {
        const el = scrollerEl()
        const itemsLength = flatItems().length

        if (!el || itemsLength === 0) {
          trace.push(FLOW_RESTORE, 'wait_for_items', { attempts, hasEl: !!el, itemsLength })
          if (attempts < MAX_ATTEMPTS) {
            attempts++
            nextTick(() => requestAnimationFrame(startRestore))
            return
          }

          trace.push(FLOW_RESTORE, 'GIVE_UP_no_items', { attempts })
          resolve()
          return
        }

        // Scroll to the target index — this is synchronous for already-measured items
        virtualizer().scrollToIndex(scrollIndex, { align: 'start' })
        trace.push(FLOW_RESTORE, 'scrollToIndex', { scrollIndex, scrollTop: el.scrollTop })

        function tryApplyScroll() {
          const el2 = scrollerEl()
          const item = virtualizer().measurementsCache.find((m) => m.index === scrollIndex)

          if (!el2) {
            if (attempts < MAX_ATTEMPTS) {
              attempts++
              nextTick(() => requestAnimationFrame(tryApplyScroll))
              return
            }

            resolve()
            return
          }

          // Two-phase loader: the target item may be rendered but still awaiting its
          // text (content === ''). Its measured height is a near-empty stub, so
          // applying a pixel offset within it would land in the wrong group. Wait
          // (bounded by MAX_ATTEMPTS) for the viewport-priority fetch to fill it.
          const flatItem = flatItems()[scrollIndex]
          const contentPending =
            scrollOffset > 0 &&
            flatItem?.type === 'line' &&
            flatItem.lineId > 0 &&
            flatItem.content === ''
          if (contentPending && attempts < MAX_ATTEMPTS) {
            trace.push(FLOW_RESTORE, 'content_pending', { attempts, scrollIndex, lineId: flatItem?.lineId })
            attempts++
            nextTick(() => requestAnimationFrame(tryApplyScroll))
            return
          }
          if (contentPending) trace.push(FLOW_RESTORE, 'content_still_pending_at_max', { attempts, scrollIndex })

          const measuredHeight = item && item.start !== undefined && item.end !== undefined ? item.end - item.start : 0
          if (item && measuredHeight > 0) {
            const targetScrollTop = item.start + scrollOffset
            const maxScrollTop = Math.max(0, el2.scrollHeight - el2.clientHeight)
            const desiredScrollTop = Math.min(targetScrollTop, maxScrollTop)
            el2.scrollTop = desiredScrollTop
            trace.push(FLOW_RESTORE, 'apply', {
              attempts, itemStart: item.start, measuredHeight, scrollOffset,
              targetScrollTop, maxScrollTop, desiredScrollTop, clamped: desiredScrollTop < targetScrollTop,
            })

            requestAnimationFrame(() => {
              if (Math.abs(el2.scrollTop - desiredScrollTop) > 1 && attempts < MAX_ATTEMPTS) {
                trace.push(FLOW_RESTORE, 'apply_drifted_retry', { attempts, got: el2.scrollTop, wanted: desiredScrollTop })
                attempts++

                nextTick(() => requestAnimationFrame(tryApplyScroll))
                return
              }

              trace.push(FLOW_RESTORE, 'DONE', { attempts, finalScrollTop: el2.scrollTop, desiredScrollTop })
              applied = true
              resolve()
            })
          } else if (attempts < MAX_ATTEMPTS) {
            // Item not yet measured — retry
            trace.push(FLOW_RESTORE, 'not_measured_retry', { attempts, scrollIndex, hasItem: !!item, measuredHeight })
            attempts++
            nextTick(() => requestAnimationFrame(tryApplyScroll))
          } else {
            // Give up after max attempts
            trace.push(FLOW_RESTORE, 'GIVE_UP_not_measured', { attempts, scrollIndex })
            resolve()
          }
        }

        attempts = 0
        requestAnimationFrame(tryApplyScroll)
      }

      startRestore()
    }).finally(() => {
      // Bump the token to cancel any scrollToGroup that started concurrently with
      // restore and is now in its rAF chain — restore takes priority.
      scrollToGroupToken++
      restoreIntentClaimed = false
      requestAnimationFrame(() => { isRestoringScrollPos = false })
      // Keep the position anchored while backfill re-measures items above it.
      // Started AFTER the token bump so the correction's captured token stays valid
      // until the next scrollToGroup/restore cancels it.
      if (applied) startRestoreCorrection(scrollIndex, scrollOffset)
    })
  }

  const topVisibleFlatIndex = computed(() => {
    const st = scrollTop.value + NAV_HEIGHT
    for (const m of virtualizer().measurementsCache) {
      if (m.end > st) return m.index
    }
    return 0
  })

  // When groups reload, scroll back to the pinned group (captured in parent before selectedLineId changes)
  function setupGroupReloadScroll(
    groups: () => any[],
    pinnedGroup: () => any,
    isLoading: () => boolean,
    hasSavedScrollPos: () => boolean = () => false,
  ) {
    let isFirstLoad = true
    let scrollGeneration = 0
    // Set when a load produced groups but no pin was available yet, so the scroll
    // to the pinned group is still owed. Settled by the pinnedGroup watcher below.
    let pinScrollOwed = false
    watch(
      groups,
      async (newGroups) => {
        // Bump FIRST, before any early return. Every groups change must invalidate a
        // callback that is already mid-flight, INCLUDING a change this callback then
        // ignores (empty list, still loading).
        //
        // A line tap can run useCommentary.load() twice in quick succession (the
        // selectedLineId and selectedLineIds watchers both fire), so the sequence is
        // groups -> [] -> groups. With the bump after the guards, the first callback
        // resumed from its await while the list was empty, still saw its own captured
        // non-empty `newGroups`, and called scrollToGroup on a panel whose scroller
        // had been unmounted by the empty-state branch — ABORT_no_scroller, and the
        // panel silently kept whatever position the virtualizer left it at. That is
        // the "commentary panel loses its place on line switch" report.
        const generation = ++scrollGeneration

        // A new load is starting; any scroll owed by the previous one is void.
        if (!newGroups.length) {
          pinScrollOwed = false
          return
        }
        // Skip partial loads — only scroll when loading is fully complete.
        if (isLoading()) return
        // A restore is running OR the panel has synchronously claimed intent to
        // restore this same reload — stand down so we don't fight it (would land
        // on the pinned group instead of the saved position). See restoreIntentClaimed.
        if (isRestoringScrollPos || restoreIntentClaimed) return
        // Consume the first-load flag only once we are genuinely ready to position
        // the panel. It used to be consumed by the FIRST fire of this watcher, which
        // is the `groups = []` that load() starts with, so the "a restore owns first
        // positioning" skip below never actually applied to a real load.
        if (isFirstLoad) {
          isFirstLoad = false
          // Blank slate (no saved scroll position): nobody else positions the panel
          // on the very first groups load, so scroll to the pinned/default group.
          // With a saved position, the restore path owns first positioning — skip.
          if (hasSavedScrollPos()) {
            pinScrollOwed = false
            return
          }
        }
        // Single nextTick with flush:'post' is sufficient — the virtualizer has
        // the new items after Vue flushes. The previous double-nextTick + rAF added
        // ~50ms of unnecessary scheduling overhead on every line tap.
        await nextTick()
        if (generation !== scrollGeneration) return
        const pinned = pinnedGroup()
        if (!pinned) {
          // The pin has not been decided yet — the default-commentator query is
          // still in flight. Record the debt; the watcher below settles it.
          pinScrollOwed = true
          return
        }
        // Re-read LIVE rather than trusting the captured array: `newGroups` is a
        // snapshot from before the await, and scrollToGroup resolves its index
        // against the live list, so only the live list can decide whether the
        // pinned group is actually there to scroll to.
        const live = groups()
        if (!live.length) return
        if (!live.some((g: any) => g.bookId === pinned.bookId)) return
        // Re-check after the awaited nextTick — a restore may have started/claimed
        // intent while we were yielded.
        if (isRestoringScrollPos || restoreIntentClaimed) return
        pinScrollOwed = false
        scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'groups-reload')
      },
      { flush: 'post' },
    )

    /**
     * A pin that arrives AFTER its groups.
     *
     * usePinnedCommentary awaits a DB query for the book's default commentators, and
     * on the FIRST commentary load of a book that query is still in flight when the
     * groups land: the watcher above finds no pin, returns, and nothing re-triggered
     * it milliseconds later when the pin appeared — so the panel never scrolled to
     * the default commentator at all. Only the first load pays for it (the list is
     * cached from then on), which is exactly why it read as "the FIRST time I open a
     * chapter it doesn't scroll to the default commentary".
     */
    watch(
      pinnedGroup,
      (pinned) => {
        if (!pinScrollOwed || !pinned) return
        if (isLoading() || isRestoringScrollPos || restoreIntentClaimed) return
        const live = groups()
        if (!live.length || !live.some((g: any) => g.bookId === pinned.bookId)) return
        pinScrollOwed = false
        scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'pin-arrived-late')
      },
      { flush: 'post' },
    )
  }

  return {
    scrollTop,
    activeHeader,
    activePinnedGroup,
    onScroll,
    scrollToGroup,
    scrollToFlatIndex,
    captureScrollPos,
    restoreCommentaryScrollPos,
    claimRestoreIntent,
    topVisibleFlatIndex,
    setupGroupReloadScroll,
  }
}
