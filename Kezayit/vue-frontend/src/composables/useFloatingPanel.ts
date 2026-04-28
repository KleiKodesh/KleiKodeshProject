import { ref, watch } from 'vue'
import { useDraggable } from '@vueuse/core'

export interface FloatingPanelPosition {
  x: number
  y: number
}

/**
 * Manages a draggable floating panel anchored at a fixed position.
 * Wraps VueUse useDraggable and persists position via a provided save callback.
 *
 * Usage:
 *   const { panelRef, panelStyle } = useFloatingPanel({
 *     initialPosition: savedPos ?? defaultPosition(),
 *     onPositionChange: (pos) => savePos(pos),
 *   })
 */
export function useFloatingPanel(options: {
  initialPosition: FloatingPanelPosition
  onPositionChange?: (position: FloatingPanelPosition) => void
}) {
  const panelRef = ref<HTMLElement | null>(null)

  const { x, y, style: panelStyle } = useDraggable(panelRef, {
    initialValue: options.initialPosition,
  })

  if (options.onPositionChange) {
    watch([x, y], ([newX, newY]) => {
      options.onPositionChange!({ x: newX, y: newY })
    })
  }

  return { panelRef, panelStyle }
}
