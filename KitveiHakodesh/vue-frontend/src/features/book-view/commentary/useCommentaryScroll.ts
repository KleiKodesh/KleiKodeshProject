import { computed, ref, watch, nextTick } from 'vue'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
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

  function onScroll(emitScroll: (scrollIndex: number, scrollOffset: number) => void) {
    scrollTop.value = scrollerEl()?.scrollTop ?? 0
    const pos = captureScrollPos()
    if (pos) emitScroll(pos.scrollIndex, pos.scrollOffset)
  }

  function scrollToGroup(bookId: number, sectionLabel?: string, subSectionLabel?: string) {
    const el = scrollerEl()
    if (!el) return
    const idx = flatItems().findIndex(
      (item) =>
        item.type === 'header' &&
        item.bookId === bookId &&
        (sectionLabel == null || item.sectionLabel === sectionLabel) &&
        (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
    )
    if (idx === -1) return
    // Use scrollToIndexWithRetry so the scroll lands correctly even when the
    // target header hasn't been measured by the virtualizer yet (e.g. immediately
    // after a commentary reload triggered by section navigation). Plain
    // virtualizer().scrollToIndex() is asynchronous and races with the render
    // cycle — scrollToIndexWithRetry keeps retrying until the item is measured
    // and then sets scrollTop directly.
    scrollToIndexWithRetry(virtualizer() as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>, el, idx, 0, 5, () => {
      scrollTop.value = el.scrollTop
    })
  }

  function scrollToFlatIndex(flatIndex: number) {
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
  ) {
    let isFirstLoad = true
    watch(
      groups,
      async (newGroups) => {
        // Skip the initial load — the session restore watcher handles the first scroll
        // position. Only scroll to the pinned group on subsequent reloads (line navigation).
        if (isFirstLoad) { isFirstLoad = false; return }
        if (!newGroups.length) return
        // Wait for nextTick before reading pinnedGroup — other watchers on the same
        // groups change (e.g. the PIN label-refresh watcher) must flush first so
        // pinnedGroup() reflects the up-to-date sectionLabel/subSectionLabel.
        await nextTick()
        const pinned = pinnedGroup()
        if (!pinned) return
        if (newGroups.some((g: any) => g.bookId === pinned.bookId)) {
          await nextTick()
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
