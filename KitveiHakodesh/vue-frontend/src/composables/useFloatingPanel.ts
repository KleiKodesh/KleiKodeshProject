import { ref, computed } from 'vue'

export interface FloatingPanelPosition {
  x: number
  y: number
}

/**
 * Manages a floating panel at a fixed position.
 * The panel is not draggable and is positioned by CSS left/top.
 */
export function useFloatingPanel(options: {
  initialPosition: FloatingPanelPosition
}) {
  const panelRef = ref<HTMLElement | null>(null)

  const panelStyle = computed(() => ({
    left: `${options.initialPosition.x}px`,
    top: `${options.initialPosition.y}px`,
  }))

  return { panelRef, panelStyle }
}
