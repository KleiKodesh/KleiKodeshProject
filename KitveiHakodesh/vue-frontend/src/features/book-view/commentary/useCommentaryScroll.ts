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
    const idx = flatItems().findIndex(
      (item) =>
        item.type === 'header' &&
        item.bookId === bookId &&
        (sectionLabel == null || item.sectionLabel === sectionLabel) &&
        (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
    )
    if (idx === -1) return
    virtualizer().scrollToIndex(idx, { align: 'start' })
    // scrollToIndex is synchronous for already-measured items — read scrollTop immediately
    scrollTop.value = scrollerEl()?.scrollTop ?? 0
    // also update after paint in case the browser deferred the scroll
    requestAnimationFrame(() => {
      scrollTop.value = scrollerEl()?.scrollTop ?? 0
    })
  }

  function scrollToFlatIndex(flatIndex: number) {
    const el = scrollerEl()
    if (!el) return
    scrollToIndexWithRetry(virtualizer() as any, el, flatIndex, -52)
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
