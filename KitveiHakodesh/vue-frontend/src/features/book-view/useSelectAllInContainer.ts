import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

/**
 * Tracks whether the user has "selected all" the text inside a specific container,
 * as a plain reactive boolean that any feature can consume.
 *
 * Why this is its own composable: several features (book-view lines, commentary, …)
 * need to know "is the current selection a whole-container select-all?" so they can
 * copy/export the ENTIRE content rather than only the DOM range — which matters when
 * the content is virtualized and the selected lines aren't all in the DOM. The flag
 * is intentionally decoupled from any key-handling: `useScopedKeys` wires Ctrl+A to
 * `selectAll()`, but callers that only need the boolean (or their own trigger) can
 * use this composable directly.
 *
 * `isSelectAll` becomes true when `selectAll()` runs, and is cleared automatically on
 * the next `selectionchange` that collapses or replaces the selection (i.e. the user
 * clicked or dragged a partial selection). So it stays true only while the genuine
 * whole-container selection is intact.
 */
export function useSelectAllInContainer(containerRef: Ref<HTMLElement | null>) {
  const isSelectAll = ref(false)

  function selectAll(): void {
    const container = containerRef.value
    if (!container) return
    const selection = window.getSelection()
    if (!selection) return
    const range = document.createRange()
    range.selectNodeContents(container)
    selection.removeAllRanges()
    selection.addRange(range)
    isSelectAll.value = true
  }

  // Clear the flag whenever the selection changes to something that is no longer the
  // whole-container select-all (user clicked, or made a partial selection).
  useEventListener('selectionchange', () => {
    if (!isSelectAll.value) return
    const selection = window.getSelection()
    if (!selection || selection.isCollapsed) isSelectAll.value = false
  })

  return { isSelectAll, selectAll }
}
