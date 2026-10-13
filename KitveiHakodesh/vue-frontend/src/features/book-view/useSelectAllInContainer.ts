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
 * the first `selectionchange` whose selection is no longer the select-all range
 * (collapsed, or replaced by any other selection). So it stays true only while the
 * genuine whole-container selection is intact.
 */
export function useSelectAllInContainer(containerRef: Ref<HTMLElement | null>) {
  const isSelectAll = ref(false)
  // The range selectAll() put in place. A live Range, so it follows the container's
  // children as virtualized rows mount and unmount — the select-all stays recognised
  // while scrolling, and any other selection no longer matches it.
  let selectAllRange: Range | null = null

  function selectAll(): void {
    const container = containerRef.value
    if (!container) return
    const selection = window.getSelection()
    if (!selection) return
    const range = document.createRange()
    range.selectNodeContents(container)
    selection.removeAllRanges()
    selection.addRange(range)
    // A clone, not the range handed to addRange: that one IS the selection's range,
    // and an engine that edits it in place would make every comparison match.
    selectAllRange = range.cloneRange()
    isSelectAll.value = true
  }

  function isStillSelectAll(selection: Selection | null): boolean {
    if (!selection || selection.isCollapsed || selection.rangeCount === 0 || !selectAllRange) return false
    const current = selection.getRangeAt(0)
    return (
      current.compareBoundaryPoints(Range.START_TO_START, selectAllRange) === 0 &&
      current.compareBoundaryPoints(Range.END_TO_END, selectAllRange) === 0
    )
  }

  // Clear the flag whenever the selection changes to something that is no longer the
  // whole-container select-all. Not only on collapse: a new partial selection can
  // replace the select-all without ever collapsing (double-click, Shift+click,
  // keyboard), and copy would then still export the whole book.
  useEventListener('selectionchange', () => {
    if (!isSelectAll.value) return
    if (isStillSelectAll(window.getSelection())) return
    isSelectAll.value = false
    selectAllRange = null
  })

  return { isSelectAll, selectAll }
}
