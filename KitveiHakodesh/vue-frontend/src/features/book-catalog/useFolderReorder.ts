import { shallowRef } from 'vue'
import type { CategoryNode } from './bookCatalogTree'
import { liftForReorder, type ReorderDragGhost } from './reorderDragGhost'

/**
 * UserSpace only: drag a root by its move handle and drop it on another root to take its place.
 * Pointer events, not HTML5 drag-and-drop: the host's webview did not start native drags here,
 * and pointer capture also keeps focus in the search field (pointerdown is prevented).
 * Each movable row carries `targetAttrs(node)`; the handle carries `handleEvents(node)`.
 * Shared by the four catalog views.
 */
export function useFolderReorder(canMove: () => ((node: CategoryNode) => boolean) | undefined, onMove: (from: CategoryNode, to: CategoryNode) => void) {
  const dragging = shallowRef<CategoryNode | null>(null)
  const over = shallowRef<CategoryNode | null>(null)
  // Root ids are unique among roots (useUserSpace keys them by id), so the id finds the node.
  // shallowRef: a deep ref would hand back proxies, which never equal the views' nodes.
  const targets = new Map<string, CategoryNode>()
  let ghost: ReorderDragGhost | null = null

  function reset() {
    ghost?.remove()
    ghost = null
    dragging.value = null
    over.value = null
  }

  function targetAttrs(node: CategoryNode) {
    if (!canMove()?.(node)) return {}
    targets.set(String(node.id), node)
    return { 'data-reorder-id': String(node.id) }
  }

  function nodeAt(x: number, y: number): CategoryNode | null {
    const el = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-reorder-id]')
    return el ? targets.get(el.dataset.reorderId!) ?? null : null
  }

  function handleEvents(node: CategoryNode) {
    return {
      pointerdown: (e: PointerEvent) => {
        if (e.button !== 0) return
        e.preventDefault()
        e.stopPropagation()
        ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
        dragging.value = node
        const handle = e.currentTarget as HTMLElement
        const item = handle.closest<HTMLElement>('[data-reorder-id]')
        if (item) ghost = liftForReorder(item, handle, e.clientX, e.clientY)
      },
      pointermove: (e: PointerEvent) => {
        if (!dragging.value) return
        ghost?.moveTo(e.clientX, e.clientY)
        const at = nodeAt(e.clientX, e.clientY)
        over.value = at && at !== dragging.value ? at : null
      },
      pointerup: () => {
        const from = dragging.value
        const to = over.value
        reset()
        if (from && to) onMove(from, to)
      },
      pointercancel: reset,
      mousedown: (e: MouseEvent) => e.stopPropagation(),
      click: (e: MouseEvent) => e.stopPropagation(),   // a click on the handle never opens the folder
    }
  }

  return { dragging, over, handleEvents, targetAttrs }
}
