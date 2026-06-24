/**
 * Provides programmatic scroll navigation for the book view lines scroller.
 *
 * scrollToLineId — scrolls to the line that has the given id, skipping if it is
 *   already fully visible. Falls back to fallbackLineIndex when the id is not found.
 *
 * scrollToLineIndex — scrolls to the line at a specific index and applies the
 *   .current CSS class to the search match at the given occurrence offset.
 *   Uses the measurements cache fast path when the item is already rendered,
 *   falling back to scrollToIndexWithRetry for items outside the rendered range.
 */
import type { Ref } from 'vue'
import type { Virtualizer, VirtualItem } from '@tanstack/vue-virtual'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { setCurrentMark } from './useBookViewLineRenderer'
import type { LineItem } from './useBookViewLinesTable'

export function useBookViewLinesNavigation(
  scrollerEl: Ref<HTMLElement | null>,
  virtualizer: () => Virtualizer<Element, Element>,
  virtualItems: () => VirtualItem[],
  lines: () => LineItem[],
  searchBarVisible: () => boolean,
  setProgrammaticScroll: () => void,
  prioritise: (lineIndex: number) => void,
) {
  function scrollToLineId(lineId: number, fallbackLineIndex?: number) {
    const lineIndex = lines().find((line) => line.id === lineId)?.lineIndex ?? fallbackLineIndex
    if (lineIndex == null) return
    prioritise(lineIndex)
    const scroller = scrollerEl.value
    const virtualItem = virtualItems().find((v) => v.index === lineIndex)
    if (virtualItem && scroller) {
      const viewTop = scroller.scrollTop
      const viewBottom = viewTop + scroller.clientHeight
      if (virtualItem.start >= viewTop && virtualItem.start + virtualItem.size <= viewBottom) return
    }
    setProgrammaticScroll()
    virtualizer().scrollToIndex(lineIndex, { align: 'start' })
  }

  function scrollToLineIndex(lineIndex: number, occurrence = 0) {
    if (!scrollerEl.value) return

    const reserved = searchBarVisible() ? 44 : 0
    const virt = virtualizer()
    const measurement = virt.measurementsCache.find((cache) => cache.index === lineIndex)

    function applyCurrentMark() {
      if (scrollerEl.value) setCurrentMark(scrollerEl.value, lineIndex, occurrence)
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

    if (measurement) {
      setProgrammaticScroll()
      const targetScrollTop = measurement.start - reserved - 8
      if (Math.abs(scrollerEl.value.scrollTop - targetScrollTop) > 2) {
        scrollerEl.value.scrollTop = targetScrollTop
      }
      const scroller = scrollerEl.value
      requestAnimationFrame(() => {
        applyCurrentMark()
        requestAnimationFrame(() => adjustToMark(scroller))
      })
      return
    }

    setProgrammaticScroll()
    scrollToIndexWithRetry(virt, scrollerEl.value, lineIndex, reserved, 5, () => {
      const scroller = scrollerEl.value
      if (!scroller) return
      applyCurrentMark()
      requestAnimationFrame(() => requestAnimationFrame(() => adjustToMark(scroller)))
    })
  }

  return { scrollToLineId, scrollToLineIndex }
}
