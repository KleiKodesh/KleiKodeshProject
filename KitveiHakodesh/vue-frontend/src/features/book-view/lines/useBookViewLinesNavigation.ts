/**
 * The ONE way to scroll the book view lines scroller to a line.
 *
 * Every caller — TOC click, Ctrl+Arrow section navigation, a search match, a
 * commentary jump — goes through `scrollToLine`. They differ only in how they
 * name the target and whether they want a match centred afterwards, so those are
 * options on one function rather than separate scrollers.
 *
 * This used to be two functions. `scrollToLineId` resolved an id and did a crude
 * visible-check plus a bare `scrollToIndex`; `scrollToLineIndex` had the real
 * positioning — the rendered/estimated fast-slow split, the retry that re-corrects
 * as estimated heights become measured ones, and the generation guard. TOC clicks
 * took the weak path while Ctrl+Arrow section navigation — the same conceptual
 * jump — took the strong one, so the two disagreed about where a section starts.
 * There is now one path, and it is the strong one.
 *
 * Positioning, in order:
 *   1. Resolve the target to a line index (by id, or given directly).
 *   2. Fast path when the row is already in the DOM — its measured start is real,
 *      so the scroll lands in one step.
 *   3. Slow path otherwise — scrollToIndexWithRetry keeps correcting while
 *      estimated heights are replaced by measured ones.
 *   4. Optional refinement: apply the `.current` mark for a search occurrence and
 *      nudge it into view. The row renders a frame or two after the scroll, so
 *      this polls rather than assuming.
 */
import type { Ref } from 'vue'
import type { Virtualizer, VirtualItem } from '@tanstack/vue-virtual'
import { scrollToIndexWithRetry, SCROLL_LANDING_GAP_PX } from '@/utils/scrollToIndexWithRetry'
import { setCurrentMark } from './useBookViewLineRenderer'
import type { LineItem } from './useBookViewLinesTable'

/**
 * Height the search bar overlays at the top of the lines scroller. Landings
 * reserve this much extra so the target is not hidden under the bar, and the
 * scroll sync skips it when deriving the top visible line.
 */
export const SEARCH_BAR_INSET_PX = 44

export interface ScrollToLineOptions {
  /**
   * Which search occurrence within the line to mark `.current` and centre.
   * Omit for a plain jump to the line — a TOC click or section navigation has no
   * match to refine toward, and the mark polling is skipped entirely.
   */
  occurrence?: number
  /**
   * Scroll even when already within 2px of the target. Explicit section
   * navigation needs this: sub-pixel rounding can put scrollTop a hair from the
   * target while the view is not actually at the section start.
   */
  force?: boolean
  /**
   * Skip the scroll when the row is already fully visible. Used by jumps that
   * only need the line on screen (a commentary jump) rather than at the top, so
   * reading position is not disturbed for a line the reader can already see.
   */
  skipIfVisible?: boolean
  /**
   * Which way a search step is travelling, deciding WHERE an off-screen match
   * comes to rest.
   *
   * 'backward' (and the default) lands it at the TOP — you are heading back
   * through text you have already read, so the match plus what follows it is what
   * you want in front of you.
   *
   * 'forward' lands it at the BOTTOM, because reading advances downward: the
   * match arrives at the edge you are reading toward, and the lines above it —
   * the ones leading up to it — stay on screen. Pulling it to the top instead
   * would discard all of that context and skip the view past everything between.
   */
  direction?: 'forward' | 'backward'
}

export function useBookViewLinesNavigation(
  scrollerEl: Ref<HTMLElement | null>,
  virtualizer: () => Virtualizer<Element, Element>,
  virtualItems: () => VirtualItem[],
  lines: () => LineItem[],
  searchBarVisible: () => boolean,
  suppressPositionSave: () => void,
  /** Ends the session-restore correction window — a jump supersedes the restore. */
  cancelRestoreCorrection: () => void,
  prioritise: (lineIndex: number) => void,
) {
  // Bumped at the start of every navigation so the multi-frame retry/poll of a
  // previous call cancels instead of stomping the newer call's mark and scroll.
  let scrollGeneration = 0

  /** Resolve a line id to its index, falling back when the line is not loaded yet. */
  function lineIndexForId(lineId: number, fallbackLineIndex?: number): number | undefined {
    return lines().find((line) => line.id === lineId)?.lineIndex ?? fallbackLineIndex
  }

  function isFullyVisible(lineIndex: number): boolean {
    const scroller = scrollerEl.value
    const virtualItem = virtualItems().find((v) => v.index === lineIndex)
    if (!virtualItem || !scroller) return false
    const viewTop = scroller.scrollTop
    const viewBottom = viewTop + scroller.clientHeight
    return virtualItem.start >= viewTop && virtualItem.start + virtualItem.size <= viewBottom
  }

  function scrollToLine(lineIndex: number, options: ScrollToLineOptions = {}) {
    const scroller = scrollerEl.value
    if (!scroller) return
    const generation = ++scrollGeneration
    const isStale = () => generation !== scrollGeneration
    prioritise(lineIndex)

    if (options.skipIfVisible && isFullyVisible(lineIndex)) return

    // The reader is going somewhere else: session restore must stop re-anchoring
    // its own target, or a late chunk load will yank the view back there.
    cancelRestoreCorrection()

    const { occurrence, force = false, direction = 'backward' } = options
    // No occurrence means no match to refine toward — a plain jump to the line.
    const wantsMark = occurrence != null
    const reserved = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0
    const virt = virtualizer()

    // Applies the .current class and reports whether the mark actually exists in
    // the DOM yet — the virtualizer renders the target row a frame or two after
    // the scroll, so a single blind attempt can silently hit nothing.
    function applyCurrentMark(): boolean {
      const el = scrollerEl.value
      if (!el) return false
      setCurrentMark(el, lineIndex, occurrence ?? 0)
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
        // Land the mark at the edge the reader is travelling toward: bottom when
        // stepping forward, top when stepping back. See `direction` above.
        scroller.scrollTop +=
          direction === 'forward'
            ? relativeBottom - scrollerRect.height + SCROLL_LANDING_GAP_PX
            : relativeTop - reserved - SCROLL_LANDING_GAP_PX
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
      suppressPositionSave()

      // A match jump moves the view only if the match is not already on screen.
      // The target here is the MARK, not the line: hauling the line to the top to
      // reach a match the reader can already see throws away their position for
      // nothing — stepping through matches within one screenful should leave the
      // page still, exactly as a browser's find does. adjustToMark owns that
      // decision (it repositions only when the mark is not comfortably in view),
      // so hand it the mark and let it choose.
      //
      // Only the FAST path can do this: it means the row is really in the DOM, so
      // the mark can be applied and measured right now. `force` still overrides —
      // section navigation is about the line's position, not a match's visibility.
      if (wantsMark && !force) {
        applyCurrentMark()
        if (adjustToMark(scroller)) return
        // The mark was not in the DOM after all (content still loading) — fall
        // through to the line scroll and the rAF retry below.
      }

      const targetScrollTop = measurement.start - reserved - SCROLL_LANDING_GAP_PX
      if (force || Math.abs(scroller.scrollTop - targetScrollTop) > 2) {
        scroller.scrollTop = targetScrollTop
      }
      if (!wantsMark) return
      requestAnimationFrame(() => {
        if (isStale()) return
        applyCurrentMark()
        requestAnimationFrame(() => { if (!isStale()) adjustToMark(scroller) })
      })
      return
    }

    // Slow path — the row is not rendered yet. scrollToIndexWithRetry keeps
    // correcting while estimated heights are replaced by measured ones; once it
    // settles, poll for the mark (the row renders a frame or two later) before
    // fine-adjusting to it.
    //
    // Suppression is re-armed on EVERY frame of this path rather than once at the
    // start. Each suppressPositionSave() opens a fixed ~300ms window, and this
    // path can easily outlast it: up to 5 retry rounds of two rAFs each, then up
    // to 30 more frames waiting for the mark, which only appears once the line
    // content loads. Any scroll landing in the gap would be misread as the reader
    // moving the view — persisting a mid-flight position, and (since the search
    // panel re-anchors on reader scrolls) making the next Enter jump to where the
    // previous one landed instead of stepping on.
    function keepSuppressed() {
      if (isStale()) return
      suppressPositionSave()
    }

    // Holds the landing in place while late content shifts it.
    //
    // scrollToIndexWithRetry settles in a handful of frames — on ESTIMATED heights
    // when the target's region has not loaded yet. When the real content then
    // arrives, every line above the target re-measures and the content slides
    // under the unchanged scrollTop, leaving the view a few SECTIONS short of
    // where the reader clicked (live-verified on הזוהר המתורגם: click daf 218b,
    // land on 215a). The TOC then truthfully highlights the wrong section — the
    // flicker was the report, this drift was the crime.
    //
    // So after the retry settles, keep re-anchoring the target to the top while
    // its measured start keeps moving, and stop once it has been still for half a
    // second (late chunks above can shift it repeatedly, not just once). Reader
    // input cancels immediately — correcting under their hands would fight them.
    function stabilizeLanding() {
      const el = scrollerEl.value
      if (!el) return
      const STILL_FRAMES_TO_FINISH = 30 // ~0.5s of no movement
      const MAX_FRAMES = 240 // ~4s hard stop if estimates never converge
      let stillFrames = 0
      let frames = 0
      let cancelled = false
      function teardown() {
        el!.removeEventListener('wheel', cancel)
        el!.removeEventListener('touchstart', cancel)
        el!.removeEventListener('pointerdown', cancel)
        el!.removeEventListener('keydown', cancel)
      }
      function cancel() {
        cancelled = true
        teardown()
      }
      el.addEventListener('wheel', cancel, { passive: true })
      el.addEventListener('touchstart', cancel, { passive: true })
      el.addEventListener('pointerdown', cancel, { passive: true })
      el.addEventListener('keydown', cancel)
      function frame() {
        if (cancelled) return
        if (isStale()) { teardown(); return }
        const scrollerNow = scrollerEl.value
        if (!scrollerNow) { teardown(); return }
        const m = virtualizer().measurementsCache.find((c) => c.index === lineIndex)
        if (m) {
          // Re-read the inset each frame — the search bar can open or close during
          // the seconds this loop may live, and anchoring to a stale inset would
          // pin the landing 44px off.
          const reservedNow = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0
          const want = Math.max(0, m.start - reservedNow - SCROLL_LANDING_GAP_PX)
          if (Math.abs(scrollerNow.scrollTop - want) > 2) {
            keepSuppressed()
            scrollerNow.scrollTop = want
            stillFrames = 0
          } else {
            stillFrames++
          }
        }
        if (stillFrames >= STILL_FRAMES_TO_FINISH || ++frames > MAX_FRAMES) { teardown(); return }
        requestAnimationFrame(frame)
      }
      requestAnimationFrame(frame)
    }

    keepSuppressed()
    scrollToIndexWithRetry(virt, scroller, lineIndex, reserved, 5, () => {
      keepSuppressed()
      // A mark jump refines PAST the line anchor (adjustToMark centres the match,
      // deliberately moving scrollTop off the section start) — anchoring the line
      // back to the top would fight it. The mark poll below already re-finds and
      // re-adjusts as content lands, so it is its own stabilisation.
      if (!wantsMark) {
        stabilizeLanding()
        return
      }
      let markAttempts = 0
      function tryApplyMark() {
        if (isStale()) return
        const el = scrollerEl.value
        if (!el) return
        keepSuppressed()
        if (applyCurrentMark()) {
          requestAnimationFrame(() => {
            if (isStale()) return
            keepSuppressed()
            adjustToMark(el)
          })
          return
        }
        if (++markAttempts < 30) requestAnimationFrame(tryApplyMark)
      }
      tryApplyMark()
    }, isStale)
  }

  /**
   * Same scroll, addressed by line id — for callers holding a TOC entry or a
   * commentary target rather than an index. `fallbackLineIndex` covers the line
   * not being loaded into the lines array yet.
   */
  function scrollToLineId(
    lineId: number,
    fallbackLineIndex?: number,
    options: ScrollToLineOptions = {},
  ) {
    const lineIndex = lineIndexForId(lineId, fallbackLineIndex)
    if (lineIndex == null) return
    scrollToLine(lineIndex, options)
  }

  return { scrollToLine, scrollToLineId }
}
