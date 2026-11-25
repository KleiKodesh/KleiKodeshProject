import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { computed, ref, type Ref } from 'vue'
import { useResizeObserver } from '@vueuse/core'

/** Below this page width the side panel (TOC) floats over the text instead of taking a column. */
export const SIDE_PANEL_INLINE_MIN_WIDTH = 520
/** The panel's share of the resize area while dragged: never thinner than this, never wider than half. */
export const SIDE_PANEL_MIN_FRACTION = 0.1
export const SIDE_PANEL_MAX_FRACTION = 0.5

/**
 * The reading pages' side panel (book view, FastDocView): inline beside the text on a wide
 * page with a sash to resize it, an overlay on a narrow one. The panel sits on the physical
 * right (RTL inline-start), so the drag measures from the resize area's right edge.
 *
 * `pageElement`: the page root, whose width decides inline vs overlay (each
 * answers to its own width). `resizeArea`: the row holding panel + sash + content, the
 * whole that the fraction is of. `fraction`: optional storage for the width (null = the
 * panel's own CSS width) - a page that persists it passes its own ref.
 */
export function useSidePanelLayout(options: {
  pageElement: Ref<HTMLElement | null>
  resizeArea: Ref<HTMLElement | null>
  fraction?: Ref<number | null>
  /**
   * Optional: decides inline vs overlay for a page with its own rule (given the page width and
   * the panel's width there). Default: inline from SIDE_PANEL_INLINE_MIN_WIDTH.
   */
  inlineWhen?: (pageWidth: number, panelWidth: number) => boolean
}) {
  const hostWindow = useHostWindow()
  const pageWidth = ref(hostWindow.innerWidth)
  useResizeObserver(options.pageElement, ([entry]) => {
    pageWidth.value = entry!.contentRect.width
  })
  const fraction = options.fraction ?? ref<number | null>(null)
  /** The inline panel's width at a page width: the dragged share, else SidePanel's CSS cap min(40%, 240px). */
  const panelWidthAt = (w: number) => (fraction.value !== null ? fraction.value * w : Math.min(0.4 * w, 240))
  const isInline = computed(() =>
    options.inlineWhen ? options.inlineWhen(pageWidth.value, panelWidthAt(pageWidth.value)) : pageWidth.value >= SIDE_PANEL_INLINE_MIN_WIDTH,
  )
  const isOverlay = computed(() => !isInline.value)

  const isDragging = ref(false)

  function onDividerPointerDown(event: PointerEvent) {
    isDragging.value = true
    ;(event.target as HTMLElement).setPointerCapture(event.pointerId)
  }
  function onResizePointerMove(event: PointerEvent) {
    const area = options.resizeArea.value
    if (!isDragging.value || !area) return
    const rect = area.getBoundingClientRect()
    fraction.value = Math.min(SIDE_PANEL_MAX_FRACTION, Math.max(SIDE_PANEL_MIN_FRACTION, (rect.right - event.clientX) / rect.width))
  }
  function onResizePointerUp() {
    isDragging.value = false
  }

  /** The inline panel's width style (none until the reader drags the sash; a dragged width lifts the panel's 30% default cap). */
  const panelStyle = computed(() => (fraction.value !== null ? { width: `${fraction.value * 100}%`, maxWidth: 'none' } : undefined))

  return { pageWidth, isInline, isOverlay, fraction, isDragging, panelStyle, onDividerPointerDown, onResizePointerMove, onResizePointerUp }
}
