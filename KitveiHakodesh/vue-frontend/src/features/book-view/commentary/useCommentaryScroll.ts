import { computed, ref, watch, nextTick } from 'vue'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
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
) {
  const scrollTop = ref(0)

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

  function onScroll(emitScroll: (scrollIndex: number, scrollOffset: number) => void) {
    scrollTop.value = scrollerEl()?.scrollTop ?? 0
    const pos = captureScrollPos()
    if (pos) emitScroll(pos.scrollIndex, pos.scrollOffset)
  }

  // Cancellation token for in-flight scrollToGroup calls. Each new call
  // increments this so any previous rAF callbacks know to bail out.
  let scrollToGroupToken = 0

  function scrollToGroup(bookId: number, sectionLabel?: string, subSectionLabel?: string) {
    const el = scrollerEl()
    if (!el) return
    const token = ++scrollToGroupToken

    function resolveIndex(): number {
      return flatItems().findIndex(
        (item) =>
          item.type === 'header' &&
          item.bookId === bookId &&
          (sectionLabel == null || item.sectionLabel === sectionLabel) &&
          (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
      )
    }

    const idx = resolveIndex()
    if (idx < 0) return

    // Step 1 — bring the target into the rendered range.
    virtualizer().scrollToIndex(idx, { align: 'start' })

    // Step 2 — as soon as the virtualizer measures the target item (which happens
    // when it renders the item into the DOM), read the exact position and apply it.
    // MutationObserver fires synchronously after each DOM mutation, so we correct
    // as soon as measurements land — no rAF polling loop needed.
    const elCaptured = el
    let applied = false

    function tryApply(): boolean {
      if (token !== scrollToGroupToken) return true // cancelled — treat as done
      const currentIdx = resolveIndex()
      if (currentIdx < 0) return false
      const m = virtualizer().measurementsCache.find((c: any) => c.index === currentIdx)
      if (!m) return false
      const targetScrollTop = Math.max(0, m.start)
      elCaptured.scrollTop = targetScrollTop
      scrollTop.value = targetScrollTop
      return true
    }

    // Fast path — item already measured (e.g. already in the rendered window).
    requestAnimationFrame(() => {
      if (applied) return
      if (tryApply()) {
        applied = true
        return
      }

      // Slow path — item not yet measured. Watch DOM mutations and apply the moment
      // the measurement lands in the cache.
      const observer = new MutationObserver(() => {
        if (applied) return
        if (tryApply()) {
          applied = true
          observer.disconnect()
        }
      })
      observer.observe(elCaptured, { childList: true, subtree: true, attributes: false })

      // Safety timeout.
      setTimeout(() => {
        if (!applied) {
          applied = true
          observer.disconnect()
        }
      }, 500)
    })
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    const el = scrollerEl()
    if (!el) return

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

  function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number): Promise<void> {
    isRestoringScrollPos = true
    // Cancel any in-flight or queued scrollToGroup call — restore takes priority.
    scrollToGroupToken++
    return new Promise<void>((resolve) => {
      let attempts = 0
      const MAX_ATTEMPTS = 20

      function startRestore() {
        const el = scrollerEl()
        const itemsLength = flatItems().length

        if (!el || itemsLength === 0) {
          if (attempts < MAX_ATTEMPTS) {
            attempts++
            nextTick(() => requestAnimationFrame(startRestore))
            return
          }

          resolve()
          return
        }

        // Scroll to the target index — this is synchronous for already-measured items
        virtualizer().scrollToIndex(scrollIndex, { align: 'start' })

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

          const measuredHeight = item && item.start !== undefined && item.end !== undefined ? item.end - item.start : 0
          if (item && measuredHeight > 0) {
            const targetScrollTop = item.start + scrollOffset
            const maxScrollTop = Math.max(0, el2.scrollHeight - el2.clientHeight)
            const desiredScrollTop = Math.min(targetScrollTop, maxScrollTop)
            el2.scrollTop = desiredScrollTop

            requestAnimationFrame(() => {
              if (Math.abs(el2.scrollTop - desiredScrollTop) > 1 && attempts < MAX_ATTEMPTS) {
                attempts++

                nextTick(() => requestAnimationFrame(tryApplyScroll))
                return
              }

              resolve()
            })
          } else if (attempts < MAX_ATTEMPTS) {
            // Item not yet measured — retry
            attempts++
            nextTick(() => requestAnimationFrame(tryApplyScroll))
          } else {
            // Give up after max attempts
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
      requestAnimationFrame(() => { isRestoringScrollPos = false })
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
  ) {
    let isFirstLoad = true
    let scrollGeneration = 0
    watch(
      groups,
      async (newGroups) => {
        if (isFirstLoad) { isFirstLoad = false; return }
        if (!newGroups.length) return
        // Skip partial loads — only scroll when loading is fully complete.
        if (isLoading()) return
        if (isRestoringScrollPos) return
        const generation = ++scrollGeneration
        // Single nextTick with flush:'post' is sufficient — the virtualizer has
        // the new items after Vue flushes. The previous double-nextTick + rAF added
        // ~50ms of unnecessary scheduling overhead on every line tap.
        await nextTick()
        if (generation !== scrollGeneration) return
        const pinned = pinnedGroup()
        if (!pinned) return
        const found = newGroups.some((g: any) => g.bookId === pinned.bookId)
        if (found) {
          if (isRestoringScrollPos) return
          scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel)
        }
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
    topVisibleFlatIndex,
    setupGroupReloadScroll,
  }
}
