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
 * Deliberately simple where the other tooltips are not: it never accepts the pointer
 * (that would fight the scrollbar drag underneath it), it has no gap bridge, and it
 * closes on the first pointer move off the track. No fixed timing windows — the only
 * timer is the standard hover-intent delay before the first show, and no state
 * transition is sequenced behind it.
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

  function closeScrollbarTocTooltip() {
    clearHoverTimer()
    scrollbarTocTooltip.value = null
  }

  /**
   * Where the vertical scrollbar sits, or null when there is none. `clientWidth`
   * excludes the track, so the difference from `offsetWidth` is its width; which
   * side it falls on follows the container's resolved direction (RTL here puts it
   * on the physical left). Overlay scrollbars report zero width — nothing to hover,
   * so nothing to show.
   */
  function scrollbarGeometry(el: HTMLElement) {
    const width = el.offsetWidth - el.clientWidth
    if (width <= 0) return null
    const rect = el.getBoundingClientRect()
    const onLeft = getComputedStyle(el).direction === 'rtl'
    return {
      side: (onLeft ? 'left' : 'right') as 'left' | 'right',
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

  useEventListener(scrollerEl, 'mousemove', (event: MouseEvent) => {
    const el = scrollerEl.value
    if (!el) return
    // The scroller's own track is bare: a move over it targets the scroller itself.
    // `mousemove` bubbles, so without this a descendant's event would be hit-tested
    // against the scroller's rect — and line HTML is arbitrary seforim-DB markup,
    // so a future inner scrollable at the pane's edge would read as "on the track".
    if (event.target !== el) {
      closeScrollbarTocTooltip()
      return
    }
    const geometry = scrollbarGeometry(el)
    const onTrack =
      geometry != null &&
      event.clientX >= geometry.trackStart &&
      event.clientX <= geometry.trackEnd &&
      event.clientY >= geometry.rect.top &&
      event.clientY <= geometry.rect.bottom
    if (!onTrack) {
      closeScrollbarTocTooltip()
      return
    }

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

  // A tab switch or book change is a real unmount (nothing here is kept alive), and
  // it can land inside the hover delay — the pending timer would then read position
  // off a detached scroller. Same guard as useNoteTooltip and useWordLinkTooltip.
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
