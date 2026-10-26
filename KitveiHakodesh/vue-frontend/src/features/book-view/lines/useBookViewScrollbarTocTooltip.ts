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
    scrollbarTocTooltip.value = null
  }

  /** The path at a line, or '' when the book has no TOC or none covers it. */
  function pathAtLine(index: number): string {
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
    // In RTL the bar sits on the physical left; the label hangs off its inner edge.
    const onLeft = getComputedStyle(bar).direction === 'rtl'
    const rect = bar.getBoundingClientRect()
    const side: 'left' | 'right' = onLeft ? 'left' : 'right'
    const edgeX = onLeft ? rect.right : rect.left

    lastPointerY = event.clientY
    const show = () => {
      const bookTitle = options.getBookTitle()
      const index = lineIndexForPointer(event.clientY)
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
  onScopeDispose(clearHoverTimer)

  /**
   * Called as the view scrolls. Dragging the thumb is exactly when the path changes
   * fastest, and the pointer is not moving during it — so without this the label
   * would sit frozen on a stale section for the whole drag.
   */
  function refreshScrollbarTocTooltip() {
    if (!scrollbarTocTooltip.value) return
    // Only the path moves with the scroll — the book does not change under it.
    const index = lineIndexForPointer(lastPointerY)
    scrollbarTocTooltip.value = { ...scrollbarTocTooltip.value, path: index == null ? '' : pathAtLine(index) }
  }

  return { scrollbarTocTooltip, closeScrollbarTocTooltip, refreshScrollbarTocTooltip }
}
