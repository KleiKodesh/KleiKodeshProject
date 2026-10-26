/**
 * Shows the book title and the CURRENT TOC path — where the reader is right now —
 * while the pointer hovers the lines scroller's scrollbar track. Reaching for the
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
  scrollbarTrackElement: Ref<HTMLElement | null>,
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

  function closeScrollbarTocTooltip() {
    clearHoverTimer()
    scrollbarTocTooltip.value = null
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

  useEventListener(scrollbarTrackElement, 'mousemove', (event: MouseEvent) => {
    const track = scrollbarTrackElement.value
    if (!track) return
    // In RTL the bar sits on the physical left; the label hangs off its inner edge.
    const onLeft = getComputedStyle(track).direction === 'rtl'
    const rect = track.getBoundingClientRect()
    const side: 'left' | 'right' = onLeft ? 'left' : 'right'
    const edgeX = onLeft ? rect.right : rect.left

    const show = () => {
      const bookTitle = options.getBookTitle()
      const path = currentPath()
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

  useEventListener(scrollbarTrackElement, 'mouseleave', closeScrollbarTocTooltip)

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
    scrollbarTocTooltip.value = { ...scrollbarTocTooltip.value, path: currentPath() }
  }

  return { scrollbarTocTooltip, closeScrollbarTocTooltip, refreshScrollbarTocTooltip }
}
