import { computed, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { ToolbarPosition } from '@/stores/bookViewStore'

/** Pointer travel before a press on the toolbar background counts as a drag, not a click. */
const DRAG_START_THRESHOLD_PX = 5

/**
 * Lets the user drag a page toolbar by its empty background to any edge of its page
 * (top/bottom/left/right, physical sides). The edge nearest the pointer wins; a fixed
 * band previews it while dragging, and the position is committed on release.
 *
 * @param pageSelector - CSS selector of the page root the toolbar docks to (closest ancestor)
 */
export function useToolbarDrag(
  toolbarEl: Ref<HTMLElement | null>,
  toolbarPosition: Ref<ToolbarPosition>,
  pageSelector: string,
) {
  const dropTarget = ref<ToolbarPosition | null>(null)
  const viewRect = ref<DOMRect | null>(null)
  let pressX = 0
  let pressY = 0
  let pressed = false

  function nearestEdge(x: number, y: number, r: DOMRect): ToolbarPosition {
    const distances: [ToolbarPosition, number][] = [
      ['top', (y - r.top) / r.height],
      ['bottom', (r.bottom - y) / r.height],
      ['left', (x - r.left) / r.width],
      ['right', (r.right - x) / r.width],
    ]
    return distances.reduce((best, d) => (d[1] < best[1] ? d : best))[0]
  }

  useEventListener(toolbarEl, 'pointerdown', (e: PointerEvent) => {
    // Background only - presses on buttons and dropdowns keep their normal behaviour.
    if (e.button !== 0 || e.target !== toolbarEl.value) return
    pressed = true
    pressX = e.clientX
    pressY = e.clientY
    toolbarEl.value!.setPointerCapture(e.pointerId)
  })

  useEventListener(toolbarEl, 'pointermove', (e: PointerEvent) => {
    if (!pressed) return
    if (!dropTarget.value && Math.hypot(e.clientX - pressX, e.clientY - pressY) < DRAG_START_THRESHOLD_PX) return
    const view = toolbarEl.value?.closest(pageSelector) as HTMLElement | null
    if (!view) return
    viewRect.value = view.getBoundingClientRect()
    dropTarget.value = nearestEdge(e.clientX, e.clientY, viewRect.value)
  })

  function endDrag(commit: boolean) {
    if (commit && dropTarget.value) toolbarPosition.value = dropTarget.value
    pressed = false
    dropTarget.value = null
  }
  useEventListener(toolbarEl, 'pointerup', () => endDrag(true))
  useEventListener(toolbarEl, 'pointercancel', () => endDrag(false))
  useEventListener(window, 'keydown', (e: KeyboardEvent) => {
    if (e.key === 'Escape' && dropTarget.value) endDrag(false)
  })

  const isDragging = computed(() => dropTarget.value !== null)

  const dropPreviewStyle = computed(() => {
    const r = viewRect.value
    const edge = dropTarget.value
    if (!r || !edge) return {}
    const horizontal = edge === 'top' || edge === 'bottom'
    // Same thickness the toolbar itself will have on that edge.
    const css = getComputedStyle(toolbarEl.value!)
    const band = parseFloat(css.getPropertyValue(horizontal ? '--toolbar-horizontal-height' : '--toolbar-vertical-width')) || 36
    return {
      left: `${edge === 'right' ? r.right - band : r.left}px`,
      top: `${edge === 'bottom' ? r.bottom - band : r.top}px`,
      width: `${horizontal ? r.width : band}px`,
      height: `${horizontal ? band : r.height}px`,
    }
  })

  return { isDragging, dropPreviewStyle }
}
