import { type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useSelectAllInContainer } from './useSelectAllInContainer'

export function useScopedKeys(
  containerRef: Ref<HTMLElement | null>,
  options?: { onCtrlF?: () => void; onCtrlV?: () => void; onCtrlShiftC?: () => void },
) {
  // Select-all detection lives in a standalone composable so features that only need
  // the boolean (not the key handling) can reuse it. Ctrl+A here just drives it.
  const { isSelectAll, selectAll: selectAllInContainer } = useSelectAllInContainer(containerRef)

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
    } else if (event.code === 'KeyV' && options?.onCtrlV) {
      event.preventDefault()
      options.onCtrlV()
    } else if (event.code === 'KeyC' && event.shiftKey && options?.onCtrlShiftC) {
      event.preventDefault()
      options.onCtrlShiftC()
    }
  })

  return { selectAllInContainer, isSelectAll }
}
