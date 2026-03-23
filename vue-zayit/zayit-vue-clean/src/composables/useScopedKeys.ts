import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

export function useScopedKeys(
  containerRef: Ref<HTMLElement | null>,
  options?: { onCtrlF?: () => void }
) {
  const isSelectAll = ref(false)

  function selectAllInContainer() {
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

  useEventListener('keydown', (event: KeyboardEvent) => {
    const container = containerRef.value
    if (!container || document.activeElement !== container) return
    const ctrl = event.ctrlKey || event.metaKey
    if (!ctrl) return

    if (event.code === 'KeyA') {
      event.preventDefault()
      selectAllInContainer()
    } else if (event.code === 'KeyF' && options?.onCtrlF) {
      event.preventDefault()
      options.onCtrlF()
    }
  })

  // Clear the flag whenever selection changes (user clicked or made a partial selection)
  useEventListener('selectionchange', () => {
    if (!isSelectAll.value) return
    const selection = window.getSelection()
    if (!selection || selection.isCollapsed) isSelectAll.value = false
  })

  return { selectAllInContainer, isSelectAll }
}
