/**
 * Shows the book title and a TOC path while the pointer hovers the lines scroller's
 * scrollbar. On the thumb (or an arrow) it is the CURRENT path — where the reader is
 * right now, "where am I?". Anywhere else on the track it is the path of the line a
 * drag of the thumb to that point would land on — "where would this take me?" — so
 * the bar can be read like an index before committing to a jump. The bar itself
 * says which it is (`getTargetLineIndex`).
 *
 * `getActiveTocEntry` + `getTocPath` turn a line index into the displayed path — the
 * same pair the copy-with-source menu and the breadcrumb use. When the selected
 * alternate TOC (daf, parasha…) has an entry covering that line, its label joins the
 * same path line rather than standing as a separate line. Title and path are kept as
 * separate fields so the label can weight them differently; with no path resolved
 * the title alone still answers the question, which is how the commentary header
 * and the copy-with-source line both degrade.
 *
 * The track is the WindowedScroller's own bar element, so hovering it is a plain
 * mouse event — no hit-testing against a native gutter. The bar reveals itself in
 * hidden-scrollbars mode. Deliberately simple where the other tooltips are not: it
 * never accepts the pointer (that would fight the thumb drag underneath it), it has
 * no gap bridge, and it closes when the pointer leaves the track. No fixed timing
 * windows — the only timer is the standard hover-intent delay before the first show.
 */
import { onScopeDispose, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { TocEntry } from '@/webview-host/queries.types'
import type { WindowedScrollerMoveSource, WindowedScrollerThumbRect } from '@/components/windowedScrollerTypes'

export interface ScrollbarTocTooltipData {
  /** Viewport Y of the pointer, so the label sits beside the pointer on the track. */
  pointerY: number
  /** Set while the label follows the thumb through a scroll: it glides with it. */
  followsThumb?: boolean
  /** Physical side the scrollbar is on — the label goes on its inner side. */
  side: 'left' | 'right'
  /** Viewport X of the scrollbar's inner edge. */
  edgeX: number
  bookTitle: string
  /** Empty when no TOC entry covers the current line — the title then stands alone. */
  path: string
}

const HOVER_DELAY_MS = 120
/** How long the scroll-follow label stays after the scrolling stops. */
const SCROLL_LINGER_MS = 700
/** Scroll-follow shows once one run (one move of the scroller) has travelled this many screens. */
const JOURNEY_SCREENS = 5
/** Books shorter than this many screens never get the scroll-follow label. */
const MIN_SCREENS = 4

export function useBookViewScrollbarTocTooltip(
  scrollbarElement: Ref<HTMLElement | null>,
  options: {
    /** The first visible row index — the reader's current position in the book. */
    getCurrentLineIndex: () => number | null
    /** What the bar has under a viewport Y: the thumb, or the line a seek there lands on. */
    getTargetLineIndex: (clientY: number) => { index: number; onThumb: boolean } | null
    getBookTitle: () => string
    getActiveTocEntry?: () => ((lineIndex: number) => TocEntry | null) | undefined
    getActiveAltTocEntry?: () => ((lineIndex: number) => TocEntry | null) | undefined
    getTocPath?: () => ((entry: TocEntry) => string) | undefined
    /**
     * Title and path for a row, for a list whose rows each belong to their own book
     * (the commentary panel's groups). Given, it replaces getBookTitle and the TOC
     * lookups.
     */
    getLabelAt?: (index: number) => { bookTitle: string; path: string }
    /**
     * While true the label is not shown: the list is growing under the bar (search
     * results streaming in), so the thumb moves without the reader moving it.
     */
    isSuspended?: () => boolean
    /**
     * The scroller's thumb (WindowedScroller's thumbRect). Given, the label also shows
     * beside the thumb while the reader scrolls (wheel, touch, keys, the bar) and fades
     * shortly after — DMScrollBar's info label. The scroller says whose move it is
     * (its `move` event): landings (a TOC jump, a restore) do not show it.
     */
    getThumbRect?: () => WindowedScrollerThumbRect | null
  },
) {
  const scrollbarTocTooltip = ref<ScrollbarTocTooltipData | null>(null)
  let hoverTimer: ReturnType<typeof setTimeout> | null = null

  function clearHoverTimer() {
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
  }

  function closeScrollbarTocTooltip() {
    clearHoverTimer()
    clearScrollLinger()
    isScrollFollow = false
    scrollbarTocTooltip.value = null
  }

  /** The bar's physical side and inner edge — the label hangs off that edge. */
  function barPlacement(bar: HTMLElement): { side: 'left' | 'right'; edgeX: number } {
    // In RTL the bar sits on the physical left.
    const onLeft = getComputedStyle(bar).direction === 'rtl'
    const rect = bar.getBoundingClientRect()
    return { side: onLeft ? 'left' : 'right', edgeX: onLeft ? rect.right : rect.left }
  }

  // ── Scroll-follow: shown beside the thumb while the reader scrolls ─────────────

  /** True while the label is the scroll-follow one, not the hover one. */
  let isScrollFollow = false
  let scrollLingerTimer: ReturnType<typeof setTimeout> | null = null

  function clearScrollLinger() {
    if (scrollLingerTimer !== null) {
      clearTimeout(scrollLingerTimer)
      scrollLingerTimer = null
    }
  }

  /**
   * Thumb top where the current run began. A run is one move of the scroller: from
   * its first reader scroll until the scroller reports the move ended (moveend).
   */
  let scrollRunStartTop: number | null = null

  /**
   * Worth labelling only when the book is long (thumb under a quarter of the track)
   * and the reader travels far — one run covering JOURNEY_SCREENS
   * screens. The thumb is one screen tall on the track, so its travel divided by its
   * height is the distance in screens. A nudge never shows it, however long it lasts.
   * Once shown, it keeps following until it lingers out.
   */
  function isJourney(thumbTop: number, thumbHeight: number, trackHeight: number): boolean {
    if (scrollRunStartTop === null) scrollRunStartTop = thumbTop
    if (isScrollFollow) return true
    if (!thumbHeight || thumbHeight * MIN_SCREENS > trackHeight) return false
    return Math.abs(thumbTop - scrollRunStartTop) >= thumbHeight * JOURNEY_SCREENS
  }

  function followScroll() {
    const bar = scrollbarElement.value
    // Where the thumb is GOING, not where its glide has got to.
    const thumb = options.getThumbRect?.()
    if (!bar || !thumb || options.isSuspended?.()) return
    if (!isJourney(thumb.top, thumb.height, thumb.trackHeight)) return
    const thumbCenterY = thumb.top + thumb.height / 2
    const index = lineIndexForPointer(thumbCenterY)
    const bookTitle = titleAt(index)
    const path = index == null ? '' : pathAtLine(index)
    if (!bookTitle && !path) return
    isScrollFollow = true
    scrollbarTocTooltip.value = { pointerY: thumbCenterY, followsThumb: true, ...barPlacement(bar), bookTitle, path }
    clearScrollLinger()
    scrollLingerTimer = setTimeout(() => {
      scrollLingerTimer = null
      if (isScrollFollow) closeScrollbarTocTooltip()
    }, SCROLL_LINGER_MS)
  }

  /** The path at a line, or '' when the book has no TOC or none covers it. */
  function pathAtLine(index: number): string {
    if (options.getLabelAt) return options.getLabelAt(index).path
    const getEntry = options.getActiveTocEntry?.()
    const getPath = options.getTocPath?.()
    const entry = getEntry && getPath ? getEntry(index) : null
    const path = entry ? getPath!(entry) || entry.text || '' : ''
    // The alternate TOC's label for the same line joins the path (not a line of its
    // own), unless the path already says it.
    const altLabel = options.getActiveAltTocEntry?.()?.(index)?.text ?? ''
    if (!altLabel || path.includes(altLabel)) return path
    return path ? `${path} · ${altLabel}` : altLabel
  }

  function titleAt(index: number | null): string {
    if (!options.getLabelAt) return options.getBookTitle()
    return index == null ? '' : options.getLabelAt(index).bookTitle
  }

  /** The line the label describes for a pointer at `clientY`. */
  function lineIndexForPointer(clientY: number): number | null {
    const target = options.getTargetLineIndex(clientY)
    if (target && !target.onThumb) return target.index
    // On the resting thumb the exact current line wins (a drag is a target above).
    return options.getCurrentLineIndex() ?? target?.index ?? null
  }

  // Where the pointer last was on the bar, so a refresh during a drag re-reads the
  // same spot.
  let lastPointerY = 0

  // pointermove, not mousemove: while the thumb is dragged it holds pointer capture,
  // and Chromium then stops delivering the compat mouse events to the bar — the
  // label would freeze at the drag's first position.
  useEventListener(scrollbarElement, 'pointermove', (event: PointerEvent) => {
    const bar = scrollbarElement.value
    if (!bar) return
    const { side, edgeX } = barPlacement(bar)
    // The pointer on the bar takes the label over from scroll-follow.
    isScrollFollow = false
    clearScrollLinger()

    lastPointerY = event.clientY
    const show = () => {
      if (options.isSuspended?.()) {
        scrollbarTocTooltip.value = null
        return
      }
      const index = lineIndexForPointer(event.clientY)
      const bookTitle = titleAt(index)
      const path = index == null ? '' : pathAtLine(index)
      // Nothing to say at all — no title and no path.
      if (!bookTitle && !path) {
        scrollbarTocTooltip.value = null
        return
      }
      scrollbarTocTooltip.value = { pointerY: event.clientY, side, edgeX, bookTitle, path }
    }

    // Already open: follow the pointer immediately, so it reads as one continuous
    // label rather than one that blinks out and back per delay.
    if (scrollbarTocTooltip.value) {
      show()
      return
    }
    clearHoverTimer()
    hoverTimer = setTimeout(() => {
      hoverTimer = null
      show()
    }, HOVER_DELAY_MS)
  })

  useEventListener(scrollbarElement, 'mouseleave', closeScrollbarTocTooltip)

  // A tab switch or book change is a real unmount, and it can land while the pointer
  // is on the track — the pending hover timer would read position off a detached
  // scroller. Same guard as useNoteTooltip and useWordLinkTooltip.
  onScopeDispose(() => {
    clearHoverTimer()
    clearScrollLinger()
  })

  /**
   * The scroller's `move`. Dragging the thumb is exactly when the path changes
   * fastest, and the pointer is not moving during it — so without this the label
   * would sit frozen on a stale section for the whole drag.
   */
  function onScrollerMove(source: WindowedScrollerMoveSource) {
    if (options.getThumbRect && (isScrollFollow || !scrollbarTocTooltip.value)) {
      if (source === 'reader') followScroll()
      return
    }
    if (!scrollbarTocTooltip.value) return
    if (options.isSuspended?.()) {
      scrollbarTocTooltip.value = null
      return
    }
    // The path moves with the scroll; the book too, where each row has its own.
    const index = lineIndexForPointer(lastPointerY)
    scrollbarTocTooltip.value = {
      ...scrollbarTocTooltip.value,
      bookTitle: titleAt(index),
      path: index == null ? '' : pathAtLine(index),
    }
  }

  /** The scroller's `moveend`: the run is over; the next reader scroll starts a new one. */
  function onScrollerMoveEnd() {
    scrollRunStartTop = null
  }

  return { scrollbarTocTooltip, closeScrollbarTocTooltip, onScrollerMove, onScrollerMoveEnd }
}
