/**
 * Shows the book title and the CURRENT TOC path — where the reader is right now —
 * while the pointer hovers the scroller's own scrollbar track. Reaching for the
 * scrollbar is the moment "where am I?" gets asked, and the answer is the same
 * wherever on the track the pointer happens to sit: this deliberately does NOT read
 * the position under the pointer.
 *
 * The position comes from the first visible row, and `getActiveTocEntry` +
 * `getTocPath` turn that index into the displayed path — the same pair the
 * copy-with-source menu and the breadcrumb use. Title and path are kept as separate
 * fields rather than one joined string so the label can weight them differently;
 * with no path resolved the title alone still answers the question, which is how
 * the commentary header and the copy-with-source line both degrade.
 *
 * Hovering the track also reveals the bar itself when the hidden-scrollbars mode is
 * on (`revealScrollbarOnHover`) — that mode only tints the bar transparent and always
 * keeps the gutter, so the bar is still there to be hovered and dragged, just unseen.
 * Labelling a position beside an invisible bar would be the worse half of the answer.
 *
 * Deliberately simple where the other tooltips are not: it never accepts the pointer
 * (that would fight the scrollbar drag underneath it), it has no gap bridge, and it
 * closes on the first pointer move off the track. No fixed timing windows — the only
 * timer is the standard hover-intent delay before the first show, and no state
 * transition is sequenced behind it.
 */
import { onScopeDispose, ref, type Ref } from 'vue'
import { useEventListener, useResizeObserver } from '@vueuse/core'
import { revealScrollbarOnHover } from '@/composables/useUiChromeVisibility'
import type { TocEntry } from '@/webview-host/queries.types'

export interface ScrollbarTocTooltipData {
  /** Viewport Y of the pointer, so the label sits beside the pointer on the track. */
  pointerY: number
  /** Physical side the scrollbar is on — the label goes on its inner side. */
  side: 'left' | 'right'
  /** Viewport X of the scrollbar's inner edge. */
  edgeX: number
  bookTitle: string
  /** Empty when no TOC entry covers the current line — the title then stands alone. */
  path: string
}

const HOVER_DELAY_MS = 120

export function useBookViewScrollbarTocTooltip(
  scrollerEl: Ref<HTMLElement | null>,
  options: {
    /** The first visible row index — the reader's current position in the book. */
    getCurrentLineIndex: () => number | null
    getBookTitle: () => string
    getActiveTocEntry?: () => ((lineIndex: number) => TocEntry | null) | undefined
    getTocPath?: () => ((entry: TocEntry) => string) | undefined
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

  /**
   * Holds the bar visible while the pointer rests on the track. The reveal lingers
   * ~1s from its last call and `mousemove` stops firing the moment the pointer
   * stops, so without this the bar would fade out from under a stationary pointer.
   * Re-arms itself; stopped whenever the pointer leaves the track.
   */
  let revealKeepalive: ReturnType<typeof setInterval> | null = null
  const REVEAL_KEEPALIVE_MS = 400

  function startRevealKeepalive(el: HTMLElement) {
    if (revealKeepalive !== null) return
    revealKeepalive = setInterval(() => revealScrollbarOnHover(el), REVEAL_KEEPALIVE_MS)
  }

  function stopRevealKeepalive() {
    if (revealKeepalive !== null) {
      clearInterval(revealKeepalive)
      revealKeepalive = null
    }
  }

  function closeScrollbarTocTooltip() {
    clearHoverTimer()
    stopRevealKeepalive()
    scrollbarTocTooltip.value = null
  }

  type ScrollbarGeometry = {
    side: 'left' | 'right'
    trackStart: number
    trackEnd: number
    edgeX: number
    rect: DOMRect
  }

  /**
   * Only the POSITION-INDEPENDENT half is cached: the bar's width, and which side it
   * sits on. That is the expensive half — `getComputedStyle` forces a style
   * resolution — and it is the half that genuinely only changes on resize.
   *
   * The rect is deliberately NOT cached. A cached rect has to be invalidated on every
   * way the scroller can move, and a ResizeObserver does not see them all: flipping
   * the toolbar between left and right (BookViewPage's `toolbar-order-end`) reorders
   * equal-width flex siblings, which translates this scroller by the toolbar's width
   * with its size unchanged, so no observer entry fires. Rather than enumerate such
   * triggers and be wrong again, the rect is read fresh — but only on the rare path
   * where the pointer is plausibly on the track, never on the reading moves this
   * cache exists to keep cheap. See the hit test for how that ordering works.
   */
  let widthAndSideCache: { width: number; onLeft: boolean } | null = null
  let widthAndSideMeasured = false

  /**
   * The generously-widened X band the track was last seen in, used to reject reading
   * moves without touching layout. Null until the first fresh measurement, which
   * makes the first move on each side fall through to stage 2 — exactly right, since
   * there is nothing yet to reject against.
   */
  let lastTrackBand: { start: number; end: number } | null = null

  function invalidateGeometry() {
    widthAndSideCache = null
    widthAndSideMeasured = false
    lastTrackBand = null
  }

  /**
   * The bar's width and side, or null when there is no bar. `clientWidth` excludes
   * the track, so the difference from `offsetWidth` is its width; which side it falls
   * on follows the container's resolved direction (RTL here puts it on the physical
   * left). Overlay scrollbars report zero width — nothing to hover, so nothing to show.
   */
  function scrollbarWidthAndSide(el: HTMLElement) {
    if (widthAndSideMeasured) return widthAndSideCache
    widthAndSideMeasured = true
    const width = el.offsetWidth - el.clientWidth
    widthAndSideCache =
      width > 0 ? { width, onLeft: getComputedStyle(el).direction === 'rtl' } : null
    return widthAndSideCache
  }

  /** Full geometry, reading the rect fresh so it can never be stale. */
  function scrollbarGeometry(el: HTMLElement): ScrollbarGeometry | null {
    const base = scrollbarWidthAndSide(el)
    if (!base) return null
    const { width, onLeft } = base
    const rect = el.getBoundingClientRect()
    return {
      side: onLeft ? 'left' : 'right',
      // Track bounds in viewport X, and the inner edge the label hangs off.
      trackStart: onLeft ? rect.left : rect.right - width,
      trackEnd: onLeft ? rect.left + width : rect.right,
      edgeX: onLeft ? rect.left + width : rect.right - width,
      rect,
    }
  }

  /** The current path, or '' when the book has no TOC or none covers this line. */
  function currentPath(): string {
    const getEntry = options.getActiveTocEntry?.()
    const getPath = options.getTocPath?.()
    if (!getEntry || !getPath) return ''

    const index = options.getCurrentLineIndex()
    if (index == null) return ''

    const entry = getEntry(index)
    if (!entry) return ''
    return getPath(entry) || entry.text || ''
  }

  /** Nothing open and nothing pending — the state every reading move is already in. */
  function isIdle() {
    return !scrollbarTocTooltip.value && hoverTimer === null && revealKeepalive === null
  }

  useEventListener(scrollerEl, 'mousemove', (event: MouseEvent) => {
    const el = scrollerEl.value
    if (!el) return
    // The scroller's own track is bare: a move over it targets the scroller itself.
    // `mousemove` bubbles, so without this a descendant's event would be hit-tested
    // against the scroller's rect — and line HTML is arbitrary seforim-DB markup,
    // so a future inner scrollable at the pane's edge would read as "on the track".
    // This is also the cheapest possible exit for a move over a line: one reference
    // comparison, before any measurement or hit test.
    if (event.target !== el) {
      if (!isIdle()) closeScrollbarTocTooltip()
      return
    }
    // Two-stage hit test, so the reading path almost never reads the rect.
    //
    // Stage 1 rejects on the LAST known track band, kept from the previous fresh
    // measurement. It costs nothing, and a move over text is rejected here — which
    // is every move made while reading. When the band is stale (the scroller was
    // translated without resizing, e.g. the toolbar flipping sides) this can only
    // mis-reject a pointer that IS on the track, never accept one that is not: stage
    // 2 re-measures and confirms. The band is widened by the scroller's own width so
    // a translation up to a full pane cannot hide the track from stage 1.
    const base = scrollbarWidthAndSide(el)
    if (!base) {
      if (!isIdle()) closeScrollbarTocTooltip()
      return
    }
    if (
      lastTrackBand !== null &&
      (event.clientX < lastTrackBand.start || event.clientX > lastTrackBand.end)
    ) {
      if (!isIdle()) closeScrollbarTocTooltip()
      return
    }

    // Stage 2 reads the rect fresh, so the decision is never made on stale geometry.
    const geometry = scrollbarGeometry(el)
    if (geometry) {
      lastTrackBand = {
        start: geometry.trackStart - el.offsetWidth,
        end: geometry.trackEnd + el.offsetWidth,
      }
    }
    const onTrack =
      geometry != null &&
      event.clientX >= geometry.trackStart &&
      event.clientX <= geometry.trackEnd &&
      event.clientY >= geometry.rect.top &&
      event.clientY <= geometry.rect.bottom
    if (!onTrack) {
      if (!isIdle()) closeScrollbarTocTooltip()
      return
    }

    // Immediately, NOT behind the hover-intent delay below: when bars are hidden
    // the reader is pointing at something invisible, and making it appear is the
    // response to the pointer arriving, not to the intent to read a label.
    revealScrollbarOnHover(el)
    startRevealKeepalive(el)

    const show = () => {
      const bookTitle = options.getBookTitle()
      const path = currentPath()
      // Nothing to say at all — no title and no path.
      if (!bookTitle && !path) {
        scrollbarTocTooltip.value = null
        return
      }
      scrollbarTocTooltip.value = {
        pointerY: event.clientY,
        side: geometry!.side,
        edgeX: geometry!.edgeX,
        bookTitle,
        path,
      }
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

  useEventListener(scrollerEl, 'mouseleave', closeScrollbarTocTooltip)

  // Re-measure the bar's width and side when the scroller resizes — including the bar
  // appearing as the first chunks load, which is a clientWidth change. This does NOT
  // have to catch every way the scroller can move: position is read fresh on the rare
  // possibly-on-track path, precisely so a translation the observer cannot see (the
  // toolbar flipping sides reorders equal-width siblings) cannot leave stale bounds.
  useResizeObserver(scrollerEl, invalidateGeometry)

  // A tab switch or book change is a real unmount (nothing here is kept alive), and
  // it can land while the pointer is on the track — the pending hover timer would
  // read position off a detached scroller, and the keepalive would go on reviving a
  // bar that no longer exists. Same guard as useNoteTooltip and useWordLinkTooltip.
  onScopeDispose(() => {
    clearHoverTimer()
    stopRevealKeepalive()
  })

  /**
   * Called as the view scrolls. Dragging the thumb is exactly when the path changes
   * fastest, and the pointer is not moving during it — so without this the label
   * would sit frozen on a stale section for the whole drag.
   */
  function refreshScrollbarTocTooltip() {
    if (!scrollbarTocTooltip.value) return
    // Only the path moves with the scroll — the book does not change under it.
    scrollbarTocTooltip.value = { ...scrollbarTocTooltip.value, path: currentPath() }
  }

  return { scrollbarTocTooltip, closeScrollbarTocTooltip, refreshScrollbarTocTooltip }
}
