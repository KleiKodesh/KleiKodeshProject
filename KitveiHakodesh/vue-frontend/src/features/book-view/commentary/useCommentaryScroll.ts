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
    const first = virtualizer().getVirtualItems()[0]
    const el = scrollerEl()
    if (!first || !el) return null
    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, el.scrollTop - first.start),
    }
  }

  function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number): Promise<void> {
    // Use TanStack's scrollToIndex to get the item into view, then apply the sub-item
    // offset. We must wait for the virtualizer to actually render and measure the target
    // item before applying the offset — otherwise item.start is based on estimated sizes
    // (40px headers, 48px lines) which are wrong for variable-height commentary content.
    //
    // Strategy: call scrollToIndex, then poll measurementsCache until the item's measured
    // size stabilises (two consecutive rAFs with the same start value), then apply offset.
    // Cap at MAX_ATTEMPTS to avoid infinite loops if the item never measures.
    const MAX_ATTEMPTS = 12
    let attempts = 0
    let lastStart: number | undefined
    const el = scrollerEl()

    return new Promise<void>((resolve) => {
      function attempt() {
        virtualizer().scrollToIndex(scrollIndex, { align: 'start' })
        requestAnimationFrame(() => {
          const item = virtualizer().measurementsCache.find((m) => m.index === scrollIndex)
          if (!item || !el) {
            if (++attempts < MAX_ATTEMPTS) attempt()
            else resolve()
            return
          }
          // Wait for the measured start to stabilise — if it changed since last rAF,
          // the virtualizer is still correcting positions, try again.
          if (item.start !== lastStart) {
            lastStart = item.start
            if (++attempts < MAX_ATTEMPTS) attempt()
            else resolve()
            return
          }
          // Start is stable — apply the sub-item offset.
          el.scrollTop = item.start + scrollOffset
          resolve()
        })
      }

      attempt()
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
    watch(
      groups,
      async (newGroups) => {
        const pinned = pinnedGroup()
        if (!pinned || !newGroups.length) return
        if (newGroups.some((g) => g.bookId === pinned.bookId)) {
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
