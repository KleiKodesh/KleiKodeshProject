/**
 * Provides programmatic scroll navigation for the book view lines scroller.
 *
 * scrollToLineId — scrolls to the line that has the given id, skipping if it is
 *   already fully visible. Falls back to fallbackLineIndex when the id is not found.
 *
 * scrollToLineIndex — scrolls to the line at a specific index and applies the
 *   .current CSS class to the search match at the given occurrence offset.
 *   Positions synchronously when the row is already rendered in the DOM (its
 *   measured start is real); otherwise goes through scrollToIndexWithRetry,
 *   which re-corrects while estimated heights are replaced by measured ones,
 *   then polls for the mark since the row renders a frame or two after the scroll.
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
  // Bumped at the start of every navigation so the multi-frame retry/poll of a
  // previous call cancels instead of stomping the newer call's mark and scroll.
  let scrollGeneration = 0

  function scrollToLineId(lineId: number, fallbackLineIndex?: number) {
    scrollGeneration++
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

  function scrollToLineIndex(lineIndex: number, occurrence = 0, forceScroll = false) {
    const scroller = scrollerEl.value
    if (!scroller) return
    const generation = ++scrollGeneration
    const isStale = () => generation !== scrollGeneration
    prioritise(lineIndex)

    const reserved = searchBarVisible() ? 44 : 0
    const virt = virtualizer()

    // Applies the .current class and reports whether the mark actually exists in
    // the DOM yet — the virtualizer renders the target row a frame or two after
    // the scroll, so a single blind attempt can silently hit nothing.
    function applyCurrentMark(): boolean {
      const el = scrollerEl.value
      if (!el) return false
      setCurrentMark(el, lineIndex, occurrence)
      return el.querySelector(`[data-index="${lineIndex}"] mark.search-match.current`) != null
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

    // Fast path — only when the target row is actually in the DOM, so its
    // measured start is real. The measurements cache is NOT a rendered-check:
    // tanstack fills it for every index (estimated sizes included), so a cache
    // hit for an off-screen row lands the scroll at an estimated position that
    // drifts as soon as the real rows render.
    const rendered = scroller.querySelector(`[data-index="${lineIndex}"]`)
    const measurement = virt.measurementsCache.find((cache) => cache.index === lineIndex)
    if (rendered && measurement) {
      setProgrammaticScroll()
      const targetScrollTop = measurement.start - reserved - 8
      // forceScroll bypasses the proximity guard — used for explicit section navigation
      // where the current scrollTop may be within 2px of the target due to sub-pixel
      // rounding but the view is not actually at the section start.
      if (forceScroll || Math.abs(scroller.scrollTop - targetScrollTop) > 2) {
        scroller.scrollTop = targetScrollTop
      }
      requestAnimationFrame(() => {
        if (isStale()) return
        applyCurrentMark()
        requestAnimationFrame(() => { if (!isStale()) adjustToMark(scroller) })
      })
      return
    }

    // Slow path — the row isn't rendered yet. scrollToIndexWithRetry keeps
    // correcting while estimated heights are replaced by measured ones; once it
    // settles, poll for the mark (the row renders a frame or two later) before
    // fine-adjusting to it. Gives up quietly when the line has no search marks
    // (TOC section navigation goes through here too).
    setProgrammaticScroll()
    scrollToIndexWithRetry(virt, scroller, lineIndex, reserved, 5, () => {
      let markAttempts = 0
      function tryApplyMark() {
        if (isStale()) return
        const el = scrollerEl.value
        if (!el) return
        if (applyCurrentMark()) {
          // The poll can outlive the 300ms programmatic-scroll latch (the mark
          // only appears once the line's content loads) — re-arm it so the
          // adjust below isn't mistaken for a user scroll by the save/sync guards.
          setProgrammaticScroll()
          requestAnimationFrame(() => { if (!isStale()) adjustToMark(el) })
          return
        }
        if (++markAttempts < 30) requestAnimationFrame(tryApplyMark)
      }
      tryApplyMark()
    }, isStale)
  }

  return { scrollToLineId, scrollToLineIndex }
}
