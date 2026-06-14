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

  // Single scan over measurementsCache per scroll tick — computes both the sticky
  // header and the top visible flat index in one pass instead of two.
  const _scrollState = computed(() => {
    const st = scrollTop.value
    let activeHeader: any = null
    let topIndex = 0
    for (const m of virtualizer().measurementsCache) {
      const item = flatItems()[m.index]
      if (item?.type === 'header' && m.end <= st + NAV_HEIGHT + 5) activeHeader = item
      if (m.end > st + NAV_HEIGHT && topIndex === 0) topIndex = m.index
    }
    return { activeHeader, topIndex }
  })

  const activeHeader = computed(
    () =>
      _scrollState.value.activeHeader ??
      (flatItems().find((i) => i.type === 'header') as any) ??
      null,
  )

  const topVisibleFlatIndex = computed(() => _scrollState.value.topIndex)

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

    // Resolve the flat index fresh on each attempt — flatItems can change between
    // the time scrollToGroup is called and the rAF fires (new groups load completes,
    // item positions shift). A stale index scrolls to the wrong item.
    function resolveIndex(): number {
      return flatItems().findIndex(
        (item) =>
          item.type === 'header' &&
          item.bookId === bookId &&
          (sectionLabel == null || item.sectionLabel === sectionLabel) &&
          (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
      )
    }

    scrollToIndexWithRetry(
      virtualizer() as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
      el, resolveIndex, -8, 5,
      () => {
        scrollTop.value = el.scrollTop
      },
      () => token !== scrollToGroupToken,
    )
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    const el = scrollerEl()
    if (!el) return

    const reserved = NAV_HEIGHT
    const virt = virtualizer() as any

    function applyCurrentMark() {
      if (el) setCurrentMark(el, flatIndex, occurrence)
    }

    function adjustToMark(scroller: HTMLElement): boolean {
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

    const m = virt.measurementsCache.find((c: any) => c.index === flatIndex)

    if (m) {
      const targetScrollTop = m.start - reserved - 8
      if (Math.abs(el.scrollTop - targetScrollTop) > 2) {
        el.scrollTop = targetScrollTop
      }
      requestAnimationFrame(() => {
        applyCurrentMark()
        requestAnimationFrame(() => adjustToMark(el))
      })
      return
    }

    scrollToIndexWithRetry(virt, el, flatIndex, reserved, 5, () => {
      const scroller = scrollerEl()
      if (!scroller) return
      applyCurrentMark()
      requestAnimationFrame(() => requestAnimationFrame(() => adjustToMark(scroller)))
    })
  }

  function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
    const el = scrollerEl()
    if (!el) return null

    const scrollTopValue = el.scrollTop
    const measured = virtualizer().measurementsCache
    if (!measured.length) return null

    // Find the item whose range contains scrollTop, or fall back to the first item.
    const first =
      measured.find((item) => item.start <= scrollTopValue && scrollTopValue < item.end) ??
      measured[0]

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
        // The safety-net watch in useCommentary can trigger a second load with the
        // section range, causing groups to update twice. The first update has a
        // measurement cache from the previous groups list, so the resolver finds
        // the right flatIndex but wrong scrollTop. Wait for loading=false.
        if (isLoading()) return
        if (isRestoringScrollPos) return
        const generation = ++scrollGeneration
        await nextTick()
        if (generation !== scrollGeneration) return
        const pinned = pinnedGroup()
        if (!pinned) return
        const found = newGroups.some((g: any) => g.bookId === pinned.bookId)
        if (found) {
          await nextTick()
          if (generation !== scrollGeneration) return
          await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
          if (generation !== scrollGeneration) return
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
